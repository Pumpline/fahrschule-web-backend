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
        // Which points a (non-deleted) lesson covers, and the LATEST date each was
        // taught. The covered set drives the "covered by lesson" lock and the
        // "manuell" hint; the latest date drives the theory-expiry check below.
        var coverage = await db.Set<LessonItem>()
            .Where(li => li.Lesson!.StudentId == studentId && !li.Lesson!.IsDeleted)
            .Select(li => new { li.StudentProgressItemId, li.Lesson!.DateOn })
            .ToListAsync(ct);
        var coveredIds = coverage.Select(c => c.StudentProgressItemId).ToHashSet();
        var lastTaughtByLesson = coverage
            .GroupBy(c => c.StudentProgressItemId)
            .ToDictionary(g => g.Key, g => g.Max(x => x.DateOn));

        // Theory topics expire after a configurable number of years (KONZEPT:
        // "nach 2 Jahren muss ein Theoriethema wiederholt werden"). Computed at
        // read time because it depends on today's date, not on any stored flag.
        var validityYears = await TheoryValidityYearsAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var (expiredIds, expiresOn) = ComputeTheoryExpiry(items, lastTaughtByLesson, validityYears, today);

        // Advance the Stand from the bottom up (sections done + exams passed),
        // never downwards - so the per-class progress and the list stay in sync.
        await EnsurePhaseAsync(student, items, expiredIds, ct);

        return BuildDto(student, items, coveredIds, expiredIds, expiresOn);
    }

    /// <summary>
    /// Raises each class's Stand to the milestone justified by the actual progress
    /// (all theory items done, theory exam passed, all practice items done,
    /// practice exam passed) - only ever upwards, so a manually set higher Stand
    /// is kept. Persisted so the student list (which reads the Stand) follows too.
    /// No audit entry: the Stand is derived from already-audited lessons/exams.
    /// </summary>
    private async Task EnsurePhaseAsync(
        Student student, List<StudentProgressItem> items, HashSet<Guid> expiredIds, CancellationToken ct)
    {
        // Passed real (non-preliminary) exams per kind.
        var passed = await db.Exams
            .Where(e => e.StudentId == student.Id && !e.IsPreliminary && e.Result == ExamResult.Passed)
            .Select(e => new { e.LicenseClassId, e.Kind })
            .ToListAsync(ct);
        var theoryPassed = passed.Where(x => x.Kind == ExamKind.Theory).Select(x => x.LicenseClassId).ToHashSet();
        var practicePassed = passed.Where(x => x.Kind == ExamKind.Practice).Select(x => x.LicenseClassId).ToHashSet();

        var changed = false;
        foreach (var sc in student.LicenseClasses)
        {
            var (theoryDone, practiceDone) = SectionCompletion(items, sc.LicenseClassId, expiredIds);
            var derived = StudentProgressRules.DerivePhase(
                theoryDone, theoryPassed.Contains(sc.LicenseClassId),
                practiceDone, practicePassed.Contains(sc.LicenseClassId));
            var raised = StudentProgressRules.RaisePhase(sc.Phase, derived);
            if (raised != sc.Phase) { sc.Phase = raised; changed = true; }
        }

        if (changed)
        {
            student.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Whether ALL required theory / practice items of a class are
    /// actually done (item-based, expired theory excluded). Empty section = not
    /// "done" by items (only an exam/manual Stand can advance past it).</summary>
    private static (bool TheoryDone, bool PracticeDone) SectionCompletion(
        List<StudentProgressItem> items, Guid classId, HashSet<Guid> expiredIds)
    {
        var required = items
            .Where(p => StudentProgressRules.AppliesToClass(
                p.Classes.Select(c => c.LicenseClassId).ToList(), classId))
            .Where(StudentProgressRules.IsRequired)
            .ToList();

        bool DoneNow(StudentProgressItem p) => StudentProgressRules.IsDone(p) && !expiredIds.Contains(p.Id);
        var theory = required.Where(p => StudentProgressRules.IsTheorySection(p.Section)).ToList();
        var practice = required.Where(p => !StudentProgressRules.IsTheorySection(p.Section)).ToList();

        return (theory.Count > 0 && theory.All(DoneNow), practice.Count > 0 && practice.All(DoneNow));
    }

    /// <summary>
    /// Works out which completed THEORY topics have lapsed. The reference date is
    /// the last time the topic was taught: the most recent counted session for a
    /// countable point, otherwise the latest covering lesson (or the manual
    /// completion date for an Anrechnung). Returns the expired ids plus, per
    /// completed theory topic, the date its validity ends (for the UI).
    /// </summary>
    private static (HashSet<Guid> ExpiredIds, Dictionary<Guid, DateOnly?> ExpiresOn) ComputeTheoryExpiry(
        List<StudentProgressItem> items, Dictionary<Guid, DateOnly> lastTaughtByLesson, int validityYears, DateOnly today)
    {
        var expired = new HashSet<Guid>();
        var expiresOn = new Dictionary<Guid, DateOnly?>();
        if (validityYears <= 0) return (expired, expiresOn);

        foreach (var p in items)
        {
            // Only completed theory topics can lapse; an open topic is just "offen".
            if (!StudentProgressRules.IsTheorySection(p.Section) || !StudentProgressRules.IsDone(p)) continue;

            DateOnly? lastTaught = StudentProgressRules.IsCountable(p.RequiredCount)
                ? (p.Entries.Count > 0 ? p.Entries.Max(e => e.PerformedOn) : null)
                : (lastTaughtByLesson.TryGetValue(p.Id, out var d) ? d : p.CompletedOn);

            expiresOn[p.Id] = StudentProgressRules.TheoryValidUntil(lastTaught, validityYears);
            if (StudentProgressRules.IsTheoryExpired(lastTaught, validityYears, today)) expired.Add(p.Id);
        }

        return (expired, expiresOn);
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
        var newNote = NullIfEmpty(request.Note);

        // New model: a simple point is normally completed by a recorded lesson;
        // the manual tick is the EXCEPTION (Anrechnung/Übernahme from another
        // school). A point already covered by a lesson must not be un-ticked here
        // - that would contradict the lesson; it has to be changed via the lesson.
        var coveredByLesson = await ProgressCoupling.IsCoveredByLessonAsync(db, item.Id, ct);
        if (!request.IsDone && coveredByLesson)
        {
            throw new AppValidationException(
                "Dieser Punkt wurde in einer Stunde abgehakt. Bitte ihn über die betreffende Stunde ändern, nicht von Hand.");
        }

        var before = (item.ManuallyCompleted, item.IsCompleted, item.CompletedOn, item.Note);

        item.ManuallyCompleted = request.IsDone;
        item.Note = newNote;
        if (request.IsDone) item.CompletedOn = request.CompletedOn ?? today;
        item.UpdatedAtUtc = DateTime.UtcNow;

        // Derive the stored IsCompleted/CompletedOn from the manual flag + any
        // lesson coverage (so "done" stays consistent with the lessons).
        await ProgressCoupling.RecomputeSimpleAsync(db, [item.Id], DateTime.UtcNow, ct);

        // No real change → don't write and don't log (e.g. re-saving the same note).
        if (before == (item.ManuallyCompleted, item.IsCompleted, item.CompletedOn, item.Note))
        {
            return await GetForStudentAsync(studentId, ct);
        }

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

        // New model: the quick "+" records a real, FULL lesson covering this
        // point, so it shows up in the hours list / Ausbildungsnachweis. The
        // start time stays 00:00 until it is filled in via the lesson edit.
        var now = DateTime.UtcNow;
        var note = NullIfEmpty(request.Note);
        var scope = item.Classes.Count == 1 ? item.Classes[0].LicenseClassId : (Guid?)null;
        var lesson = new Lesson
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            Type = IsTheorySection(item.Section) ? LessonType.Theory : LessonType.Practice,
            LicenseClassId = scope,
            DateOn = request.PerformedOn,
            StartTime = default,
            DurationMinutes = await DefaultLessonDurationAsync(ct),
            Note = note,
            CreatedAtUtc = now,
        };
        db.Lessons.Add(lesson);
        lesson.Items.Add(new LessonItem
        {
            LessonId = lesson.Id, StudentProgressItemId = item.Id, CountsTowardRequirement = true,
        });
        db.Set<StudentProgressEntry>().Add(new StudentProgressEntry
        {
            Id = Guid.NewGuid(),
            StudentProgressItemId = item.Id,
            LessonId = lesson.Id,
            PerformedOn = request.PerformedOn,
            Note = note,
            CreatedAtUtc = now,
        });
        item.UpdatedAtUtc = now;
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

        if (entry.LessonId is { } lessonId)
        {
            var lesson = await db.Lessons.Include(l => l.Items)
                .FirstOrDefaultAsync(l => l.Id == lessonId, ct);
            if (lesson is not null && lesson.Items.Count <= 1)
            {
                // The lesson exists only for this counted drive → soft-delete it
                // and drop its counted session.
                lesson.IsDeleted = true;
                lesson.DeletedAtUtc = DateTime.UtcNow;
                lesson.DeletedByUserId = actor.UserId;
                db.Set<StudentProgressEntry>().Remove(entry);
            }
            else
            {
                // Part of a multi-topic lesson → keep the lesson, just downgrade
                // this point to "practice" and drop the counted session.
                var li = lesson?.Items.FirstOrDefault(i => i.StudentProgressItemId == itemId);
                if (li is not null) li.CountsTowardRequirement = false;
                db.Set<StudentProgressEntry>().Remove(entry);
            }
        }
        else
        {
            // Legacy/manual session without a backing lesson.
            db.Set<StudentProgressEntry>().Remove(entry);
        }

        item.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var count = await db.Set<StudentProgressEntry>()
            .CountAsync(e => e.StudentProgressItemId == itemId, ct);
        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Gezählte Stunde entfernt",
            AuditEntityType, $"{studentId}/{item.Title}",
            newValuesJson: $"{{\"Anzahl\":{count}}}", cancellationToken: ct);

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

    private static StudentProgressDto BuildDto(
        Student student, List<StudentProgressItem> items, HashSet<Guid> coveredIds,
        HashSet<Guid> expiredIds, Dictionary<Guid, DateOnly?> expiresOn)
    {
        var studentClassCount = student.LicenseClasses.Count;
        var classes = student.LicenseClasses
            .Where(lc => lc.LicenseClass != null)
            .OrderBy(lc => lc.LicenseClass!.SortOrder)
            .Select(lc => BuildClassProgress(lc, items, studentClassCount, coveredIds, expiredIds, expiresOn))
            .ToList();

        return new StudentProgressDto { Classes = classes };
    }

    private static ClassProgressDto BuildClassProgress(
        StudentLicenseClass studentClass, List<StudentProgressItem> items, int studentClassCount,
        HashSet<Guid> coveredIds, HashSet<Guid> expiredIds, Dictionary<Guid, DateOnly?> expiresOn)
    {
        // Points that count for this class (its own + shared).
        var forClass = items
            .Where(p => StudentProgressRules.AppliesToClass(
                p.Classes.Select(c => c.LicenseClassId).ToList(), studentClass.LicenseClassId))
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Title)
            .ToList();

        // The Stand can make whole sections count as complete (owner's override):
        // Stand ≥ Theorieprüfung ⇒ theory 100 %, Stand ≥ Praxisprüfung ⇒ practice 100 %.
        var theoryForced = StudentProgressRules.TheoryCountsComplete(studentClass.Phase);
        var practiceForced = StudentProgressRules.PracticeCountsComplete(studentClass.Phase);
        bool ForcedDone(StudentProgressItem p) =>
            StudentProgressRules.IsTheorySection(p.Section) ? theoryForced : practiceForced;

        // Voluntary extra-lesson counters are shown but do not count toward the
        // Pflicht completion (KONZEPT 3.3). An expired theory topic no longer
        // counts as done until it is taught again - unless the Stand forces it.
        var required = forClass.Where(StudentProgressRules.IsRequired).ToList();
        var done = required.Count(p =>
            ForcedDone(p) || (StudentProgressRules.IsDone(p) && !expiredIds.Contains(p.Id)));

        var sections = forClass
            .GroupBy(p => p.Section)
            .OrderBy(g => g.Min(p => p.SortOrder)).ThenBy(g => g.Key)
            .Select(g => new ProgressSectionDto
            {
                Section = g.Key,
                Items = g.OrderBy(p => p.SortOrder).ThenBy(p => p.Title)
                    .Select(p => ToItemDto(p, studentClassCount, coveredIds, expiredIds, expiresOn, ForcedDone(p))).ToList(),
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

    private static ProgressItemDto ToItemDto(
        StudentProgressItem p, int studentClassCount, HashSet<Guid> coveredIds,
        HashSet<Guid> expiredIds, Dictionary<Guid, DateOnly?> expiresOn, bool forcedByPhase)
    {
        var countable = StudentProgressRules.IsCountable(p.RequiredCount);
        // Shared "Grundstoff" = a point with no class restriction (applies to all
        // of the student's classes). Shown once in its own card, even when the
        // student currently has a single class (matches the design mockup).
        var isShared = p.Classes.Count == 0 || p.Classes.Count > 1;
        // An expired theory topic is reported as NOT done (it must be repeated),
        // but we keep its original completion date for the "war erledigt am" hint.
        var actuallyDone = StudentProgressRules.IsDone(p);
        var expired = expiredIds.Contains(p.Id) && !forcedByPhase; // the Stand override wins
        // Counts as done only because the class Stand was set forward.
        var completedViaPhase = forcedByPhase && !(actuallyDone && !expiredIds.Contains(p.Id));

        return new ProgressItemDto
        {
            Id = p.Id,
            Title = p.Title,
            RequiredCount = p.RequiredCount,
            IsCountable = countable,
            CurrentCount = p.Entries.Count,
            IsDone = forcedByPhase || (actuallyDone && !expired),
            CompletedOn = p.CompletedOn,
            Note = p.Note,
            // Simple point completed without a lesson covering it = a manual mark.
            CompletedManually = !countable && p.ManuallyCompleted && !coveredIds.Contains(p.Id),
            CoveredByLesson = coveredIds.Contains(p.Id),
            IsExpired = expired,
            ExpiresOn = expiresOn.GetValueOrDefault(p.Id),
            CompletedViaPhase = completedViaPhase,
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
            .Include(p => p.Classes)
            .FirstOrDefaultAsync(p => p.Id == itemId && p.StudentId == studentId, ct)
            ?? throw new NotFoundException("Dieser Ausbildungspunkt wurde nicht gefunden. Bitte die Seite neu laden.");

    private static void RequireCountable(StudentProgressItem item)
    {
        if (!StudentProgressRules.IsCountable(item.RequiredCount))
        {
            throw new AppValidationException("Dieser Punkt hat keinen Zähler. Bitte ihn direkt abhaken.");
        }
    }

    /// <summary>A section counts as theory when its name starts with "Theorie".
    /// Delegates to the shared rule so service and tests agree.</summary>
    private static bool IsTheorySection(string section) => StudentProgressRules.IsTheorySection(section);

    /// <summary>The pre-selected lesson duration from the settings (data, not
    /// code - project rule 3); falls back to 90 minutes.</summary>
    private async Task<int> DefaultLessonDurationAsync(CancellationToken ct)
    {
        var raw = await db.Settings
            .Where(s => s.Key == Settings.SettingsService.LessonDefaultDurationMinutes)
            .Select(s => s.Value).FirstOrDefaultAsync(ct);
        return int.TryParse(raw, out var minutes) && minutes > 0 ? minutes : 90;
    }

    /// <summary>The configured theory validity in years (data, not code - rule 3);
    /// falls back to 2. 0 means theory topics never expire.</summary>
    private async Task<int> TheoryValidityYearsAsync(CancellationToken ct)
    {
        var raw = await db.Settings
            .Where(s => s.Key == Settings.SettingsService.TheoryValidityYears)
            .Select(s => s.Value).FirstOrDefaultAsync(ct);
        return int.TryParse(raw, out var years) && years >= 0 ? years : 2;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
