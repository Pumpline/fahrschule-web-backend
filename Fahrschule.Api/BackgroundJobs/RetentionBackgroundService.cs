using Fahrschule.Application.Retention;

namespace Fahrschule.Api.BackgroundJobs;

/// <summary>
/// Runs the retention job automatically once a day (KONZEPT rule 7: data marked
/// for deletion is removed permanently after the retention period).
///
/// Web concept "BackgroundService / IHostedService": a long-running task that
/// the ASP.NET host starts on boot and stops on shutdown - the closest thing to
/// a recurring "Update loop" in Unity terms, but ticking once per day instead of
/// once per frame. It runs OUTSIDE any HTTP request, so it has no user and no
/// request-scoped services of its own; it must open a fresh DI "scope" per run
/// to borrow the (scoped) DbContext and services - that is what the
/// IServiceScopeFactory below is for.
/// </summary>
public class RetentionBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<RetentionBackgroundService> logger) : BackgroundService
{
    // Once a day is plenty: the deadline is measured in days, so checking more
    // often would only add load without removing anything sooner.
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    // Small delay after startup so we don't compete with database migration /
    // seeding that also happens on boot.
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return; // app is shutting down before the first run
        }

        // PeriodicTimer gives a clean "wait one interval" loop that stops as soon
        // as the application is shutting down.
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
            // Open a fresh scope so we get a request-style DbContext for this run.
            using var scope = scopeFactory.CreateScope();
            var retention = scope.ServiceProvider.GetRequiredService<IRetentionService>();
            var result = await retention.RunAsync(actor: null, ct);

            if (result.DeletedCount > 0)
            {
                logger.LogInformation(
                    "Aufbewahrungs-Job: {Count} Schüler endgültig gelöscht.", result.DeletedCount);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal during shutdown - nothing to report.
        }
        catch (Exception ex)
        {
            // A failed run must never take the whole application down; log it and
            // try again on the next tick.
            logger.LogError(ex, "Aufbewahrungs-Job fehlgeschlagen.");
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
