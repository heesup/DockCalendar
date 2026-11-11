using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DockCalendar.Google;

/// <summary>The saved sign-in no longer works; the user has to sign in again.</summary>
internal sealed class SignInRequiredException(string message) : Exception(message);

/// <summary>
/// Google sign-in for an installed app: the system browser, a one-off loopback
/// redirect and PKCE. Only the read-only calendar scope is requested. The refresh
/// token is kept DPAPI-encrypted in the settings file; access tokens stay in memory.
/// </summary>
internal sealed class GoogleAuth : IDisposable
{
    public const string Scope = "https://www.googleapis.com/auth/calendar.readonly";
    private const string AuthorizeEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(5);

    private readonly AppSettings _settings;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private string? _accessToken;
    private DateTime _accessTokenExpires;

    public GoogleAuth(AppSettings settings)
    {
        _settings = settings;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.ClientId);

    public bool IsSignedIn => IsConfigured && _settings.RefreshToken is not null;

    /// <summary>
    /// Opens the browser on Google's consent page and waits for it to redirect back.
    /// </summary>
    public async Task SignInAsync(CancellationToken cancellation)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(S.T("OAuth 클라이언트 ID를 먼저 입력하세요.", "Enter an OAuth client ID first."));
        }

        var verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));
        var redirect = $"http://127.0.0.1:{FreePort()}/";

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirect);
        listener.Start();

        var url = AuthorizeEndpoint +
            "?response_type=code" +
            $"&client_id={Uri.EscapeDataString(_settings.ClientId!.Trim())}" +
            $"&redirect_uri={Uri.EscapeDataString(redirect)}" +
            $"&scope={Uri.EscapeDataString(Scope)}" +
            $"&state={state}" +
            $"&code_challenge={challenge}" +
            "&code_challenge_method=S256" +
            "&access_type=offline" +
            "&prompt=consent";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(SignInTimeout);

        string code;
        while (true)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                throw new TimeoutException(S.T("로그인 시간이 초과되었습니다.", "Sign-in timed out."));
            }

            var query = context.Request.QueryString;
            if (query["state"] != state)
            {
                // Something else on this machine probing the port (a favicon request, say).
                await RespondAsync(context, 400, "Unexpected request.").ConfigureAwait(false);
                continue;
            }

            if (query["error"] is { } error)
            {
                await RespondAsync(context, 200, S.T("로그인이 취소되었습니다. 이 탭을 닫아도 됩니다.", "Sign-in was cancelled. You can close this tab.")).ConfigureAwait(false);
                throw new InvalidOperationException(S.T($"Google 로그인 거부됨: {error}", $"Google sign-in was refused: {error}"));
            }

            code = query["code"] ?? string.Empty;
            await RespondAsync(context, 200, S.T("Dock Calendar에 연결되었습니다. 이 탭을 닫아도 됩니다.", "Dock Calendar is connected. You can close this tab.")).ConfigureAwait(false);
            break;
        }

        var fields = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirect,
            ["code_verifier"] = verifier,
        };
        using var token = await PostTokenAsync(fields, cancellation).ConfigureAwait(false);
        var root = token.RootElement;
        if (!root.TryGetProperty("refresh_token", out var refresh) || refresh.GetString() is not { Length: > 0 } refreshToken)
        {
            throw new InvalidOperationException(S.T("Google이 갱신 토큰을 주지 않았습니다. 다시 시도하세요.", "Google returned no refresh token. Try again."));
        }

        _settings.RefreshToken = refreshToken;
        _settings.Save();
        RememberAccessToken(root);
    }

    public void SignOut()
    {
        _accessToken = null;
        _settings.RefreshToken = null;
        _settings.AccountName = null;
        _settings.Save();
    }

    /// <summary>A valid access token, refreshed when the one in memory is about to expire.</summary>
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellation)
    {
        await _tokenGate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            if (_accessToken is not null && DateTime.UtcNow < _accessTokenExpires)
            {
                return _accessToken;
            }

            var refreshToken = _settings.RefreshToken
                ?? throw new SignInRequiredException(S.T("Google에 로그인하세요.", "Sign in to Google."));

            using var token = await PostTokenAsync(
                new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = refreshToken,
                },
                cancellation).ConfigureAwait(false);
            RememberAccessToken(token.RootElement);
            return _accessToken!;
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    /// <summary>Forgets the access token after the API rejected it.</summary>
    public void InvalidateAccessToken() => _accessToken = null;

    private async Task<JsonDocument> PostTokenAsync(Dictionary<string, string> fields, CancellationToken cancellation)
    {
        fields["client_id"] = _settings.ClientId!.Trim();
        if (_settings.ClientSecret is { Length: > 0 } secret)
        {
            fields["client_secret"] = secret.Trim();
        }

        using var response = await _http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(fields), cancellation).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            return JsonDocument.Parse(body);
        }

        var (error, description) = ReadError(body);
        if (error == "invalid_grant")
        {
            // The refresh token was revoked or expired: only a new sign-in fixes it.
            _settings.RefreshToken = null;
            _settings.Save();
            throw new SignInRequiredException(S.T("Google 로그인이 만료되었습니다. 다시 로그인하세요.", "The Google sign-in expired. Sign in again."));
        }

        throw new InvalidOperationException($"Google OAuth: {description ?? error ?? ((int)response.StatusCode).ToString()}");
    }

    private void RememberAccessToken(JsonElement root)
    {
        _accessToken = root.GetProperty("access_token").GetString();
        var seconds = root.TryGetProperty("expires_in", out var expires) ? expires.GetInt32() : 3600;
        _accessTokenExpires = DateTime.UtcNow.AddSeconds(seconds - 60);
    }

    private static (string? Error, string? Description) ReadError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            return (
                root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String ? error.GetString() : null,
                root.TryGetProperty("error_description", out var description) ? description.GetString() : null);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static async Task RespondAsync(HttpListenerContext context, int status, string message)
    {
        var html = "<!doctype html><meta charset=\"utf-8\"><title>Dock Calendar</title>" +
            "<body style=\"font-family:Segoe UI,sans-serif;display:grid;place-items:center;height:90vh;color:#222\">" +
            $"<p style=\"font-size:18px\">{WebUtility.HtmlEncode(message)}</p></body>";
        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.StatusCode = status;
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        context.Response.Close();
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public void Dispose()
    {
        _http.Dispose();
        _tokenGate.Dispose();
    }
}
