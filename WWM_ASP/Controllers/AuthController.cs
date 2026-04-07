using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WWM_ASP.Data;
using WWM_ASP.Models;
using WWM_ASP.Services;

namespace WWM_ASP.Controllers;

/// <summary>
/// Handles regular user authentication:
///   - Username + password login
///   - Discord OAuth
///   - Logout
/// </summary>
public class AuthController(AppDbContext db, DiscordAuthService discord) : Controller
{
    // ─── Username/Password login ──────────────────────────────────────────

    [HttpGet("/login")]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");

        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    [HttpPost("/login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LoginPost(string account, string password, string? returnUrl = null)
    {
        var user = await db.Users
            .IgnoreQueryFilters()   // include soft-deleted to show appropriate error
            .FirstOrDefaultAsync(u => u.Account == account || u.Email == account);

        if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.Password))
        {
            ModelState.AddModelError(string.Empty, "Incorrect account or password.");
            return View("Login");
        }

        if (user.IsDeleted)
        {
            ModelState.AddModelError(string.Empty, "This account has been disabled.");
            return View("Login");
        }

        await SignInUserAsync(user);

        return Redirect(returnUrl ?? Url.Action("Index", "Home")!);
    }

    // ─── Discord OAuth ────────────────────────────────────────────────────

    [HttpGet("/auth/discord")]
    public IActionResult DiscordRedirect()
    {
        var state = Guid.NewGuid().ToString("N");
        HttpContext.Session.SetString("discord_state", state);
        return Redirect(discord.BuildRedirectUrl(state));
    }

    [HttpGet("/auth/discord/callback")]
    public async Task<IActionResult> DiscordCallback(string? code, string? state, string? error)
    {
        if (error != null || code == null)
            return RedirectToAction(nameof(Login));

        // Validate state to prevent CSRF
        var savedState = HttpContext.Session.GetString("discord_state");
        if (state != savedState)
            return RedirectToAction(nameof(Login));

        HttpContext.Session.Remove("discord_state");

        var tokens = await discord.ExchangeCodeAsync(code);
        if (tokens == null)
        {
            TempData["Error"] = "Failed to authenticate with Discord.";
            return RedirectToAction(nameof(Login));
        }

        var discordUser = await discord.GetUserAsync(tokens.AccessToken);
        if (discordUser == null)
        {
            TempData["Error"] = "Failed to get user info from Discord.";
            return RedirectToAction(nameof(Login));
        }

        // Find or create user
        var user = await db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.DiscordId == discordUser.Id);

        if (user == null)
        {
            // New user — create account
            user = new User
            {
                DiscordId           = discordUser.Id,
                Name                = discordUser.Username,
                Account             = discordUser.Username,
                Email               = discordUser.Email,
                DiscordToken        = tokens.AccessToken,
                DiscordRefreshToken = tokens.RefreshToken,
                DiscordAvatar       = discordUser.Avatar,
                ZCoins              = 5000,
                CreatedAt           = DateTime.UtcNow,
                UpdatedAt           = DateTime.UtcNow,
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }
        else
        {
            // Update tokens + avatar
            user.DiscordToken        = tokens.AccessToken;
            user.DiscordRefreshToken = tokens.RefreshToken;
            user.DiscordAvatar       = discordUser.Avatar;
            user.UpdatedAt           = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        if (user.IsDeleted)
        {
            TempData["Error"] = "This account has been disabled.";
            return RedirectToAction(nameof(Login));
        }

        await SignInUserAsync(user);
        return RedirectToAction("Index", "Home");
    }

    // ─── Logout ───────────────────────────────────────────────────────────

    [HttpPost("/logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync("user");
        return RedirectToAction(nameof(Login));
    }

    // ─── Helper ───────────────────────────────────────────────────────────

    private async Task SignInUserAsync(User user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name,           user.DisplayName),
            new("avatar",                  user.AvatarUrl),
            new("z_coins",                 user.ZCoins.ToString()),
        };

        var identity  = new ClaimsIdentity(claims, "user");
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync("user", principal, new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc   = DateTimeOffset.UtcNow.AddDays(30),
        });
    }
}
