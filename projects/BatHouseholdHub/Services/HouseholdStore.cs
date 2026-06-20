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
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ILogger<HouseholdStore> _logger;
    public HouseholdData Data { get; private set; }

    public HouseholdStore(IWebHostEnvironment environment, ILogger<HouseholdStore> logger)
    {
        _logger = logger;
        var folder = Path.Combine(environment.ContentRootPath, "App_Data");
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, "household.json");
        Data = Load();
        var changed = EnsureStarterRecipes();
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

    private static HouseholdData Seed() => new()
    {
        Transactions =
        [
            new() { Description = "Paycheck", Category = "Income", Owner = "Me", Amount = 3200, IsIncome = true, Date = DateTime.Today.AddDays(-8), Source = "Starter" },
            new() { Description = "Jess paycheck", Category = "Income", Owner = "Jess", Amount = 2850, IsIncome = true, Date = DateTime.Today.AddDays(-6), Source = "Starter" },
            new() { Description = "Groceries", Category = "Groceries", Owner = "Shared", Amount = 146.32m, Date = DateTime.Today.AddDays(-2), Source = "Starter" },
            new() { Description = "Internet", Category = "Bills", Owner = "Shared", Amount = 79.99m, Date = DateTime.Today.AddDays(-4), Source = "Starter" }
        ],
        SavingsGoals = [new() { Name = "Emergency cushion", Current = 2400, Target = 6000 }],
        CreditAccounts = [new() { Name = "Everyday Card", LastFour = "4242", Balance = 640, Limit = 5000, Apr = 19.99m, DueDay = 18 }],
        Recipes =
        [
            new() { Name = "Cozy taco bowls", Category = "Dinner", Minutes = 30, Ingredients = "Rice\nBlack beans\nGround turkey\nSalsa\nAvocado", Instructions = "Cook rice and turkey. Warm beans. Build bowls and add toppings." },
            new() { Name = "Sheet-pan chicken", Category = "Dinner", Minutes = 40, Ingredients = "Chicken thighs\nBaby potatoes\nBroccoli\nLemon", Instructions = "Season everything and roast at 425°F until golden." },
            new() { Name = "Pesto pasta night", Category = "Dinner", Minutes = 20, Ingredients = "Pasta\nPesto\nCherry tomatoes\nParmesan", Instructions = "Boil pasta, toss with pesto, tomatoes, and parmesan." },
            new() { Name = "Breakfast-for-dinner", Category = "Dinner", Minutes = 25, Ingredients = "Eggs\nBread\nBreakfast potatoes\nFruit", Instructions = "Make eggs, toast, and crispy potatoes. Serve with fruit." }
        ],
        Groceries = [new() { Name = "Coffee", Section = "Pantry" }, new() { Name = "Spinach", Section = "Produce" }],
        MealPlans = [new() { Date = DateTime.Today, Meal = "Cozy taco bowls" }],
        RecurringTransactions = [new() { Description = "Internet", Category = "Bills", Owner = "Shared", Amount = 79.99m, DayOfMonth = 15 }]
    };
}
