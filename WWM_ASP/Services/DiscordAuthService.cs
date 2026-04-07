using System.Net.Http.Headers;
using System.Text.Json;
using System.Web;

namespace WWM_ASP.Services;

/// <summary>
/// Handles Discord OAuth2 flow.
/// Equivalent to Laravel's SocialiteServiceProvider / DiscordController.
/// </summary>
public class DiscordAuthService(HttpClient http, IConfiguration config)
{
    private const string AuthorizeUrl = "https://discord.com/api/oauth2/authorize";
    private const string TokenUrl     = "https://discord.com/api/oauth2/token";
    private const string UserUrl      = "https://discord.com/api/users/@me";

    private string ClientId     => config["Discord:ClientId"]     ?? throw new InvalidOperationException("Discord:ClientId not configured");
    private string ClientSecret => config["Discord:ClientSecret"] ?? throw new InvalidOperationException("Discord:ClientSecret not configured");
    private string RedirectUri  => config["Discord:RedirectUri"]  ?? throw new InvalidOperationException("Discord:RedirectUri not configured");

    // ─── Step 1: Build the OAuth redirect URL ─────────────────────────────
    public string BuildRedirectUrl(string state)
    {
        var q = HttpUtility.ParseQueryString(string.Empty);
        q["client_id"]     = ClientId;
        q["redirect_uri"]  = RedirectUri;
        q["response_type"] = "code";
        q["scope"]         = "identify email";
        q["state"]         = state;
        return $"{AuthorizeUrl}?{q}";
    }

    // ─── Step 2: Exchange code for tokens ────────────────────────────────
    public async Task<DiscordTokenResponse?> ExchangeCodeAsync(string code)
    {
        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"]     = ClientId,
            ["client_secret"] = ClientSecret,
            ["grant_type"]    = "authorization_code",
            ["code"]          = code,
            ["redirect_uri"]  = RedirectUri,
        });

        var res = await http.PostAsync(TokenUrl, body);
        if (!res.IsSuccessStatusCode) return null;

        var json = await res.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<DiscordTokenResponse>(json, JsonOptions);
    }

    // ─── Step 3: Get user info from Discord ──────────────────────────────
    public async Task<DiscordUser?> GetUserAsync(string accessToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, UserUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var res = await http.SendAsync(req);
        if (!res.IsSuccessStatusCode) return null;

        var json = await res.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<DiscordUser>(json, JsonOptions);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };
}

// ─── DTOs ─────────────────────────────────────────────────────────────────────

public record DiscordTokenResponse(
    string AccessToken,
    string TokenType,
    int    ExpiresIn,
    string RefreshToken,
    string Scope
);

public record DiscordUser(
    string  Id,
    string  Username,
    string? Discriminator,
    string? Avatar,
    string? Email
)
{
    public string AvatarUrl => Avatar != null
        ? $"https://cdn.discordapp.com/avatars/{Id}/{Avatar}.png"
        : $"https://ui-avatars.com/api/?name={Uri.EscapeDataString(Username)}";
}
