using DockCalendar.Calendar;
using Xunit;

namespace DockCalendar.Tests;

public class EventLayoutTests
{
    private static readonly DateTime Day = new(2026, 11, 10);

    private static CalendarEvent Timed(string id, double startHour, double endHour) =>
        new(id, "cal", id, Day.AddHours(startHour), Day.AddHours(endHour), false, null, null, "#039BE5");

    private static CalendarEvent AllDay(string id, int startOffset, int days) =>
        new(id, "cal", id, Day.AddDays(startOffset), Day.AddDays(startOffset + days), true, null, null, "#039BE5");

    [Fact]
    public void EventsThatDoNotOverlapEachTakeTheFullWidth()
    {
        var slots = EventLayout.LayoutDay([Timed("a", 9, 10), Timed("b", 10, 11)], Day);

        Assert.All(slots, slot => Assert.Equal((0, 1), (slot.Column, slot.Columns)));
    }

    [Fact]
    public void OverlappingEventsSitSideBySide()
    {
        var slots = EventLayout.LayoutDay([Timed("a", 11, 12), Timed("b", 11.5, 12.5)], Day);

        Assert.Equal(0, slots.Single(slot => slot.Event.Id == "a").Column);
        Assert.Equal(1, slots.Single(slot => slot.Event.Id == "b").Column);
        Assert.All(slots, slot => Assert.Equal(2, slot.Columns));
    }

    [Fact]
    public void AColumnIsReusedOnceItsEventHasEnded()
    {
        // a overlaps b and c, but b has ended before c starts, so two columns are enough.
        var slots = EventLayout.LayoutDay([Timed("a", 9, 12), Timed("b", 9, 10), Timed("c", 10.5, 11.5)], Day);

        Assert.All(slots, slot => Assert.Equal(2, slot.Columns));
        Assert.Equal(1, slots.Single(slot => slot.Event.Id == "c").Column);
    }

    [Fact]
    public void AnEventCrossingMidnightIsClippedToTheDay()
    {
        var late = new CalendarEvent("late", "cal", "late", Day.AddHours(22), Day.AddHours(26), false, null, null, "#039BE5");

        var today = Assert.Single(EventLayout.LayoutDay([late], Day));
        var tomorrow = Assert.Single(EventLayout.LayoutDay([late], Day.AddDays(1)));

        Assert.Equal((22 * 60, 24 * 60), (today.StartMinute, today.EndMinute));
        Assert.Equal((0, 2 * 60), (tomorrow.StartMinute, tomorrow.EndMinute));
    }

    [Fact]
    public void ShortEventsGetAMinimumHeightAndStayInsideTheDay()
    {
        var slot = Assert.Single(EventLayout.LayoutDay([Timed("brief", 23.9, 23.95)], Day));

        Assert.Equal(1440, slot.EndMinute);
        Assert.Equal(EventLayout.MinimumMinutes, slot.EndMinute - slot.StartMinute);
    }

    [Fact]
    public void AllDayEventsAreLeftOutOfTheTimedColumns()
    {
        Assert.Empty(EventLayout.LayoutDay([AllDay("holiday", 0, 1)], Day));
    }

    [Fact]
    public void AnAllDayEventDoesNotSpillIntoTheNextDay()
    {
        // Google gives an exclusive end date: a one-day event ends at the next midnight.
        var slot = Assert.Single(EventLayout.LayoutBanners([AllDay("holiday", 1, 1)], Day, 7));

        Assert.Equal((1, 1), (slot.FirstDay, slot.Span));
    }

    [Fact]
    public void BannersAreClippedToTheVisibleDaysAndStackWhenTheyOverlap()
    {
        var slots = EventLayout.LayoutBanners([AllDay("trip", -2, 5), AllDay("fair", 1, 2), AllDay("later", 4, 1)], Day, 7);

        var trip = slots.Single(slot => slot.Event.Id == "trip");
        Assert.Equal((0, 3, 0), (trip.FirstDay, trip.Span, trip.Row));
        Assert.Equal(1, slots.Single(slot => slot.Event.Id == "fair").Row);
        Assert.Equal(0, slots.Single(slot => slot.Event.Id == "later").Row);
    }
}
