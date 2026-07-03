namespace BatHouseholdHub.Models;

public class HouseholdData
{
    public List<Transaction> Transactions { get; set; } = [];
    public List<Bill> Bills { get; set; } = [];
    public List<Recipe> Recipes { get; set; } = [];
    public List<GroceryItem> Groceries { get; set; } = [];
    /// <summary>Names of everything ever added to the grocery list (capped), so the app can
    /// offer one-tap re-adds of frequently bought items.</summary>
    public List<string> GroceryHistory { get; set; } = [];
    public List<MealPlan> MealPlans { get; set; } = [];
    public List<SavingsGoal> SavingsGoals { get; set; } = [];
    public List<RecurringTransaction> RecurringTransactions { get; set; } = [];
    public List<PurchaseIdea> PurchaseIdeas { get; set; } = [];
    public List<ShoppingSite> ShoppingSites { get; set; } = [];
    public List<UploadedFile> UploadedFiles { get; set; } = [];
    public List<DebtAccount> DebtAccounts { get; set; } = [];
    public List<IncomeEvent> IncomeEvents { get; set; } = [];
    public List<QuickLogEntry> QuickLogs { get; set; } = [];
    public List<PushSubscriptionRecord> PushSubscriptions { get; set; } = [];
    public List<string> NotifiedBillKeys { get; set; } = [];
    public List<PlaidItem> PlaidItems { get; set; } = [];
    public List<PlaidAccount> PlaidAccounts { get; set; } = [];
    public HouseholdFunds Funds { get; set; } = new();
    public HomeButlerSettings HomeButler { get; set; } = new();
    public List<PersonProfile> Profiles { get; set; } = [];
}

/// <summary>A household member's profile lock. When a PIN is set, switching the app to this
/// person (and seeing their private transaction details) requires entering it once per
/// browser session. Stored as PBKDF2 hash + salt — the PIN itself is never persisted.</summary>
public class PersonProfile
{
    public string Name { get; set; } = "";
    public string PinHash { get; set; } = "";
    public string Salt { get; set; } = "";
}

/// <summary>A linked bank connection via Plaid — one per institution a household member
/// connects through Link. The access token is the long-lived credential Plaid issues for
/// pulling that institution's transactions; it lives only in household.json on the home
/// server, never in git.</summary>
public class PlaidItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ItemId { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string InstitutionName { get; set; } = "";
    public string Owner { get; set; } = "Shared";
    public string? SyncCursor { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? LastSyncedAt { get; set; }
}

/// <summary>One bank account under a linked Plaid item, refreshed on every sync. Balances
/// feed the Available Funds math when IncludeInFunds is on (defaulted for checking/savings,
/// off for credit cards and loans, whose "balance" is debt, not spendable money).</summary>
public class PlaidAccount
{
    public string AccountId { get; set; } = "";
    public string ItemId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Mask { get; set; } = "";
    public string Type { get; set; } = "";
    public string Owner { get; set; } = "Shared";
    public bool IncludeInFunds { get; set; }
    public decimal? Available { get; set; }
    public decimal? Current { get; set; }
    public DateTime LastUpdated { get; set; }

    public decimal SpendableBalance => Available ?? Current ?? 0m;
    public string DisplayName => string.IsNullOrWhiteSpace(Mask) ? Name : $"{Name} ••{Mask}";
}

/// <summary>A browser's Web Push subscription (endpoint + encryption keys), captured once
/// the household enables notifications, so the server can wake the phone for bills due soon
/// without anyone having the app open.</summary>
public class PushSubscriptionRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Endpoint { get; set; } = "";
    public string P256dh { get; set; } = "";
    public string Auth { get; set; } = "";
    public string Owner { get; set; } = "Shared";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>A quick, timestamped note jotted from anywhere in the app — for things worth
/// remembering that don't fit a bill, transaction, or grocery item (e.g. "called Affirm
/// about due date" or "Jess picking up the package Thursday").</summary>
public class QuickLogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Text { get; set; } = "";
    public string Owner { get; set; } = "Shared";
    public DateTime LoggedAt { get; set; } = DateTime.Now;
}

