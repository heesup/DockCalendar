using System.Text.Json;

namespace DockCalendar.Calendar;

/// <summary>
/// Holds the events around the dates being looked at. Views read from memory at once;
/// <see cref="EnsureAsync"/> fetches in the background and raises <see cref="Changed"/>.
/// The last result is also kept on disk so the flyout has something to draw on startup.
/// </summary>
internal sealed class CalendarStore : IDisposable
{
    // Fetch well past the visible range so stepping to the next month or week is instant.
    private static readonly TimeSpan FetchPadding = TimeSpan.FromDays(45);
    private static readonly TimeSpan KeepMargin = TimeSpan.FromDays(7);
    private static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(5);

    private readonly ICalendarSource _source;
    private readonly AppSettings _settings;
    private readonly string? _cachePath;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _disposed = new();

    private List<CalendarEvent> _events = [];
    private List<CalendarInfo> _calendars = [];
    private DateTime _loadedFrom;
    private DateTime _loadedTo;
    private DateTime _fetchedAt;
    private Task? _running;
    private (DateTime From, DateTime To, bool Force)? _queued;

    public CalendarStore(ICalendarSource source, AppSettings settings, string? cachePath)
    {
        _source = source;
        _settings = settings;
        _cachePath = cachePath;
        LoadCache();
    }

    /// <summary>Raised on a background thread whenever events, calendars or status change.</summary>
    public event EventHandler? Changed;

    public SourceState SourceState => _source.State;

    public bool IsSyncing { get; private set; }

    /// <summary>Why the last sync failed, or null when it worked.</summary>
    public string? Error { get; private set; }

    public DateTime? LastSync
    {
        get
        {
            lock (_gate)
            {
                return _fetchedAt == default ? null : _fetchedAt;
            }
        }
    }

    public IReadOnlyList<CalendarInfo> Calendars
    {
        get
        {
            lock (_gate)
            {
                return _calendars;
            }
        }
    }

    public bool IsVisible(CalendarInfo calendar) => _settings.IsCalendarVisible(calendar.Id, calendar.Selected);

    /// <summary>Events touching [from, to), from visible calendars, in start order.</summary>
    public IReadOnlyList<CalendarEvent> EventsBetween(DateTime from, DateTime to)
    {
        lock (_gate)
        {
            var visible = _calendars.Where(IsVisible).Select(calendar => calendar.Id).ToHashSet();
            return _events
                .Where(item => visible.Contains(item.CalendarId) && item.Overlaps(from, to))
                .OrderBy(item => item.Start)
                .ThenBy(item => item.Title, StringComparer.CurrentCulture)
                .ToList();
        }
    }

    /// <summary>
    /// Makes sure [from, to) is loaded and recent. Calls made while a fetch is running
    /// are collapsed into one follow-up fetch for the latest range asked for.
    /// </summary>
    public Task EnsureAsync(DateTime from, DateTime to, bool force = false)
    {
        lock (_gate)
        {
            if (_running is not null)
            {
                var keepForce = force || _queued?.Force == true;
                _queued = (from, to, keepForce);
                return _running;
            }

            if (!NeedsFetch(from, to, force))
            {
                return Task.CompletedTask;
            }

            _running = Task.Run(() => RunAsync(from, to));
            return _running;
        }
    }

    /// <summary>Drops everything, for sign-out.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _events = [];
            _calendars = [];
            _loadedFrom = _loadedTo = _fetchedAt = default;
        }

        Error = null;
        try
        {
            if (_cachePath is not null && File.Exists(_cachePath))
            {
                File.Delete(_cachePath);
            }
        }
        catch (Exception)
        {
            // The cache is only a convenience.
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Tells listeners that calendar visibility changed.</summary>
    public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private bool NeedsFetch(DateTime from, DateTime to, bool force)
    {
        if (_source.State != SourceState.Ready)
        {
            return false;
        }

        var covered = _loadedFrom <= from - KeepMargin && _loadedTo >= to + KeepMargin;
        var fresh = DateTime.Now - _fetchedAt < FreshFor;
        return force || !covered || !fresh;
    }

    private async Task RunAsync(DateTime from, DateTime to)
    {
        while (true)
        {
            IsSyncing = true;
            Changed?.Invoke(this, EventArgs.Empty);
            try
            {
                await FetchAsync(from - FetchPadding, to + FetchPadding, _disposed.Token).ConfigureAwait(false);
                Error = null;
            }
            catch (OperationCanceledException) when (_disposed.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                Error = exception.Message;
            }

            lock (_gate)
            {
                if (_queued is { } next && NeedsFetch(next.From, next.To, next.Force))
                {
                    _queued = null;
                    (from, to) = (next.From, next.To);
                    continue;
                }

                _queued = null;
                _running = null;
            }

            IsSyncing = false;
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }
    }

    private async Task FetchAsync(DateTime from, DateTime to, CancellationToken cancellation)
    {
        var calendars = await _source.GetCalendarsAsync(cancellation).ConfigureAwait(false);
        var wanted = calendars.Where(IsVisible).ToList();
        var results = await Task.WhenAll(wanted.Select(async calendar =>
        {
            try
            {
                return (Events: await _source.GetEventsAsync(calendar, from, to, cancellation).ConfigureAwait(false), Failed: (string?)null);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return (Events: (IReadOnlyList<CalendarEvent>)[], Failed: $"{calendar.Name}: {exception.Message}");
            }
        })).ConfigureAwait(false);

        var failures = results.Where(result => result.Failed is not null).Select(result => result.Failed!).ToList();
        if (wanted.Count > 0 && failures.Count == wanted.Count)
        {
            // Nothing could be read: keep what is on screen and report why.
            throw new InvalidOperationException(failures[0]);
        }

        lock (_gate)
        {
            _calendars = calendars.ToList();
            _events = results.SelectMany(result => result.Events).ToList();
            _loadedFrom = from;
            _loadedTo = to;
            _fetchedAt = DateTime.Now;
        }

        SaveCache();
        if (failures.Count > 0)
        {
            throw new InvalidOperationException(string.Join("; ", failures));
        }
    }

    private sealed record Cache(DateTime From, DateTime To, DateTime FetchedAt, List<CalendarInfo> Calendars, List<CalendarEvent> Events);

    private void LoadCache()
    {
        try
        {
            if (_cachePath is null || !File.Exists(_cachePath) || _source.State != SourceState.Ready)
            {
                return;
            }

            var cache = JsonSerializer.Deserialize<Cache>(File.ReadAllText(_cachePath));
            if (cache is null)
            {
                return;
            }

            _calendars = cache.Calendars;
            _events = cache.Events;
            _loadedFrom = cache.From;
            _loadedTo = cache.To;

            // Shown at once, but old enough that the first Ensure call refreshes it.
            _fetchedAt = cache.FetchedAt;
        }
        catch (Exception)
        {
            // An unreadable cache is the same as none.
        }
    }

    private void SaveCache()
    {
        if (_cachePath is null)
        {
            return;
        }

        try
        {
            Cache cache;
            lock (_gate)
            {
                cache = new Cache(_loadedFrom, _loadedTo, _fetchedAt, _calendars, _events);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            File.WriteAllText(_cachePath, JsonSerializer.Serialize(cache));
        }
        catch (Exception)
        {
            // The cache is only a convenience.
        }
    }

    public void Dispose()
    {
        _disposed.Cancel();
        _disposed.Dispose();
    }
}
