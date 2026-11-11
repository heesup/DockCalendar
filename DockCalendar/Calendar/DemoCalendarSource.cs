namespace DockCalendar.Calendar;

/// <summary>Made-up events for trying the flyout without a Google account (<c>--demo</c>).</summary>
internal sealed class DemoCalendarSource : ICalendarSource
{
    private static readonly CalendarInfo Work = new("work", S.T("업무", "Work"), "#039BE5", Primary: true, Selected: true);
    private static readonly CalendarInfo Personal = new("personal", S.T("개인", "Personal"), "#33B679", Primary: false, Selected: true);
    private static readonly CalendarInfo Family = new("family", S.T("가족", "Family"), "#F6BF26", Primary: false, Selected: true);

    public SourceState State => SourceState.Ready;

    public Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(CancellationToken cancellation) =>
        Task.FromResult<IReadOnlyList<CalendarInfo>>([Work, Personal, Family]);

    public Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(CalendarInfo calendar, DateTime from, DateTime to, CancellationToken cancellation)
    {
        var events = new List<CalendarEvent>();
        for (var day = from.Date; day < to; day = day.AddDays(1))
        {
            events.AddRange(EventsOn(day).Where(item => item.CalendarId == calendar.Id));
        }

        return Task.FromResult<IReadOnlyList<CalendarEvent>>(events);
    }

    private static IEnumerable<CalendarEvent> EventsOn(DateTime day)
    {
        var weekday = day.DayOfWeek;
        var isWorkday = weekday is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

        if (isWorkday)
        {
            yield return Timed(Work, day, 9, 0, 30, S.T("데일리 스탠드업", "Daily stand-up"), "Google Meet");
        }

        switch (weekday)
        {
            case DayOfWeek.Monday:
                yield return Timed(Work, day, 10, 0, 60, S.T("주간 계획 회의", "Weekly planning"), S.T("회의실 A", "Room A"));
                yield return Timed(Work, day, 14, 0, 90, S.T("디자인 리뷰", "Design review"), null, "#8E24AA");
                break;
            case DayOfWeek.Tuesday:
                yield return Timed(Work, day, 11, 0, 60, S.T("1:1 미팅", "1:1 with Jon"), null);
                yield return Timed(Work, day, 11, 30, 60, S.T("고객사 통화", "Customer call"), "Zoom", "#E67C73");
                yield return Timed(Personal, day, 19, 0, 60, S.T("헬스장", "Gym"), null);
                break;
            case DayOfWeek.Wednesday:
                yield return Timed(Work, day, 13, 0, 60, S.T("팀 점심", "Team lunch"), S.T("버거 가게", "Burger Ltd"), "#F4511E");
                yield return Timed(Work, day, 15, 0, 60, S.T("사업 계획 초안", "Business project - first draft"), null);
                break;
            case DayOfWeek.Thursday:
                yield return Timed(Work, day, 10, 30, 45, S.T("스프린트 회고", "Sprint retro"), null);
                yield return Timed(Personal, day, 18, 30, 60, S.T("바이올린 레슨", "Violin lesson"), null, "#F4511E");
                break;
            case DayOfWeek.Friday:
                yield return Timed(Work, day, 16, 0, 60, S.T("데모 데이", "Demo day"), S.T("라운지", "Lounge"), "#D50000");
                yield return Timed(Family, day, 19, 30, 120, S.T("가족 저녁 식사", "Family dinner"), null);
                break;
            case DayOfWeek.Saturday:
                yield return Timed(Personal, day, 10, 0, 120, S.T("등산", "Hiking"), null);
                break;
        }

        if (day.Day == 15)
        {
            yield return new CalendarEvent($"payday-{day:yyyyMMdd}", Family.Id, S.T("관리비 납부", "Pay the bills"), day, day.AddDays(1), true, null, null, Family.Color);
        }

        if (day.Day == 20)
        {
            yield return new CalendarEvent($"trip-{day:yyyyMMdd}", Personal.Id, S.T("제주 여행", "Trip to Jeju"), day, day.AddDays(3), true, S.T("제주", "Jeju"), null, Personal.Color);
        }
    }

    private static CalendarEvent Timed(CalendarInfo calendar, DateTime day, int hour, int minute, int length, string title, string? location, string? color = null)
    {
        var start = day.AddHours(hour).AddMinutes(minute);
        return new CalendarEvent($"{calendar.Id}-{start:yyyyMMddHHmm}", calendar.Id, title, start, start.AddMinutes(length), false, location, null, color ?? calendar.Color);
    }
}