/// <summary>Connection settings for the local LLM ("Home Butler") used as a free,
/// no-token fallback for tasks like shopping tracker product lookup once Open Graph scraping
/// comes up empty. Points at an Ollama-compatible server on the household's own network.</summary>
public class HomeButlerSettings
{
    public string BaseUrl { get; set; } = "";
    public string Model { get; set; } = "qwen2.5";
}

public class UploadedFile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public string Note { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}

public class Transaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Date { get; set; } = DateTime.Today;
    public string Description { get; set; } = "";
    public string Category { get; set; } = "Other";
    public string Owner { get; set; } = "Shared";
    public decimal Amount { get; set; }
    public bool IsIncome { get; set; }
    public Guid? RecurringRuleId { get; set; }
    public string Account { get; set; } = "";
    public string Institution { get; set; } = "";
    public string Source { get; set; } = "Manual";
    public string SourceKey { get; set; } = "";
    public MoneyType MoneyType { get; set; } = MoneyType.Expense;
    /// <summary>Set when an import reconciliation pass matches this transaction to a planned
    /// bill (and marks that bill Paid). Left null when the transaction couldn't be matched,
    /// in which case NeedsReview is set instead.</summary>
    public Guid? MatchedBillId { get; set; }
    public bool NeedsReview { get; set; }
}

public class RocketImportResult
{
    public int TotalRows { get; set; }
    public int Imported { get; set; }
    public int SkippedDuplicates { get; set; }
    public int ExcludedTransfers { get; set; }
    public int InvalidRows { get; set; }
    public int AccountCount { get; set; }
    public DateTime? EarliestDate { get; set; }
    public DateTime? LatestDate { get; set; }
}

public class RecurringTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Description { get; set; } = "";
    public string Category { get; set; } = "Bills";
    public string Owner { get; set; } = "Shared";
    public decimal Amount { get; set; }
    public bool IsIncome { get; set; }
    public int DayOfMonth { get; set; } = 1;
    public bool IsActive { get; set; } = true;
}

public enum BillCategory { DebtPayment, FixedBill, TransferSavings }
public enum BillFrequency { Monthly, Weekly, Biweekly, Quarterly, Yearly }
// Overdue is computed-only (never stored in ManualStatus) and MUST stay last: statuses
// persist as numbers in household.json, so reordering would corrupt existing saves.
public enum BillStatus { Upcoming, Pending, Paid, Reserved, Delayed, NeedsReview, Skipped, Overdue }
public enum MoneyType { Cash, Income, Expense, Transfer, RentReserve, DebtPayment, SavingsMove }

public class Bill
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public BillCategory Category { get; set; } = BillCategory.FixedBill;
    public decimal Amount { get; set; }
    public int DueDay { get; set; } = 1;
    public BillFrequency Frequency { get; set; } = BillFrequency.Monthly;
    public string Owner { get; set; } = "Shared";
    public string Notes { get; set; } = "";
    public bool AutoPay { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastPaidDate { get; set; }
    public BillStatus ManualStatus { get; set; } = BillStatus.Upcoming;
    /// <summary>When ManualStatus is Delayed, the bill waits on this income event instead of
    /// a due date — e.g. "Pay Ahmad $600 after Vista final check arrives."</summary>
    public Guid? LinkedIncomeEventId { get; set; }
    /// <summary>Set when a matched bank transaction updates Amount to what was actually
    /// charged: how much it moved (new − old) and when, so the UI can flag price changes.</summary>
    public decimal? LastAmountDelta { get; set; }
    public DateTime? AmountUpdatedAt { get; set; }

    /// <summary>What this bill costs per month regardless of billing cadence, so totals
    /// don't undercount a biweekly bill or overcount a yearly one.</summary>
    public decimal MonthlyEquivalent => Frequency switch
    {
        BillFrequency.Weekly => Amount * 52m / 12m,
        BillFrequency.Biweekly => Amount * 26m / 12m,
        BillFrequency.Quarterly => Amount / 3m,
        BillFrequency.Yearly => Amount / 12m,
        _ => Amount
    };

    // Cycle math lives in Services.BillSchedule — one implementation shared by cashflow,
    // the calendar, and notifications. These stay as instance methods so call sites read well.
    public bool IsPaidThisCycle(DateTime asOf) => Services.BillSchedule.IsPaidThisCycle(this, asOf);
    public BillStatus EffectiveStatus(DateTime asOf) => Services.BillSchedule.EffectiveStatus(this, asOf);
}

