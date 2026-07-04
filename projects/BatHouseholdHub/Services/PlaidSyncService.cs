using Going.Plaid;
using Going.Plaid.Accounts;
using Going.Plaid.Transactions;
using BatHouseholdHub.Models;

namespace BatHouseholdHub.Services;

/// <summary>Does one full Plaid sync pass: live account balances first (they feed the
/// Available Funds math), then incremental transactions via the /transactions/sync cursor.
/// Scoped so both the 6-hour background timer and the Banking page's "Sync now" button run
/// the exact same code.</summary>
public class PlaidSyncRunner(PlaidClient client, IConfiguration config, HouseholdStore store, ILogger<PlaidSyncRunner> logger)
{
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(config["PLAID_CLIENT_ID"]) && !string.IsNullOrWhiteSpace(config["PLAID_SECRET"]);

    public async Task SyncAllAsync()
    {
        if (!IsConfigured) return;

        foreach (var item in store.Data.PlaidItems.ToList())
        {
            try { await SyncItemAsync(item); }
            catch (Exception ex) { logger.LogError(ex, "Failed to sync Plaid item {Institution}", item.InstitutionName); }
        }
    }

    private async Task SyncItemAsync(PlaidItem item)
    {
        var accountNames = await SyncBalancesAsync(item);

        Transaction Map(Going.Plaid.Entity.Transaction t)
        {
            var amount = t.Amount ?? 0m;
            return new Transaction
            {
                Date = (t.Date ?? DateOnly.FromDateTime(DateTime.Today)).ToDateTime(TimeOnly.MinValue),
                Description = t.MerchantName ?? "Bank transaction",
                Category = t.PersonalFinanceCategory?.Primary ?? "Other",
                Owner = item.Owner,
                Amount = Math.Abs(amount),
                IsIncome = amount < 0,
                Account = t.AccountId is { } id && accountNames.TryGetValue(id, out var name) ? name : t.AccountId ?? "",
                Institution = item.InstitutionName,
                Source = "Plaid",
                SourceKey = $"plaid:{t.TransactionId}",
                MoneyType = amount < 0 ? MoneyType.Income : MoneyType.Expense
            };
        }

        string? cursor = item.SyncCursor;
        var added = new List<Transaction>();
        var modified = new List<Transaction>();
        var removedKeys = new List<string>();
        bool hasMore;
        do
        {
            var response = await client.TransactionsSyncAsync(new TransactionsSyncRequest { AccessToken = item.AccessToken, Cursor = cursor });
            if (response.Error is not null) throw new InvalidOperationException(response.Error.ErrorMessage);

            // A pending charge that later settles arrives as Modified (new amount/merchant);
            // a dropped pending charge arrives as Removed. Applying only Added would leave
            // stale or duplicate rows once the cursor moved past those events.
            added.AddRange(response.Added.Select(Map));
            modified.AddRange(response.Modified.Select(Map));
            removedKeys.AddRange(response.Removed.Select(r => $"plaid:{r.TransactionId}"));

            cursor = response.NextCursor;
            hasMore = response.HasMore;
        }
        while (hasMore);

        await store.ApplyPlaidSyncAsync(added, modified, removedKeys);
        await store.SetPlaidSyncCursorAsync(item.Id, cursor);
    }

    /// <summary>Fetches live balances for the item's accounts, persists them, and returns an
    /// AccountId → display-name map so transactions carry a readable account name instead of
    /// Plaid's opaque id string.</summary>
    private async Task<Dictionary<string, string>> SyncBalancesAsync(PlaidItem item)
    {
        var names = new Dictionary<string, string>();
        var response = await client.AccountsBalanceGetAsync(new AccountsBalanceGetRequest { AccessToken = item.AccessToken });
        if (response.Error is not null)
        {
            logger.LogWarning("Balance fetch failed for {Institution}: {Error}", item.InstitutionName, response.Error.ErrorMessage);
            return names;
        }

        var fetched = new List<PlaidAccount>();
        foreach (var account in response.Accounts)
        {
            var record = new PlaidAccount
            {
                AccountId = account.AccountId,
                Name = account.Name,
                Mask = account.Mask ?? "",
                Type = account.Type.ToString().ToLowerInvariant(),
                Available = account.Balances?.Available,
                Current = account.Balances?.Current,
                LastUpdated = DateTime.Now
            };
            fetched.Add(record);
            names[record.AccountId] = record.DisplayName;
        }
        await store.UpsertPlaidAccountsAsync(item.ItemId, item.Owner, fetched);
        return names;
    }
}

/// <summary>Runs a full Plaid sync at startup and every 6 hours after.</summary>
public class PlaidSyncService(IServiceScopeFactory scopeFactory, ILogger<PlaidSyncService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<PlaidSyncRunner>().SyncAllAsync();
            }
            catch (Exception ex) { logger.LogError(ex, "Failed to sync Plaid data"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
