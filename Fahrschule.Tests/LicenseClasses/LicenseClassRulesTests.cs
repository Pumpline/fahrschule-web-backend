using Fahrschule.Application.LicenseClasses;

namespace Fahrschule.Tests.LicenseClasses;

/// <summary>Tests für die Fachregeln der Führerscheinklassen-Pflege.</summary>
public class LicenseClassRulesTests
{
    [Theory]
    [InlineData("  b ", "B")]
    [InlineData("b96", "B96")]
    [InlineData("A1", "A1")]
    [InlineData(null, "")]
    public void NormalizeCode_trimmt_und_macht_Grossbuchstaben(string? eingabe, string erwartet)
    {
        Assert.Equal(erwartet, LicenseClassRules.NormalizeCode(eingabe));
    }

    [Fact]
    public void Leeres_Kuerzel_wird_abgelehnt()
    {
        var fehler = LicenseClassRules.Validate("", minimumAge: null);

        Assert.Single(fehler);
        Assert.Contains("Kürzel", fehler[0]);
    }

    [Fact]
    public void Zu_langes_Kuerzel_wird_abgelehnt()
    {
        var fehler = LicenseClassRules.Validate(new string('X', LicenseClassRules.MaxCodeLength + 1), null);

        Assert.Single(fehler);
        Assert.Contains("höchstens", fehler[0]);
    }

    [Theory]
    [InlineData(9)]    // unter der Plausibilitätsgrenze (Tippfehler-Schutz)
    [InlineData(100)]  // darüber
    public void Unplausibles_Mindestalter_wird_abgelehnt(int alter)
    {
        var fehler = LicenseClassRules.Validate("B", alter);

        Assert.Single(fehler);
        Assert.Contains("Mindestalter", fehler[0]);
    }

    [Theory]
    [InlineData(null)] // kein Mindestalter ist erlaubt
    [InlineData(15)]
    [InlineData(18)]
    public void Gueltige_Eingaben_haben_keine_Fehler(int? alter)
    {
        Assert.Empty(LicenseClassRules.Validate("B", alter));
    }
}
