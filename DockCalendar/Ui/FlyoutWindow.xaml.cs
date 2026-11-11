using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DockCalendar.Calendar;

namespace DockCalendar.Ui;

internal enum ViewMode
{
    Month,
    Week,
    Day,
}

/// <summary>
/// The calendar flyout: a clock, mini month and agenda on the left, and for the week
/// and day views a time grid that opens to the right.
/// </summary>
public partial class FlyoutWindow : Window
{
    private const double LeftWidth = 372;
    private const double WeekPanelWidth = 720;
    private const double DayPanelWidth = 340;
    private const double FullHeight = 712;

    // A click on the Dock band first takes focus away (which hides the flyout) and then
    // asks to toggle it. Ignoring a toggle this soon after a hide keeps it closed.
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(400);

    // Command Palette can take longer than that to deliver the click, so the same click
    // is also recognised by where it happened: the pointer has not moved since the
    // window lost focus.
    private static readonly TimeSpan SameClickWindow = TimeSpan.FromSeconds(1.5);
    private const int SameClickDistance = 6;

    private readonly AppServices _services;
    private readonly FlyoutOptions _options;
    private readonly CalendarStore _store;
    private readonly DispatcherTimer _clock;
    private Native.POINT _anchor;
    private DateTime _hiddenAt = DateTime.MinValue;
    private DateTime _focusLostAt = DateTime.MinValue;
    private Native.POINT _focusLostCursor;
    private DateTime _selected = DateTime.Today;
    private DateTime _month = FirstOfMonth(DateTime.Today);
    private DateTime _clockDay = DateTime.Today;
    private ViewMode _view;
    private bool _dark;
    private bool _signingIn;
    private bool _scrollToNow;

    internal FlyoutWindow(AppServices services, FlyoutOptions options)
    {
        _services = services;
        _options = options;
        _store = services.Store;
        InitializeComponent();

        _view = (options.InitialView ?? services.Settings.View).ToLowerInvariant() switch
        {
            "week" => ViewMode.Week,
            "day" => ViewMode.Day,
            _ => ViewMode.Month,
        };

        ApplyTexts();
        WireEvents();
        InitializeSettingsPanel();

        _clock = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => UpdateClock();

        // The store reports from background threads.
        _store.Changed += (_, _) => Dispatcher.BeginInvoke(OnStoreChanged);
    }

    /// <summary>Raised after the flyout has been hidden.</summary>
    internal event EventHandler? Hidden;

    internal void Toggle(Native.POINT cursor)
    {
        if (IsVisible)
        {
            HideFlyout();
            return;
        }

        if (DateTime.UtcNow - _hiddenAt < ReopenGuard || IsClickThatClosedIt(cursor))
        {
            return;
        }

        _anchor = cursor;
        ShowFlyout();
    }

    private void ShowFlyout()
    {
        _dark = Theme.IsDark();
        Theme.Apply(Resources, _dark);

        _selected = DateTime.Today;
        _month = FirstOfMonth(_selected);
        _clockDay = DateTime.Today;
        _scrollToNow = true;
        ShowSettings(false);
        SelectSegment();
        UpdateClock();
        RenderAll();

        var handle = new WindowInteropHelper(this).EnsureHandle();
        Native.ApplyFlyoutChrome(handle, _dark);

        // Placed twice: showing the window on a monitor with another scale resizes it.
        Place();
        Show();

        // WPF writes the window styles again while showing, so strip them afterwards too.
        Native.MakeFlyoutWindow(handle);
        Place();
        _shownAt = DateTime.UtcNow;
        _refocused = false;
        Native.ForceForeground(handle);
        Activate();
        Focus();

        _clock.Start();
        RequestData(force: false);
    }

