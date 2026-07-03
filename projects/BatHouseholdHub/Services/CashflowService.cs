using BatHouseholdHub.Models;

namespace BatHouseholdHub.Services;

public enum CashflowWindow { UntilNextPaycheck, ThisMonth, Next30Days, Custom }

public class CashflowSummary
{
    public DateTime SelectedDate { get; set; }
    public decimal CurrentFunds { get; set; }
    public decimal PendingPayments { get; set; }
    public decimal ReservedMoney { get; set; }
    public decimal UpcomingBillsBeforeSelectedDate { get; set; }
    public decimal RequiredDebtPaymentsBeforeSelectedDate { get; set; }
    public decimal ExpectedIncomeBeforeSelectedDate { get; set; }
    public decimal BufferAmount { get; set; }
    public decimal AmountLeftAfterBuffer { get; set; }
    public IncomeEvent? NextPaycheck { get; set; }
    public decimal BillsDueThisWeek { get; set; }
    public decimal BillsDueThisMonth { get; set; }
    public List<Bill> UnpaidBills { get; set; } = [];
    public List<Bill> PaidBills { get; set; } = [];
    public List<Bill> PendingBills { get; set; } = [];
    public List<Bill> ReservedBills { get; set; } = [];
    /// <summary>Delayed bills still waiting on their linked income event to arrive.</summary>
    public List<Bill> DelayedBills { get; set; } = [];
    public List<Transaction> NeedsReviewTransactions { get; set; } = [];
}

/// <summary>Turns funds, bills, and income events into the plain-language numbers shown
/// on the Bills page: what's available, what's due, what's set aside, and what's left after
/// covering it — for whichever date window the household is looking at.</summary>
public class CashflowService(HouseholdStore store)
{
    public CashflowSummary BuildSummary(CashflowWindow window = CashflowWindow.UntilNextPaycheck, DateTime? customDate = null, DateTime? asOf = null)
    {
        var today = (asOf ?? DateTime.Today).Date;
        var data = store.Data;
        var activeBills = data.Bills.Where(x => x.IsActive).ToList();

        var nextPaycheck = data.IncomeEvents
            .Where(x => x.Status != IncomeStatus.Received && x.ExpectedDate.Date >= today)
            .OrderBy(x => x.ExpectedDate)
            .FirstOrDefault();

        var selectedDate = window switch
        {
            CashflowWindow.ThisMonth => new DateTime(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month)),
            CashflowWindow.Next30Days => today.AddDays(30),
            CashflowWindow.Custom => (customDate ?? today).Date,
            _ => nextPaycheck?.ExpectedDate.Date ?? today.AddDays(14)
        };

        int DueDayThisCycle(Bill bill) => Math.Min(bill.DueDay, DateTime.DaysInMonth(today.Year, today.Month));
        DateTime NextDueDate(Bill bill)
        {
            var dueDay = DueDayThisCycle(bill);
            var dueThisMonth = new DateTime(today.Year, today.Month, dueDay);
            return dueThisMonth >= today ? dueThisMonth : dueThisMonth.AddMonths(1);
        }

        // A biweekly bill hits twice in a month and a yearly one hits once a year — count
        // every occurrence from the next due date through the end of the window instead of
        // pretending everything is monthly.
        decimal AmountDueBy(Bill bill, DateTime end)
        {
            var next = NextDueDate(bill);
            if (next > end) return 0m;
            var stepDays = bill.Frequency switch { BillFrequency.Weekly => 7, BillFrequency.Biweekly => 14, _ => 0 };
            if (stepDays > 0) return bill.Amount * (1 + (int)((end - next).TotalDays / stepDays));
            var stepMonths = bill.Frequency switch { BillFrequency.Quarterly => 3, BillFrequency.Yearly => 12, _ => 1 };
            var occurrences = 0;
            for (var due = next; due <= end; due = due.AddMonths(stepMonths)) occurrences++;
            return bill.Amount * occurrences;
        }

