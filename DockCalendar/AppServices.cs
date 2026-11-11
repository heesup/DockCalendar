using System.Globalization;
using DockCalendar.Calendar;
using DockCalendar.Google;

namespace DockCalendar;

/// <summary>The objects shared by the Dock band and the flyout window.</summary>
internal sealed class AppServices : IDisposable
{
    private AppServices(AppSettings settings, GoogleAuth auth, CalendarStore store, bool demo)
    {
        Settings = settings;
        Auth = auth;
        Store = store;
        IsDemo = demo;
    }

    public AppSettings Settings { get; }

    public GoogleAuth Auth { get; }

    public CalendarStore Store { get; }

    public bool IsDemo { get; }

    public static AppServices Create(bool demo)
    {
        var settings = AppSettings.Load(AppSettings.DefaultPath);
        var auth = new GoogleAuth(settings);
        ICalendarSource source = demo ? new DemoCalendarSource() : new GoogleCalendarClient(auth);
        var store = new CalendarStore(source, settings, demo ? null : AppSettings.CachePath);
        return new AppServices(settings, auth, store, demo);
    }

    public void Dispose()
    {
        Store.Dispose();
        Auth.Dispose();
    }
}

/// <summary>Korean on a Korean system, English everywhere else.</summary>
internal static class S
{
    private static readonly bool Korean =
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ko", StringComparison.OrdinalIgnoreCase);

    public static string T(string korean, string english) => Korean ? korean : english;
}
