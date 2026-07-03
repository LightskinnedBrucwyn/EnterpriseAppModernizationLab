using BatHouseholdHub.Models;

namespace BatHouseholdHub.Services;

/// <summary>The single source of truth for when a bill is due, how many times it hits in a
/// window, whether the current cycle is paid, and what status to show. Every consumer —
/// cashflow math, the calendar grid and its totals, push notifications — goes through here,
/// so a biweekly bill counts twice a month everywhere or nowhere, never one-but-not-the-other.</summary>
public static class BillSchedule
{
    private static int StepDays(BillFrequency f) => f switch { BillFrequency.Weekly => 7, BillFrequency.Biweekly => 14, _ => 0 };
    private static int StepMonths(BillFrequency f) => f switch { BillFrequency.Quarterly => 3, BillFrequency.Yearly => 12, _ => 1 };

    private static DateTime ClampedDueDate(int year, int month, int dueDay) =>
        new(year, month, Math.Min(dueDay, DateTime.DaysInMonth(year, month)));

    /// <summary>Every due date landing inside [from, to], inclusive. Weekly/biweekly bills
    /// anchor on their due day and step by 7/14 days; quarterly/yearly bills anchor on the
    /// last payment month when one is known (we don't store an explicit anchor month).</summary>
    public static IEnumerable<DateTime> DueDatesBetween(Bill bill, DateTime from, DateTime to)
    {
        from = from.Date; to = to.Date;
        if (to < from) yield break;

        var stepDays = StepDays(bill.Frequency);
        if (stepDays > 0)
        {
            var anchor = ClampedDueDate(from.Year, from.Month, bill.DueDay);
            while (anchor > from) anchor = anchor.AddDays(-stepDays);
            while (anchor < from) anchor = anchor.AddDays(stepDays);
            for (var due = anchor; due <= to; due = due.AddDays(stepDays)) yield return due;
            yield break;
        }

        var stepMonths = StepMonths(bill.Frequency);
        var anchorMonth = new DateTime(from.Year, from.Month, 1);
        if (stepMonths > 1 && bill.LastPaidDate is { } paid)
        {
            var m = new DateTime(paid.Year, paid.Month, 1);
            while (m < anchorMonth) m = m.AddMonths(stepMonths);
            anchorMonth = m;
        }
        var lastMonth = new DateTime(to.Year, to.Month, 1);
        for (var month = anchorMonth; month <= lastMonth; month = month.AddMonths(stepMonths))
        {
            var due = ClampedDueDate(month.Year, month.Month, bill.DueDay);
            if (due >= from && due <= to) yield return due;
        }
    }

    /// <summary>The due date governing the cycle asOf falls in — possibly already in the past,
    /// which is what makes a bill overdue rather than "upcoming next month".</summary>
    public static DateTime CurrentCycleDueDate(Bill bill, DateTime asOf)
    {
        asOf = asOf.Date;
        var stepDays = StepDays(bill.Frequency);
        if (stepDays > 0)
        {
            var anchor = ClampedDueDate(asOf.Year, asOf.Month, bill.DueDay);
            while (anchor > asOf) anchor = anchor.AddDays(-stepDays);
            return anchor;
        }
        var stepMonths = StepMonths(bill.Frequency);
        if (stepMonths > 1 && bill.LastPaidDate is { } paid)
        {
            var due = ClampedDueDate(paid.Year, paid.Month, bill.DueDay);
            while (due.AddMonths(stepMonths) <= asOf) due = due.AddMonths(stepMonths);
            return due <= asOf ? due : ClampedDueDate(asOf.Year, asOf.Month, bill.DueDay);
        }
        return ClampedDueDate(asOf.Year, asOf.Month, bill.DueDay);
    }

    /// <summary>Whether the cycle asOf falls in has been paid. Monthly (and quarterly/yearly)
    /// keep the original same-calendar-month rule; weekly/biweekly count a payment made any
    /// time after the previous occurrence, so paying a biweekly bill on the 15th doesn't
    /// wrongly cover the 29th too.</summary>
    public static bool IsPaidThisCycle(Bill bill, DateTime asOf)
    {
        if (bill.LastPaidDate is not { } paid) return false;
        var stepDays = StepDays(bill.Frequency);
        if (stepDays > 0)
        {
            var currentDue = CurrentCycleDueDate(bill, asOf);
            return paid.Date > currentDue.AddDays(-stepDays);
        }
        return paid.Year == asOf.Year && paid.Month == asOf.Month;
    }

    /// <summary>Paid wins; an unpaid bill whose due date already slipped past shows Overdue
    /// instead of a stale "Upcoming"; any other manual status speaks for itself.</summary>
    public static BillStatus EffectiveStatus(Bill bill, DateTime asOf)
    {
        if (IsPaidThisCycle(bill, asOf)) return BillStatus.Paid;
        if (bill.ManualStatus == BillStatus.Upcoming && CurrentCycleDueDate(bill, asOf) < asOf.Date) return BillStatus.Overdue;
        return bill.ManualStatus;
    }

    /// <summary>What this bill still costs between asOf and end: every occurrence in the
    /// window, plus the current cycle's amount when it's already past due and unpaid —
    /// a missed July 2nd bill is still owed on July 5th, not silently pushed to August.</summary>
    public static decimal AmountDueBetween(Bill bill, DateTime asOf, DateTime end)
    {
        asOf = asOf.Date; end = end.Date;
        var occurrences = DueDatesBetween(bill, asOf, end).Count();
        var currentDue = CurrentCycleDueDate(bill, asOf);
        if (currentDue < asOf && !IsPaidThisCycle(bill, asOf)) occurrences++;
        return bill.Amount * occurrences;
    }
}
