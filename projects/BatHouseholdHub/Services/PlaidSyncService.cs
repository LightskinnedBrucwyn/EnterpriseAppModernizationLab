using Going.Plaid;
using Going.Plaid.Transactions;
using BatHouseholdHub.Models;

namespace BatHouseholdHub.Services;

/// <summary>Pulls new bank transactions for every connected Plaid item every 6 hours,
/// using the incremental /transactions/sync cursor so each run only fetches what's new.
/// Mirrors the manual CSV import: same Transaction shape, same dedup/reconcile pass.</summary>
public class PlaidSyncService(IServiceScopeFactory scopeFactory, PlaidClient client, IConfiguration config, ILogger<PlaidSyncService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        do
        {
            try { await SyncAllAsync(); }
            catch (Exception ex) { logger.LogError(ex, "Failed to sync Plaid transactions"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SyncAllAsync()
    {
        if (string.IsNullOrWhiteSpace(config["PLAID_CLIENT_ID"]) || string.IsNullOrWhiteSpace(config["PLAID_SECRET"])) return;

        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<HouseholdStore>();

        foreach (var item in store.Data.PlaidItems.ToList())
        {
            try { await SyncItemAsync(store, item); }
            catch (Exception ex) { logger.LogError(ex, "Failed to sync Plaid item {Institution}", item.InstitutionName); }
        }
    }

    private async Task SyncItemAsync(HouseholdStore store, PlaidItem item)
    {
        string? cursor = item.SyncCursor;
        var parsed = new List<Transaction>();
        bool hasMore;
        do
        {
            var response = await client.TransactionsSyncAsync(new TransactionsSyncRequest { AccessToken = item.AccessToken, Cursor = cursor });
            if (response.Error is not null) throw new InvalidOperationException(response.Error.ErrorMessage);

            foreach (var t in response.Added)
            {
                var amount = t.Amount ?? 0m;
                parsed.Add(new Transaction
                {
                    Date = (t.Date ?? DateOnly.FromDateTime(DateTime.Today)).ToDateTime(TimeOnly.MinValue),
                    Description = t.MerchantName ?? "Bank transaction",
                    Category = t.PersonalFinanceCategory?.Primary ?? "Other",
                    Owner = item.Owner,
                    Amount = Math.Abs(amount),
                    IsIncome = amount < 0,
                    Account = t.AccountId ?? "",
                    Institution = item.InstitutionName,
                    Source = "Plaid",
                    SourceKey = $"plaid:{t.TransactionId}",
                    MoneyType = amount < 0 ? MoneyType.Income : MoneyType.Expense
                });
            }

            cursor = response.NextCursor;
            hasMore = response.HasMore;
        }
        while (hasMore);

        if (parsed.Count > 0) await store.ImportPlaidTransactionsAsync(parsed);
        await store.SetPlaidSyncCursorAsync(item.Id, cursor);
    }
}
