using Fahrschule.Application.Audit;
using Fahrschule.Application.Common;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Contracts.Students;
using Fahrschule.Domain.Entities;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Application.Students;

public interface IStudentProgressService
{
    Task<StudentProgressDto> GetForStudentAsync(Guid studentId, CancellationToken ct = default);
    Task<CreditPreviewDto> GetCreditPreviewAsync(Guid studentId, Guid licenseClassId, CancellationToken ct = default);
    Task<StudentProgressDto> SetItemAsync(Guid studentId, Guid itemId, SetProgressItemRequest request, Actor actor, CancellationToken ct = default);
    Task<StudentProgressDto> AddEntryAsync(Guid studentId, Guid itemId, AddProgressEntryRequest request, Actor actor, CancellationToken ct = default);
    Task<StudentProgressDto> RemoveEntryAsync(Guid studentId, Guid itemId, Guid entryId, Actor actor, CancellationToken ct = default);
}

/// <summary>
/// Business logic for a student's training progress (KONZEPT 3.3 / 3.3a).
///
/// The personal checklist is a SNAPSHOT of the curriculum: when a student has
/// classes, the currently valid curriculum points are copied into their own
/// list (with the version and title as they were then). Later master changes
/// do not act retroactively. Shared "Grundstoff" points are kept once and
/// counted for every class they apply to.
///
/// Snapshotting is done lazily ("ensure on read"): every read makes sure the
/// student has a progress row for each applicable point. This also backfills
/// students that already existed before this feature. Going forward it keeps
/// the list in sync when a class is added.
/// </summary>
public class StudentProgressService(FahrschuleDbContext db, IAuditWriter auditWriter) : IStudentProgressService
{
    private const string AuditEntityType = "Ausbildungsfortschritt";

    public async Task<StudentProgressDto> GetForStudentAsync(Guid studentId, CancellationToken ct = default)
    {
        var student = await LoadStudentAsync(studentId, ct);
        await EnsureSnapshotAsync(student, ct);

        var items = await LoadProgressAsync(studentId, ct);
        return BuildDto(student, items);
    }

    public async Task<StudentProgressDto> SetItemAsync(
        Guid studentId, Guid itemId, SetProgressItemRequest request, Actor actor, CancellationToken ct = default)
    {
        var item = await LoadItemAsync(studentId, itemId, ct);

        // Countable points are driven by their counter, not by a single tick.
        if (StudentProgressRules.IsCountable(item.RequiredCount))
        {
            throw new AppValidationException(
                "Dieser Punkt wird über den Zähler (+/−) gepflegt und kann nicht direkt abgehakt werden.");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var newCompleted = request.IsDone;
        DateOnly? newCompletedOn = request.IsDone ? request.CompletedOn ?? today : null;
        var newNote = NullIfEmpty(request.Note);

        // No real change → don't write and don't log (e.g. re-saving the same
        // "Erledigt am"/note, or a tick that doesn't actually flip the state).
        // Only genuine changes land in the audit log.
        if (item.IsCompleted == newCompleted && item.CompletedOn == newCompletedOn && item.Note == newNote)
        {
            return await GetForStudentAsync(studentId, ct);
        }

        item.IsCompleted = newCompleted;
        item.CompletedOn = newCompletedOn;
        item.Note = newNote;
        item.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        // "Erledigtes wieder austragen" is the noteworthy case (KONZEPT 3.3) -
        // the confirmation happens in the UI; here we record it in the audit log.
        var action = request.IsDone ? "Abgehakt" : "Wieder ausgetragen";
        await auditWriter.WriteAsync(actor.UserId, actor.UserName, action,
            AuditEntityType, $"{studentId}/{item.Title}", cancellationToken: ct);

        return await GetForStudentAsync(studentId, ct);
    }

    public async Task<StudentProgressDto> AddEntryAsync(
        Guid studentId, Guid itemId, AddProgressEntryRequest request, Actor actor, CancellationToken ct = default)
    {
        var item = await LoadItemAsync(studentId, itemId, ct);
        RequireCountable(item);

        // Add through the DbSet (not item.Entries) so we only insert the new
        // row and update the item - we never re-save the already-loaded entries.
        db.Set<StudentProgressEntry>().Add(new StudentProgressEntry
        {
            Id = Guid.NewGuid(),
            StudentProgressItemId = item.Id,
            PerformedOn = request.PerformedOn,
            Note = NullIfEmpty(request.Note),
            CreatedAtUtc = DateTime.UtcNow,
        });
        item.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Stunde gezählt",
            AuditEntityType, $"{studentId}/{item.Title}",
            newValuesJson: $"{{\"Anzahl\":{item.Entries.Count + 1}}}", cancellationToken: ct);

        return await GetForStudentAsync(studentId, ct);
    }

