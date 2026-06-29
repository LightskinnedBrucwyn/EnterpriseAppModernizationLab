namespace BatHouseholdHub.Services;

/// <summary>Takes a daily backup of household.json so a bad edit or disk problem doesn't
/// mean losing the household's only copy of its data.</summary>
public class HouseholdBackupService(HouseholdStore store, ILogger<HouseholdBackupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        do
        {
            try { await store.BackupIfNeededAsync(); }
            catch (Exception ex) { logger.LogError(ex, "Failed to back up household.json"); }
        }
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken));
    }
}
