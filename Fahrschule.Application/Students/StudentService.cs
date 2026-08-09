using System.Text.Json;
using Fahrschule.Application.Audit;
using Fahrschule.Application.Common;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Application.Settings;
using Fahrschule.Contracts.Admin;
using Fahrschule.Contracts.Students;
using Fahrschule.Domain.Entities;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Application.Students;

/// <summary>Filter/paging options for the student list.</summary>
public record StudentListQuery(
    string? Search,
    Guid? LicenseClassId,
    IReadOnlyList<StudentPhase>? Phases,
    int Page,
    int PageSize);

public interface IStudentService
{
    Task<StudentListResultDto> GetListAsync(StudentListQuery query, CancellationToken ct = default);

    /// <summary>The FULL record incl. all values - INTERNAL only (export, PDFs).</summary>
    Task<StudentDetailDto> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>The lightweight "Akte" for the detail page (no sensitive values,
    /// only which fields are filled). Data minimisation (KONZEPT 3.1).</summary>
    Task<StudentAkteDto> GetAkteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Reveal one sensitive field's value on demand - and audit the access.</summary>
    Task<StudentFieldValueDto> GetFieldAsync(Guid id, string field, Actor actor, CancellationToken ct = default);

    Task<StudentAkteDto> CreateAsync(CreateStudentRequest request, Actor actor, CancellationToken ct = default);
    Task<StudentAkteDto> UpdateAsync(Guid id, UpdateStudentRequest request, Actor actor, CancellationToken ct = default);
    Task DeleteAsync(Guid id, Actor actor, CancellationToken ct = default);
    Task<StudentAkteDto> AddLicenseClassAsync(Guid id, Guid licenseClassId, Actor actor, CancellationToken ct = default);
    Task<StudentAkteDto> RemoveLicenseClassAsync(Guid id, Guid licenseClassId, Actor actor, CancellationToken ct = default);

    Task<StudentAkteDto> SetPhaseAsync(Guid id, Guid licenseClassId, StudentPhase phase, Actor actor, CancellationToken ct = default);

    /// <summary>Students marked for deletion ("Zur Löschung vorgemerkt", KONZEPT 3.7).</summary>
    Task<List<DeletedStudentDto>> GetDeletedAsync(CancellationToken ct = default);

    /// <summary>Undo a soft delete (admin, logged - KONZEPT 3.7 / rule 7).</summary>
    Task RestoreAsync(Guid id, Actor actor, CancellationToken ct = default);
}

/// <summary>
/// Business logic for the student module (KONZEPT 3.1). Same proven pattern as
/// the configuration data: audit log, soft delete, optimistic concurrency.
/// The status lives per licence class (StudentLicenseClass.Phase), not per
/// student. Adding a class checks the minimum age against the class.
/// </summary>
public class StudentService(
    FahrschuleDbContext db,
    ISettingsService settingsService,
    IAuditWriter auditWriter) : IStudentService
{
    private const int MaxPageSize = 100;

    public async Task<StudentListResultDto> GetListAsync(StudentListQuery query, CancellationToken ct = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize <= 0 ? 12 : query.PageSize, 1, MaxPageSize);

        var students = db.Students
            .Include(s => s.LicenseClasses).ThenInclude(lc => lc.LicenseClass)
            .AsQueryable();

        // Search: first name, last name or the record number ("Journalnummer")
        // contains the term, case-insensitive. The number is included so the
        // office can go straight from the paper journal to the file.
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            students = students.Where(s =>
                EF.Functions.ILike(s.FirstName, $"%{term}%") ||
                EF.Functions.ILike(s.LastName, $"%{term}%") ||
                (s.JournalNumber != null && EF.Functions.ILike(s.JournalNumber, $"%{term}%")));
        }

        // Filter: has a registration for this class.
        if (query.LicenseClassId is { } classId)
        {
            students = students.Where(s => s.LicenseClasses.Any(lc => lc.LicenseClassId == classId));
        }

        // Filter: has at least one class in one of the selected phases.
        if (query.Phases is { Count: > 0 } phases)
        {
            students = students.Where(s => s.LicenseClasses.Any(lc => phases.Contains(lc.Phase)));
        }

        var total = await students.CountAsync(ct);

