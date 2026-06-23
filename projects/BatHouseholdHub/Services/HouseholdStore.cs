using System.Text.Json;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BatHouseholdHub.Models;
using Microsoft.VisualBasic.FileIO;

namespace BatHouseholdHub.Services;

public class HouseholdStore
{
    private readonly string _path;
    private readonly string _uploadsFolder;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ILogger<HouseholdStore> _logger;
    public HouseholdData Data { get; private set; }

    public HouseholdStore(IWebHostEnvironment environment, ILogger<HouseholdStore> logger)
    {
        _logger = logger;
        var folder = Path.Combine(environment.ContentRootPath, "App_Data");
        Directory.CreateDirectory(folder);
        _uploadsFolder = Path.Combine(folder, "uploads");
        Directory.CreateDirectory(_uploadsFolder);
        _path = Path.Combine(folder, "household.json");
        Data = Load();
        var changed = EnsureStarterRecipes();
        changed |= EnsureKnownBills();
        changed |= ProcessRecurringTransactions();
        if (changed)
            File.WriteAllText(_path, JsonSerializer.Serialize(Data, new JsonSerializerOptions { WriteIndented = true }));
    }

    private HouseholdData Load()
    {
        if (!File.Exists(_path)) return Seed();
        try
        {
            return JsonSerializer.Deserialize<HouseholdData>(File.ReadAllText(_path)) ?? Seed();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "household.json is corrupted; backing it up and starting fresh");
            var backupPath = $"{_path}.corrupt-{DateTime.Now:yyyyMMddHHmmss}.json";
            File.Copy(_path, backupPath, overwrite: true);
            return Seed();
        }
    }

    /// <summary>Re-checks recurring transactions; call periodically so bills due later in the
    /// month still post if the app keeps running across the month boundary.</summary>
    public async Task CheckRecurringAsync()
    {
        await _lock.WaitAsync();
        bool changed;
        try { changed = ProcessRecurringTransactions(); }
        finally { _lock.Release(); }
        if (changed) await SaveAsync();
    }

    public async Task<UploadedFile> SaveUploadAsync(Stream content, string fileName, string contentType, string note)
    {
        var record = new UploadedFile { FileName = fileName, ContentType = contentType, Note = note };
        await using var file = File.Create(UploadPath(record.Id));
        await content.CopyToAsync(file);
        record.SizeBytes = file.Length;
        Data.UploadedFiles.Add(record);
        await SaveAsync();
        return record;
    }

    public async Task DeleteUploadAsync(Guid id)
    {
        var record = Data.UploadedFiles.FirstOrDefault(x => x.Id == id);
        if (record is null) return;
        Data.UploadedFiles.Remove(record);
        File.Delete(UploadPath(id));
        await SaveAsync();
    }

    public string UploadPath(Guid id) => Path.Combine(_uploadsFolder, id.ToString("N"));

    public async Task SaveAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(Data, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_path, json);
        }
        finally { _lock.Release(); }
    }

    public async Task<RocketImportResult> ImportRocketCsvAsync(Stream stream, string owner, bool replaceTransactions)
    {
        var result = new RocketImportResult();
        var parsed = new List<Transaction>();
        var accounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var excludedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "Internal Transfers", "Credit Card Payment", "Savings Transfer", "Investment" };

        using var parser = new TextFieldParser(stream, Encoding.UTF8, true)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = true
        };
        parser.SetDelimiters(",");
        if (parser.EndOfData) return result;
        var headers = parser.ReadFields() ?? [];
        var columns = headers.Select((name, index) => (name, index))
            .ToDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);
        string Read(string[] fields, string name) => columns.TryGetValue(name, out var i) && i < fields.Length ? fields[i].Trim() : "";

        while (!parser.EndOfData)
        {
            string[] fields;
            try { fields = parser.ReadFields() ?? []; }
            catch (MalformedLineException) { result.InvalidRows++; continue; }
            result.TotalRows++;

            var category = Read(fields, "Category");
            if (excludedCategories.Contains(category) || !string.IsNullOrWhiteSpace(Read(fields, "Ignored From")))
            { result.ExcludedTransfers++; continue; }

            if (!DateTime.TryParseExact(Read(fields, "Date"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                || !decimal.TryParse(Read(fields, "Amount"), NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var signedAmount))
            { result.InvalidRows++; continue; }

            var accountName = Read(fields, "Account Name");
            var accountNumber = Read(fields, "Account Number");
            var account = string.IsNullOrWhiteSpace(accountNumber) ? accountName : $"{accountName} ••••{accountNumber}";
            var description = Read(fields, "Custom Name");
            if (string.IsNullOrWhiteSpace(description)) description = Read(fields, "Name");
            if (string.IsNullOrWhiteSpace(description)) description = Read(fields, "Description");
            var sourceKeyRaw = $"{date:yyyy-MM-dd}|{accountNumber}|{signedAmount.ToString(CultureInfo.InvariantCulture)}|{description}|{category}";
            var sourceKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceKeyRaw)));

            parsed.Add(new Transaction
            {
                Date = date,
                Description = description,
                Category = string.IsNullOrWhiteSpace(category) ? "Uncategorized" : category,
                Owner = owner,
                Amount = Math.Abs(signedAmount),
                IsIncome = signedAmount < 0,
                Account = account,
                Institution = Read(fields, "Institution Name"),
                Source = "Rocket Money",
                SourceKey = sourceKey
            });
            accounts.Add(account);
        }

        if (replaceTransactions)
        {
            Data.Transactions.Clear();
            Data.RecurringTransactions.RemoveAll(x => x.Description == "Internet" && x.Amount == 79.99m && x.DayOfMonth == 15);
        }
        var existing = Data.Transactions.Where(x => !string.IsNullOrWhiteSpace(x.SourceKey)).Select(x => x.SourceKey).ToHashSet();
        foreach (var transaction in parsed)
        {
            if (!existing.Add(transaction.SourceKey)) { result.SkippedDuplicates++; continue; }
            Data.Transactions.Add(transaction);
            result.Imported++;
        }
        result.AccountCount = accounts.Count;
        result.EarliestDate = parsed.Count == 0 ? null : parsed.Min(x => x.Date);
        result.LatestDate = parsed.Count == 0 ? null : parsed.Max(x => x.Date);
        await SaveAsync();
        return result;
    }

    /// <summary>The household's real-world bill list, organized the way Trey actually thinks
    /// about them. Reconciled by name on every startup so renaming/adding here updates existing
    /// installs without wiping amounts or due days the household has already entered.</summary>
    private static readonly (string Name, BillCategory Category)[] KnownBills =
    [
        ("Chase card manual payment", BillCategory.DebtPayment),
        ("Chase autopay", BillCategory.DebtPayment),
        ("Affirm", BillCategory.DebtPayment),
        ("Klover", BillCategory.DebtPayment),
        ("Dave", BillCategory.DebtPayment),
        ("Brigit", BillCategory.DebtPayment),
        ("Ally car", BillCategory.DebtPayment),
        ("SNAP Finance", BillCategory.DebtPayment),
        ("Upgrade", BillCategory.DebtPayment),
        ("Progressive", BillCategory.FixedBill),
        ("Verizon", BillCategory.FixedBill),
        ("Rocket Money", BillCategory.FixedBill),
        ("IdentityIQ", BillCategory.FixedBill),
        ("Experian", BillCategory.FixedBill),
        ("Uber One", BillCategory.FixedBill),
        ("HBO Max", BillCategory.FixedBill),
        ("Claude", BillCategory.FixedBill),
        ("Amazon Prime", BillCategory.FixedBill),
        ("City of Davenport", BillCategory.FixedBill),
        ("SoFi transfer", BillCategory.TransferSavings),
        ("Apple Cash transfers", BillCategory.TransferSavings),
        ("Zelle transfers", BillCategory.TransferSavings)
    ];

    private bool EnsureKnownBills()
    {
        var changed = false;
        foreach (var (name, category) in KnownBills)
        {
            if (Data.Bills.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
            Data.Bills.Add(new Bill { Name = name, Category = category });
            changed = true;
        }
        return changed;
    }

    /// <summary>Marks a bill paid for the current cycle and logs the real expense so income
    /// minus spending recalculates immediately on Money and Today instead of drifting out of sync.</summary>
    public async Task MarkBillPaidAsync(Guid id)
    {
        var bill = Data.Bills.FirstOrDefault(x => x.Id == id);
        if (bill is null) return;
        var today = DateTime.Today;
        bill.LastPaidDate = today;
        if (bill.Amount > 0)
        {
            Data.Transactions.Add(new Transaction
            {
                Date = today,
                Description = bill.Name,
                Category = bill.Category switch { BillCategory.DebtPayment => "Debt", BillCategory.TransferSavings => "Transfer", _ => "Bills" },
                Owner = "Shared",
                Amount = bill.Amount,
                IsIncome = false,
                Source = "Bill payment"
            });
        }
        await SaveAsync();
    }

    public async Task UnmarkBillPaidAsync(Guid id)
    {
        var bill = Data.Bills.FirstOrDefault(x => x.Id == id);
        if (bill is null || bill.LastPaidDate is not { } paidDate) return;
        bill.LastPaidDate = null;
        var posted = Data.Transactions.FirstOrDefault(x => x.Source == "Bill payment" && x.Description == bill.Name
            && x.Date.Year == paidDate.Year && x.Date.Month == paidDate.Month);
        if (posted is not null) Data.Transactions.Remove(posted);
        await SaveAsync();
    }

    public bool ProcessRecurringTransactions()
    {
        var changed = false;
        var today = DateTime.Today;
        foreach (var rule in Data.RecurringTransactions.Where(x => x.IsActive && x.DayOfMonth <= today.Day))
        {
            var alreadyPosted = Data.Transactions.Any(x => x.RecurringRuleId == rule.Id && x.Date.Month == today.Month && x.Date.Year == today.Year);
            if (alreadyPosted) continue;
            Data.Transactions.Add(new Transaction
            {
                Date = new DateTime(today.Year, today.Month, Math.Min(rule.DayOfMonth, DateTime.DaysInMonth(today.Year, today.Month))),
                Description = rule.Description, Category = rule.Category, Owner = rule.Owner,
                Amount = rule.Amount, IsIncome = rule.IsIncome, RecurringRuleId = rule.Id
            });
            changed = true;
        }
        return changed;
    }

    private bool EnsureStarterRecipes()
    {
        var starters = new[]
        {
            new Recipe { Name = "Sheet-pan chicken", Category = "Dinner", Minutes = 40, Ingredients = "Chicken thighs\nBaby potatoes\nBroccoli\nLemon", Instructions = "Season everything and roast at 425°F until golden." },
            new Recipe { Name = "Pesto pasta night", Category = "Dinner", Minutes = 20, Ingredients = "Pasta\nPesto\nCherry tomatoes\nParmesan", Instructions = "Boil pasta, toss with pesto, tomatoes, and parmesan." },
            new Recipe { Name = "Breakfast-for-dinner", Category = "Dinner", Minutes = 25, Ingredients = "Eggs\nBread\nBreakfast potatoes\nFruit", Instructions = "Make eggs, toast, and crispy potatoes. Serve with fruit." }
        };
        var changed = false;
        foreach (var recipe in starters.Where(recipe => !Data.Recipes.Any(x => x.Name == recipe.Name)))
        { Data.Recipes.Add(recipe); changed = true; }
        return changed;
    }

    /// <summary>No financial placeholder data — real households start empty and fill in
    /// transactions (manually or via Rocket Money import), bills, and goals themselves.
    /// Starter recipes are kept since they're genuinely reusable content, not fake numbers.</summary>
    private static HouseholdData Seed() => new()
    {
        Recipes =
        [
            new() { Name = "Cozy taco bowls", Category = "Dinner", Minutes = 30, Ingredients = "Rice\nBlack beans\nGround turkey\nSalsa\nAvocado", Instructions = "Cook rice and turkey. Warm beans. Build bowls and add toppings." },
            new() { Name = "Sheet-pan chicken", Category = "Dinner", Minutes = 40, Ingredients = "Chicken thighs\nBaby potatoes\nBroccoli\nLemon", Instructions = "Season everything and roast at 425°F until golden." },
            new() { Name = "Pesto pasta night", Category = "Dinner", Minutes = 20, Ingredients = "Pasta\nPesto\nCherry tomatoes\nParmesan", Instructions = "Boil pasta, toss with pesto, tomatoes, and parmesan." },
            new() { Name = "Breakfast-for-dinner", Category = "Dinner", Minutes = 25, Ingredients = "Eggs\nBread\nBreakfast potatoes\nFruit", Instructions = "Make eggs, toast, and crispy potatoes. Serve with fruit." }
        ]
    };
}
