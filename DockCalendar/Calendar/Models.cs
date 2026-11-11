namespace DockCalendar.Calendar;

/// <param name="Color">"#RRGGBB".</param>
/// <param name="Selected">Whether the calendar is ticked in Google Calendar itself.</param>
internal sealed record CalendarInfo(string Id, string Name, string Color, bool Primary, bool Selected);

/// <summary>An event in local time. All-day events run from midnight to midnight, end exclusive.</summary>
/// <param name="Color">"#RRGGBB": the event's own colour, or its calendar's.</param>
internal sealed record CalendarEvent(
    string Id,
    string CalendarId,
    string Title,
    DateTime Start,
    DateTime End,
    bool IsAllDay,
    string? Location,
    string? Link,
    string Color)
{
    /// <summary>All-day events and timed ones spanning a day or more go in the all-day strip.</summary>
    public bool IsBanner => IsAllDay || End - Start >= TimeSpan.FromHours(24);

    public bool Overlaps(DateTime from, DateTime to) =>
        Start < to && (End > from || (End == Start && Start >= from));
}

internal enum SourceState
{
    /// <summary>No OAuth client ID has been entered yet.</summary>
    NotConfigured,
    SignedOut,
    Ready,
}

internal interface ICalendarSource
{
    SourceState State { get; }

    Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(CancellationToken cancellation);

    Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(CalendarInfo calendar, DateTime from, DateTime to, CancellationToken cancellation);
}
