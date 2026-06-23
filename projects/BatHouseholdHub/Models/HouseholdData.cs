namespace BatHouseholdHub.Models;

public class HouseholdData
{
    public List<Transaction> Transactions { get; set; } = [];
    public List<Bill> Bills { get; set; } = [];
    public List<Recipe> Recipes { get; set; } = [];
    public List<GroceryItem> Groceries { get; set; } = [];
    public List<MealPlan> MealPlans { get; set; } = [];
    public List<SavingsGoal> SavingsGoals { get; set; } = [];
    public List<RecurringTransaction> RecurringTransactions { get; set; } = [];
    public List<PurchaseIdea> PurchaseIdeas { get; set; } = [];
    public List<UploadedFile> UploadedFiles { get; set; } = [];
    public List<DebtAccount> DebtAccounts { get; set; } = [];
    public List<IncomeEvent> IncomeEvents { get; set; } = [];
    public HouseholdFunds Funds { get; set; } = new();
    public HomeButlerSettings HomeButler { get; set; } = new();
}

/// <summary>Connection settings for the local LLM ("Home Butler") used as a free,
/// no-token fallback for tasks like wishboard product lookup once Open Graph scraping
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
public enum BillPriority { Critical, Debt, Subscription, Optional }
public enum BillStatus { Upcoming, Pending, Paid, NeedsReview }

public class Bill
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public BillCategory Category { get; set; } = BillCategory.FixedBill;
    public decimal Amount { get; set; }
    public decimal OriginalLoanAmount { get; set; }
    public int DueDay { get; set; } = 1;
    public BillFrequency Frequency { get; set; } = BillFrequency.Monthly;
    public BillPriority Priority { get; set; } = BillPriority.Subscription;
    public string Owner { get; set; } = "Shared";
    public string Notes { get; set; } = "";
    public bool AutoPay { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastPaidDate { get; set; }
    public BillStatus ManualStatus { get; set; } = BillStatus.Upcoming;

    public bool IsPaidThisCycle(DateTime asOf) => LastPaidDate is { } d && d.Year == asOf.Year && d.Month == asOf.Month;
    public BillStatus EffectiveStatus(DateTime asOf) => IsPaidThisCycle(asOf) ? BillStatus.Paid : ManualStatus;
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
        BillStatus.Pending => "Pending",
        BillStatus.Paid => "Paid",
        BillStatus.NeedsReview => "Needs review",
        _ => "Upcoming"
    };

    public static string CssClass(this BillStatus status) => status switch
    {
        BillStatus.Pending => "status-pending",
        BillStatus.Paid => "status-paid",
        BillStatus.NeedsReview => "status-review",
        _ => "status-upcoming"
    };
}

public static class BillPriorityExtensions
{
    public static string Label(this BillPriority priority) => priority switch
    {
        BillPriority.Critical => "Critical",
        BillPriority.Debt => "Debt",
        BillPriority.Optional => "Optional",
        _ => "Subscription"
    };

    public static string CssClass(this BillPriority priority) => priority switch
    {
        BillPriority.Critical => "priority-critical",
        BillPriority.Debt => "priority-debt",
        BillPriority.Optional => "priority-optional",
        _ => "priority-subscription"
    };
}
