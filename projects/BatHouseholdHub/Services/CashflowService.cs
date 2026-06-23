using BatHouseholdHub.Models;

namespace BatHouseholdHub.Services;

public class CashflowSummary
{
    public decimal CurrentFunds { get; set; }
    public decimal PendingPayments { get; set; }
    public decimal UpcomingBillsBeforeNextPaycheck { get; set; }
    public decimal ExpectedIncomeBeforeNextPaycheck { get; set; }
    public decimal AmountLeftAfterBills { get; set; }
    public decimal BufferAmount { get; set; }
    public decimal AmountLeftAfterBuffer { get; set; }
    public IncomeEvent? NextPaycheck { get; set; }
    public decimal BillsDueThisWeek { get; set; }
    public decimal BillsDueThisMonth { get; set; }
    public List<Bill> UnpaidBills { get; set; } = [];
    public List<Bill> PaidBills { get; set; } = [];
    public List<Bill> PendingBills { get; set; } = [];
}

/// <summary>Turns funds, bills, and income events into the plain-language numbers shown
/// on the Bills page: what's available, what's due, and what's left after covering it.</summary>
public class CashflowService(HouseholdStore store)
{
    public CashflowSummary BuildSummary(DateTime? asOf = null)
    {
        var today = (asOf ?? DateTime.Today).Date;
        var data = store.Data;
        var activeBills = data.Bills.Where(x => x.IsActive).ToList();

        var nextPaycheck = data.IncomeEvents
            .Where(x => x.Status != IncomeStatus.Received && x.ExpectedDate.Date >= today)
            .OrderBy(x => x.ExpectedDate)
            .FirstOrDefault();
        var horizon = nextPaycheck?.ExpectedDate.Date ?? today.AddDays(14);

        int DueDayThisCycle(Bill bill) => Math.Min(bill.DueDay, DateTime.DaysInMonth(today.Year, today.Month));
        DateTime NextDueDate(Bill bill)
        {
            var dueDay = DueDayThisCycle(bill);
            var dueThisMonth = new DateTime(today.Year, today.Month, dueDay);
            return dueThisMonth >= today ? dueThisMonth : dueThisMonth.AddMonths(1);
        }

        var unpaid = activeBills.Where(x => x.EffectiveStatus(today) != BillStatus.Paid).ToList();
        var pending = activeBills.Where(x => x.EffectiveStatus(today) == BillStatus.Pending).ToList();
        var paid = activeBills.Where(x => x.EffectiveStatus(today) == BillStatus.Paid).ToList();

        // Pending bills are already counted in PendingPayments — exclude them here so a bill
        // due before the next paycheck doesn't get subtracted from the formula twice.
        var upcomingBeforePaycheck = unpaid.Where(x => x.EffectiveStatus(today) != BillStatus.Pending && NextDueDate(x) <= horizon).Sum(x => x.Amount);
        var pendingTotal = pending.Sum(x => x.Amount);

        var expectedIncome = data.IncomeEvents
            .Where(x => x.Status != IncomeStatus.Received && x.ExpectedDate.Date >= today && x.ExpectedDate.Date <= horizon)
            .Sum(x => x.NetAmount);

        var currentFunds = data.Funds.Total;
        var amountLeftAfterBills = currentFunds + expectedIncome - pendingTotal - upcomingBeforePaycheck;
        var amountLeftAfterBuffer = amountLeftAfterBills - data.Funds.Buffer;

        var weekEnd = today.AddDays(7);
        var monthEnd = new DateTime(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));

        return new CashflowSummary
        {
            CurrentFunds = currentFunds,
            PendingPayments = pendingTotal,
            UpcomingBillsBeforeNextPaycheck = upcomingBeforePaycheck,
            ExpectedIncomeBeforeNextPaycheck = expectedIncome,
            AmountLeftAfterBills = amountLeftAfterBills,
            BufferAmount = data.Funds.Buffer,
            AmountLeftAfterBuffer = amountLeftAfterBuffer,
            NextPaycheck = nextPaycheck,
            BillsDueThisWeek = unpaid.Where(x => NextDueDate(x) <= weekEnd).Sum(x => x.Amount),
            BillsDueThisMonth = unpaid.Where(x => NextDueDate(x) <= monthEnd).Sum(x => x.Amount),
            UnpaidBills = unpaid,
            PaidBills = paid,
            PendingBills = pending
        };
    }
}
