using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WWM_ASP.Data;

namespace WWM_ASP.Controllers.Admin;

/// <summary>
/// Staff login/logout for the admin panel.
/// Routes: /admin/login, /admin/logout
/// </summary>
public class AdminAuthController(AppDbContext db) : Controller
{
    [HttpGet("/admin/login")]
    public IActionResult Login()
    {
        if (User.Identity?.IsAuthenticated == true &&
            HttpContext.User.Identity?.AuthenticationType == "staff")
            return RedirectToAction("Index", "Dashboard", new { area = "Admin" });

        return View("~/Views/Admin/Auth/Login.cshtml");
    }

    [HttpPost("/admin/login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LoginPost(string account, string password)
    {
        var staff = await db.Staffs
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Account == account || s.Email == account);

        if (staff == null || !BCrypt.Net.BCrypt.Verify(password, staff.Password))
        {
            ModelState.AddModelError(string.Empty, "Incorrect account or password.");
            return View("~/Views/Admin/Auth/Login.cshtml");
        }

        if (staff.IsDeleted)
        {
            ModelState.AddModelError(string.Empty, "This account has been disabled.");
            return View("~/Views/Admin/Auth/Login.cshtml");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, staff.Id.ToString()),
            new(ClaimTypes.Name,           staff.DisplayName),
            new("role",                    staff.Role),
            new("role_label",              staff.RoleLabel),
        };

        var identity  = new ClaimsIdentity(claims, "staff");
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync("staff", principal, new AuthenticationProperties
        {
            IsPersistent  = true,
            ExpiresUtc    = DateTimeOffset.UtcNow.AddHours(8),
        });

        return RedirectToAction("Index", "Dashboard");
    }

    [HttpPost("/admin/logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync("staff");
        return RedirectToAction(nameof(Login));
    }
}
