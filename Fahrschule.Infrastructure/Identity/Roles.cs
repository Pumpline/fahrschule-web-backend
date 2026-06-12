namespace Fahrschule.Infrastructure.Identity;

/// <summary>
/// The three roles of the application (see CLAUDE.md, principle 4).
///
/// Defined as constants so typos in role checks are caught at compile
/// time - not at runtime. The VALUES are German on purpose: the owner
/// sees role names in the admin panel.
/// </summary>
public static class Roles
{
    /// <summary>Owner: full access, including admin panel and GDPR functions.</summary>
    public const string Admin = "Admin";

    /// <summary>Driving instructor: lesson entry, progress, appointments - receives appointment push.</summary>
    public const string Fahrlehrer = "Fahrlehrer";

    /// <summary>Office staff: sees/operates everything like the instructor, but WITHOUT push notifications.</summary>
    public const string Verwaltung = "Verwaltung";

    public static readonly string[] All = [Admin, Fahrlehrer, Verwaltung];
}