        var pageItems = await students
            .OrderBy(s => s.LastName).ThenBy(s => s.FirstName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new StudentListResultDto
        {
            Total = total,
            Page = page,
            PageSize = pageSize,
            Items = pageItems.Select(s => new StudentListItemDto
            {
                Id = s.Id,
                FullName = $"{s.FirstName} {s.LastName}".Trim(),
                JournalNumber = s.JournalNumber,
                ClassCodes = [.. s.LicenseClasses
                    .Where(lc => lc.LicenseClass != null)
                    .Select(lc => lc.LicenseClass!.Code)
                    .OrderBy(code => code)],
                ProgressPercent = StudentRules.OverallProgress(s.LicenseClasses.Select(lc => lc.Phase)),
            }).ToList(),
        };
    }

    public async Task<StudentDetailDto> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await ToDetailDtoAsync(await LoadAsync(id, ct), ct);

    public async Task<StudentAkteDto> CreateAsync(CreateStudentRequest request, Actor actor, CancellationToken ct = default)
    {
        var firstName = Clean(request.FirstName);
        var lastName = Clean(request.LastName);
        ValidateNames(firstName, lastName);

        var journalNumber = NullIfEmpty(request.JournalNumber);
        ValidateJournalNumber(journalNumber);
        await EnsureJournalNumberIsFreeAsync(journalNumber, null, ct);

        var now = DateTime.UtcNow;
        var student = new Student
        {
            Id = Guid.NewGuid(),
            FirstName = firstName!,
            LastName = lastName!,
            JournalNumber = journalNumber,
            DateOfBirth = request.DateOfBirth,
            Email = NullIfEmpty(request.Email),
            Phone = NullIfEmpty(request.Phone),
            Address = NullIfEmpty(request.Address),
            Notes = NullIfEmpty(request.Notes),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        db.Students.Add(student);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Angelegt",
            "Schüler", student.Id.ToString(), newValuesJson: Snapshot(student), cancellationToken: ct);

        return await ToAkteDtoAsync(await LoadAsync(student.Id, ct), ct);
    }

    public async Task<StudentAkteDto> UpdateAsync(Guid id, UpdateStudentRequest request, Actor actor, CancellationToken ct = default)
    {
        var student = await LoadAsync(id, ct);
        var firstName = Clean(request.FirstName);
        var lastName = Clean(request.LastName);
        ValidateNames(firstName, lastName);

        var journalNumber = NullIfEmpty(request.JournalNumber);
        ValidateJournalNumber(journalNumber);
        await EnsureJournalNumberIsFreeAsync(journalNumber, id, ct);

        var oldSnapshot = Snapshot(student);
        var priorChange = await ApplyPriorLicenseClassesAsync(student, request.PriorLicenseClassIds, ct);

        // Name and journal number are always editable (they are never hidden).
        // The sensitive fields are only overwritten when the client actually
        // loaded/edited them (EditableFields) - otherwise an unrevealed field
        // would be wiped on save (lazy-load safety).
        student.FirstName = firstName!;
        student.LastName = lastName!;
        student.JournalNumber = journalNumber;
        student.PriorLicenseNote = NullIfEmpty(request.PriorLicenseNote);
        student.RequiredBasicTheoryLessonsOverride =
            request.RequiredBasicTheoryLessonsOverride is > 0 ? request.RequiredBasicTheoryLessonsOverride : null;
        student.RequiredBasicTheoryLessonsOverrideReason =
            student.RequiredBasicTheoryLessonsOverride is null
                ? null // no override → no dangling reason
                : NullIfEmpty(request.RequiredBasicTheoryLessonsOverrideReason);
        var editable = request.EditableFields ?? [];
        if (editable.Contains("dateOfBirth")) student.DateOfBirth = request.DateOfBirth;
        if (editable.Contains("email")) student.Email = NullIfEmpty(request.Email);
        if (editable.Contains("phone")) student.Phone = NullIfEmpty(request.Phone);
        if (editable.Contains("address")) student.Address = NullIfEmpty(request.Address);
        if (editable.Contains("notes")) student.Notes = NullIfEmpty(request.Notes);

        // No real change → don't touch the database and don't write an audit
        // entry (only actual changes are logged; reading/revealing a field stays
        // audited). Saving identical values must not produce a "Geändert" entry.
        var newSnapshot = Snapshot(student);
        if (newSnapshot == oldSnapshot && priorChange is null)
        {
            return await ToAkteDtoAsync(student, ct);
        }

        student.UpdatedAtUtc = DateTime.UtcNow;
        db.Entry(student).Property<uint>("xmin").OriginalValue = request.Version;
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Geändert",
            "Schüler", student.Id.ToString(),
            AppendPrior(oldSnapshot, priorChange?.Before), AppendPrior(newSnapshot, priorChange?.After), ct);

