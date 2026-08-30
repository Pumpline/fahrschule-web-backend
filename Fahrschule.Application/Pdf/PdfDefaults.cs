using QuestPDF.Infrastructure;

namespace Fahrschule.Application.Pdf;

/// <summary>
/// One place for the settings that every generated document shares.
///
/// Why this exists: without it, QuestPDF resolves fonts from the MACHINE it
/// runs on (<c>UseEnvironmentFonts</c> defaults to true). The same document
/// then looks different on the office PC and inside the Linux container - and
/// the container image installs no font packages at all, so a missing weight
/// would silently be substituted or synthesised. Small marks suffer first:
/// the dots on ä/ö/ü, the ß, the € sign.
///
/// Therefore: environment fonts OFF, and the font family named explicitly.
/// QuestPDF ships the complete Lato family (Regular to Black, each with an
/// italic) inside the package, so this works everywhere without installing
/// anything - and every receipt looks the same as the one printed last year.
/// </summary>
public static class PdfDefaults
{
    /// <summary>The font used by all documents (bundled with QuestPDF).</summary>
    public const string FontFamily = "Lato";

    private static bool _applied;
    private static readonly Lock Gate = new();

    /// <summary>Applied once per process, from the PDF services' static constructors.</summary>
    public static void Apply()
    {
        lock (Gate)
        {
            if (_applied) return;

            // Free for companies below the revenue threshold (KONZEPT 7).
            QuestPDF.Settings.License = LicenseType.Community;

            // Only fonts that QuestPDF brings along - never the ones that happen
            // to be installed on the machine. Makes the output reproducible.
            QuestPDF.Settings.UseEnvironmentFonts = false;

            _applied = true;
        }
    }
}
