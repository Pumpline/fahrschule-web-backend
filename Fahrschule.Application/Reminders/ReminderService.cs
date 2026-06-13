using System.Text.Json;
using Fahrschule.Application.Audit;
using Fahrschule.Application.Common;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Contracts.Reminders;
using Fahrschule.Domain.Entities;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Application.Reminders;

public interface IReminderService
{
    /// <summary>Follow-ups, open ones first by due date. <paramref name="includeDone"/>
    /// also returns completed ones; <paramref name="studentId"/> filters to one student.</summary>
    Task<List<ReminderDto>> GetListAsync(bool includeDone, Guid? studentId, CancellationToken ct = default);
    Task<ReminderDto> CreateAsync(SaveReminderRequest request, Actor actor, CancellationToken ct = default);
    Task<ReminderDto> UpdateAsync(Guid id, SaveReminderRequest request, Actor actor, CancellationToken ct = default);
    /// <summary>Marks a follow-up done or open again.</summary>
    Task<ReminderDto> SetDoneAsync(Guid id, bool done, Actor actor, CancellationToken ct = default);
    Task DeleteAsync(Guid id, Actor actor, CancellationToken ct = default);
}

/// <summary>
/// Business logic for follow-ups / reminders ("Wiedervorlagen", KONZEPT Stufe 2).
/// Same proven pattern as the calendar: thin controller, audit on every change,
/// German user-facing messages. Reminders are operational (no soft delete); a
/// reminder linked to a student is removed with that student by the retention job.
/// </summary>
public class ReminderService(FahrschuleDbContext db, IAuditWriter auditWriter) : IReminderService
{
    private const int MaxTitleLength = 200;
    private const int MaxNoteLength = 1000;

    public async Task<List<ReminderDto>> GetListAsync(bool includeDone, Guid? studentId, CancellationToken ct = default)
    {
        var query = db.Reminders.Include(r => r.Student).AsQueryable();
        if (!includeDone) query = query.Where(r => !r.IsDone);
        if (studentId is { } sid) query = query.Where(r => r.StudentId == sid);

        // Open ones first, each block sorted by due date (earliest/most overdue first).
        var reminders = await query
            .OrderBy(r => r.IsDone)
            .ThenBy(r => r.DueOn)
            .ToListAsync(ct);

        return reminders.Select(ToDto).ToList();
    }

    public async Task<ReminderDto> CreateAsync(SaveReminderRequest request, Actor actor, CancellationToken ct = default)
    {
        var title = Clean(request.Title);
        await ValidateAsync(title, request.Note, request.StudentId, ct);

        var now = DateTime.UtcNow;
        var reminder = new Reminder
        {
            Id = Guid.NewGuid(),
            Title = title!,
            Note = NullIfEmpty(request.Note),
            DueOn = request.DueOn,
            StudentId = request.StudentId,
            IsDone = false,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.Reminders.Add(reminder);
        await db.SaveChangesAsync(ct);

        await WriteAuditAsync(actor, "Wiedervorlage angelegt", reminder, ct);
        return await ReloadAsync(reminder.Id, ct);
    }

    public async Task<ReminderDto> UpdateAsync(Guid id, SaveReminderRequest request, Actor actor, CancellationToken ct = default)
    {
        var reminder = await LoadAsync(id, ct);
        var title = Clean(request.Title);
        await ValidateAsync(title, request.Note, request.StudentId, ct);

        reminder.Title = title!;
        reminder.Note = NullIfEmpty(request.Note);
        reminder.DueOn = request.DueOn;
        reminder.StudentId = request.StudentId;
        reminder.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await WriteAuditAsync(actor, "Wiedervorlage geändert", reminder, ct);
        return await ReloadAsync(reminder.Id, ct);
    }

    public async Task<ReminderDto> SetDoneAsync(Guid id, bool done, Actor actor, CancellationToken ct = default)
    {
        var reminder = await LoadAsync(id, ct);
        reminder.IsDone = done;
        reminder.DoneAtUtc = done ? DateTime.UtcNow : null;
        reminder.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await WriteAuditAsync(actor, done ? "Wiedervorlage erledigt" : "Wiedervorlage wieder offen", reminder, ct);
        return await ReloadAsync(reminder.Id, ct);
    }

    public async Task DeleteAsync(Guid id, Actor actor, CancellationToken ct = default)
    {
        var reminder = await LoadAsync(id, ct);
        db.Reminders.Remove(reminder);
        await db.SaveChangesAsync(ct);

        await WriteAuditAsync(actor, "Wiedervorlage gelöscht", reminder, ct);
    }

    private async Task<Reminder> LoadAsync(Guid id, CancellationToken ct)
        => await db.Reminders.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException("Diese Wiedervorlage wurde nicht gefunden. Bitte die Liste neu laden.");

    private async Task ValidateAsync(string? title, string? note, Guid? studentId, CancellationToken ct)
    {
        var errors = new List<string>();
        if (string.IsNullOrEmpty(title)) errors.Add("Bitte eine Bezeichnung eintragen.");
        else if (title.Length > MaxTitleLength) errors.Add("Die Bezeichnung ist zu lang.");
        if ((note?.Length ?? 0) > MaxNoteLength) errors.Add("Die Notiz ist zu lang.");
        if (errors.Count > 0) throw new AppValidationException(string.Join(" ", errors));

        if (studentId is { } sid && !await db.Students.AnyAsync(s => s.Id == sid, ct))
        {
            throw new AppValidationException("Dieser Schüler wurde nicht gefunden. Bitte die Seite neu laden.");
        }
    }

    private async Task<ReminderDto> ReloadAsync(Guid id, CancellationToken ct)
        => ToDto(await db.Reminders.Include(r => r.Student).FirstAsync(r => r.Id == id, ct));

    private async Task WriteAuditAsync(Actor actor, string action, Reminder reminder, CancellationToken ct)
    {
        var studentName = reminder.StudentId is null
            ? null
            : await db.Students.Where(s => s.Id == reminder.StudentId)
                .Select(s => s.FirstName + " " + s.LastName).FirstOrDefaultAsync(ct);

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, action,
            "Wiedervorlage", reminder.Id.ToString(),
            newValuesJson: JsonSerializer.Serialize(new
            {
                reminder.Title,
                Fällig = reminder.DueOn,
                Schüler = studentName ?? "—",
                Erledigt = reminder.IsDone,
            }), cancellationToken: ct);
    }

    private static ReminderDto ToDto(Reminder r) => new()
    {
        Id = r.Id,
        Title = r.Title,
        Note = r.Note,
        DueOn = r.DueOn,
        StudentId = r.StudentId,
        StudentName = r.Student is null ? null : $"{r.Student.FirstName} {r.Student.LastName}".Trim(),
        IsDone = r.IsDone,
        DoneAtUtc = r.DoneAtUtc,
    };

    private static string? Clean(string? value) => value?.Trim();
    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