    public async Task<StudentProgressDto> RemoveEntryAsync(
        Guid studentId, Guid itemId, Guid entryId, Actor actor, CancellationToken ct = default)
    {
        var item = await LoadItemAsync(studentId, itemId, ct);
        var entry = item.Entries.FirstOrDefault(e => e.Id == entryId)
            ?? throw new NotFoundException("Diese gezählte Stunde wurde nicht gefunden. Bitte die Seite neu laden.");

        item.Entries.Remove(entry);
        item.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Gezählte Stunde entfernt",
            AuditEntityType, $"{studentId}/{item.Title}",
            newValuesJson: $"{{\"Anzahl\":{item.Entries.Count}}}", cancellationToken: ct);

        return await GetForStudentAsync(studentId, ct);
    }

    public async Task<CreditPreviewDto> GetCreditPreviewAsync(
        Guid studentId, Guid licenseClassId, CancellationToken ct = default)
    {
        var student = await LoadStudentAsync(studentId, ct);
        if (student.LicenseClasses.Any(lc => lc.LicenseClassId == licenseClassId))
        {
            throw new AppValidationException("Diese Klasse ist bei dem Schüler bereits eingetragen.");
        }

        var licenseClass = await db.LicenseClasses.FirstOrDefaultAsync(c => c.Id == licenseClassId, ct)
            ?? throw new NotFoundException("Diese Führerscheinklasse existiert nicht (mehr).");

        // The points that today's plan requires for the candidate class.
        var current = await db.CurriculumItems
            .Where(x => x.SupersededAtUtc == null && x.IsActive)
            .Include(x => x.Classes)
            .ToListAsync(ct);
        var applicable = current
            .Where(x => x.Classes.Count == 0 || x.Classes.Any(c => c.LicenseClassId == licenseClassId))
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Title)
            .ToList();

        // What the student already has (by stable item key).
        var existing = (await db.StudentProgressItems
                .Include(p => p.Entries)
                .Where(p => p.StudentId == studentId)
                .ToListAsync(ct))
            .ToDictionary(p => p.CurriculumItemKey);

        var result = new CreditPreviewDto { LicenseClassId = licenseClassId, Code = licenseClass.Code };

        foreach (var src in applicable)
        {
            var dto = new CreditPreviewItemDto { Section = src.Section, Title = src.Title };
            if (existing.TryGetValue(src.ItemKey, out var done) && StudentProgressRules.IsDone(done))
            {
                // Done already - unchanged version is credited, a newer version
                // means the content changed and should be checked (KONZEPT 3.3a).
                if (done.CurriculumItemVersion == src.Version) result.AlreadyCredited.Add(dto);
                else result.NeedsReview.Add(dto);
            }
            else
            {
                result.NewPoints.Add(dto);
            }
        }

        return result;
    }

    // --- snapshot ---

    /// <summary>
    /// Makes sure the student has a progress row for every currently applicable
    /// curriculum point, and that existing rows carry all class links that now
    /// apply (e.g. after a class was added). Never removes anything - removing a
    /// class must not destroy recorded progress.
    /// </summary>
    private async Task EnsureSnapshotAsync(Student student, CancellationToken ct)
    {
        var studentClassIds = student.LicenseClasses.Select(lc => lc.LicenseClassId).ToHashSet();
        if (studentClassIds.Count == 0)
        {
            return; // no classes → nothing to train yet
        }

        // Current curriculum points (latest version, active) with their classes.
        var current = await db.CurriculumItems
            .Where(x => x.SupersededAtUtc == null && x.IsActive)
            .Include(x => x.Classes)
            .ToListAsync(ct);

        // Applies when the point has no class restriction (all) or intersects
        // the student's classes (KONZEPT 3.2).
        var applicable = current
            .Where(x => x.Classes.Count == 0 || x.Classes.Any(c => studentClassIds.Contains(c.LicenseClassId)))
            .ToList();

        var existing = await db.StudentProgressItems
            .Include(p => p.Classes)
            .Where(p => p.StudentId == student.Id)
            .ToListAsync(ct);
        var existingByKey = existing.ToDictionary(p => p.CurriculumItemKey);

        var now = DateTime.UtcNow;
        var changed = false;

        foreach (var source in applicable)
        {
            // The classes (out of the student's) this point counts for. Empty
            // when the point applies to all classes (a shared point).
            var snapshotClassIds = source.Classes.Count == 0
                ? []
                : source.Classes.Select(c => c.LicenseClassId)
                    .Where(studentClassIds.Contains).Distinct().ToList();

            if (existingByKey.TryGetValue(source.ItemKey, out var progress))
            {
                // Already snapshotted - only add class links that now apply.
                foreach (var classId in snapshotClassIds)
                {
                    if (progress.Classes.All(c => c.LicenseClassId != classId))
                    {
                        progress.Classes.Add(new StudentProgressItemClass { LicenseClassId = classId });
                        changed = true;
                    }
                }
                continue;
            }

            db.StudentProgressItems.Add(new StudentProgressItem
            {
                Id = Guid.NewGuid(),
                StudentId = student.Id,
                CurriculumItemKey = source.ItemKey,
                CurriculumItemVersion = source.Version,
                Section = source.Section,
                Title = source.Title,
                RequiredCount = source.RequiredCount,
                SortOrder = source.SortOrder,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                Classes = [.. snapshotClassIds.Select(id => new StudentProgressItemClass { LicenseClassId = id })],
            });
            changed = true;
        }

        // Per-class practice / extra-theory points are DERIVED from the class's
        // target numbers (Sonderfahrten, Zusatzstoff double lessons) rather than
        // from the theory-topic catalogue (KONZEPT 3.3). We materialise them as
        // normal progress rows so the existing counter/entry mechanics apply, with
        // a deterministic synthetic key per (class, slot) so reads don't duplicate.
        EnsureDerivedItems(student, existingByKey, now, ref changed);

        if (changed)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Ensures the generated Praxis / Zusatzstoff rows exist for each of the
    /// student's classes, mirroring the per-class target numbers. A class only
    /// gets these rows once it actually requires them, so unconfigured classes
    /// stay untouched:
    ///  - Theorie-Zusatzstoff (only when the class has Zusatzstoff double lessons):
    ///    a "Zusatzstoff (Doppelstunden)" counter + a voluntary extra-theory counter,
    ///  - Praxis (only when the class has any special drives): the Überland/Autobahn/
    ///    Nacht counters (each if &gt; 0), a "Grundfahraufgaben" check-off, and a
    ///    voluntary extra-lessons counter.
    /// A mandatory counter follows the class setting (an admin change updates its
    /// target). Nothing is ever removed - recorded sessions stay.
    /// </summary>
    private void EnsureDerivedItems(
        Student student, Dictionary<Guid, StudentProgressItem> existingByKey, DateTime now, ref bool changed)
    {
        foreach (var studentClass in student.LicenseClasses)
        {
            var lc = studentClass.LicenseClass;
            if (lc is null) continue;
            var classId = studentClass.LicenseClassId;

            var hasTheory = lc.RequiredTheoryDoubleLessons > 0;
            var hasPractice = lc.RequiredSpecialDrivesOverland > 0
                || lc.RequiredSpecialDrivesHighway > 0 || lc.RequiredSpecialDrivesNight > 0;

            // (slot, section, title, requiredCount, sortOrder, generate?)
            var slots = new (string Slot, string Section, string Title, int? Required, int Sort, bool Generate)[]
            {
                ("theory-zusatz", "Theorie-Zusatzstoff", "Zusatzstoff (Doppelstunden)", lc.RequiredTheoryDoubleLessons, 200, hasTheory),
                ("theory-extra", "Theorie-Zusatzstoff", "Zusätzliche Theoriestunden (freiwillig)", 0, 210, hasTheory),
                ("drive-overland", "Praxis", "Überlandfahrt", lc.RequiredSpecialDrivesOverland, 300, lc.RequiredSpecialDrivesOverland > 0),
                ("drive-highway", "Praxis", "Autobahnfahrt", lc.RequiredSpecialDrivesHighway, 310, lc.RequiredSpecialDrivesHighway > 0),
                ("drive-night", "Praxis", "Nachtfahrt", lc.RequiredSpecialDrivesNight, 320, lc.RequiredSpecialDrivesNight > 0),
                ("basic-tasks", "Praxis", "Grundfahraufgaben", null, 330, hasPractice),
                ("practice-extra", "Praxis", "Übungs-/Zusatzstunden (freiwillig)", 0, 340, hasPractice),
            };

            foreach (var slot in slots)
            {
                var key = DerivedKey(classId, slot.Slot);
                if (existingByKey.TryGetValue(key, out var item))
                {
                    // Keep a counter's target in sync with the current class setting.
                    if (item.RequiredCount != slot.Required)
                    {
                        item.RequiredCount = slot.Required;
                        item.UpdatedAtUtc = now;
                        changed = true;
                    }
                    if (item.Classes.All(c => c.LicenseClassId != classId))
                    {
                        item.Classes.Add(new StudentProgressItemClass { LicenseClassId = classId });
                        changed = true;
                    }
                    continue;
                }

                if (!slot.Generate) continue;

                db.StudentProgressItems.Add(new StudentProgressItem
                {
                    Id = Guid.NewGuid(),
                    StudentId = student.Id,
                    CurriculumItemKey = key,
                    CurriculumItemVersion = 0, // derived, not a curriculum snapshot
                    Section = slot.Section,
                    Title = slot.Title,
                    RequiredCount = slot.Required,
                    SortOrder = slot.Sort,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    Classes = [new StudentProgressItemClass { LicenseClassId = classId }],
                });
                changed = true;
            }
        }
    }

    /// <summary>Deterministic synthetic curriculum key for a derived per-class
    /// point, so repeated reads find the same row instead of duplicating it.</summary>
    private static Guid DerivedKey(Guid classId, string slot)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes(classId.ToString("N") + "|" + slot));
        return new Guid(bytes);
    }

    // --- DTO building ---

    private static StudentProgressDto BuildDto(Student student, List<StudentProgressItem> items)
    {
        var studentClassCount = student.LicenseClasses.Count;
        var classes = student.LicenseClasses
            .Where(lc => lc.LicenseClass != null)
            .OrderBy(lc => lc.LicenseClass!.SortOrder)
            .Select(lc => BuildClassProgress(lc, items, studentClassCount))
            .ToList();

        return new StudentProgressDto { Classes = classes };
    }

    private static ClassProgressDto BuildClassProgress(
        StudentLicenseClass studentClass, List<StudentProgressItem> items, int studentClassCount)
    {
        // Points that count for this class (its own + shared).
        var forClass = items
            .Where(p => StudentProgressRules.AppliesToClass(
                p.Classes.Select(c => c.LicenseClassId).ToList(), studentClass.LicenseClassId))
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Title)
            .ToList();

        // Voluntary extra-lesson counters are shown but do not count toward the
        // Pflicht completion (KONZEPT 3.3).
        var required = forClass.Where(StudentProgressRules.IsRequired).ToList();
        var done = required.Count(StudentProgressRules.IsDone);

        var sections = forClass
            .GroupBy(p => p.Section)
            .OrderBy(g => g.Min(p => p.SortOrder)).ThenBy(g => g.Key)
            .Select(g => new ProgressSectionDto
            {
                Section = g.Key,
                Items = g.OrderBy(p => p.SortOrder).ThenBy(p => p.Title)
                    .Select(p => ToItemDto(p, studentClassCount)).ToList(),
            })
            .ToList();

        return new ClassProgressDto
        {
            LicenseClassId = studentClass.LicenseClassId,
            Code = studentClass.LicenseClass!.Code,
            Description = studentClass.LicenseClass!.Description,
            Phase = studentClass.Phase.ToString(),
            DoneCount = done,
            TotalCount = required.Count,
            DonePercent = StudentProgressRules.Percent(done, required.Count),
            Sections = sections,
        };
    }

    private static ProgressItemDto ToItemDto(StudentProgressItem p, int studentClassCount)
    {
        var countable = StudentProgressRules.IsCountable(p.RequiredCount);
        // Shared "Grundstoff" = a point with no class restriction (applies to all
        // of the student's classes). Shown once in its own card, even when the
        // student currently has a single class (matches the design mockup).
        var isShared = p.Classes.Count == 0 || p.Classes.Count > 1;

        return new ProgressItemDto
        {
            Id = p.Id,
            Title = p.Title,
            RequiredCount = p.RequiredCount,
            IsCountable = countable,
            CurrentCount = p.Entries.Count,
            IsDone = StudentProgressRules.IsDone(p),
            CompletedOn = p.CompletedOn,
            Note = p.Note,
            IsShared = isShared,
            Entries = countable
                ? p.Entries.OrderBy(e => e.PerformedOn).ThenBy(e => e.CreatedAtUtc)
                    .Select(e => new ProgressEntryDto { Id = e.Id, PerformedOn = e.PerformedOn, Note = e.Note })
                    .ToList()
                : [],
        };
    }

    // --- loading helpers ---

    private async Task<Student> LoadStudentAsync(Guid studentId, CancellationToken ct)
        => await db.Students
            .Include(s => s.LicenseClasses).ThenInclude(lc => lc.LicenseClass)
            .FirstOrDefaultAsync(s => s.Id == studentId, ct)
            ?? throw new NotFoundException("Dieser Schüler wurde nicht gefunden. Bitte die Liste neu laden.");

    private async Task<List<StudentProgressItem>> LoadProgressAsync(Guid studentId, CancellationToken ct)
        => await db.StudentProgressItems
            .Include(p => p.Classes)
            .Include(p => p.Entries)
            .Where(p => p.StudentId == studentId)
            .ToListAsync(ct);

    private async Task<StudentProgressItem> LoadItemAsync(Guid studentId, Guid itemId, CancellationToken ct)
        => await db.StudentProgressItems
            .Include(p => p.Entries)
            .FirstOrDefaultAsync(p => p.Id == itemId && p.StudentId == studentId, ct)
            ?? throw new NotFoundException("Dieser Ausbildungspunkt wurde nicht gefunden. Bitte die Seite neu laden.");

    private static void RequireCountable(StudentProgressItem item)
    {
        if (!StudentProgressRules.IsCountable(item.RequiredCount))
        {
            throw new AppValidationException("Dieser Punkt hat keinen Zähler. Bitte ihn direkt abhaken.");
        }
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
