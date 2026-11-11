using System.Text.Json;
using DockCalendar.Calendar;
using DockCalendar.Google;
using Xunit;

namespace DockCalendar.Tests;

public class GoogleParsingTests
{
    private static readonly CalendarInfo Calendar = new("me@example.com", "Me", "#9FE1E7", Primary: true, Selected: true);

    private static List<CalendarEvent> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return GoogleCalendarClient.ParseEvents(document.RootElement, Calendar).ToList();
    }

    [Fact]
    public void TimedEventIsConvertedToLocalTime()
    {
        var item = Assert.Single(Parse("""
            {"items":[{"id":"1","summary":"Review","location":"Room A","htmlLink":"https://calendar.google.com/x",
              "start":{"dateTime":"2026-11-10T09:00:00Z"},"end":{"dateTime":"2026-11-10T10:00:00Z"}}]}
            """));

        Assert.Equal(new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc).ToLocalTime(), item.Start);
        Assert.Equal(TimeSpan.FromHours(1), item.End - item.Start);
        Assert.False(item.IsAllDay);
        Assert.Equal("Room A", item.Location);
        Assert.Equal("#9FE1E7", item.Color);
    }

    [Fact]
    public void AllDayEventKeepsItsDatesWhateverTheTimeZone()
    {
        var item = Assert.Single(Parse("""
            {"items":[{"id":"1","summary":"Holiday","start":{"date":"2026-11-10"},"end":{"date":"2026-11-11"}}]}
            """));

        Assert.True(item.IsAllDay);
        Assert.Equal(new DateTime(2026, 11, 10), item.Start);
        Assert.Equal(new DateTime(2026, 11, 11), item.End);
    }

    [Fact]
    public void EventColourOverridesTheCalendarColour()
    {
        var item = Assert.Single(Parse("""
            {"items":[{"id":"1","colorId":"11","start":{"date":"2026-11-10"},"end":{"date":"2026-11-11"}}]}
            """));

        Assert.Equal("#D50000", item.Color);
    }

    [Fact]
    public void CancelledAndDeclinedEventsAreSkipped()
    {
        var events = Parse("""
            {"items":[
              {"id":"1","status":"cancelled"},
              {"id":"2","summary":"Declined","start":{"date":"2026-11-10"},"end":{"date":"2026-11-11"},
               "attendees":[{"self":true,"responseStatus":"declined"}]},
              {"id":"3","summary":"Kept","start":{"date":"2026-11-10"},"end":{"date":"2026-11-11"},
               "attendees":[{"self":true,"responseStatus":"accepted"},{"responseStatus":"declined"}]}
            ]}
            """);

        Assert.Equal("Kept", Assert.Single(events).Title);
    }

    [Fact]
    public void AnEventWithoutASummaryGetsAPlaceholderTitle()
    {
        var item = Assert.Single(Parse("""
            {"items":[{"id":"1","start":{"date":"2026-11-10"},"end":{"date":"2026-11-11"}}]}
            """));

        Assert.False(string.IsNullOrWhiteSpace(item.Title));
    }

    [Fact]
    public void CalendarListUsesTheUsersOwnNameForACalendar()
    {
        using var document = JsonDocument.Parse("""
            {"items":[
              {"id":"a","summary":"Team","summaryOverride":"My team","backgroundColor":"#16a765","selected":true},
              {"id":"b","summary":"Holidays","primary":false}
            ]}
            """);

        var calendars = GoogleCalendarClient.ParseCalendars(document.RootElement).ToList();

        Assert.Equal(("My team", "#16A765", true), (calendars[0].Name, calendars[0].Color, calendars[0].Selected));
        Assert.False(calendars[1].Selected);
    }
}
