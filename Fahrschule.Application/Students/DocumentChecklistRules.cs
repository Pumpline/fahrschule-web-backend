namespace Fahrschule.Application.Students;

/// <summary>
/// Pure rules for the per-student document checklist - no database, testable.
/// </summary>
public static class DocumentChecklistRules
{
    /// <summary>
    /// Is the document expiring soon (or already expired)? True when an expiry
    /// date exists and is within <paramref name="reminderDays"/> from
    /// <paramref name="today"/> (KONZEPT 3.1: automatic highlight / "Bald fällig").
    /// </summary>
    public static bool IsExpiringSoon(DateOnly? expiresOn, DateOnly today, int reminderDays)
    {
        if (expiresOn is null) return false;
        var thresholdDay = today.AddDays(reminderDays);
        return expiresOn.Value <= thresholdDay;
    }

    /// <summary>Days until expiry (negative = already expired); null if no date.</summary>
    public static int? DaysUntilExpiry(DateOnly? expiresOn, DateOnly today)
        => expiresOn is null ? null : expiresOn.Value.DayNumber - today.DayNumber;

    /// <summary>
    /// May the document be marked as present? If the catalogue item requires an
    /// expiry date, it can only be ticked "present" once a date is entered
    /// (KONZEPT 3.1: Ablaufdatum-Pflicht). Returns the error message, or null.
    /// </summary>
    public static string? CheckCanBePresent(bool isPresent, bool expiryDateRequired, DateOnly? expiresOn)
    {
        if (isPresent && expiryDateRequired && expiresOn is null)
        {
            return "Für diese Unterlage muss zuerst das Ablaufdatum eingetragen werden, " +
                   "bevor sie als „liegt vor“ markiert werden kann.";
        }
        return null;
    }
}