public class DebtAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public decimal Balance { get; set; }
    public decimal MinimumPayment { get; set; }
    public decimal InterestRate { get; set; }
    public int DueDay { get; set; } = 1;
    public string Owner { get; set; } = "Shared";
    public string Notes { get; set; } = "";
    public Guid? LinkedBillId { get; set; }
}

public enum IncomeStatus { Received, Pending, Estimated }

public class IncomeEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Source { get; set; } = "";
    public string Owner { get; set; } = "Shared";
    public decimal NetAmount { get; set; }
    public DateTime PayPeriodStart { get; set; } = DateTime.Today;
    public DateTime PayPeriodEnd { get; set; } = DateTime.Today;
    public DateTime ExpectedDate { get; set; } = DateTime.Today;
    public IncomeStatus Status { get; set; } = IncomeStatus.Estimated;
}

public class HouseholdFunds
{
    public decimal Trey { get; set; }
    public decimal Jess { get; set; }
    public decimal Shared { get; set; }
    public decimal Buffer { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.Today;
    public decimal Total => Trey + Jess + Shared;
}

public class Recipe
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Category { get; set; } = "Dinner";
    public int Minutes { get; set; } = 30;
    public string Ingredients { get; set; } = "";
    public string Instructions { get; set; } = "";
}

public class GroceryItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Section { get; set; } = "Other";
    public bool IsChecked { get; set; }
}

public class MealPlan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Date { get; set; } = DateTime.Today;
    public string Meal { get; set; } = "";
}

public class SavingsGoal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public decimal Current { get; set; }
    public decimal Target { get; set; }
}

public class PurchaseIdea
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Category { get; set; } = "Tech";
    public string Owner { get; set; } = "Shared";
    public string Priority { get; set; } = "Someday";
    public decimal Price { get; set; }
    public decimal Saved { get; set; }
    public string ProductUrl { get; set; } = "";
    public string ImageUrl { get; set; } = "";
    public string Notes { get; set; } = "";
    public List<WishContribution> Contributions { get; set; } = [];
}

public class WishContribution
{
    public decimal Amount { get; set; }
    public DateTime Date { get; set; } = DateTime.Today;
}

/// <summary>A quick-link shortcut to a favorite shopping site, shown as a tab on the Home page.</summary>
public class ShoppingSite
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string Owner { get; set; } = "Jess";
}

public static class BillCategoryExtensions
{
    public static string Label(this BillCategory category) => category switch
    {
        BillCategory.DebtPayment => "Debt Payment",
        BillCategory.TransferSavings => "Transfer / Savings",
        _ => "Fixed Bill"
    };

    public static string CssClass(this BillCategory category) => category switch
    {
        BillCategory.DebtPayment => "debt",
        BillCategory.TransferSavings => "transfer",
        _ => "fixed"
    };
}

public static class BillStatusExtensions
{
    public static string Label(this BillStatus status) => status switch
    {
        BillStatus.Pending => "Processing",
        BillStatus.Paid => "Paid",
        BillStatus.Reserved => "Set aside",
        BillStatus.Delayed => "Waiting on income",
        BillStatus.NeedsReview => "Needs review",
        BillStatus.Skipped => "Skipped",
        BillStatus.Overdue => "Overdue",
        _ => "Upcoming"
    };

    public static string CssClass(this BillStatus status) => status switch
    {
        BillStatus.Pending => "status-pending",
        BillStatus.Paid => "status-paid",
        BillStatus.Reserved => "status-reserved",
        BillStatus.Delayed => "status-delayed",
        BillStatus.NeedsReview => "status-review",
        BillStatus.Skipped => "status-skipped",
        BillStatus.Overdue => "status-overdue",
        _ => "status-upcoming"
    };
}

public static class BillFrequencyExtensions
{
    public static string Label(this BillFrequency frequency) => frequency switch
    {
        BillFrequency.Weekly => "weekly",
        BillFrequency.Biweekly => "every 2 weeks",
        BillFrequency.Quarterly => "quarterly",
        BillFrequency.Yearly => "yearly",
        _ => "monthly"
    };
}
