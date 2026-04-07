using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WWM_ASP.Data;
using WWM_ASP.Models;

namespace WWM_ASP.Controllers;

[Authorize(Policy = "UserAuth")]
[Route("profile")]
public class ProfileController(AppDbContext db, IWebHostEnvironment env) : Controller
{
    private long CurrentUserId =>
        long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // ─── Edit ─────────────────────────────────────────────────────────────

    [HttpGet("")]
    public async Task<IActionResult> Edit()
    {
        var user = await db.Users.FindAsync(CurrentUserId);
        if (user == null) return NotFound();

        return View("~/Views/Profile/Edit.cshtml", user);
    }

    // ─── Update profile info ───────────────────────────────────────────────

    [HttpPost("update")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(
        string?   name,
        string?   account,
        string?   email,
        string?   country,
        string?   onlineFrom,
        string?   onlineTo,
        string?   ingameName,
        string?   ingameId,
        IFormFile? avatar)
    {
        var user = await db.Users.FindAsync(CurrentUserId);
        if (user == null) return NotFound();

        // Name, account, email only editable for non-Discord users
        if (string.IsNullOrEmpty(user.DiscordId))
        {
            // Uniqueness checks
            if (!string.IsNullOrEmpty(account) && account != user.Account)
            {
                bool taken = await db.Users.AnyAsync(u => u.Account == account && u.Id != user.Id);
                if (taken) ModelState.AddModelError("account", "Account already taken.");
            }
            if (!string.IsNullOrEmpty(email) && email != user.Email)
            {
                bool taken = await db.Users.AnyAsync(u => u.Email == email && u.Id != user.Id);
                if (taken) ModelState.AddModelError("email", "Email already in use.");
            }

            if (!ModelState.IsValid)
                return View("~/Views/Profile/Edit.cshtml", user);

            user.Name    = name;
            user.Account = account;
            user.Email   = email;
        }

        // Handle avatar upload
        if (avatar != null && avatar.Length > 0)
        {
            var allowed = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
            var ext     = Path.GetExtension(avatar.FileName).ToLowerInvariant();

            if (!allowed.Contains(ext))
            {
                ModelState.AddModelError("avatar", "Only image files are allowed.");
                return View("~/Views/Profile/Edit.cshtml", user);
            }

            var uploadsDir = Path.Combine(env.WebRootPath, "uploads", "avatars");
            Directory.CreateDirectory(uploadsDir);

            var fileName = $"{Guid.NewGuid()}{ext}";
            var filePath = Path.Combine(uploadsDir, fileName);

            await using var stream = System.IO.File.Create(filePath);
            await avatar.CopyToAsync(stream);

            // Delete old local avatar if it exists
            if (!string.IsNullOrEmpty(user.Avatar))
            {
                var oldPath = Path.Combine(env.WebRootPath, user.Avatar.TrimStart('/'));
                if (System.IO.File.Exists(oldPath))
                    System.IO.File.Delete(oldPath);
            }

            user.Avatar = $"/uploads/avatars/{fileName}";
        }

        user.Country    = country;
        user.OnlineFrom = onlineFrom;
        user.OnlineTo   = onlineTo;
        user.IngameName = ingameName;
        user.IngameId   = ingameId;
        user.UpdatedAt  = DateTime.UtcNow;

        await db.SaveChangesAsync();

        TempData["Success"] = "Profile updated.";
        return RedirectToAction(nameof(Edit));
    }

    // ─── Change password ───────────────────────────────────────────────────

    [HttpPost("password")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdatePassword(
        string? currentPassword,
        string  password,
        string  passwordConfirm)
    {
        var user = await db.Users.FindAsync(CurrentUserId);
        if (user == null) return NotFound();

        if (password.Length < 8)
        {
            TempData["PwError"] = "Password must be at least 8 characters.";
            return RedirectToAction(nameof(Edit));
        }

        if (password != passwordConfirm)
        {
            TempData["PwError"] = "Passwords do not match.";
            return RedirectToAction(nameof(Edit));
        }

        // Require current password if user has one
        if (!string.IsNullOrEmpty(user.Password))
        {
            if (string.IsNullOrEmpty(currentPassword) ||
                !BCrypt.Net.BCrypt.Verify(currentPassword, user.Password))
            {
                TempData["PwError"] = "Current password is incorrect.";
                return RedirectToAction(nameof(Edit));
            }
        }

        user.Password  = BCrypt.Net.BCrypt.HashPassword(password);
        user.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        TempData["Success"] = "Password updated.";
        return RedirectToAction(nameof(Edit));
    }
}
