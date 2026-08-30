using Fahrschule.Application.Audit;
using Fahrschule.Application.Auth;
using Fahrschule.Application.Curriculum;
using Fahrschule.Application.Documents;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Application.Settings;
using Fahrschule.Application.Students;
using Fahrschule.Application.Users;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Fahrschule.Application;

/// <summary>Registers the business logic services in the DI container.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        // Binds the "Jwt" section from appsettings to the JwtOptions class
        // ("options pattern" - typed configuration instead of loose strings).
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        // VAPID keys for Web Push (KONZEPT 3.5); absent → push is simply off.
        services.Configure<Push.WebPushOptions>(configuration.GetSection(Push.WebPushOptions.SectionName));

        // "Scoped" = one instance per HTTP request (matches the DbContext).
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<ILicenseClassService, LicenseClassService>();
        services.AddScoped<ICurriculumItemService, CurriculumItemService>();
        services.AddScoped<IDocumentCatalogService, DocumentCatalogService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<IStudentService, StudentService>();
        services.AddScoped<IStudentDocumentService, StudentDocumentService>();
        services.AddScoped<IStudentProgressService, StudentProgressService>();
        services.AddScoped<ILessonService, LessonService>();
        services.AddScoped<IExamService, ExamService>();
        services.AddScoped<Calendar.ICalendarService, Calendar.CalendarService>();
        services.AddScoped<Payments.IPaymentService, Payments.PaymentService>();
        services.AddScoped<Pdf.ITrainingRecordPdfService, Pdf.TrainingRecordPdfService>();
        services.AddScoped<Pdf.IReceiptPdfService, Pdf.ReceiptPdfService>();
        services.AddScoped<IAuditQueryService, AuditQueryService>();
        services.AddScoped<IAuditVisibilityService, AuditVisibilityService>();
        services.AddScoped<Admin.IStudentExportService, Admin.StudentExportService>();
        services.AddScoped<Dashboard.IDashboardService, Dashboard.DashboardService>();
        services.AddScoped<Retention.IRetentionService, Retention.RetentionService>();
        services.AddScoped<Theory.ITheoryAttendanceService, Theory.TheoryAttendanceService>();
        services.AddScoped<Push.IPushService, Push.PushService>();
        services.AddScoped<Push.IAppointmentReminderService, Push.AppointmentReminderService>();

        // "Singleton" = one instance for the whole runtime (stateless, config only).
        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        // The login throttle keeps per-IP state in memory, so it MUST be a
        // singleton (one shared instance) - a scoped instance would forget the
        // failed attempts after every request. TimeProvider.System is the real
        // clock; the tests inject a fake one to fast-forward time.
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ILoginThrottle, LoginThrottle>();

        return services;
    }
}
