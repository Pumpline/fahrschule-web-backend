using Fahrschule.Application.Curriculum;

namespace Fahrschule.Tests.Curriculum;

/// <summary>Tests für die Versionierungs-Regeln des Ausbildungsplans (KONZEPT 3.3a).</summary>
public class CurriculumRulesTests
{
    private static readonly Guid KlasseB = Guid.NewGuid();
    private static readonly Guid KlasseA1 = Guid.NewGuid();

    [Fact]
    public void Geaenderte_Bezeichnung_braucht_neue_Version()
    {
        Assert.True(CurriculumRules.NeedsNewVersion(
            "Vorfahrt", "Vorfahrt und Verkehrsregelungen", null, null, [], []));
    }

    [Fact]
    public void Geaenderte_Soll_Anzahl_braucht_neue_Version()
    {
        // z. B. Überlandfahrten von 5 auf 4 gesenkt → neue Rechtslage = neue Version.
        Assert.True(CurriculumRules.NeedsNewVersion("Überlandfahrt", "Überlandfahrt", 5, 4, [], []));
    }

    [Fact]
    public void Geaenderte_Klassen_Zuordnung_braucht_neue_Version()
    {
        Assert.True(CurriculumRules.NeedsNewVersion(
            "Thema", "Thema", null, null, [KlasseB], [KlasseB, KlasseA1]));
    }

    [Fact]
    public void Gleiche_Klassen_in_anderer_Reihenfolge_brauchen_KEINE_neue_Version()
    {
        // Mengen-Vergleich: [B, A1] und [A1, B] sind dieselbe Zuordnung.
        Assert.False(CurriculumRules.NeedsNewVersion(
            "Thema", "Thema", null, null, [KlasseB, KlasseA1], [KlasseA1, KlasseB]));
    }

    [Fact]
    public void Nur_aktiv_oder_Reihenfolge_braucht_KEINE_neue_Version()
    {
        // aktiv/Reihenfolge werden gar nicht erst verglichen – inhaltsgleich = keine Version.
        Assert.False(CurriculumRules.NeedsNewVersion("Thema", "Thema", 3, 3, [KlasseB], [KlasseB]));
    }

    [Theory]
    [InlineData("", true)]      // leer → Fehler
    [InlineData("Vorfahrt", false)]
    public void Bezeichnung_wird_geprueft(string titel, bool fehlerErwartet)
    {
        var fehler = CurriculumRules.Validate(titel, requiredCount: null);
        Assert.Equal(fehlerErwartet, fehler.Count > 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void Unplausible_Soll_Anzahl_wird_abgelehnt(int anzahl)
    {
        var fehler = CurriculumRules.Validate("Überlandfahrt", anzahl);
        Assert.Single(fehler);
        Assert.Contains("Soll-Anzahl", fehler[0]);
    }
}
