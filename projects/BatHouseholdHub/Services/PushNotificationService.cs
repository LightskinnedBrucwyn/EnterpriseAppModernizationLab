using WebPush;
using BatHouseholdHub.Models;

namespace BatHouseholdHub.Services;

/// <summary>Checks unpaid bills once a day and pushes a phone notification for anything due
/// within 2 days, so a due date doesn't sneak up on someone who hasn't opened the app.
/// Self-contained: no third-party bank credentials, just the browser Push API + VAPID keys.</summary>
public class PushNotificationService(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<PushNotificationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var publicKey = config["VAPID_PUBLIC_KEY"];
        var privateKey = config["VAPID_PRIVATE_KEY"];
        if (string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(privateKey))
        {
            logger.LogWarning("VAPID keys not configured; push notifications disabled");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(12));
        do
        {
            try { await CheckAndNotifyAsync(publicKey, privateKey); }
            catch (Exception ex) { logger.LogError(ex, "Failed to run push notification check"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task CheckAndNotifyAsync(string publicKey, string privateKey)
    {
        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<HouseholdStore>();
        if (store.Data.PushSubscriptions.Count == 0) return;

        var today = DateTime.Today;
        var horizon = today.AddDays(2);
        var dueSoon = store.Data.Bills.Where(b => b.IsActive && b.EffectiveStatus(today) != BillStatus.Paid).Select(b =>
        {
            var dueDay = Math.Min(b.DueDay, DateTime.DaysInMonth(today.Year, today.Month));
            var dueDate = new DateTime(today.Year, today.Month, dueDay);
            if (dueDate < today) dueDate = dueDate.AddMonths(1);
            return (Bill: b, DueDate: dueDate);
        }).Where(x => x.DueDate <= horizon).ToList();

        var vapid = new VapidDetails("mailto:household-hub@local", publicKey, privateKey);
        var client = new WebPushClient();

        foreach (var (bill, dueDate) in dueSoon)
        {
            var key = $"{bill.Id}:{dueDate:yyyyMMdd}";
            if (store.Data.NotifiedBillKeys.Contains(key)) continue;

            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                title = dueDate.Date == today ? $"{bill.Name} is due today" : $"{bill.Name} due {dueDate:MMM d}",
                body = $"{bill.Amount:C0} · {bill.Category.Label()}",
                url = "/bills"
            });

            foreach (var sub in store.Data.PushSubscriptions.ToList())
            {
                try
                {
                    var subscription = new PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth);
                    await client.SendNotificationAsync(subscription, payload, vapid);
                }
                catch (WebPushException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Gone or System.Net.HttpStatusCode.NotFound)
                {
                    await store.RemovePushSubscriptionAsync(sub.Endpoint);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to send push notification for {Bill}", bill.Name);
                }
            }

            await store.MarkBillNotifiedAsync(key);
        }
    }
}