    private void HideFlyout()
    {
        if (!IsVisible)
        {
            return;
        }

        _clock.Stop();
        Hide();
        _hiddenAt = DateTime.UtcNow;
        Hidden?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Lets the DWM backdrop show through the parts WPF does not paint.
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
            Native.MakeFlyoutWindow(source.Handle);
        }
    }

    // Right after opening, focus can be pulled away again by whatever was clicked to open
    // the window (the Dock re-activating itself, or a launcher process ending). That is
    // not the user leaving, so the window takes focus back once instead of closing.
    private static readonly TimeSpan OpeningGrace = TimeSpan.FromMilliseconds(700);
    private DateTime _shownAt = DateTime.MinValue;
    private bool _refocused;

    private bool RefocusIfJustOpened()
    {
        if (_refocused || DateTime.UtcNow - _shownAt > OpeningGrace)
        {
            return false;
        }

        _refocused = true;
        Dispatcher.BeginInvoke(() =>
        {
            if (IsVisible)
            {
                Native.ForceForeground(new WindowInteropHelper(this).Handle);
                Activate();
            }
        });
        return true;
    }
    /// <summary>True when this toggle comes from the very click that took focus away and hid the window.</summary>
    private bool IsClickThatClosedIt(Native.POINT cursor) =>
        DateTime.UtcNow - _focusLostAt < SameClickWindow &&
        Math.Abs(cursor.X - _focusLostCursor.X) <= SameClickDistance &&
        Math.Abs(cursor.Y - _focusLostCursor.Y) <= SameClickDistance;

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        _focusLostAt = DateTime.UtcNow;
        _focusLostCursor = Native.CursorPosition();

        // A standalone run ends when the flyout hides, which would kill a sign-in that
        // is waiting for the browser.
        if (_options.Pinned || (_signingIn && _options.ExitWhenHidden) || RefocusIfJustOpened())
        {
            return;
        }

        HideFlyout();
    }

    /// <summary>
    /// Puts the window beside the screen edge the pointer is at (where the Dock is),
    /// or in the bottom-right corner when it was opened from somewhere else.
    /// </summary>
    private void Place()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var (monitor, work, scale) = Native.MonitorAt(_anchor);
        var gap = (int)Math.Round(12 * scale);
        var panel = _view switch
        {
            ViewMode.Week => WeekPanelWidth,
            ViewMode.Day => DayPanelWidth,
            _ => 0,
        };

        var width = Math.Min((int)Math.Round((LeftWidth + panel) * scale), work.Width - (2 * gap));
        var height = Math.Min((int)Math.Round(FullHeight * scale), work.Height - (2 * gap));

        var fromTop = _anchor.Y - monitor.Top;
        var fromBottom = monitor.Bottom - _anchor.Y;
        var fromLeft = _anchor.X - monitor.Left;
        var fromRight = monitor.Right - _anchor.X;
        var nearest = Math.Min(Math.Min(fromTop, fromBottom), Math.Min(fromLeft, fromRight));
        var clear = (int)Math.Round(28 * scale);

        int x;
        int y;
        if (nearest > 140 * scale)
        {
            x = work.Right - gap - width;
            y = work.Bottom - gap - height;
        }
        else if (nearest == fromTop)
        {
            x = _anchor.X - (width / 2);
            y = Math.Max(work.Top + gap, _anchor.Y + clear);
        }
        else if (nearest == fromBottom)
        {
            x = _anchor.X - (width / 2);
            y = Math.Min(work.Bottom - gap, _anchor.Y - clear) - height;
        }
        else if (nearest == fromLeft)
        {
            x = Math.Max(work.Left + gap, _anchor.X + clear);
            y = _anchor.Y - (height / 2);
        }
        else
        {
            x = Math.Min(work.Right - gap, _anchor.X - clear) - width;
            y = _anchor.Y - (height / 2);
        }

        x = Math.Clamp(x, work.Left + gap, Math.Max(work.Left + gap, work.Right - gap - width));
        y = Math.Clamp(y, work.Top + gap, Math.Max(work.Top + gap, work.Bottom - gap - height));
        Native.MoveWindow(handle, x, y, width, height);
    }

    private void ApplyTexts()
    {
        MonthSegment.Content = S.T("월", "Month");
        WeekSegment.Content = S.T("주", "Week");
        DaySegment.Content = S.T("일", "Day");
        TodayButton.Content = S.T("오늘", "Today");
        SettingsButton.ToolTip = S.T("설정", "Settings");
        RefreshButton.ToolTip = S.T("지금 동기화", "Sync now");
        ClockButton.ToolTip = S.T("오늘로 이동", "Go to today");
        MonthTitle.ToolTip = S.T("오늘로 이동", "Go to today");
        ConnectButton.Content = S.T("Google 캘린더 연결하기", "Connect Google Calendar");
    }

    private void WireEvents()
    {
        MonthSegment.Click += (_, _) => SetView(ViewMode.Month);
        WeekSegment.Click += (_, _) => SetView(ViewMode.Week);
        DaySegment.Click += (_, _) => SetView(ViewMode.Day);

        ClockButton.Click += (_, _) => Select(DateTime.Today);
        MonthTitle.Click += (_, _) => Select(DateTime.Today);
        TodayButton.Click += (_, _) => Select(DateTime.Today);
        PreviousMonth.Click += (_, _) => StepMonth(-1);
        NextMonth.Click += (_, _) => StepMonth(1);
        PreviousRange.Click += (_, _) => StepRange(-1);
        NextRange.Click += (_, _) => StepRange(1);
        MonthGrid.PreviewMouseWheel += (_, args) =>
        {
            StepMonth(args.Delta > 0 ? -1 : 1);
            args.Handled = true;
        };

        RefreshButton.Click += (_, _) => RequestData(force: true);
        SettingsButton.Click += (_, _) => ShowSettings(SettingsPanel.Visibility != Visibility.Visible);
        ConnectButton.Click += (_, _) => ShowSettings(true);
        PreviewKeyDown += OnKey;
    }

    private void OnKey(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.Escape)
        {
            if (SettingsPanel.Visibility == Visibility.Visible)
            {
                ShowSettings(false);
            }
            else
            {
                HideFlyout();
            }

            args.Handled = true;
            return;
        }

        // Typing in the settings fields must not drive the calendar.
        if (Keyboard.FocusedElement is TextBox or PasswordBox || SettingsPanel.Visibility == Visibility.Visible)
        {
            return;
        }

        args.Handled = true;
        switch (args.Key)
        {
            case Key.Left: Select(_selected.AddDays(-1)); break;
            case Key.Right: Select(_selected.AddDays(1)); break;
            case Key.Up: Select(_selected.AddDays(-7)); break;
            case Key.Down: Select(_selected.AddDays(7)); break;
            case Key.PageUp: Select(_selected.AddMonths(-1)); break;
            case Key.PageDown: Select(_selected.AddMonths(1)); break;
            case Key.Home or Key.T: Select(DateTime.Today); break;
            case Key.M: SetView(ViewMode.Month); break;
            case Key.W: SetView(ViewMode.Week); break;
            case Key.D: SetView(ViewMode.Day); break;
            case Key.F5: RequestData(force: true); break;
            default: args.Handled = false; break;
        }
    }

    private void SetView(ViewMode view)
    {
        if (_view == view)
        {
            SelectSegment();
            return;
        }

        _view = view;
        _scrollToNow = true;
        _services.Settings.View = view.ToString().ToLowerInvariant();
        _services.Settings.Save();
        SelectSegment();
        RenderAll();
        Place();
        RequestData(force: false);
    }

    private void SelectSegment()
    {
        MonthSegment.IsChecked = _view == ViewMode.Month;
        WeekSegment.IsChecked = _view == ViewMode.Week;
        DaySegment.IsChecked = _view == ViewMode.Day;
    }

    private void Select(DateTime date)
    {
        _selected = date.Date;
        _month = FirstOfMonth(_selected);
        RenderAll();
        RequestData(force: false);
    }

    private void StepMonth(int direction)
    {
        _month = _month.AddMonths(direction);
        RenderAll();
        RequestData(force: false);
    }

    private void StepRange(int direction) =>
        Select(_selected.AddDays(direction * (_view == ViewMode.Week ? 7 : 1)));

    /// <summary>Asks the store for everything currently on screen.</summary>
    private void RequestData(bool force)
    {
        var from = MonthGridStart();
        var to = from.AddDays(42);
        var (first, days) = TimeRange();
        if (first < from)
        {
            from = first;
        }

        if (first.AddDays(days) > to)
        {
            to = first.AddDays(days);
        }

        _ = _store.EnsureAsync(from, to, force);
    }

    private void OnStoreChanged()
    {
        if (!IsVisible)
        {
            return;
        }

        RenderAll();
        if (SettingsPanel.Visibility == Visibility.Visible)
        {
            RefreshSettingsPanel();
        }
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        ClockTime.Text = now.ToString("T");
        ClockDate.Text = now.ToString("D");

        if (now.Date != _clockDay)
        {
            // Midnight passed while the flyout was open.
            _clockDay = now.Date;
            RenderAll();
        }
        else if (now.Second == 0)
        {
            UpdateNowLine();
        }
    }

    private static void OpenLink(string? link)
    {
        if (string.IsNullOrWhiteSpace(link) || !link.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // No default browser: nothing useful to do.
        }
    }

    private static DateTime FirstOfMonth(DateTime date) => new(date.Year, date.Month, 1);
}
