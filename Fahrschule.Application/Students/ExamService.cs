using System.Text.Json;
using Fahrschule.Application.Audit;
using Fahrschule.Application.Common;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Application.Settings;
using Fahrschule.Contracts.Settings;
using Fahrschule.Contracts.Students;
using Fahrschule.Domain.Entities;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Application.Students;

public interface IExamService
{
    Task<ExamListDto> GetForStudentAsync(Guid studentId, CancellationToken ct = default);
    Task<ExamListDto> CreateAsync(Guid studentId, CreateExamRequest request, Actor actor, CancellationToken ct = default);
}

/// <summary>
/// Business logic for exams (KONZEPT 3.4). Real exams are counted as attempts; a
/// failed one starts a repeat lock that shortens automatically once enough
/// practice lessons are logged afterwards (the hours live only in the training
/// progress). Preliminary exams ("Vorprüfung") are only noted. A practical exam
/// can only be entered once the theory exam of that class has been passed.
///
/// Attempt numbers and locks are DERIVED (never stored) from the exams and the
/// logged lessons, so the data stays consistent when something is edited.
/// </summary>
public class ExamService(FahrschuleDbContext db, ISettingsService settingsService, IAuditWriter auditWriter) : IExamService
{
    public async Task<ExamListDto> GetForStudentAsync(Guid studentId, CancellationToken ct = default)
    {
        var student = await LoadStudentAsync(studentId, ct);
        var codes = ClassCodes(student);

        var exams = await db.Exams.Where(e => e.StudentId == studentId).ToListAsync(ct);
        var lessons = await db.Lessons.Where(l => l.StudentId == studentId).ToListAsync(ct);
        var settings = await settingsService.GetAsync(ct);

        // Attempt numbers: count real exams per kind+class in chronological order.
        var attempts = AttemptNumbers(exams);

        var examDtos = exams
            .OrderByDescending(e => e.DateOn).ThenByDescending(e => e.CreatedAtUtc)
            .Select(e => ToDto(e, codes, attempts))
            .ToList();

        // One lock per kind+class that has an unresolved failed exam.
        var locks = new List<ExamLockDto>();
        foreach (var group in exams.Where(e => !e.IsPreliminary).GroupBy(e => (e.Kind, e.LicenseClassId)))
        {
            var info = TryBuildLock(group.Key.Kind, group.Key.LicenseClassId,
                codes.GetValueOrDefault(group.Key.LicenseClassId, ""), group.ToList(), lessons, settings);
            if (info is not null) locks.Add(info);
        }

        return new ExamListDto { Exams = examDtos, Locks = locks };
    }

    public async Task<ExamListDto> CreateAsync(
        Guid studentId, CreateExamRequest request, Actor actor, CancellationToken ct = default)
    {
        if (!Enum.TryParse<ExamKind>(request.Kind, out var kind))
        {
            throw new AppValidationException("Bitte Theorie oder Praxis wählen.");
        }
        if (!Enum.TryParse<ExamResult>(request.Result, out var result))
        {
            throw new AppValidationException("Bitte ein Ergebnis wählen (geplant, bestanden oder nicht bestanden).");
        }

        var student = await LoadStudentAsync(studentId, ct);
        if (student.LicenseClasses.All(lc => lc.LicenseClassId != request.LicenseClassId))
        {
            throw new AppValidationException("Diese Klasse ist beim Schüler nicht eingetragen. Bitte die Seite neu laden.");
        }

        var exams = await db.Exams.Where(e => e.StudentId == studentId).ToListAsync(ct);

        // A real practical exam needs a passed theory exam of the same class first.
        if (kind == ExamKind.Practice && !request.IsPreliminary)
        {
            var theoryPassed = exams.Any(e => e.LicenseClassId == request.LicenseClassId
                && e.Kind == ExamKind.Theory && !e.IsPreliminary && e.Result == ExamResult.Passed);
            if (!theoryPassed)
            {
                throw new AppValidationException(
                    "Eine Praxisprüfung ist erst möglich, wenn die Theorieprüfung dieser Klasse bestanden ist.");
            }
        }

        // A real repeat must not be scheduled before the active lock ends.
        if (!request.IsPreliminary)
        {
            var settings = await settingsService.GetAsync(ct);
            var lessons = await db.Lessons.Where(l => l.StudentId == studentId).ToListAsync(ct);
            var existing = exams.Where(e => !e.IsPreliminary && e.Kind == kind && e.LicenseClassId == request.LicenseClassId).ToList();
            var activeLock = TryBuildLock(kind, request.LicenseClassId, "", existing, lessons, settings);
            if (activeLock is not null && request.DateOn < activeLock.LockedUntil)
            {
                throw new AppValidationException(
                    $"Wegen der Wiederholungs-Sperre frühestens am {activeLock.LockedUntil:dd.MM.yyyy} möglich. " +
                    $"Mit {settings.ExamLockPracticeLessonsForShortening} Übungsstunden verkürzt sich die Sperre " +
                    $"(im Ausbildungsfortschritt eintragen).");
            }
        }

        var exam = new Exam
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            LicenseClassId = request.LicenseClassId,
            Kind = kind,
            IsPreliminary = request.IsPreliminary,
            DateOn = request.DateOn,
            Result = result,
            Note = NullIfEmpty(request.Note),
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Exams.Add(exam);
        await db.SaveChangesAsync(ct);

        var code = ClassCodes(student).GetValueOrDefault(request.LicenseClassId, "");
        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Prüfung eingetragen",
            "Prüfung", studentId.ToString(),
            newValuesJson: JsonSerializer.Serialize(new
            {
                Art = ArtLabel(kind, request.IsPreliminary),
                Klasse = code,
                Datum = request.DateOn.ToString("dd.MM.yyyy"),
                Ergebnis = ResultLabel(result),
            }), cancellationToken: ct);

