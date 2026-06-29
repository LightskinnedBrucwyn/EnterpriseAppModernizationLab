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
    private readonly string _backupsFolder;
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
        _backupsFolder = Path.Combine(folder, "backups");
        Directory.CreateDirectory(_backupsFolder);
        _path = Path.Combine(folder, "household.json");
        Data = Load();
        var changed = EnsureStarterRecipes();
        changed |= EnsureKnownBills();
        changed |= EnsureKnownIncomeSources();
        changed |= EnsureKnownShoppingSites();
        changed |= LinkDelayedBillsToIncome();
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

    /// <summary>Snapshots household.json into App_Data/backups once per calendar day and prunes
    /// anything older than 30 days. household.json is the household's only copy of its data, so
    /// this is cheap insurance against a bad edit, a corrupt write, or disk trouble.</summary>
    public Task BackupIfNeededAsync()
    {
        try
        {
            var backupPath = Path.Combine(_backupsFolder, $"household-{DateTime.Today:yyyyMMdd}.json");
            if (!File.Exists(backupPath) && File.Exists(_path))
                File.Copy(_path, backupPath);

            var cutoff = DateTime.Today.AddDays(-30);
            foreach (var file in Directory.GetFiles(_backupsFolder, "household-*.json"))
            {
                var stamp = Path.GetFileNameWithoutExtension(file)["household-".Length..];
                if (DateTime.TryParseExact(stamp, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date < cutoff)
                    File.Delete(file);
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to back up household.json"); }
        return Task.CompletedTask;
    }

    public async Task AddQuickLogAsync(string text, string owner)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Data.QuickLogs.Insert(0, new QuickLogEntry { Text = text.Trim(), Owner = owner });
        await SaveAsync();
    }

    public async Task DeleteQuickLogAsync(Guid id)
    {
        Data.QuickLogs.RemoveAll(x => x.Id == id);
        await SaveAsync();
    }

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

    /// <summary>The household's real-world bill list, organized the way Trey actually thinks
    /// about them. Amounts and due days below come from mining the Rocket Money export for
    /// recurring patterns; bills with no clear fixed amount are left at 0 with a note instead
    /// of a guess. Reconciled by name on every startup so renaming/adding here updates existing
    /// installs without wiping amounts the household has already entered themselves.</summary>
    private static readonly (string Name, BillCategory Category, decimal Amount, int DueDay, BillFrequency Frequency, BillPriority Priority, string Notes)[] KnownBills =
    [
        ("Chase card manual payment", BillCategory.DebtPayment, 0m, 1, BillFrequency.Monthly, BillPriority.Debt, "Manual extra payments vary in amount — update each cycle."),
        ("Chase autopay", BillCategory.DebtPayment, 163m, 23, BillFrequency.Monthly, BillPriority.Debt, ""),
        ("Affirm", BillCategory.DebtPayment, 161.44m, 22, BillFrequency.Monthly, BillPriority.Debt, "Combined total of simultaneous installment plans."),
        ("Klover", BillCategory.DebtPayment, 4.99m, 22, BillFrequency.Monthly, BillPriority.Debt, "Membership fee only — cash advance repayments are separate and vary."),
        ("Dave", BillCategory.DebtPayment, 0m, 1, BillFrequency.Monthly, BillPriority.Debt, "Highly variable ($25–$179) with no fixed cadence — confirm amount."),
        ("Brigit", BillCategory.DebtPayment, 8.99m, 22, BillFrequency.Monthly, BillPriority.Subscription, ""),
        ("Ally car", BillCategory.DebtPayment, 439.44m, 28, BillFrequency.Monthly, BillPriority.Debt, ""),
        ("SNAP Finance", BillCategory.DebtPayment, 21.50m, 15, BillFrequency.Biweekly, BillPriority.Debt, ""),
        ("Upgrade", BillCategory.DebtPayment, 0m, 1, BillFrequency.Monthly, BillPriority.Debt, "No matching transactions found — confirm amount and due day."),
        ("Progressive", BillCategory.FixedBill, 169.66m, 22, BillFrequency.Monthly, BillPriority.Critical, ""),
        ("Verizon", BillCategory.FixedBill, 64.55m, 10, BillFrequency.Monthly, BillPriority.Subscription, ""),
        ("Rocket Money", BillCategory.FixedBill, 10.66m, 22, BillFrequency.Monthly, BillPriority.Subscription, ""),
        ("IdentityIQ", BillCategory.FixedBill, 30.74m, 10, BillFrequency.Monthly, BillPriority.Subscription, ""),
        ("Experian", BillCategory.FixedBill, 27.05m, 10, BillFrequency.Monthly, BillPriority.Subscription, ""),
        ("Uber One", BillCategory.FixedBill, 9.99m, 28, BillFrequency.Monthly, BillPriority.Optional, ""),
        ("HBO Max", BillCategory.FixedBill, 5.30m, 22, BillFrequency.Monthly, BillPriority.Optional, "Amount unverified from import — confirm against latest statement."),
        ("Claude", BillCategory.FixedBill, 21.32m, 27, BillFrequency.Monthly, BillPriority.Subscription, ""),
        ("Amazon Prime", BillCategory.FixedBill, 16.23m, 22, BillFrequency.Monthly, BillPriority.Subscription, "Prime Video Channels adds another ~$14.06 on a separate charge."),
        ("City of Davenport", BillCategory.FixedBill, 40m, 3, BillFrequency.Monthly, BillPriority.Critical, ""),
        ("SoFi transfer", BillCategory.TransferSavings, 0m, 1, BillFrequency.Monthly, BillPriority.Optional, "Rent money moved aside ahead of the due date — treated as reserved, not spendable."),
        ("Apple Cash transfers", BillCategory.TransferSavings, 0m, 1, BillFrequency.Monthly, BillPriority.Optional, "Highly variable transfers, not a fixed bill."),
        ("Zelle transfers", BillCategory.TransferSavings, 0m, 1, BillFrequency.Monthly, BillPriority.Optional, "Highly variable transfers, not a fixed bill."),
        ("Ahmad", BillCategory.FixedBill, 600m, 1, BillFrequency.Monthly, BillPriority.Critical, "Rent owed to Ahmad — delayed until the Vista final check arrives.")
    ];

    private static MoneyType DefaultMoneyType(BillCategory category) => category switch
    {
        BillCategory.DebtPayment => MoneyType.DebtPayment,
        BillCategory.TransferSavings => MoneyType.Transfer,
        _ => MoneyType.Expense
    };

    private bool EnsureKnownBills()
    {
        var changed = false;
        foreach (var (name, category, amount, dueDay, frequency, priority, notes) in KnownBills)
        {
            var existing = Data.Bills.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                var bill = new Bill
                {
                    Name = name, Category = category, Amount = amount, DueDay = dueDay,
                    Frequency = frequency, Priority = priority, Notes = notes, MoneyType = DefaultMoneyType(category)
                };
                // The rent transfer is money already set aside, not spendable; Ahmad's rent
                // waits on income rather than a due date — see the real-life example in the brief.
                if (name == "SoFi transfer") { bill.ManualStatus = BillStatus.Reserved; bill.MoneyType = MoneyType.RentReserve; }
                if (name == "Ahmad") bill.ManualStatus = BillStatus.Delayed;
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

    /// <summary>Links Ahmad's delayed rent payment to the Vista final check income event so the
    /// dashboard knows which paycheck unblocks it. Only sets the link if it isn't already set,
    /// so re-linking never clobbers a household's own choice.</summary>
    private bool LinkDelayedBillsToIncome()
    {
        var ahmad = Data.Bills.FirstOrDefault(x => x.Name.Equals("Ahmad", StringComparison.OrdinalIgnoreCase));
        if (ahmad is null || ahmad.LinkedIncomeEventId is not null) return false;
        var vista = Data.IncomeEvents.FirstOrDefault(x => x.Source.Equals("Vista final check", StringComparison.OrdinalIgnoreCase));
        if (vista is null) return false;
        ahmad.LinkedIncomeEventId = vista.Id;
        return true;
    }

    /// <summary>Marks a bill paid for the current cycle and logs the real expense so income
    /// minus spending recalculates immediately on Money and Today instead of drifting out of sync.</summary>
    public async Task MarkBillPaidAsync(Guid id)
    {
        var bill = Data.Bills.FirstOrDefault(x => x.Id == id);
        if (bill is null) return;
        var today = DateTime.Today;
        var alreadyPosted = Data.Transactions.Any(x => x.Source == "Bill payment" && x.Description == bill.Name
            && x.Date.Year == today.Year && x.Date.Month == today.Month);
        bill.LastPaidDate = today;
        bill.ManualStatus = BillStatus.Upcoming;
        if (bill.Amount > 0 && !alreadyPosted)
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

    /// <summary>Starter income sources so the Income timeline isn't empty on first run.
    /// Amounts are left at 0 — Trey fills in real figures as paychecks come in.</summary>
    private static readonly string[] KnownIncomeSources = ["ByteForza paycheck", "ByteForza bonus", "Vista final check"];

    private static readonly (string Name, string Url)[] KnownShoppingSites =
    [
        ("Amazon", "https://www.amazon.com"),
        ("Target", "https://www.target.com"),
        ("SHEIN", "https://www.shein.com"),
        ("Old Navy", "https://oldnavy.gap.com"),
        ("Etsy", "https://www.etsy.com")
    ];

    private bool EnsureKnownShoppingSites()
    {
        var changed = false;
        foreach (var (name, url) in KnownShoppingSites)
        {
            if (Data.ShoppingSites.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
            Data.ShoppingSites.Add(new ShoppingSite { Name = name, Url = url, Owner = "Jess" });
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

    private bool EnsureKnownIncomeSources()
    {
        var changed = false;
        foreach (var source in KnownIncomeSources)
        {
            if (Data.IncomeEvents.Any(x => x.Source.Equals(source, StringComparison.OrdinalIgnoreCase))) continue;
            Data.IncomeEvents.Add(new IncomeEvent { Source = source, Owner = "Trey", Status = IncomeStatus.Estimated });
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
