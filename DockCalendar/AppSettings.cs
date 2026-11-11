using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DockCalendar;

/// <summary>
/// Settings kept in %LocalAppData%\DockCalendar\settings.json. The OAuth client secret
/// and refresh token are encrypted with DPAPI for the current Windows user before
/// they are written, so the file holds no usable credential on its own.
/// </summary>
internal sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _gate = new();

    public static string Directory { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DockCalendar");

    public static string DefaultPath => System.IO.Path.Combine(Directory, "settings.json");

    public static string CachePath => System.IO.Path.Combine(Directory, "cache.json");

    [JsonIgnore]
    public string? Path { get; private set; }

    public string? ClientId { get; set; }

    public string? ClientSecretProtected { get; set; }

    public string? RefreshTokenProtected { get; set; }

    public string? AccountName { get; set; }

    /// <summary>"month", "week" or "day": the view the flyout opens in.</summary>
    public string View { get; set; } = "month";

    /// <summary>Null follows the Windows regional setting.</summary>
    public bool? WeekStartsMonday { get; set; }

    /// <summary>Calendars the user switched off or on, overriding Google's own selection.</summary>
    public List<string> HiddenCalendars { get; set; } = [];

    public List<string> ShownCalendars { get; set; } = [];

    [JsonIgnore]
    public string? ClientSecret
    {
        get => Unprotect(ClientSecretProtected);
        set => ClientSecretProtected = Protect(value);
    }

    [JsonIgnore]
    public string? RefreshToken
    {
        get => Unprotect(RefreshTokenProtected);
        set => RefreshTokenProtected = Protect(value);
    }

    [JsonIgnore]
    public DayOfWeek FirstDayOfWeek => WeekStartsMonday switch
    {
        true => DayOfWeek.Monday,
        false => DayOfWeek.Sunday,
        null => CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek,
    };

    public bool IsCalendarVisible(string id, bool selectedInGoogle) =>
        !HiddenCalendars.Contains(id) && (selectedInGoogle || ShownCalendars.Contains(id));

    public void SetCalendarVisible(string id, bool visible)
    {
        HiddenCalendars.Remove(id);
        ShownCalendars.Remove(id);
        (visible ? ShownCalendars : HiddenCalendars).Add(id);
    }

    public static AppSettings Load(string path)
    {
        AppSettings? settings = null;
        try
        {
            if (File.Exists(path))
            {
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
            }
        }
        catch (Exception)
        {
            // A damaged settings file falls back to defaults rather than blocking startup.
        }

        settings ??= new AppSettings();
        settings.Path = path;
        return settings;
    }

    public void Save()
    {
        if (Path is null)
        {
            return;
        }

        lock (_gate)
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, JsonOptions));
        }
    }

    private static string? Protect(string? value) =>
        string.IsNullOrEmpty(value)
            ? null
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));

    private static string? Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser));
        }
        catch (Exception)
        {
            // Encrypted for another user or machine: treat as not set.
            return null;
        }
    }
}