        return await GetForStudentAsync(studentId, ct);
    }

    // --- helpers ---

    /// <summary>Builds the lock info for a kind+class, or null when there is no
    /// unresolved failed exam (no failed one, or a later pass resolved it).</summary>
    private static ExamLockDto? TryBuildLock(
        ExamKind kind, Guid classId, string classCode,
        List<Exam> groupExams, List<Lesson> lessons, AppSettingsDto settings)
    {
        var ordered = groupExams
            .Where(e => !e.IsPreliminary)
            .OrderBy(e => e.DateOn).ThenBy(e => e.CreatedAtUtc).ToList();

        var failed = ordered.LastOrDefault(e => e.Result == ExamResult.Failed);
        if (failed is null) return null;

        // A pass after the failed attempt resolves the lock.
        var resolved = ordered.Any(e => e.Result == ExamResult.Passed && IsAfter(e, failed));
        if (resolved) return null;

        var lessonType = kind == ExamKind.Theory ? LessonType.Theory : LessonType.Practice;
        var lessonsSince = lessons.Count(l => l.Type == lessonType
            && l.LicenseClassId == classId && l.DateOn > failed.DateOn);

        var shortened = ExamRules.IsShortened(lessonsSince, settings.ExamLockPracticeLessonsForShortening);
        var lockEnd = ExamRules.LockEnd(failed.DateOn, shortened, settings.ExamLockNormalWeeks, settings.ExamLockShortenedWeeks);

        return new ExamLockDto
        {
            LicenseClassId = classId,
            ClassCode = classCode,
            Kind = kind.ToString(),
            FailedOn = failed.DateOn,
            LockedUntil = lockEnd,
            IsShortened = shortened,
            LessonsSince = lessonsSince,
            LessonsNeeded = settings.ExamLockPracticeLessonsForShortening,
            NormalWeeks = settings.ExamLockNormalWeeks,
            ShortenedWeeks = settings.ExamLockShortenedWeeks,
        };
    }

    private static bool IsAfter(Exam a, Exam b)
        => a.DateOn > b.DateOn || (a.DateOn == b.DateOn && a.CreatedAtUtc > b.CreatedAtUtc);

    /// <summary>Maps each real exam id to its attempt number (1, 2, ...).</summary>
    private static Dictionary<Guid, int> AttemptNumbers(List<Exam> exams)
    {
        var counters = new Dictionary<(ExamKind, Guid), int>();
        var result = new Dictionary<Guid, int>();
        foreach (var e in exams.Where(x => !x.IsPreliminary)
                     .OrderBy(x => x.DateOn).ThenBy(x => x.CreatedAtUtc))
        {
            var key = (e.Kind, e.LicenseClassId);
            counters[key] = counters.GetValueOrDefault(key) + 1;
            result[e.Id] = counters[key];
        }
        return result;
    }

    private static ExamDto ToDto(Exam e, Dictionary<Guid, string> codes, Dictionary<Guid, int> attempts) => new()
    {
        Id = e.Id,
        Kind = e.Kind.ToString(),
        IsPreliminary = e.IsPreliminary,
        LicenseClassId = e.LicenseClassId,
        ClassCode = codes.GetValueOrDefault(e.LicenseClassId, e.LicenseClass?.Code ?? ""),
        DateOn = e.DateOn,
        Result = e.Result.ToString(),
        AttemptNumber = e.IsPreliminary ? null : attempts.GetValueOrDefault(e.Id),
        Note = e.Note,
    };

    private async Task<Student> LoadStudentAsync(Guid studentId, CancellationToken ct)
        => await db.Students
            .Include(s => s.LicenseClasses).ThenInclude(lc => lc.LicenseClass)
            .FirstOrDefaultAsync(s => s.Id == studentId, ct)
            ?? throw new NotFoundException("Dieser Schüler wurde nicht gefunden. Bitte die Liste neu laden.");

    private static Dictionary<Guid, string> ClassCodes(Student s)
        => s.LicenseClasses.Where(lc => lc.LicenseClass != null)
            .ToDictionary(lc => lc.LicenseClassId, lc => lc.LicenseClass!.Code);

    private static string ArtLabel(ExamKind kind, bool preliminary) => (kind, preliminary) switch
    {
        (ExamKind.Theory, false) => "Theorieprüfung",
        (ExamKind.Practice, false) => "Praxisprüfung",
        (ExamKind.Theory, true) => "Theorie-Vorprüfung",
        _ => "Praxis-Vorprüfung",
    };

    private static string ResultLabel(ExamResult result) => result switch
    {
        ExamResult.Passed => "bestanden",
        ExamResult.Failed => "nicht bestanden",
        _ => "geplant",
    };

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