        return await ToAkteDtoAsync(student, ct);
    }

    public async Task<StudentAkteDto> GetAkteAsync(Guid id, CancellationToken ct = default)
        => await ToAkteDtoAsync(await LoadAsync(id, ct), ct);

    public async Task<StudentFieldValueDto> GetFieldAsync(Guid id, string field, Actor actor, CancellationToken ct = default)
    {
        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("Dieser Schüler wurde nicht gefunden. Bitte die Liste neu laden.");

        var (label, value) = field switch
        {
            "dateOfBirth" => ("Geburtsdatum", student.DateOfBirth?.ToString("yyyy-MM-dd")),
            "email" => ("E-Mail", student.Email),
            "phone" => ("Telefon", student.Phone),
            "address" => ("Adresse", student.Address),
            "notes" => ("Notizen", student.Notes),
            _ => throw new AppValidationException("Unbekanntes Feld."),
        };

        // Accessing a single personal-data field is logged (GDPR access trail).
        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Stammdaten angesehen",
            "Schüler", id.ToString(),
            newValuesJson: $"{{\"Feld\":\"{label}\"}}", cancellationToken: ct);

        return new StudentFieldValueDto { Key = field, Value = value ?? string.Empty };
    }

    public async Task DeleteAsync(Guid id, Actor actor, CancellationToken ct = default)
    {
        var student = await LoadAsync(id, ct);

        // Soft delete (project rule 7): students carry retention rules, so we
        // only flag here; the retention job removes them after the deadline.
        student.IsDeleted = true;
        student.DeletedAtUtc = DateTime.UtcNow;
        student.DeletedByUserId = actor.UserId;
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Gelöscht",
            "Schüler", student.Id.ToString(),
            oldValuesJson: Snapshot(student), cancellationToken: ct);
    }

    public async Task<StudentAkteDto> AddLicenseClassAsync(Guid id, Guid licenseClassId, Actor actor, CancellationToken ct = default)
    {
        var student = await LoadAsync(id, ct);

        if (student.LicenseClasses.Any(lc => lc.LicenseClassId == licenseClassId))
        {
            throw new AppValidationException("Diese Klasse ist bei dem Schüler bereits eingetragen.");
        }

        var licenseClass = await db.LicenseClasses.FirstOrDefaultAsync(c => c.Id == licenseClassId, ct)
            ?? throw new AppValidationException("Diese Führerscheinklasse existiert nicht (mehr).");

        // Check the minimum age against the class (KONZEPT: age validation).
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var ageError = StudentRules.CheckMinimumAge(student.DateOfBirth, licenseClass.MinimumAge, today);
        if (ageError is not null)
        {
            throw new AppValidationException(ageError);
        }

        student.LicenseClasses.Add(new StudentLicenseClass
        {
            StudentId = student.Id,
            LicenseClassId = licenseClassId,
            Phase = StudentPhase.Theory,
            AddedAtUtc = DateTime.UtcNow,
        });
        student.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Geändert",
            "Schüler", student.Id.ToString(),
            newValuesJson: $"{{\"KlasseHinzugefügt\":\"{licenseClass.Code}\"}}", cancellationToken: ct);

        return await ToAkteDtoAsync(await LoadAsync(id, ct), ct);
    }

    public async Task<StudentAkteDto> RemoveLicenseClassAsync(Guid id, Guid licenseClassId, Actor actor, CancellationToken ct = default)
    {
        var student = await LoadAsync(id, ct);
        var entry = student.LicenseClasses.FirstOrDefault(lc => lc.LicenseClassId == licenseClassId)
            ?? throw new NotFoundException("Diese Klasse ist bei dem Schüler nicht eingetragen.");

        var code = entry.LicenseClass?.Code ?? licenseClassId.ToString();
        student.LicenseClasses.Remove(entry);
        student.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Geändert",
            "Schüler", student.Id.ToString(),
            oldValuesJson: $"{{\"KlasseEntfernt\":\"{code}\"}}", cancellationToken: ct);

        return await ToAkteDtoAsync(await LoadAsync(id, ct), ct);
    }


    public async Task<StudentAkteDto> SetPhaseAsync(Guid id, Guid licenseClassId, StudentPhase phase, Actor actor, CancellationToken ct = default)
    {
        var student = await LoadAsync(id, ct);
        var entry = student.LicenseClasses.FirstOrDefault(lc => lc.LicenseClassId == licenseClassId)
            ?? throw new NotFoundException("Diese Klasse ist bei dem Schüler nicht eingetragen.");

        var oldPhase = entry.Phase;
        entry.Phase = phase;
        student.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Geändert",
            "Schüler", student.Id.ToString(),
            oldValuesJson: $"{{\"Phase\":\"{oldPhase}\"}}",
            newValuesJson: $"{{\"Phase\":\"{phase}\"}}", cancellationToken: ct);

        return await ToAkteDtoAsync(student, ct);
    }

    public async Task<List<DeletedStudentDto>> GetDeletedAsync(CancellationToken ct = default)
    {
        // Bypass the soft-delete filter to see the ones marked for deletion.
        var students = await db.Students.IgnoreQueryFilters()
            .Where(s => s.IsDeleted)
            .Include(s => s.LicenseClasses).ThenInclude(lc => lc.LicenseClass)
            .OrderByDescending(s => s.DeletedAtUtc)
            .ToListAsync(ct);

        return students.Select(s => new DeletedStudentDto
        {
            Id = s.Id,
            FullName = $"{s.FirstName} {s.LastName}".Trim(),
            DeletedAtUtc = s.DeletedAtUtc,
            ClassCodes = [.. s.LicenseClasses.Where(lc => lc.LicenseClass != null)
                .Select(lc => lc.LicenseClass!.Code).OrderBy(c => c)],
        }).ToList();
    }

    public async Task RestoreAsync(Guid id, Actor actor, CancellationToken ct = default)
    {
        var student = await db.Students.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == id && s.IsDeleted, ct)
            ?? throw new NotFoundException("Dieser zur Löschung vorgemerkte Schüler wurde nicht gefunden. Bitte die Liste neu laden.");

        student.IsDeleted = false;
        student.DeletedAtUtc = null;
        student.DeletedByUserId = null;
        student.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Wiederhergestellt",
            "Schüler", student.Id.ToString(), cancellationToken: ct);
    }

    private async Task<Student> LoadAsync(Guid id, CancellationToken ct)
        => await db.Students
            .Include(s => s.LicenseClasses).ThenInclude(lc => lc.LicenseClass)
            .Include(s => s.PriorLicenseClasses).ThenInclude(pc => pc.LicenseClass)
            .FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("Dieser Schüler wurde nicht gefunden. Vielleicht wurde er gerade gelöscht – bitte Liste neu laden.");

    private static void ValidateNames(string? firstName, string? lastName)
    {
        var errors = new List<string>();
        if (string.IsNullOrEmpty(firstName)) errors.Add("Bitte den Vornamen eintragen.");
        else if (firstName.Length > StudentRules.MaxNameLength) errors.Add("Der Vorname ist zu lang.");
        if (string.IsNullOrEmpty(lastName)) errors.Add("Bitte den Nachnamen eintragen.");
        else if (lastName.Length > StudentRules.MaxNameLength) errors.Add("Der Nachname ist zu lang.");
        if (errors.Count > 0) throw new AppValidationException(string.Join(" ", errors));
    }

    /// <summary>What the Vorbesitz looked like before and after a save (codes,
    /// for the audit log). null when nothing changed.</summary>
    private record PriorLicenseChange(string Before, string After);

    /// <summary>
    /// Sets the Vorbesitz to exactly the given classes. null = the client did not
    /// send the field, so the current list is kept untouched (an old client must
    /// never wipe it). Returns what changed, or null when it is the same set.
    /// </summary>
    private async Task<PriorLicenseChange?> ApplyPriorLicenseClassesAsync(
        Student student, List<Guid>? wanted, CancellationToken ct)
    {
        if (wanted is null) return null;

        var target = wanted.Distinct().ToList();
        var current = student.PriorLicenseClasses.Select(pc => pc.LicenseClassId).ToHashSet();
        if (current.SetEquals(target)) return null;

        var classes = await db.LicenseClasses
            .Where(c => target.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Code, ct);

        foreach (var id in target)
        {
            if (!classes.ContainsKey(id))
            {
                throw new AppValidationException("Eine gewählte Führerscheinklasse existiert nicht (mehr). Bitte die Seite neu laden.");
            }
            // A class the student is TRAINING for cannot at the same time be one
            // they already hold - that would be a data-entry slip, and it would
            // silently shorten their own Grundstoff.
            if (student.LicenseClasses.Any(lc => lc.LicenseClassId == id))
            {
                throw new AppValidationException(
                    $"Die Klasse {classes[id]} wird gerade ausgebildet. Sie kann nicht gleichzeitig als bereits vorhandener Führerschein eingetragen werden.");
            }
        }

        var before = CodesOf(student);
        student.PriorLicenseClasses.RemoveAll(pc => !target.Contains(pc.LicenseClassId));
        foreach (var id in target.Where(id => !current.Contains(id)))
        {
            student.PriorLicenseClasses.Add(new StudentPriorLicenseClass
            {
                StudentId = student.Id,
                LicenseClassId = id,
                AddedAtUtc = DateTime.UtcNow,
            });
        }

        var after = string.Join(", ", target.Select(id => classes[id]).OrderBy(c => c));
        return new PriorLicenseChange(before, after);
    }

    private static string CodesOf(Student s)
        => string.Join(", ", s.PriorLicenseClasses
            .Where(pc => pc.LicenseClass != null)
            .Select(pc => pc.LicenseClass!.Code).OrderBy(c => c));

    /// <summary>Adds the Vorbesitz codes to an audit snapshot when they changed,
    /// so the owner sees them in the same before/after entry.</summary>
    private static string AppendPrior(string snapshot, string? codes)
        => codes is null ? snapshot : snapshot[..^1] + $",\"Vorbesitz\":\"{codes}\"}}";

    private static void ValidateJournalNumber(string? journalNumber)
    {
        if (journalNumber is not null && journalNumber.Length > StudentRules.MaxJournalNumberLength)
        {
            throw new AppValidationException(
                $"Die Journalnummer darf höchstens {StudentRules.MaxJournalNumberLength} Zeichen lang sein.");
        }
    }

    /// <summary>
    /// A journal number identifies exactly one student on the printed documents,
    /// so the same number must not sit on two files. Compared case-insensitively
    /// against the students that are still in the list; a number of a student
    /// marked for deletion stays blocked as long as that record exists (it can be
    /// restored). <paramref name="ownId"/> excludes the student being edited.
    /// </summary>
    private async Task EnsureJournalNumberIsFreeAsync(string? journalNumber, Guid? ownId, CancellationToken ct)
    {
        if (journalNumber is null) return;

        // Stored numbers are already trimmed, so upper-casing both sides is enough
        // for the comparison - and it translates to SQL (upper(...)).
        var normalized = StudentRules.NormalizeJournalNumber(journalNumber);
        var clash = await db.Students.IgnoreQueryFilters()
            .Where(s => s.Id != ownId && s.JournalNumber != null && s.JournalNumber.ToUpper() == normalized)
            .Select(s => new { s.IsDeleted })
            .FirstOrDefaultAsync(ct);
        if (clash is null) return;

        throw new AppValidationException(clash.IsDeleted
            ? $"Die Journalnummer „{journalNumber}\" gehört zu einem Schüler, der zur Löschung vorgemerkt ist. " +
              "Bitte eine andere Nummer verwenden."
            : $"Die Journalnummer „{journalNumber}\" ist bereits bei einem anderen Schüler eingetragen.");
    }

    /// <summary>
    /// The Vorbesitz block plus the Grundstoff requirement that follows from it
    /// (§ 4 Abs. 3 FahrschAusbO). Built here AND in the progress service from the
    /// same pure rule, so the file and the progress can never disagree.
    /// </summary>
    private async Task<StudentPriorLicenseDto> BuildPriorLicenseAsync(Student s, CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);

        // How many Grundstoff topics the student's personal plan actually holds -
        // the requirement can never exceed that (see the rule). Loaded rather than
        // counted in SQL so the SAME rule decides what a Grundstoff item is.
        var planItems = await db.StudentProgressItems
            .Include(p => p.Classes)
            .Where(p => p.StudentId == s.Id)
            .ToListAsync(ct);
        var availableTopics = planItems.Count(StudentProgressRules.IsBasicTheory);

        // A student whose progress has never been opened has no snapshot yet (it
        // is created lazily on the first read). Fall back to the CURRENT plan -
        // that is exactly what they will get - so the file never claims "0 owed".
        if (planItems.Count == 0)
        {
            var current = await db.CurriculumItems
                .Where(x => x.SupersededAtUtc == null && x.IsActive)
                .Select(x => new { x.Section, ClassCount = x.Classes.Count, x.RequiredCount })
                .ToListAsync(ct);
            availableTopics = current.Count(x =>
                StudentProgressRules.IsBasicTheory(x.Section, x.ClassCount, x.RequiredCount));
        }

        var hasPrior = s.PriorLicenseClasses.Count > 0 || !string.IsNullOrWhiteSpace(s.PriorLicenseNote);

        return new StudentPriorLicenseDto
        {
            Classes =
            [
                .. s.PriorLicenseClasses
                    .Where(pc => pc.LicenseClass != null)
                    .OrderBy(pc => pc.LicenseClass!.SortOrder)
                    .Select(pc => new StudentPriorLicenseClassDto
                    {
                        LicenseClassId = pc.LicenseClassId,
                        Code = pc.LicenseClass!.Code,
                        Description = pc.LicenseClass!.Description,
                    }),
            ],
            Note = s.PriorLicenseNote,
            HasPriorLicense = hasPrior,
            RequiredBasicTheoryLessons = StudentProgressRules.RequiredBasicTheoryLessons(
                hasPrior, s.RequiredBasicTheoryLessonsOverride,
                settings.TheoryBasicDoubleLessons, settings.TheoryBasicDoubleLessonsWithPriorLicense,
                availableTopics),
            RequiredBasicTheoryLessonsOverride = s.RequiredBasicTheoryLessonsOverride,
            RequiredBasicTheoryLessonsOverrideReason = s.RequiredBasicTheoryLessonsOverrideReason,
        };
    }

    private async Task<StudentDetailDto> ToDetailDtoAsync(Student s, CancellationToken ct)
        => ToDetailDto(s, await BuildPriorLicenseAsync(s, ct));

    private async Task<StudentAkteDto> ToAkteDtoAsync(Student s, CancellationToken ct)
        => ToAkteDto(s, await BuildPriorLicenseAsync(s, ct));

    private StudentDetailDto ToDetailDto(Student s, StudentPriorLicenseDto prior) => new()
    {
        Id = s.Id,
        FirstName = s.FirstName,
        LastName = s.LastName,
        JournalNumber = s.JournalNumber,
        DateOfBirth = s.DateOfBirth,
        Email = s.Email,
        Phone = s.Phone,
        Address = s.Address,
        Notes = s.Notes,
        Classes = ClassDtos(s),
        PriorLicense = prior,
        Version = db.Entry(s).Property<uint>("xmin").CurrentValue,
    };

    /// <summary>The lightweight Akte: name, classes, version and which sensitive
    /// fields are filled - but NOT their values (data minimisation).</summary>
    private StudentAkteDto ToAkteDto(Student s, StudentPriorLicenseDto prior) => new()
    {
        Id = s.Id,
        FirstName = s.FirstName,
        LastName = s.LastName,
        JournalNumber = s.JournalNumber,
        Classes = ClassDtos(s),
        PriorLicense = prior,
        Version = db.Entry(s).Property<uint>("xmin").CurrentValue,
        Fields =
        [
            new() { Key = "dateOfBirth", Label = "Geburtsdatum", HasValue = s.DateOfBirth is not null },
            new() { Key = "email", Label = "E-Mail", HasValue = !string.IsNullOrWhiteSpace(s.Email) },
            new() { Key = "phone", Label = "Telefon", HasValue = !string.IsNullOrWhiteSpace(s.Phone) },
            new() { Key = "address", Label = "Adresse", HasValue = !string.IsNullOrWhiteSpace(s.Address) },
            new() { Key = "notes", Label = "Notizen", HasValue = !string.IsNullOrWhiteSpace(s.Notes) },
        ],
    };

    private static List<StudentLicenseClassDto> ClassDtos(Student s) =>
    [
        .. s.LicenseClasses
            .Where(lc => lc.LicenseClass != null)
            .OrderBy(lc => lc.LicenseClass!.SortOrder)
            .Select(lc => new StudentLicenseClassDto
            {
                LicenseClassId = lc.LicenseClassId,
                Code = lc.LicenseClass!.Code,
                Description = lc.LicenseClass!.Description,
                Phase = lc.Phase.ToString(),
            }),
    ];

    /// <summary>Snapshot for the audit log. Note: this includes personal data,
    /// which is exactly what the audit log is meant to record (before/after) -
    /// but never special categories, as we don't store any.</summary>
    private static string Snapshot(Student s) => JsonSerializer.Serialize(new
    {
        s.FirstName, s.LastName, s.JournalNumber, s.DateOfBirth, s.Email, s.Phone, s.Address, s.Notes,
        s.PriorLicenseNote, s.RequiredBasicTheoryLessonsOverride, s.RequiredBasicTheoryLessonsOverrideReason,
    });

    private static string? Clean(string? value) => value?.Trim();
    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
