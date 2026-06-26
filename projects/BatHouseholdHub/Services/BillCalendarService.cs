using BatHouseholdHub.Models;

namespace BatHouseholdHub.Services;

public class BillCalendarItem
{
    public DateTime Date { get; set; }
    public Bill Bill { get; set; } = null!;
    public BillStatus Status { get; set; }
}

/// <summary>Lays bills out across a month for the calendar view. Monthly bills land once
/// on their due day (clamped to the last day of the month); other frequencies can be added
/// later as the household starts using them.</summary>
public class BillCalendarService(HouseholdStore store)
{
    public List<BillCalendarItem> BuildMonth(int year, int month)
    {
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var items = new List<BillCalendarItem>();
        foreach (var bill in store.Data.Bills.Where(x => x.IsActive))
        {
            if (bill.Frequency != BillFrequency.Monthly) continue;
            var day = Math.Min(bill.DueDay, daysInMonth);
            var date = new DateTime(year, month, day);
            items.Add(new BillCalendarItem { Date = date, Bill = bill, Status = bill.EffectiveStatus(date) });
        }
        return items.OrderBy(x => x.Date).ThenBy(x => x.Bill.Category).ToList();
    }

    public List<BillCalendarItem> BillsOnDay(int year, int month, int day) =>
        BuildMonth(year, month).Where(x => x.Date.Day == day).ToList();

    public decimal DailyTotal(int year, int month, int day) =>
        BillsOnDay(year, month, day).Sum(x => x.Bill.Amount);
}
