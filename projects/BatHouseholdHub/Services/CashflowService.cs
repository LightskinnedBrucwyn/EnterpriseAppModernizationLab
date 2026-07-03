using BatHouseholdHub.Models;

namespace BatHouseholdHub.Services;

public enum CashflowWindow { UntilNextPaycheck, ThisMonth, Custom }

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
            CashflowWindow.Custom => (customDate ?? today).Date,
            _ => nextPaycheck?.ExpectedDate.Date ?? today.AddDays(14)
        };

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
        // AmountDueBetween counts every occurrence in the window and keeps an unpaid
        // past-due cycle in the total — an overdue bill is still owed, not next month's problem.
        var dueBeforeSelected = unpaid.Where(x => !excludedIds.Contains(x.Id)).ToList();

        var upcomingBeforeSelected = dueBeforeSelected.Where(x => x.Category != BillCategory.DebtPayment).Sum(x => BillSchedule.AmountDueBetween(x, today, selectedDate));
        var requiredDebtBeforeSelected = dueBeforeSelected.Where(x => x.Category == BillCategory.DebtPayment).Sum(x => BillSchedule.AmountDueBetween(x, today, selectedDate));
        var pendingTotal = pending.Sum(x => x.Amount);
        var reservedTotal = reserved.Sum(x => x.Amount);

        var expectedIncome = data.IncomeEvents
            .Where(x => x.Status != IncomeStatus.Received && x.ExpectedDate.Date >= today && x.ExpectedDate.Date <= selectedDate)
            .Sum(x => x.NetAmount);

        // Live bank balances (when accounts are linked) plus the manual figures, which
        // cover cash and anything not in a linked account.
        var currentFunds = data.Funds.Total + store.BankFundsTotal();
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
            BillsDueThisWeek = unpaid.Sum(x => BillSchedule.AmountDueBetween(x, today, weekEnd)),
            BillsDueThisMonth = unpaid.Sum(x => BillSchedule.AmountDueBetween(x, today, monthEnd)),
            UnpaidBills = unpaid,
            PaidBills = paid,
            PendingBills = pending,
            ReservedBills = reserved,
            DelayedBills = delayedBlocked,
            NeedsReviewTransactions = data.Transactions.Where(x => x.NeedsReview).OrderByDescending(x => x.Date).ToList()
        };
    }
}