        bool IsBlockedByIncome(Bill bill)
        {
            if (bill.ManualStatus != BillStatus.Delayed) return false;
            if (bill.LinkedIncomeEventId is not { } incomeId) return true;
            var linked = data.IncomeEvents.FirstOrDefault(x => x.Id == incomeId);
            return linked is null || linked.Status != IncomeStatus.Received;
        }

        var unpaid = activeBills.Where(x => x.EffectiveStatus(today) != BillStatus.Paid).ToList();
        var paid = activeBills.Where(x => x.EffectiveStatus(today) == BillStatus.Paid).ToList();
        var reserved = unpaid.Where(x => x.EffectiveStatus(today) == BillStatus.Reserved).ToList();
        var skipped = unpaid.Where(x => x.EffectiveStatus(today) == BillStatus.Skipped).ToList();
        var delayedBlocked = unpaid.Where(x => x.EffectiveStatus(today) == BillStatus.Delayed && IsBlockedByIncome(x)).ToList();
        // Delayed bills whose linked income has arrived are payable now, same as Pending.
        var delayedUnblocked = unpaid.Where(x => x.EffectiveStatus(today) == BillStatus.Delayed && !IsBlockedByIncome(x)).ToList();
        var pending = unpaid.Where(x => x.EffectiveStatus(today) == BillStatus.Pending).Concat(delayedUnblocked).ToList();

        var excludedIds = reserved.Select(x => x.Id)
            .Concat(skipped.Select(x => x.Id))
            .Concat(delayedBlocked.Select(x => x.Id))
            .Concat(pending.Select(x => x.Id))
            .ToHashSet();
        var dueBeforeSelected = unpaid.Where(x => !excludedIds.Contains(x.Id) && NextDueDate(x) <= selectedDate).ToList();

        var upcomingBeforeSelected = dueBeforeSelected.Where(x => x.Category != BillCategory.DebtPayment).Sum(x => AmountDueBy(x, selectedDate));
        var requiredDebtBeforeSelected = dueBeforeSelected.Where(x => x.Category == BillCategory.DebtPayment).Sum(x => AmountDueBy(x, selectedDate));
        var pendingTotal = pending.Sum(x => x.Amount);
        var reservedTotal = reserved.Sum(x => x.Amount);

        var expectedIncome = data.IncomeEvents
            .Where(x => x.Status != IncomeStatus.Received && x.ExpectedDate.Date >= today && x.ExpectedDate.Date <= selectedDate)
            .Sum(x => x.NetAmount);

        var currentFunds = data.Funds.Total;
        var availableBeforeIncome = currentFunds - pendingTotal - reservedTotal - upcomingBeforeSelected - requiredDebtBeforeSelected;
        var availableAfterIncome = availableBeforeIncome + expectedIncome;
        var amountLeftAfterBuffer = availableAfterIncome - data.Funds.Buffer;

        var weekEnd = today.AddDays(7);
        var monthEnd = new DateTime(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));

        return new CashflowSummary
        {
            SelectedDate = selectedDate,
            CurrentFunds = currentFunds,
            PendingPayments = pendingTotal,
            ReservedMoney = reservedTotal,
            UpcomingBillsBeforeSelectedDate = upcomingBeforeSelected,
            RequiredDebtPaymentsBeforeSelectedDate = requiredDebtBeforeSelected,
            ExpectedIncomeBeforeSelectedDate = expectedIncome,
            BufferAmount = data.Funds.Buffer,
            AmountLeftAfterBuffer = amountLeftAfterBuffer,
            NextPaycheck = nextPaycheck,
            BillsDueThisWeek = unpaid.Sum(x => AmountDueBy(x, weekEnd)),
            BillsDueThisMonth = unpaid.Sum(x => AmountDueBy(x, monthEnd)),
            UnpaidBills = unpaid,
            PaidBills = paid,
            PendingBills = pending,
            ReservedBills = reserved,
            DelayedBills = delayedBlocked,
            NeedsReviewTransactions = data.Transactions.Where(x => x.NeedsReview).OrderByDescending(x => x.Date).ToList()
        };
    }
}
