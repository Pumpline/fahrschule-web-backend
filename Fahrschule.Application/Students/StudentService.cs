using System.Text.Json;
using Fahrschule.Application.Audit;
using Fahrschule.Application.Common;
using Fahrschule.Application.LicenseClasses;
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
public class StudentService(FahrschuleDbContext db, IAuditWriter auditWriter) : IStudentService
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
        => ToDetailDto(await LoadAsync(id, ct));

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

        return ToAkteDto(await LoadAsync(student.Id, ct));
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

        // Name and journal number are always editable (they are never hidden).
        // The sensitive fields are only overwritten when the client actually
        // loaded/edited them (EditableFields) - otherwise an unrevealed field
        // would be wiped on save (lazy-load safety).
        student.FirstName = firstName!;
        student.LastName = lastName!;
        student.JournalNumber = journalNumber;
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
        if (newSnapshot == oldSnapshot)
        {
            return ToAkteDto(student);
        }

        student.UpdatedAtUtc = DateTime.UtcNow;
        db.Entry(student).Property<uint>("xmin").OriginalValue = request.Version;
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Geändert",
            "Schüler", student.Id.ToString(), oldSnapshot, newSnapshot, ct);

        return ToAkteDto(student);
    }

    public async Task<StudentAkteDto> GetAkteAsync(Guid id, CancellationToken ct = default)
        => ToAkteDto(await LoadAsync(id, ct));

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

        return ToAkteDto(await LoadAsync(id, ct));
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

        return ToAkteDto(await LoadAsync(id, ct));
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

        return ToAkteDto(student);
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

    private StudentDetailDto ToDetailDto(Student s) => new()
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
        Version = db.Entry(s).Property<uint>("xmin").CurrentValue,
    };

    /// <summary>The lightweight Akte: name, classes, version and which sensitive
    /// fields are filled - but NOT their values (data minimisation).</summary>
    private StudentAkteDto ToAkteDto(Student s) => new()
    {
        Id = s.Id,
        FirstName = s.FirstName,
        LastName = s.LastName,
        JournalNumber = s.JournalNumber,
        Classes = ClassDtos(s),
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
    });

    private static string? Clean(string? value) => value?.Trim();
    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
