using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using DockCalendar.Calendar;

namespace DockCalendar.Google;

/// <summary>Reads calendars and events from the Google Calendar v3 REST API.</summary>
internal sealed class GoogleCalendarClient : ICalendarSource
{
    private const string Api = "https://www.googleapis.com/calendar/v3";
    private const string DefaultColor = "#039BE5";

    // Google's event colour ids, as the Calendar web app names and paints them.
    private static readonly Dictionary<string, string> EventColors = new()
    {
        ["1"] = "#7986CB",
        ["2"] = "#33B679",
        ["3"] = "#8E24AA",
        ["4"] = "#E67C73",
        ["5"] = "#F6BF26",
        ["6"] = "#F4511E",
        ["7"] = "#039BE5",
        ["8"] = "#616161",
        ["9"] = "#3F51B5",
        ["10"] = "#0B8043",
        ["11"] = "#D50000",
    };

    private readonly GoogleAuth _auth;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public GoogleCalendarClient(GoogleAuth auth)
    {
        _auth = auth;
    }

    public SourceState State =>
        !_auth.IsConfigured ? SourceState.NotConfigured
        : !_auth.IsSignedIn ? SourceState.SignedOut
        : SourceState.Ready;

    public async Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(CancellationToken cancellation)
    {
        var calendars = new List<CalendarInfo>();
        string? pageToken = null;
        do
        {
            var url = $"{Api}/users/me/calendarList?maxResults=250&fields=nextPageToken,items(id,summary,summaryOverride,backgroundColor,primary,selected)";
            if (pageToken is not null)
            {
                url += $"&pageToken={Uri.EscapeDataString(pageToken)}";
            }

            using var document = await GetAsync(url, cancellation).ConfigureAwait(false);
            calendars.AddRange(ParseCalendars(document.RootElement));
            pageToken = Text(document.RootElement, "nextPageToken");
        }
        while (pageToken is not null);

        return calendars
            .OrderByDescending(calendar => calendar.Primary)
            .ThenBy(calendar => calendar.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(CalendarInfo calendar, DateTime from, DateTime to, CancellationToken cancellation)
    {
        var events = new List<CalendarEvent>();
        string? pageToken = null;
        do
        {
            // singleEvents expands recurring events into their occurrences.
            var url = $"{Api}/calendars/{Uri.EscapeDataString(calendar.Id)}/events" +
                "?singleEvents=true&orderBy=startTime&maxResults=2500" +
                $"&timeMin={Uri.EscapeDataString(Rfc3339(from))}" +
                $"&timeMax={Uri.EscapeDataString(Rfc3339(to))}" +
                "&fields=nextPageToken,items(id,status,summary,location,htmlLink,colorId,start,end,attendees(self,responseStatus))";
            if (pageToken is not null)
            {
                url += $"&pageToken={Uri.EscapeDataString(pageToken)}";
            }

            using var document = await GetAsync(url, cancellation).ConfigureAwait(false);
            events.AddRange(ParseEvents(document.RootElement, calendar));
            pageToken = Text(document.RootElement, "nextPageToken");
        }
        while (pageToken is not null);

        return events;
    }

    internal static IEnumerable<CalendarInfo> ParseCalendars(JsonElement root)
    {
        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (Text(item, "id") is not { } id)
            {
                continue;
            }

            yield return new CalendarInfo(
                id,
                Text(item, "summaryOverride") ?? Text(item, "summary") ?? id,
                Text(item, "backgroundColor")?.ToUpperInvariant() ?? DefaultColor,
                Flag(item, "primary"),
                Flag(item, "selected"));
        }
    }

    internal static IEnumerable<CalendarEvent> ParseEvents(JsonElement root, CalendarInfo calendar)
    {
        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (Text(item, "status") == "cancelled" || DeclinedBySelf(item) || Text(item, "id") is not { } id)
            {
                continue;
            }

            if (!item.TryGetProperty("start", out var startElement) || !item.TryGetProperty("end", out var endElement))
            {
                continue;
            }

            var start = ReadTime(startElement, out var allDay);
            var end = ReadTime(endElement, out _);
            if (start is null || end is null)
            {
                continue;
            }

            var color = Text(item, "colorId") is { } colorId && EventColors.TryGetValue(colorId, out var eventColor)
                ? eventColor
                : calendar.Color;

            yield return new CalendarEvent(
                id,
                calendar.Id,
                Text(item, "summary") is { Length: > 0 } title ? title : S.T("(제목 없음)", "(No title)"),
                start.Value,
                end.Value,
                allDay,
                Text(item, "location") is { Length: > 0 } location ? location : null,
                Text(item, "htmlLink"),
                color);
        }
    }

    /// <summary>All-day events carry a bare date; timed ones an RFC 3339 instant, shown in local time.</summary>
    private static DateTime? ReadTime(JsonElement element, out bool allDay)
    {
        allDay = false;
        if (Text(element, "date") is { } date &&
            DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            allDay = true;
            return day;
        }

        if (Text(element, "dateTime") is { } dateTime &&
            DateTimeOffset.TryParse(dateTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant))
        {
            return instant.LocalDateTime;
        }

        return null;
    }

    private static bool DeclinedBySelf(JsonElement item)
    {
        if (!item.TryGetProperty("attendees", out var attendees) || attendees.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var attendee in attendees.EnumerateArray())
        {
            if (Flag(attendee, "self") && Text(attendee, "responseStatus") == "declined")
            {
                return true;
            }
        }

        return false;
    }

    private async Task<JsonDocument> GetAsync(string url, CancellationToken cancellation)
    {
        for (var attempt = 0; ; attempt++)
        {
            var token = await _auth.GetAccessTokenAsync(cancellation).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await _http.SendAsync(request, cancellation).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return JsonDocument.Parse(body);
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                // The token was revoked early; get a new one and retry once.
                _auth.InvalidateAccessToken();
                continue;
            }

            throw new InvalidOperationException($"Google Calendar: {ErrorMessage(body) ?? ((int)response.StatusCode).ToString()}");
        }
    }

    private static string? ErrorMessage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
                ? Text(error, "message")
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Rfc3339(DateTime local) =>
        local.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
