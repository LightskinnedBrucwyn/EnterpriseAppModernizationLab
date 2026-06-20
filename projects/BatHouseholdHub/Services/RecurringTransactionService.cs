namespace BatHouseholdHub.Services;

/// <summary>Keeps recurring transactions posting on schedule for an app that runs continuously
/// across month boundaries, instead of only checking once at startup.</summary>
public class RecurringTransactionService(HouseholdStore store, ILogger<RecurringTransactionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        do
        {
            try { await store.CheckRecurringAsync(); }
            catch (Exception ex) { logger.LogError(ex, "Failed to process recurring transactions"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
