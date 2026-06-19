using Fahrschule.Application.Push;

namespace Fahrschule.Api.BackgroundJobs;

/// <summary>
/// Sends appointment reminders (KONZEPT 3.5): every minute it looks for
/// appointments that start within the configured lead time and pushes a sparse
/// reminder to the Fahrlehrer's devices, marking each as reminded so it is never
/// pushed twice. Same BackgroundService pattern as the retention job: it runs
/// outside any HTTP request and opens a fresh DI scope per tick for the scoped
/// services (DbContext etc.).
/// </summary>
public class ReminderBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<ReminderBackgroundService> logger) : BackgroundService
{
    // Once a minute: reminders are time-sensitive (a "30 minutes before" needs
    // minute precision), but more often would be wasteful.
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    // Small delay after startup so we don't compete with migration / seeding.
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return; // shutting down before the first run
        }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            await RunOnceAsync(stoppingToken);
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var reminders = scope.ServiceProvider.GetRequiredService<IAppointmentReminderService>();
            await reminders.RunAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal during shutdown.
        }
        catch (Exception ex)
        {
            // A failed run must never take the app down; log and retry next tick.
            logger.LogError(ex, "Termin-Erinnerungs-Job fehlgeschlagen.");
        }
    }

    private static async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
