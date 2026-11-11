using DockCalendar.Calendar;
using Xunit;

namespace DockCalendar.Tests;

public class StoreAndSettingsTests
{
    private sealed class FakeSource : ICalendarSource
    {
        public int EventFetches;

        public SourceState State { get; set; } = SourceState.Ready;

        public List<CalendarInfo> Calendars { get; } =
        [
            new("work", "Work", "#039BE5", Primary: true, Selected: true),
            new("extra", "Extra", "#33B679", Primary: false, Selected: false),
        ];

        public Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(CancellationToken cancellation) =>
            Task.FromResult<IReadOnlyList<CalendarInfo>>(Calendars);

        public Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(CalendarInfo calendar, DateTime from, DateTime to, CancellationToken cancellation)
        {
            Interlocked.Increment(ref EventFetches);
            var start = DateTime.Today.AddHours(9);
            return Task.FromResult<IReadOnlyList<CalendarEvent>>(
                [new CalendarEvent(calendar.Id + "-1", calendar.Id, calendar.Name, start, start.AddHours(1), false, null, null, calendar.Color)]);
        }
    }

    private static AppSettings NewSettings() => AppSettings.Load(Path.Combine(Path.GetTempPath(), $"dockcalendar-test-{Guid.NewGuid():N}.json"));

    [Fact]
    public async Task OnlyCalendarsSelectedInGoogleAreFetchedByDefault()
    {
        var source = new FakeSource();
        using var store = new CalendarStore(source, NewSettings(), cachePath: null);

        await store.EnsureAsync(DateTime.Today, DateTime.Today.AddDays(1));

        Assert.Equal(1, source.EventFetches);
        Assert.Equal("Work", Assert.Single(store.EventsBetween(DateTime.Today, DateTime.Today.AddDays(1))).Title);
    }

    [Fact]
    public async Task ACalendarTheUserTurnedOnIsFetchedAndOneTurnedOffIsHidden()
    {
        var source = new FakeSource();
        var settings = NewSettings();
        settings.SetCalendarVisible("extra", true);
        settings.SetCalendarVisible("work", false);
        using var store = new CalendarStore(source, settings, cachePath: null);

        await store.EnsureAsync(DateTime.Today, DateTime.Today.AddDays(1));

        Assert.Equal("Extra", Assert.Single(store.EventsBetween(DateTime.Today, DateTime.Today.AddDays(1))).Title);
    }

    [Fact]
    public async Task ARangeAlreadyLoadedAndFreshIsNotFetchedAgain()
    {
        var source = new FakeSource();
        using var store = new CalendarStore(source, NewSettings(), cachePath: null);

        await store.EnsureAsync(DateTime.Today, DateTime.Today.AddDays(1));
        await store.EnsureAsync(DateTime.Today.AddDays(7), DateTime.Today.AddDays(14));
        Assert.Equal(1, source.EventFetches);

        await store.EnsureAsync(DateTime.Today, DateTime.Today.AddDays(1), force: true);
        Assert.Equal(2, source.EventFetches);
    }

    [Fact]
    public async Task NothingIsFetchedWhileSignedOut()
    {
        var source = new FakeSource { State = SourceState.SignedOut };
        using var store = new CalendarStore(source, NewSettings(), cachePath: null);

        await store.EnsureAsync(DateTime.Today, DateTime.Today.AddDays(1), force: true);

        Assert.Equal(0, source.EventFetches);
    }

    [Fact]
    public void SecretsAreEncryptedInTheSettingsFileAndReadBack()
    {
        var settings = NewSettings();
        settings.ClientSecret = "top-secret";
        settings.RefreshToken = "refresh-token";
        settings.Save();

        try
        {
            var text = File.ReadAllText(settings.Path!);
            Assert.DoesNotContain("top-secret", text);
            Assert.DoesNotContain("refresh-token", text);

            var loaded = AppSettings.Load(settings.Path!);
            Assert.Equal("top-secret", loaded.ClientSecret);
            Assert.Equal("refresh-token", loaded.RefreshToken);
        }
        finally
        {
            File.Delete(settings.Path!);
        }
    }

    [Fact]
    public void TheBandShowsTheNextEventOfTheDay()
    {
        var source = new FakeSource();
        using var store = new CalendarStore(source, NewSettings(), cachePath: null);
        store.EnsureAsync(DateTime.Today, DateTime.Today.AddDays(1)).Wait();

        var (_, before) = BandText.For(DateTime.Today.AddHours(8), store);
        var (_, after) = BandText.For(DateTime.Today.AddHours(11), store);

        Assert.EndsWith("Work", before);
        Assert.Equal(string.Empty, after);
    }
}
