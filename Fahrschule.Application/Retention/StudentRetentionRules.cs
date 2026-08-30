namespace Fahrschule.Application.Retention;

/// <summary>
/// Pure retention rules for student records (§ 31 Abs. 3 FahrlG): the training
/// records must be kept for five years AFTER THE END OF THE YEAR in which the
/// instruction was completed, then deleted without delay. Kept side-effect free
/// so the date maths can be unit-tested on its own.
/// </summary>
public static class StudentRetentionRules
{
    /// <summary>
    /// The day a student's training counts as finished: the latest of the last
    /// lesson, the last exam, and the registration date. Using "last instruction
    /// activity" (not the per-class "Completed" phase) also covers drop-outs, who
    /// never reach a completed state; the registration date is the floor for
    /// students who left before any lesson or exam was recorded.
    /// </summary>
    public static DateOnly TrainingEndDate(DateOnly registeredOn, DateOnly? lastLesson, DateOnly? lastExam)
    {
        var end = registeredOn;
        if (lastLesson is { } lesson && lesson > end) end = lesson;
        if (lastExam is { } exam && exam > end) end = exam;
        return end;
    }

    /// <summary>
    /// The first day the data MAY be deleted. § 31 keeps the records for
    /// <paramref name="retentionYears"/> full years after the END of the training
    /// year, so deletion becomes due on 1 January of
    /// (training-end year + retentionYears + 1).
    /// Example: training ends in 2026, 5 years → keep through 2031, delete from 2032-01-01.
    /// </summary>
    public static DateOnly DeletionDueDate(DateOnly trainingEnd, int retentionYears)
        => new(trainingEnd.Year + retentionYears + 1, 1, 1);

    /// <summary>True once the deletion deadline has been reached or passed.</summary>
    public static bool IsDue(DateOnly today, DateOnly trainingEnd, int retentionYears)
        => today >= DeletionDueDate(trainingEnd, retentionYears);

    /// <summary>
    /// The day a student may really be deleted. Training records fall under
    /// § 31 FahrlG, but a RECEIPT is a tax document under § 147 AO and has to be
    /// kept much longer (10 years). So the later of the two deadlines decides -
    /// otherwise deleting the student would take a receipt with it before its
    /// own period has run out (project rule 1 and 7).
    /// </summary>
    public static DateOnly DueDateWithReceipts(
        DateOnly trainingEnd, int retentionYears, DateOnly? lastReceiptIssuedOn, int receiptRetentionYears)
    {
        var due = DeletionDueDate(trainingEnd, retentionYears);
        if (lastReceiptIssuedOn is not { } receipt) return due;

        var receiptDue = new DateOnly(receipt.Year + receiptRetentionYears + 1, 1, 1);
        return receiptDue > due ? receiptDue : due;
    }
}
