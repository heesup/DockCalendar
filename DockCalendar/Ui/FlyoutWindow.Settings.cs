using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using DockCalendar.Calendar;
using DockCalendar.Google;

namespace DockCalendar.Ui;

// The settings panel: Google sign-in, which calendars to show, and the first day of the week.
public partial class FlyoutWindow
{
    private const string CredentialsPage = "https://console.cloud.google.com/apis/credentials";

    private CancellationTokenSource? _signIn;
    private string? _signInMessage;

    private void InitializeSettingsPanel()
    {
        SettingsTitle.Text = S.T("설정", "Settings");
        AccountHeading.Text = S.T("Google 캘린더", "Google Calendar");
        ClientIdLabel.Text = S.T("OAuth 클라이언트 ID", "OAuth client ID");
        ClientSecretLabel.Text = S.T("클라이언트 보안 비밀번호", "Client secret");
        SignOutButton.Content = S.T("로그아웃", "Sign out");
        HelpText.Text = S.T(
            "Google Cloud Console에서 Calendar API를 사용 설정하고 '데스크톱 앱' 유형의 OAuth 클라이언트를 만든 뒤, 클라이언트 ID와 보안 비밀번호를 붙여 넣으세요. 일정은 읽기 전용으로만 가져옵니다.",
            "In Google Cloud Console, enable the Calendar API and create an OAuth client of type \"Desktop app\", then paste its client ID and secret here. Events are read-only.");
        ConsoleLink.Content = S.T("Google Cloud Console 열기", "Open Google Cloud Console");
        CalendarsHeading.Text = S.T("표시할 캘린더", "Calendars");
        WeekStartHeading.Text = S.T("한 주의 시작", "Week starts on");
        WeekStartAuto.Content = S.T("자동", "Auto");
        WeekStartSunday.Content = S.T("일요일", "Sunday");
        WeekStartMonday.Content = S.T("월요일", "Monday");

        SettingsBack.Click += (_, _) => ShowSettings(false);
        ConsoleLink.Click += (_, _) => OpenLink(CredentialsPage);
        SignInButton.Click += async (_, _) => await SignInOrCancelAsync();
        SignOutButton.Click += (_, _) => SignOut();
        WeekStartAuto.Click += (_, _) => SetWeekStart(null);
        WeekStartSunday.Click += (_, _) => SetWeekStart(false);
        WeekStartMonday.Click += (_, _) => SetWeekStart(true);
    }

    private void ShowSettings(bool show)
    {
        SettingsPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        CalendarPanel.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        if (!show)
        {
            return;
        }

        ClientIdBox.Text = _services.Settings.ClientId ?? string.Empty;
        ClientSecretBox.Password = _services.Settings.ClientSecret ?? string.Empty;
        _signInMessage = null;
        RefreshSettingsPanel();
    }

    private void RefreshSettingsPanel()
    {
        var settings = _services.Settings;
        var signedIn = _services.Auth.IsSignedIn;
        var demo = _services.IsDemo;

        AccountStatus.Text = _signInMessage ?? (demo
            ? S.T("데모 모드에서는 샘플 일정을 보여 줍니다.", "Demo mode shows sample events.")
            : signedIn
                ? S.T("연결됨", "Connected") + (_store.Calendars.FirstOrDefault(calendar => calendar.Primary) is { } primary ? $" · {primary.Name}" : string.Empty)
                : S.T("연결되지 않음", "Not connected"));

        SignInButton.Content = _signingIn
            ? S.T("취소", "Cancel")
            : signedIn ? S.T("다시 로그인", "Sign in again") : S.T("Google로 로그인", "Sign in with Google");
        SignInButton.IsEnabled = !demo;
        SignOutButton.Visibility = signedIn && !_signingIn ? Visibility.Visible : Visibility.Collapsed;
        ClientIdBox.IsEnabled = ClientSecretBox.IsEnabled = !_signingIn && !demo;

        WeekStartAuto.IsChecked = settings.WeekStartsMonday is null;
        WeekStartSunday.IsChecked = settings.WeekStartsMonday == false;
        WeekStartMonday.IsChecked = settings.WeekStartsMonday == true;

        CalendarList.Children.Clear();
        CalendarsHeading.Visibility = _store.Calendars.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        foreach (var calendar in _store.Calendars)
        {
            CalendarList.Children.Add(CalendarRow(calendar));
        }
    }

    private Button CalendarRow(CalendarInfo calendar)
    {
        var visible = _store.IsVisible(calendar);
        var color = Theme.Brush(Theme.Parse(calendar.Color));

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Ellipse
        {
            Width = 14,
            Height = 14,
            Stroke = color,
            StrokeThickness = 2,
            Fill = visible ? color : null,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var name = Label(calendar.Name, visible ? "Fg" : "FgFaint", 13, HorizontalAlignment.Left);
        name.MaxWidth = 280;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        row.Children.Add(name);

        var button = new Button { Style = (Style)FindResource("RowButton"), Content = row };
        button.Click += (_, _) =>
        {
            _services.Settings.SetCalendarVisible(calendar.Id, !visible);
            _services.Settings.Save();
            _store.NotifyChanged();

            // A calendar that was hidden has not been fetched yet.
            RequestData(force: true);
        };
        return button;
    }

    private void SetWeekStart(bool? monday)
    {
        _services.Settings.WeekStartsMonday = monday;
        _services.Settings.Save();
        RenderAll();
        RequestData(force: false);
    }

    private async Task SignInOrCancelAsync()
    {
        if (_signingIn)
        {
            _signIn?.Cancel();
            return;
        }

        var settings = _services.Settings;
        settings.ClientId = ClientIdBox.Text.Trim();
        settings.ClientSecret = ClientSecretBox.Password.Trim();
        settings.Save();

        _signIn = new CancellationTokenSource();
        _signingIn = true;
        _signInMessage = S.T("브라우저에서 Google 로그인을 완료하세요…", "Finish signing in to Google in your browser…");
        RefreshSettingsPanel();

        try
        {
            await _services.Auth.SignInAsync(_signIn.Token);
            _signInMessage = null;
            _store.Clear();
        }
        catch (OperationCanceledException)
        {
            _signInMessage = S.T("로그인을 취소했습니다.", "Sign-in cancelled.");
        }
        catch (Exception exception)
        {
            _signInMessage = exception.Message;
        }
        finally
        {
            _signingIn = false;
            _signIn.Dispose();
            _signIn = null;
        }

        // The browser took focus and the flyout hid itself; bring it back with the outcome.
        // Showing the panel clears the message, so hold on to a failure and put it back.
        var failure = _signInMessage;
        if (!IsVisible)
        {
            ShowFlyout();
        }

        ShowSettings(failure is not null);
        if (failure is not null)
        {
            _signInMessage = failure;
            RefreshSettingsPanel();
        }

        RenderAll();
        RequestData(force: true);
    }

    private void SignOut()
    {
        _services.Auth.SignOut();
        _store.Clear();
        _signInMessage = null;
        RefreshSettingsPanel();
        RenderAll();
    }
}
