using System.Text.Json;
using Fahrschule.Application.Audit;
using Fahrschule.Application.Common;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Application.Students;
using Fahrschule.Contracts.Theory;
using Fahrschule.Domain.Entities;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Application.Theory;

public interface ITheorySessionService
{
    /// <summary>The theory topics to choose for a session (current catalogue, simple check-off).</summary>
    Task<List<TheoryTopicDto>> GetTopicsAsync(CancellationToken ct = default);
    Task<List<TheorySessionListItemDto>> GetListAsync(CancellationToken ct = default);
    Task<TheorySessionDetailDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<TheorySessionDetailDto> CreateAsync(CreateTheorySessionRequest request, Actor actor, CancellationToken ct = default);
    Task<TheorySessionDetailDto> AddAttendeesAsync(Guid sessionId, AddAttendeesRequest request, Actor actor, CancellationToken ct = default);
    Task<TheorySessionDetailDto> RemoveAttendeeAsync(Guid sessionId, Guid studentId, Actor actor, CancellationToken ct = default);
    Task DeleteAsync(Guid sessionId, Actor actor, CancellationToken ct = default);
}

/// <summary>
/// Theory attendance lists ("Theorie-Anwesenheitslisten", KONZEPT Stufe 2). A
/// theory double lesson is a GROUP record: one date + one topic + the attending
/// students. Marking a student present also ticks that topic in their personal
/// theory checklist (the owner chose the integrated variant - no double entry).
/// We remember which progress point each attendance ticked, so removing it undoes
/// exactly that and nothing another source completed.
/// </summary>
public class TheorySessionService(
    FahrschuleDbContext db,
    IStudentProgressService progress,
    IAuditWriter auditWriter) : ITheorySessionService
{
    private const int MaxDurationMinutes = 600;

    public async Task<List<TheoryTopicDto>> GetTopicsAsync(CancellationToken ct = default)
        => await CurrentTheoryTopics()
            .OrderBy(x => x.Section).ThenBy(x => x.SortOrder).ThenBy(x => x.Title)
            .Select(x => new TheoryTopicDto { Id = x.Id, ItemKey = x.ItemKey, Section = x.Section, Title = x.Title })
            .ToListAsync(ct);

    public async Task<List<TheorySessionListItemDto>> GetListAsync(CancellationToken ct = default)
        => await db.TheorySessions
            .OrderByDescending(s => s.DateOn).ThenByDescending(s => s.CreatedAtUtc)
            .Select(s => new TheorySessionListItemDto
            {
                Id = s.Id,
                DateOn = s.DateOn,
                TopicTitle = s.TopicTitle,
                TopicSection = s.TopicSection,
                AttendeeCount = s.Attendances.Count,
            })
            .ToListAsync(ct);

    public async Task<TheorySessionDetailDto> GetByIdAsync(Guid id, CancellationToken ct = default)
        => ToDetail(await LoadSessionAsync(id, ct));

    public async Task<TheorySessionDetailDto> CreateAsync(CreateTheorySessionRequest request, Actor actor, CancellationToken ct = default)
    {
        if (request.DurationMinutes <= 0 || request.DurationMinutes > MaxDurationMinutes)
        {
            throw new AppValidationException("Bitte eine gültige Dauer für die Theoriestunde eintragen.");
        }

        var topic = await CurrentTheoryTopics().FirstOrDefaultAsync(x => x.Id == request.CurriculumItemId, ct)
            ?? throw new AppValidationException("Bitte ein gültiges Theorie-Thema wählen.");

        var session = new TheorySession
        {
            Id = Guid.NewGuid(),
            DateOn = request.DateOn,
            DurationMinutes = request.DurationMinutes,
            CurriculumItemKey = topic.ItemKey,
            TopicTitle = topic.Title,
            TopicSection = topic.Section,
            Note = NullIfEmpty(request.Note),
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.TheorySessions.Add(session);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Theorie-Stunde angelegt",
            "Theorie-Anwesenheit", session.DateOn.ToString("dd.MM.yyyy"),
            newValuesJson: JsonSerializer.Serialize(new { Thema = topic.Title, Datum = session.DateOn.ToString("dd.MM.yyyy") }),
            cancellationToken: ct);

        foreach (var studentId in request.StudentIds.Distinct())
        {
            await MarkAttendeeAsync(session, studentId, actor, ct);
        }

        return ToDetail(await LoadSessionAsync(session.Id, ct));
    }

    public async Task<TheorySessionDetailDto> AddAttendeesAsync(Guid sessionId, AddAttendeesRequest request, Actor actor, CancellationToken ct = default)
    {
        var session = await LoadSessionAsync(sessionId, ct);
        foreach (var studentId in request.StudentIds.Distinct())
        {
            await MarkAttendeeAsync(session, studentId, actor, ct);
        }
        return ToDetail(await LoadSessionAsync(sessionId, ct));
    }

    public async Task<TheorySessionDetailDto> RemoveAttendeeAsync(Guid sessionId, Guid studentId, Actor actor, CancellationToken ct = default)
    {
        var session = await LoadSessionAsync(sessionId, ct);
        var attendance = session.Attendances.FirstOrDefault(a => a.StudentId == studentId);
        if (attendance is not null)
        {
            await RevertTickAsync(attendance, session, ct);
            db.Set<TheoryAttendance>().Remove(attendance);
            await db.SaveChangesAsync(ct);

            await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Theorie-Anwesenheit entfernt",
                "Ausbildungsfortschritt", $"{studentId}/{session.TopicTitle}", cancellationToken: ct);
        }
        return ToDetail(await LoadSessionAsync(sessionId, ct));
    }

    public async Task DeleteAsync(Guid sessionId, Actor actor, CancellationToken ct = default)
    {
        var session = await LoadSessionAsync(sessionId, ct);

        // Undo every attendance's tick first, then remove the session (cascade
        // takes the attendance rows with it).
        foreach (var attendance in session.Attendances.ToList())
        {
            await RevertTickAsync(attendance, session, ct);
        }

        db.TheorySessions.Remove(session);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Theorie-Stunde gelöscht",
            "Theorie-Anwesenheit", session.DateOn.ToString("dd.MM.yyyy"),
            oldValuesJson: JsonSerializer.Serialize(new { Thema = session.TopicTitle, Datum = session.DateOn.ToString("dd.MM.yyyy") }),
            cancellationToken: ct);
    }

    // --- attendee handling ---

    private async Task MarkAttendeeAsync(TheorySession session, Guid studentId, Actor actor, CancellationToken ct)
    {
        if (await db.Set<TheoryAttendance>().AnyAsync(a => a.TheorySessionId == session.Id && a.StudentId == studentId, ct))
        {
            return; // already recorded
        }

        if (!await db.Students.AnyAsync(s => s.Id == studentId, ct))
        {
            throw new AppValidationException("Ein gewählter Schüler wurde nicht gefunden. Bitte die Seite neu laden.");
        }

        // Make sure the student's theory checklist is up to date (creates the
        // topic row if it applies), then tick that topic on the session's date.
        await progress.GetForStudentAsync(studentId, ct);
        var item = await db.StudentProgressItems.FirstOrDefaultAsync(
            p => p.StudentId == studentId && p.CurriculumItemKey == session.CurriculumItemKey, ct);

        Guid? ticked = null;
        if (item is not null && !item.IsCompleted && !StudentProgressRules.IsCountable(item.RequiredCount))
        {
            item.IsCompleted = true;
            item.CompletedOn = session.DateOn;
            item.UpdatedAtUtc = DateTime.UtcNow;
            ticked = item.Id;
        }

        db.Set<TheoryAttendance>().Add(new TheoryAttendance
        {
            TheorySessionId = session.Id,
            StudentId = studentId,
            TickedProgressItemId = ticked,
            CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Theorie besucht",
            "Ausbildungsfortschritt", $"{studentId}/{session.TopicTitle}",
            newValuesJson: JsonSerializer.Serialize(new
            {
                Datum = session.DateOn.ToString("dd.MM.yyyy"),
                Abgehakt = ticked != null,
            }), cancellationToken: ct);
    }

    /// <summary>Undo exactly the tick this attendance set - and only if it is still
    /// in that state (completed on this session's date), so we never revert a
    /// completion another lesson/attendance made.</summary>
    private async Task RevertTickAsync(TheoryAttendance attendance, TheorySession session, CancellationToken ct)
    {
        if (attendance.TickedProgressItemId is not { } itemId)
        {
            return;
        }
        var item = await db.StudentProgressItems.FirstOrDefaultAsync(p => p.Id == itemId, ct);
        if (item is not null && item.IsCompleted && item.CompletedOn == session.DateOn)
        {
            item.IsCompleted = false;
            item.CompletedOn = null;
            item.UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    // --- helpers ---

    /// <summary>Current (latest, active) theory catalogue topics that are simple
    /// check-off points - the ones an attendance can tick.</summary>
    private IQueryable<CurriculumItem> CurrentTheoryTopics()
        => db.CurriculumItems.Where(x =>
            x.SupersededAtUtc == null && x.IsActive
            && x.RequiredCount == null && x.Section.StartsWith("Theorie"));

    private async Task<TheorySession> LoadSessionAsync(Guid id, CancellationToken ct)
        => await db.TheorySessions
            .Include(s => s.Attendances).ThenInclude(a => a.Student)
            .FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("Diese Theoriestunde wurde nicht gefunden. Bitte die Liste neu laden.");

    private static TheorySessionDetailDto ToDetail(TheorySession s) => new()
    {
        Id = s.Id,
        DateOn = s.DateOn,
        DurationMinutes = s.DurationMinutes,
        CurriculumItemKey = s.CurriculumItemKey,
        TopicTitle = s.TopicTitle,
        TopicSection = s.TopicSection,
        Note = s.Note,
        Attendees = [.. s.Attendances
            .Where(a => a.Student != null)
            .Select(a => new TheoryAttendeeDto
            {
                StudentId = a.StudentId,
                FullName = $"{a.Student!.FirstName} {a.Student!.LastName}".Trim(),
                CountedProgress = a.TickedProgressItemId != null,
            })
            .OrderBy(a => a.FullName)],
    };

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
