namespace DockCalendar.Calendar;

/// <summary>Where one event sits in a day column of the time grid.</summary>
/// <param name="StartMinute">Minutes from midnight, clipped to the day.</param>
/// <param name="Column">Index among the side-by-side columns of its overlap group.</param>
/// <param name="Columns">How many columns that group needs.</param>
internal sealed record TimedSlot(CalendarEvent Event, int StartMinute, int EndMinute, int Column, int Columns);

/// <param name="FirstDay">Index of the first visible day the bar covers.</param>
internal sealed record BannerSlot(CalendarEvent Event, int FirstDay, int Span, int Row);

internal static class EventLayout
{
    /// <summary>Events shorter than this are drawn this tall so their title stays readable.</summary>
    public const int MinimumMinutes = 25;

    /// <summary>
    /// Lays out one day's timed events. Events that overlap share the width: each
    /// takes the first free column, and a run of connected overlaps is split evenly.
    /// </summary>
    public static IReadOnlyList<TimedSlot> LayoutDay(IEnumerable<CalendarEvent> events, DateTime day)
    {
        var dayEnd = day.AddDays(1);
        var items = events
            .Where(item => !item.IsBanner && item.Overlaps(day, dayEnd))
            .Select(item =>
            {
                var start = (int)(Max(item.Start, day) - day).TotalMinutes;
                var end = (int)(Min(item.End, dayEnd) - day).TotalMinutes;
                end = Math.Min(1440, Math.Max(end, start + MinimumMinutes));
                start = Math.Min(start, end - MinimumMinutes);
                return (Event: item, Start: start, End: end);
            })
            .OrderBy(item => item.Start)
            .ThenByDescending(item => item.End)
            .ToList();

        var slots = new List<TimedSlot>();
        var group = new List<(CalendarEvent Event, int Start, int End, int Column)>();
        var columnEnds = new List<int>();
        var groupEnd = 0;

        void Flush()
        {
            slots.AddRange(group.Select(item => new TimedSlot(item.Event, item.Start, item.End, item.Column, columnEnds.Count)));
            group.Clear();
            columnEnds.Clear();
        }

        foreach (var item in items)
        {
            if (group.Count > 0 && item.Start >= groupEnd)
            {
                Flush();
            }

            var column = columnEnds.FindIndex(end => end <= item.Start);
            if (column < 0)
            {
                column = columnEnds.Count;
                columnEnds.Add(item.End);
            }
            else
            {
                columnEnds[column] = item.End;
            }

            group.Add((item.Event, item.Start, item.End, column));
            groupEnd = group.Count == 1 ? item.End : Math.Max(groupEnd, item.End);
        }

        Flush();
        return slots;
    }

    /// <summary>Stacks all-day and multi-day events into rows across the visible days.</summary>
    public static IReadOnlyList<BannerSlot> LayoutBanners(IEnumerable<CalendarEvent> events, DateTime firstDay, int days)
    {
        var rangeEnd = firstDay.AddDays(days);
        var rowEnds = new List<int>();
        var slots = new List<BannerSlot>();

        var banners = events
            .Where(item => item.IsBanner && item.Overlaps(firstDay, rangeEnd))
            .OrderBy(item => item.Start)
            .ThenByDescending(item => item.End);

        foreach (var item in banners)
        {
            var first = Math.Max(0, (int)(item.Start.Date - firstDay).TotalDays);

            // An end at exactly midnight belongs to the day before.
            var lastDate = item.End > item.Start ? item.End.AddTicks(-1).Date : item.Start.Date;
            var last = Math.Min(days - 1, (int)(lastDate - firstDay).TotalDays);
            if (last < first)
            {
                continue;
            }

            var row = rowEnds.FindIndex(end => end < first);
            if (row < 0)
            {
                row = rowEnds.Count;
                rowEnds.Add(last);
            }
            else
            {
                rowEnds[row] = last;
            }

            slots.Add(new BannerSlot(item, first, last - first + 1, row));
        }

        return slots;
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
}
