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
    Task<StudentDetailDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<StudentDetailDto> CreateAsync(CreateStudentRequest request, Actor actor, CancellationToken ct = default);
    Task<StudentDetailDto> UpdateAsync(Guid id, UpdateStudentRequest request, Actor actor, CancellationToken ct = default);
    Task DeleteAsync(Guid id, Actor actor, CancellationToken ct = default);
    Task<StudentDetailDto> AddLicenseClassAsync(Guid id, Guid licenseClassId, Actor actor, CancellationToken ct = default);
    Task<StudentDetailDto> RemoveLicenseClassAsync(Guid id, Guid licenseClassId, Actor actor, CancellationToken ct = default);
    Task<StudentDetailDto> SetPhaseAsync(Guid id, Guid licenseClassId, StudentPhase phase, Actor actor, CancellationToken ct = default);

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

        // Name search (first or last name contains the term, case-insensitive).
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            students = students.Where(s =>
                EF.Functions.ILike(s.FirstName, $"%{term}%") ||
                EF.Functions.ILike(s.LastName, $"%{term}%"));
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

    public async Task<StudentDetailDto> CreateAsync(CreateStudentRequest request, Actor actor, CancellationToken ct = default)
    {
        var firstName = Clean(request.FirstName);
        var lastName = Clean(request.LastName);
        ValidateNames(firstName, lastName);

        var now = DateTime.UtcNow;
        var student = new Student
        {
            Id = Guid.NewGuid(),
            FirstName = firstName!,
            LastName = lastName!,
            DateOfBirth = request.DateOfBirth,
            Email = NullIfEmpty(request.Email),
            Phone = NullIfEmpty(request.Phone),
            Street = NullIfEmpty(request.Street),
            PostalCode = NullIfEmpty(request.PostalCode),
            City = NullIfEmpty(request.City),
            Notes = NullIfEmpty(request.Notes),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        db.Students.Add(student);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Angelegt",
            "Schüler", student.Id.ToString(), newValuesJson: Snapshot(student), cancellationToken: ct);

        return ToDetailDto(await LoadAsync(student.Id, ct));
    }

    public async Task<StudentDetailDto> UpdateAsync(Guid id, UpdateStudentRequest request, Actor actor, CancellationToken ct = default)
    {
        var student = await LoadAsync(id, ct);
        var firstName = Clean(request.FirstName);
        var lastName = Clean(request.LastName);
        ValidateNames(firstName, lastName);

        var oldSnapshot = Snapshot(student);

        student.FirstName = firstName!;
        student.LastName = lastName!;
        student.DateOfBirth = request.DateOfBirth;
        student.Email = NullIfEmpty(request.Email);
        student.Phone = NullIfEmpty(request.Phone);
        student.Street = NullIfEmpty(request.Street);
        student.PostalCode = NullIfEmpty(request.PostalCode);
        student.City = NullIfEmpty(request.City);
        student.Notes = NullIfEmpty(request.Notes);
        student.UpdatedAtUtc = DateTime.UtcNow;

        db.Entry(student).Property<uint>("xmin").OriginalValue = request.Version;
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Geändert",
            "Schüler", student.Id.ToString(), oldSnapshot, Snapshot(student), ct);

        return ToDetailDto(student);
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

    public async Task<StudentDetailDto> AddLicenseClassAsync(Guid id, Guid licenseClassId, Actor actor, CancellationToken ct = default)
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

        return ToDetailDto(await LoadAsync(id, ct));
    }

    public async Task<StudentDetailDto> RemoveLicenseClassAsync(Guid id, Guid licenseClassId, Actor actor, CancellationToken ct = default)
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

        return ToDetailDto(await LoadAsync(id, ct));
    }

    public async Task<StudentDetailDto> SetPhaseAsync(Guid id, Guid licenseClassId, StudentPhase phase, Actor actor, CancellationToken ct = default)
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

        return ToDetailDto(student);
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

    private StudentDetailDto ToDetailDto(Student s) => new()
    {
        Id = s.Id,
        FirstName = s.FirstName,
        LastName = s.LastName,
        DateOfBirth = s.DateOfBirth,
        Email = s.Email,
        Phone = s.Phone,
        Street = s.Street,
        PostalCode = s.PostalCode,
        City = s.City,
        Notes = s.Notes,
        Classes = [.. s.LicenseClasses
            .Where(lc => lc.LicenseClass != null)
            .OrderBy(lc => lc.LicenseClass!.SortOrder)
            .Select(lc => new StudentLicenseClassDto
            {
                LicenseClassId = lc.LicenseClassId,
                Code = lc.LicenseClass!.Code,
                Description = lc.LicenseClass!.Description,
                Phase = lc.Phase.ToString(),
            })],
        Version = db.Entry(s).Property<uint>("xmin").CurrentValue,
    };

    /// <summary>Snapshot for the audit log. Note: this includes personal data,
    /// which is exactly what the audit log is meant to record (before/after) -
    /// but never special categories, as we don't store any.</summary>
    private static string Snapshot(Student s) => JsonSerializer.Serialize(new
    {
        s.FirstName, s.LastName, s.DateOfBirth, s.Email, s.Phone, s.Street, s.PostalCode, s.City, s.Notes,
    });

    private static string? Clean(string? value) => value?.Trim();
    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
