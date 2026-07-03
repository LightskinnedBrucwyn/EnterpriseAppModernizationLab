using BatHouseholdHub.Models;

namespace BatHouseholdHub.Services;

/// <summary>Shared transaction filters so month-window logic isn't re-typed per page.</summary>
public static class TransactionQueries
{
    public static IEnumerable<Transaction> InMonth(this IEnumerable<Transaction> transactions, DateTime month) =>
        transactions.Where(x => x.Date.Year == month.Year && x.Date.Month == month.Month);
}
