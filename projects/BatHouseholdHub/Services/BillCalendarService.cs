using BatHouseholdHub.Models;

namespace BatHouseholdHub.Services;

public class BillCalendarItem
{
    public DateTime Date { get; set; }
    public Bill Bill { get; set; } = null!;
    public BillStatus Status { get; set; }
}

/// <summary>Lays bills out across a month for the calendar view using BillSchedule, so a
/// biweekly bill lands on every hit in the month and quarterly/yearly bills only appear in
/// the months they're actually due. Month totals come from the same occurrence list the
/// grid renders, so the "due this month" figure always matches the pills on screen.</summary>
public class BillCalendarService(HouseholdStore store)
{
    public List<BillCalendarItem> BuildMonth(int year, int month)
    {
        var first = new DateTime(year, month, 1);
        var last = new DateTime(year, month, DateTime.DaysInMonth(year, month));
        var items = new List<BillCalendarItem>();
        foreach (var bill in store.Data.Bills.Where(x => x.IsActive))
        {
            foreach (var date in BillSchedule.DueDatesBetween(bill, first, last))
            {
                items.Add(new BillCalendarItem { Date = date, Bill = bill, Status = bill.EffectiveStatus(date) });
            }
        }
        return items.OrderBy(x => x.Date).ThenBy(x => x.Bill.Category).ToList();
    }

    public List<BillCalendarItem> BillsOnDay(int year, int month, int day) =>
        BuildMonth(year, month).Where(x => x.Date.Day == day).ToList();

    public decimal MonthTotal(int year, int month) =>
        BuildMonth(year, month).Sum(x => x.Bill.Amount);

    public decimal MonthTotalForCategory(int year, int month, BillCategory category) =>
        BuildMonth(year, month).Where(x => x.Bill.Category == category).Sum(x => x.Bill.Amount);

    public int MonthCountForCategory(int year, int month, BillCategory category) =>
        BuildMonth(year, month).Count(x => x.Bill.Category == category);
}
