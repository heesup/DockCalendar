using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DockCalendar.Calendar;

namespace DockCalendar.Ui;

// Drawing: the mini month, the agenda under it and the week/day time grid.
public partial class FlyoutWindow
{
    private const double HourHeight = 46;
    private const double GutterWidth = 54;
    private const int AgendaDays = 7;

    private Border? _nowLine;

    private void RenderAll()
    {
        TimePanel.Visibility = _view == ViewMode.Month ? Visibility.Collapsed : Visibility.Visible;
        MonthTitle.Content = _month.ToString("Y");
        BuildWeekdayHeader();
        BuildMonth();
        BuildAgenda();
        if (_view != ViewMode.Month)
        {
            BuildTimeGrid();
        }

        UpdateStatus();
    }

    private DateTime MonthGridStart() => StartOfWeek(_month);

    private DateTime StartOfWeek(DateTime date)
    {
        var offset = ((int)date.DayOfWeek - (int)_services.Settings.FirstDayOfWeek + 7) % 7;
        return date.Date.AddDays(-offset);
    }

    /// <summary>The days the time grid covers: the selected day, or the week around it.</summary>
    private (DateTime First, int Days) TimeRange() => _view switch
    {
        ViewMode.Week => (StartOfWeek(_selected), 7),
        ViewMode.Day => (_selected, 1),
        _ => (_selected, 0),
    };

    // ---- Mini month ----------------------------------------------------------------

    private void BuildWeekdayHeader()
    {
        WeekdayHeader.Children.Clear();
        var start = MonthGridStart();
        for (var index = 0; index < 7; index++)
        {
            WeekdayHeader.Children.Add(Label(
                CultureInfo.CurrentCulture.DateTimeFormat.GetShortestDayName(start.AddDays(index).DayOfWeek),
                "FgDim",
                12,
                HorizontalAlignment.Center));
        }
    }

    private void BuildMonth()
    {
        MonthGrid.Children.Clear();
        var start = MonthGridStart();
        var events = _store.EventsBetween(start, start.AddDays(42));
        var (rangeFirst, rangeDays) = TimeRange();

        for (var index = 0; index < 42; index++)
        {
            var day = start.AddDays(index);
            var isToday = day == DateTime.Today;
            var isSelected = day == _selected;
            var inRange = rangeDays > 1 && day >= rangeFirst && day < rangeFirst.AddDays(rangeDays);

            var cell = new Grid { Height = 42 };

            // The week shown in the time grid gets a band across its row.
            if (inRange)
            {
                var band = new Border { Margin = new Thickness(-1, 5, -1, 7) };
                band.SetResourceReference(Border.BackgroundProperty, "Card");
                cell.Children.Add(band);
            }

            var circle = new Ellipse { Width = 30, Height = 30, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 0, 0) };
            if (isToday)
            {
                circle.SetResourceReference(Shape.FillProperty, "Accent");
            }
            else if (isSelected)
            {
                circle.StrokeThickness = 1.5;
                circle.SetResourceReference(Shape.StrokeProperty, "Accent");
            }

            cell.Children.Add(circle);

            var number = Label(
                day.Day.ToString(CultureInfo.CurrentCulture),
                isToday ? "OnAccent" : day.Month == _month.Month ? "Fg" : "FgFaint",
                13,
                HorizontalAlignment.Center);
            number.VerticalAlignment = VerticalAlignment.Top;
            number.Margin = new Thickness(0, 9, 0, 0);
            if (isToday)
            {
                number.FontWeight = FontWeights.SemiBold;
            }

            cell.Children.Add(number);

            var colors = events
                .Where(item => item.Overlaps(day, day.AddDays(1)))
                .Select(item => item.Color)
                .Distinct()
                .Take(3)
                .ToList();
            if (colors.Count > 0)
            {
                var dots = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 0, 2),
                };
                foreach (var color in colors)
                {
                    dots.Children.Add(new Ellipse
                    {
                        Width = 4,
                        Height = 4,
                        Margin = new Thickness(1, 0, 1, 0),
                        Fill = Theme.Brush(Theme.Parse(color)),
                    });
                }

