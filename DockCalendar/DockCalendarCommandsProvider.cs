using DockCalendar.Calendar;
using DockCalendar.Ui;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DockCalendar;

public sealed partial class DockCalendarCommandsProvider : CommandProvider, IDisposable
{
    /// <summary>The one name used for the extension, its command and its Dock band.</summary>
    internal const string ProductName = "Dock Calendar";

    private static readonly TimeSpan BandTick = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(10);

    private readonly AppServices _services;
    private readonly FlyoutHost _host;
    private readonly ListItem _bandItem;
    private readonly WrappedDockItem _dockItem;
    private readonly ICommandItem[] _commands;
    private readonly Timer _bandTimer;
    private readonly Timer _syncTimer;

    public DockCalendarCommandsProvider()
    {
        Id = "DockCalendar";
        DisplayName = ProductName;
        Icon = new IconInfo("");

        _services = AppServices.Create(demo: false);
        _host = new FlyoutHost(_services, new FlyoutOptions(Pinned: false, ExitWhenHidden: false, InitialView: null));
        _host.Start();
        ToggleSignal.Listen(_host.Toggle);

        var toggle = new ToggleCalendarCommand(_host);
        _bandItem = new ListItem(toggle) { Icon = new IconInfo("") };
        _dockItem = new WrappedDockItem([_bandItem], "dockcalendar.dock.calendar", ProductName);
        _commands =
        [
            new CommandItem(toggle)
            {
                Title = ProductName,
                Subtitle = S.T("월간 · 주간 · 일간 보기와 Google 캘린더 일정", "Month, week and day views with Google Calendar events"),
                Icon = new IconInfo(""),
            },
        ];

        UpdateBand();
        _services.Store.Changed += OnStoreChanged;

        // The band text is set in place. Raising ItemsChanged instead would make Command
        // Palette rebuild the whole Dock and reset the other bands.
        _bandTimer = new Timer(_ => UpdateBand(), null, BandTick, BandTick);
        _syncTimer = new Timer(_ => SyncAroundToday(), null, TimeSpan.Zero, SyncInterval);
    }

    public override ICommandItem[] TopLevelCommands() => _commands;

    public override ICommandItem[]? GetDockBands() => [_dockItem];

    private void OnStoreChanged(object? sender, EventArgs args) => UpdateBand();

    private void SyncAroundToday()
    {
        var today = DateTime.Today;
        _ = _services.Store.EnsureAsync(today.AddDays(-7), today.AddDays(8), force: true);
    }

    private void UpdateBand()
    {
        try
        {
            var (title, subtitle) = BandText.For(DateTime.Now, _services.Store);
            _bandItem.Title = title;
            _bandItem.Subtitle = subtitle;
        }
        catch
        {
            // A band refresh must never take the extension down.
        }
    }

    public override void Dispose()
    {
        _bandTimer.Dispose();
        _syncTimer.Dispose();
        _services.Store.Changed -= OnStoreChanged;
        _host.Shutdown();
        _services.Dispose();
        base.Dispose();
    }
}

internal sealed partial class ToggleCalendarCommand : InvokableCommand
{
    private readonly FlyoutHost _host;

    public ToggleCalendarCommand(FlyoutHost host)
    {
        _host = host;
        Id = "dockcalendar.command.toggle";
        Name = DockCalendarCommandsProvider.ProductName;
        Icon = new IconInfo("");
    }

    public override ICommandResult Invoke()
    {
        _host.Toggle();
        return CommandResult.KeepOpen();
    }
}

/// <summary>What the Dock band shows: today's date and the next thing on it.</summary>
internal static class BandText
{
    private const int MaxTitleLength = 28;

    public static (string Title, string Subtitle) For(DateTime now, CalendarStore store)
    {
        var title = ShortDate(now);
        var today = store.EventsBetween(now.Date, now.Date.AddDays(1));

        var next = today
            .Where(item => !item.IsAllDay && item.End > now)
            .OrderBy(item => item.Start)
            .FirstOrDefault();
        if (next is not null)
        {
            return (title, $"{next.Start:t} {Shorten(next.Title)}");
        }

        var allDay = today.FirstOrDefault(item => item.IsAllDay);
        return (title, allDay is null ? string.Empty : Shorten(allDay.Title));
    }

    /// <summary>
    /// The date as short as the culture allows, because the Dock cuts long band titles
    /// off: the culture's month-and-day pattern with the month abbreviated, then the
    /// weekday. "Nov 10 (Tue)" in English, "11월 10일 (화)" in Korean.
    /// </summary>
    internal static string ShortDate(DateTime date)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        var monthDay = culture.DateTimeFormat.MonthDayPattern.Replace("MMMM", "MMM");
        return $"{date.ToString(monthDay, culture)} ({date.ToString("ddd", culture)})";
    }
    private static string Shorten(string text) =>
        text.Length <= MaxTitleLength ? text : text[..(MaxTitleLength - 1)].TrimEnd() + "…";
}
