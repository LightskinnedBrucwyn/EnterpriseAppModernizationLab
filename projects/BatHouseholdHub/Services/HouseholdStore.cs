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
    private bool _legacyFundsMigrationDetected;
    private bool _legacyFundsMigrationBackedUp;
    private bool _legacyFundsMigrationSaved;
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
        if (environment.IsDevelopment())
        {
            changed |= EnsureSyntheticDemoBills();
            changed |= EnsureSyntheticDemoIncomeSources();
            changed |= EnsureSyntheticDemoShoppingSites();
            changed |= LinkSyntheticDelayedBillsToIncome();
        }
        changed |= ProcessRecurringTransactions();
        if (changed || _legacyFundsMigrationDetected)
            SaveDataWithMigrationProtection();
    }

    private HouseholdData Load()
    {
        if (!File.Exists(_path)) return Seed();
        try
        {
            var data = JsonSerializer.Deserialize<HouseholdData>(File.ReadAllText(_path)) ?? Seed();
            if (data.Funds.MigratedLegacyMemberFunds)
            {
                _legacyFundsMigrationDetected = true;
                _logger.LogInformation("household.json schema migration detected for household funds.");
            }
            return data;
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
            EnsureMigrationBackupIfNeeded();
            var json = JsonSerializer.Serialize(Data, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_path, json);
            LogMigrationSavedIfNeeded();
        }
        finally { _lock.Release(); }
    }

    private void SaveDataWithMigrationProtection()
    {
        EnsureMigrationBackupIfNeeded();
        File.WriteAllText(_path, JsonSerializer.Serialize(Data, new JsonSerializerOptions { WriteIndented = true }));
        LogMigrationSavedIfNeeded();
    }

    private void EnsureMigrationBackupIfNeeded()
    {
        if (!_legacyFundsMigrationDetected || _legacyFundsMigrationBackedUp) return;
        try
        {
            var backupPath = $"{_path}.migration-{DateTime.Now:yyyyMMddHHmmss}.json.bak";
            File.Copy(_path, backupPath, overwrite: false);
            _legacyFundsMigrationBackedUp = true;
            _logger.LogInformation("Private runtime backup created before household.json schema migration.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "household.json schema migration failed before save because a private runtime backup could not be created.");
            throw;
        }
    }

    private void LogMigrationSavedIfNeeded()
    {
        if (!_legacyFundsMigrationDetected || _legacyFundsMigrationSaved) return;
        _legacyFundsMigrationSaved = true;
        _logger.LogInformation("household.json schema migration completed successfully.");
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
        var newlyImported = new List<Transaction>();
        foreach (var transaction in parsed)
        {
            if (!existing.Add(transaction.SourceKey)) { result.SkippedDuplicates++; continue; }
            Data.Transactions.Add(transaction);
            newlyImported.Add(transaction);
            result.Imported++;
        }
        ReconcileImportedTransactions(newlyImported);
        result.AccountCount = accounts.Count;
        result.EarliestDate = parsed.Count == 0 ? null : parsed.Min(x => x.Date);
        result.LatestDate = parsed.Count == 0 ? null : parsed.Max(x => x.Date);
        await SaveAsync();
        return result;
    }

    private static string NormalizeName(string text) => new string(text.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    /// <summary>Matches freshly imported transactions against planned bills and pending
    /// payments by name and amount. A clean match marks the bill Paid; a transaction that
    /// looks like a bill payment but can't be confidently matched is flagged Needs Review
    /// instead of silently being treated as ordinary spending.</summary>
    private void ReconcileImportedTransactions(List<Transaction> imported)
    {
        foreach (var transaction in imported.Where(x => !x.IsIncome))
        {
            var description = NormalizeName(transaction.Description);
            if (description.Length == 0) continue;

            var nameMatches = Data.Bills
                .Where(b => b.IsActive && b.EffectiveStatus(transaction.Date) is BillStatus.Upcoming or BillStatus.Pending)
                .Where(b => { var name = NormalizeName(b.Name); return name.Length > 0 && (description.Contains(name) || name.Contains(description)); })
                .ToList();
            if (nameMatches.Count == 0) continue;

            var amountMatch = nameMatches
                .Where(b => b.Amount == 0 || Math.Abs(b.Amount - transaction.Amount) <= Math.Max(1m, b.Amount * 0.05m))
                .OrderBy(b => Math.Abs(b.Amount - transaction.Amount))
                .FirstOrDefault();

            if (amountMatch is not null)
            {
                amountMatch.LastPaidDate = transaction.Date;
                amountMatch.ManualStatus = BillStatus.Upcoming;
                transaction.MatchedBillId = amountMatch.Id;
            }
            else
            {
                transaction.NeedsReview = true;
            }
        }
    }

    /// <summary>Synthetic development-only demo bills. Production instances start without
    /// source-controlled household finance seed data.</summary>
    private static readonly (string Name, BillCategory Category, decimal Amount, int DueDay, BillFrequency Frequency, BillPriority Priority, string Notes)[] SyntheticDemoBills =
    [
        ("Example Credit Card Minimum", BillCategory.DebtPayment, 42.00m, 12, BillFrequency.Monthly, BillPriority.Debt, "Synthetic demo value. Replace in private runtime data only."),
        ("Example Auto Loan", BillCategory.DebtPayment, 210.00m, 20, BillFrequency.Monthly, BillPriority.Debt, "Synthetic demo value."),
        ("Example Utility", BillCategory.FixedBill, 75.00m, 5, BillFrequency.Monthly, BillPriority.Critical, "Synthetic demo value."),
        ("Example Phone Plan", BillCategory.FixedBill, 55.00m, 15, BillFrequency.Monthly, BillPriority.Subscription, "Synthetic demo value."),
        ("Example Rent Reserve", BillCategory.TransferSavings, 300.00m, 1, BillFrequency.Monthly, BillPriority.Critical, "Synthetic reserve example; not real household data."),
        ("Example Shared Rent", BillCategory.FixedBill, 600.00m, 1, BillFrequency.Monthly, BillPriority.Critical, "Synthetic delayed-payment example.")
    ];

    private static MoneyType DefaultMoneyType(BillCategory category) => category switch
    {
        BillCategory.DebtPayment => MoneyType.DebtPayment,
        BillCategory.TransferSavings => MoneyType.Transfer,
        _ => MoneyType.Expense
    };

    private bool EnsureSyntheticDemoBills()
    {
        var changed = false;
        foreach (var (name, category, amount, dueDay, frequency, priority, notes) in SyntheticDemoBills)
        {
            var existing = Data.Bills.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                var bill = new Bill
                {
                    Name = name, Category = category, Amount = amount, DueDay = dueDay,
                    Frequency = frequency, Priority = priority, Notes = notes, MoneyType = DefaultMoneyType(category)
                };
                if (name == "Example Rent Reserve") { bill.ManualStatus = BillStatus.Reserved; bill.MoneyType = MoneyType.RentReserve; }
                if (name == "Example Shared Rent") bill.ManualStatus = BillStatus.Delayed;
                Data.Bills.Add(bill);
                changed = true;
                continue;
            }
            if (existing.Amount == 0 && amount != 0)
            {
                existing.Amount = amount;
                existing.DueDay = dueDay;
                existing.Frequency = frequency;
                existing.Priority = priority;
                if (string.IsNullOrWhiteSpace(existing.Notes)) existing.Notes = notes;
                changed = true;
            }
        }
        return changed;
    }

    /// <summary>Links a synthetic delayed bill to synthetic income for development demos only.</summary>
    private bool LinkSyntheticDelayedBillsToIncome()
    {
        var bill = Data.Bills.FirstOrDefault(x => x.Name.Equals("Example Shared Rent", StringComparison.OrdinalIgnoreCase));
        if (bill is null || bill.LinkedIncomeEventId is not null) return false;
        var income = Data.IncomeEvents.FirstOrDefault(x => x.Source.Equals("Example Reimbursement", StringComparison.OrdinalIgnoreCase));
        if (income is null) return false;
        bill.LinkedIncomeEventId = income.Id;
        return true;
    }

    /// <summary>Marks a bill paid for the current cycle and logs the real expense so income
    /// minus spending recalculates immediately on Money and Today instead of drifting out of sync.</summary>
    public async Task MarkBillPaidAsync(Guid id)
    {
        var bill = Data.Bills.FirstOrDefault(x => x.Id == id);
        if (bill is null) return;
        var today = DateTime.Today;
        bill.LastPaidDate = today;
        bill.ManualStatus = BillStatus.Upcoming;
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

    public async Task MarkBillPendingAsync(Guid id)
    {
        var bill = Data.Bills.FirstOrDefault(x => x.Id == id);
        if (bill is null) return;
        bill.ManualStatus = bill.ManualStatus == BillStatus.Pending ? BillStatus.Upcoming : BillStatus.Pending;
        await SaveAsync();
    }

    /// <summary>Sets a bill's status directly — used for Reserve, Delay (with an optional linked
    /// income event), Skip, and reverting back to Upcoming from the bill row's status menu.</summary>
    public async Task SetBillStatusAsync(Guid id, BillStatus status, Guid? linkedIncomeEventId = null)
    {
        var bill = Data.Bills.FirstOrDefault(x => x.Id == id);
        if (bill is null) return;
        bill.ManualStatus = status;
        bill.LinkedIncomeEventId = status == BillStatus.Delayed ? linkedIncomeEventId : null;
        await SaveAsync();
    }

    public async Task MarkTransactionReviewedAsync(Guid transactionId)
    {
        var transaction = Data.Transactions.FirstOrDefault(x => x.Id == transactionId);
        if (transaction is null) return;
        transaction.NeedsReview = false;
        await SaveAsync();
    }

    /// <summary>Manually links a Needs Review transaction to a bill and marks that bill paid —
    /// for the cases the automatic Rocket Money matching pass couldn't resolve on its own.</summary>
    public async Task LinkTransactionToBillAsync(Guid transactionId, Guid billId)
    {
        var transaction = Data.Transactions.FirstOrDefault(x => x.Id == transactionId);
        var bill = Data.Bills.FirstOrDefault(x => x.Id == billId);
        if (transaction is null || bill is null) return;
        transaction.NeedsReview = false;
        transaction.MatchedBillId = bill.Id;
        bill.LastPaidDate = transaction.Date;
        bill.ManualStatus = BillStatus.Upcoming;
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

    /// <summary>Synthetic development-only income sources so the timeline can be exercised
    /// without committing household pay data.</summary>
    private static readonly string[] SyntheticDemoIncomeSources = ["Example Paycheck", "Example Bonus", "Example Reimbursement"];

    private static readonly (string Name, string Url)[] SyntheticDemoShoppingSites =
    [
        ("Example Marketplace", "https://example.com/market"),
        ("Example Home Goods", "https://example.com/home"),
        ("Example Clothing Store", "https://example.com/clothing")
    ];

    private bool EnsureSyntheticDemoShoppingSites()
    {
        var changed = false;
        foreach (var (name, url) in SyntheticDemoShoppingSites)
        {
            if (Data.ShoppingSites.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
            Data.ShoppingSites.Add(new ShoppingSite { Name = name, Url = url, Owner = "Person B" });
            changed = true;
        }
        return changed;
    }

    public async Task AddShoppingSiteAsync(string name, string url, string owner)
    {
        Data.ShoppingSites.Add(new ShoppingSite { Name = name.Trim(), Url = url.Trim(), Owner = owner });
        await SaveAsync();
    }

    public async Task DeleteShoppingSiteAsync(Guid id)
    {
        Data.ShoppingSites.RemoveAll(x => x.Id == id);
        await SaveAsync();
    }

    private bool EnsureSyntheticDemoIncomeSources()
    {
        var changed = false;
        foreach (var source in SyntheticDemoIncomeSources)
        {
            if (Data.IncomeEvents.Any(x => x.Source.Equals(source, StringComparison.OrdinalIgnoreCase))) continue;
            Data.IncomeEvents.Add(new IncomeEvent { Source = source, Owner = "Person A", Status = IncomeStatus.Estimated });
            changed = true;
        }
        return changed;
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