                cell.Children.Add(dots);
            }

            var button = new Button { Style = (Style)FindResource("DayButton"), Content = cell, ToolTip = day.ToString("D") };
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.Click += (_, _) => Select(day);
            MonthGrid.Children.Add(button);
        }
    }

    // ---- Agenda --------------------------------------------------------------------

    private void BuildAgenda()
    {
        AgendaList.Children.Clear();
        AgendaTitle.Text = DayHeading(_selected);

        var events = _store.EventsBetween(_selected, _selected.AddDays(AgendaDays));
        for (var index = 0; index < AgendaDays; index++)
        {
            var day = _selected.AddDays(index);
            var dayEvents = events
                .Where(item => item.Overlaps(day, day.AddDays(1)))
                .OrderByDescending(item => item.IsBanner)
                .ThenBy(item => item.Start)
                .ToList();

            if (index == 0)
            {
                if (dayEvents.Count == 0)
                {
                    var empty = Label(S.T("일정 없음", "No events"), "FgFaint", 13, HorizontalAlignment.Left);
                    empty.Margin = new Thickness(8, 6, 0, 8);
                    AgendaList.Children.Add(empty);
                }
            }
            else if (dayEvents.Count == 0)
            {
                continue;
            }
            else
            {
                var heading = Label(DayHeading(day), "FgDim", 12, HorizontalAlignment.Left);
                heading.FontWeight = FontWeights.SemiBold;
                heading.Margin = new Thickness(8, 12, 0, 2);
                AgendaList.Children.Add(heading);
            }

            foreach (var item in dayEvents)
            {
                AgendaList.Children.Add(AgendaRow(item, day));
            }
        }
    }

    private Button AgendaRow(CalendarEvent item, DateTime day)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(new Border
        {
            Width = 4,
            CornerRadius = new CornerRadius(2),
            Background = Theme.Brush(Theme.Parse(item.Color)),
            Margin = new Thickness(0, 1, 10, 1),
        });

        var text = new StackPanel();
        Grid.SetColumn(text, 1);
        var title = Label(item.Title, "Fg", 13.5, HorizontalAlignment.Left);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        text.Children.Add(title);
        text.Children.Add(Label(TimeText(item, day), "FgDim", 12, HorizontalAlignment.Left));
        if (item.Location is not null)
        {
            var location = Label(item.Location, "FgFaint", 12, HorizontalAlignment.Left);
            location.TextTrimming = TextTrimming.CharacterEllipsis;
            text.Children.Add(location);
        }

        grid.Children.Add(text);

        var row = new Button { Style = (Style)FindResource("RowButton"), Content = grid, ToolTip = Describe(item) };
        if (item.Link is null)
        {
            row.Cursor = null;
        }
        else
        {
            row.Click += (_, _) => OpenLink(item.Link);
        }

        return row;
    }

    private static string DayHeading(DateTime day)
    {
        var offset = (day - DateTime.Today).Days;
        var date = $"{day:M} {day:dddd}";
        return offset switch
        {
            0 => S.T("오늘", "Today") + " · " + date,
            1 => S.T("내일", "Tomorrow") + " · " + date,
            -1 => S.T("어제", "Yesterday") + " · " + date,
            _ => date,
        };
    }

    /// <summary>When an event happens, as read on one particular day.</summary>
    private static string TimeText(CalendarEvent item, DateTime day)
    {
        if (item.IsAllDay)
        {
            var lastDay = item.End.AddDays(-1);
            return lastDay > item.Start
                ? S.T("종일", "All day") + $" · {item.Start:M} – {lastDay:M}"
                : S.T("종일", "All day");
        }

        if (item.Start.Date == item.End.Date || item.End == item.Start.Date.AddDays(1))
        {
            return $"{item.Start:t} – {item.End:t}";
        }

        // Runs past midnight: say which part falls on this day.
        var startsToday = item.Start.Date == day;
        var endsToday = item.End.Date == day;
        return startsToday ? $"{item.Start:t} – {item.End:M} {item.End:t}"
            : endsToday ? $"{item.Start:M} {item.Start:t} – {item.End:t}"
            : $"{item.Start:M} {item.Start:t} – {item.End:M} {item.End:t}";
    }

    private static string Describe(CalendarEvent item)
    {
        var lines = new List<string> { item.Title, TimeText(item, item.Start.Date) };
        if (item.Location is not null)
        {
            lines.Add(item.Location);
        }

        return string.Join(Environment.NewLine, lines);
    }

    // ---- Week / day time grid ------------------------------------------------------

    private void BuildTimeGrid()
    {
        var (first, days) = TimeRange();
        var events = _store.EventsBetween(first, first.AddDays(days));

        RangeTitle.Text = days == 1
            ? $"{first:M} {first:dddd}"
            : first.Month == first.AddDays(6).Month
                ? $"{first:M} – {first.AddDays(6).Day.ToString(CultureInfo.CurrentCulture)}{S.T("일", string.Empty)}"
                : $"{first:M} – {first.AddDays(6):M}";

        foreach (var grid in new[] { DayHeaderGrid, BannerGrid, TimeGrid })
        {
            grid.Children.Clear();
            grid.ColumnDefinitions.Clear();
            grid.RowDefinitions.Clear();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(GutterWidth) });
            for (var index = 0; index < days; index++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }
        }

        BuildDayHeaders(first, days);
        BuildBanners(events, first, days);
        BuildHours(first, days);

        for (var index = 0; index < days; index++)
        {
            foreach (var slot in EventLayout.LayoutDay(events, first.AddDays(index)))
            {
                TimeGrid.Children.Add(EventBlock(slot, index + 1));
            }
        }

        _nowLine = null;
        var todayIndex = (DateTime.Today - first).Days;
        if (todayIndex >= 0 && todayIndex < days)
        {
            _nowLine = new Border { Height = 2, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, CornerRadius = new CornerRadius(1) };
            _nowLine.SetResourceReference(Border.BackgroundProperty, "Accent");
            Grid.SetColumn(_nowLine, todayIndex + 1);
            Panel.SetZIndex(_nowLine, 10);
            TimeGrid.Children.Add(_nowLine);
            UpdateNowLine();
        }

        if (_scrollToNow)
        {
            // Open on the working day: an hour and a half above now, or at 7:30 on other days.
            _scrollToNow = false;
            var hour = todayIndex >= 0 && todayIndex < days ? Math.Max(0, DateTime.Now.TimeOfDay.TotalHours - 1.5) : 7.5;
            Dispatcher.BeginInvoke(() => TimeScroll.ScrollToVerticalOffset(hour * HourHeight), DispatcherPriority.Loaded);
        }
    }

    private void BuildDayHeaders(DateTime first, int days)
    {
        for (var index = 0; index < days; index++)
        {
            var day = first.AddDays(index);
            var isToday = day == DateTime.Today;

            var stack = new StackPanel { Margin = new Thickness(0, 2, 0, 4) };
            stack.Children.Add(Label(day.ToString("ddd"), isToday ? "Accent" : "FgDim", 12, HorizontalAlignment.Center));

            var badge = new Grid { Width = 32, Height = 32, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
            if (isToday)
            {
                var circle = new Ellipse();
                circle.SetResourceReference(Shape.FillProperty, "Accent");
                badge.Children.Add(circle);
            }
            else if (day == _selected && days > 1)
            {
                var ring = new Ellipse { StrokeThickness = 1.5 };
                ring.SetResourceReference(Shape.StrokeProperty, "Accent");
                badge.Children.Add(ring);
            }

            var number = Label(day.Day.ToString(CultureInfo.CurrentCulture), isToday ? "OnAccent" : "Fg", 16, HorizontalAlignment.Center);
            number.VerticalAlignment = VerticalAlignment.Center;
            number.FontWeight = FontWeights.SemiBold;
            badge.Children.Add(number);
            stack.Children.Add(badge);

            var button = new Button { Style = (Style)FindResource("DayButton"), Content = stack, ToolTip = day.ToString("D") };
            button.Click += (_, _) => Select(day);
            Grid.SetColumn(button, index + 1);
            DayHeaderGrid.Children.Add(button);
        }
    }

    private void BuildBanners(IReadOnlyList<CalendarEvent> events, DateTime first, int days)
    {
        var banners = EventLayout.LayoutBanners(events, first, days);
        BannerScroll.Visibility = banners.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (banners.Count == 0)
        {
            return;
        }

        for (var row = 0; row <= banners.Max(slot => slot.Row); row++)
        {
            BannerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        var caption = Label(S.T("종일", "All day"), "FgFaint", 10.5, HorizontalAlignment.Right);
        caption.Margin = new Thickness(0, 4, 8, 0);
        BannerGrid.Children.Add(caption);

        foreach (var slot in banners)
        {
            var color = Theme.Parse(slot.Event.Color);
            var text = Label(slot.Event.Title, "Fg", 12, HorizontalAlignment.Left);
            text.TextTrimming = TextTrimming.CharacterEllipsis;

            var bar = new Border
            {
                Background = Theme.Brush(color, _dark ? (byte)0x66 : (byte)0x40),
                BorderBrush = Theme.Brush(color),
                BorderThickness = new Thickness(3, 0, 0, 0),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2, 6, 3),
                Margin = new Thickness(2, 1, 2, 1),
                Child = text,
                ToolTip = Describe(slot.Event),
            };
            MakeClickable(bar, slot.Event);
            Grid.SetColumn(bar, slot.FirstDay + 1);
            Grid.SetColumnSpan(bar, slot.Span);
            Grid.SetRow(bar, slot.Row);
            BannerGrid.Children.Add(bar);
        }
    }

    private void BuildHours(DateTime first, int days)
    {
        TimeGrid.Height = 24 * HourHeight;

        for (var index = 0; index < days; index++)
        {
            var column = new Border { BorderThickness = new Thickness(1, 0, 0, 0) };
            column.SetResourceReference(Border.BorderBrushProperty, "Stroke");
            if (first.AddDays(index) == DateTime.Today && days > 1)
            {
                column.SetResourceReference(Border.BackgroundProperty, "Card");
            }

            Grid.SetColumn(column, index + 1);
            TimeGrid.Children.Add(column);
        }

        for (var hour = 1; hour < 24; hour++)
        {
            var label = Label(HourText(hour), "FgFaint", 10.5, HorizontalAlignment.Right);
            label.VerticalAlignment = VerticalAlignment.Top;
            label.Margin = new Thickness(0, (hour * HourHeight) - 7, 8, 0);
            TimeGrid.Children.Add(label);

            var line = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, hour * HourHeight, 0, 0) };
            line.SetResourceReference(Border.BackgroundProperty, "Stroke");
            Grid.SetColumn(line, 1);
            Grid.SetColumnSpan(line, days);
            TimeGrid.Children.Add(line);
        }
    }

    private Grid EventBlock(TimedSlot slot, int column)
    {
        var top = slot.StartMinute / 60.0 * HourHeight;
        var height = Math.Max(16, ((slot.EndMinute - slot.StartMinute) / 60.0 * HourHeight) - 1);
        var color = Theme.Parse(slot.Event.Color);

        var text = new StackPanel();
        var title = Label(slot.Event.Title, "Fg", 12, HorizontalAlignment.Left);
        title.FontWeight = FontWeights.SemiBold;
        title.TextWrapping = height >= 60 ? TextWrapping.Wrap : TextWrapping.NoWrap;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.MaxHeight = height >= 60 ? 34 : 17;
        text.Children.Add(title);
        if (height >= 36)
        {
            text.Children.Add(Label(slot.Event.Start.ToString("t"), "FgDim", 11, HorizontalAlignment.Left));
        }

        var block = new Border
        {
            Background = Theme.Brush(color, _dark ? (byte)0x66 : (byte)0x40),
            BorderBrush = Theme.Brush(color),
            BorderThickness = new Thickness(3, 0, 0, 0),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, height < 22 ? 0 : 2, 3, 0),
            Margin = new Thickness(0, 0, 2, 0),
            ClipToBounds = true,
            Child = text,
            ToolTip = Describe(slot.Event),
        };
        MakeClickable(block, slot.Event);
        Grid.SetColumn(block, slot.Column);

        // Events that overlap share this host's width through its star columns.
        var host = new Grid
        {
            Height = height,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(2, top, 2, 0),
        };
        for (var index = 0; index < slot.Columns; index++)
        {
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        host.Children.Add(block);
        Grid.SetColumn(host, column);
        Panel.SetZIndex(host, 5);
        return host;
    }

    private void UpdateNowLine()
    {
        if (_nowLine is not null)
        {
            _nowLine.Margin = new Thickness(0, (DateTime.Now.TimeOfDay.TotalHours * HourHeight) - 1, 0, 0);
        }
    }

    private static void MakeClickable(FrameworkElement element, CalendarEvent item)
    {
        if (item.Link is null)
        {
            return;
        }

        element.Cursor = System.Windows.Input.Cursors.Hand;
        element.MouseLeftButtonUp += (_, _) => OpenLink(item.Link);
    }

    private static string HourText(int hour)
    {
        var time = DateTime.Today.AddHours(hour);
        return CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('H')
            ? time.ToString("HH:00", CultureInfo.CurrentCulture)
            : time.ToString(S.T("tt h시", "h tt"), CultureInfo.CurrentCulture);
    }

    // ---- Footer --------------------------------------------------------------------

    private void UpdateStatus()
    {
        var needsSignIn = !_services.IsDemo && _store.SourceState != SourceState.Ready;
        ConnectButton.Visibility = needsSignIn ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Visibility = needsSignIn ? Visibility.Collapsed : Visibility.Visible;
        RefreshButton.IsEnabled = !needsSignIn && !_store.IsSyncing;
        StatusText.ToolTip = null;

        if (_services.IsDemo)
        {
            StatusText.Text = S.T("데모 데이터", "Demo data");
        }
        else if (_store.IsSyncing)
        {
            StatusText.Text = S.T("동기화 중…", "Syncing…");
        }
        else if (_store.Error is { } error)
        {
            StatusText.Text = S.T("동기화 실패: ", "Sync failed: ") + error;
            StatusText.ToolTip = error;
        }
        else if (_store.LastSync is { } synced)
        {
            StatusText.Text = S.T($"Google 캘린더 · {synced:t} 동기화", $"Google Calendar · synced {synced:t}");
        }
        else
        {
            StatusText.Text = string.Empty;
        }
    }

    private static TextBlock Label(string text, string brushKey, double size, HorizontalAlignment alignment)
    {
        var label = new TextBlock { Text = text, FontSize = size, HorizontalAlignment = alignment };
        label.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        return label;
    }
}
