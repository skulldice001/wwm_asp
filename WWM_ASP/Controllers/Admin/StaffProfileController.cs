using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WWM_ASP.Data;

namespace WWM_ASP.Controllers.Admin;

[Authorize(Policy = "StaffAuth")]
[Route("admin/profile")]
public class StaffProfileController(AppDbContext db) : Controller
{
    private long CurrentStaffId =>
        long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // ─── Edit ─────────────────────────────────────────────────────────────

    [HttpGet("")]
    public async Task<IActionResult> Edit()
    {
        var staff = await db.Staffs.FindAsync(CurrentStaffId);
        if (staff == null) return NotFound();

        return View("~/Views/Admin/Staff/Profile.cshtml", staff);
    }

    // ─── Update info ───────────────────────────────────────────────────────

    [HttpPost("update")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(string? name, string? email)
    {
        var staff = await db.Staffs.FindAsync(CurrentStaffId);
        if (staff == null) return NotFound();

        if (!string.IsNullOrEmpty(email) && email != staff.Email)
        {
            bool taken = await db.Staffs.AnyAsync(s => s.Email == email && s.Id != staff.Id);
            if (taken)
            {
                ModelState.AddModelError("email", "Email already in use.");
                return View("~/Views/Admin/Staff/Profile.cshtml", staff);
            }
        }

        staff.Name      = name;
        staff.Email     = email;
        staff.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        TempData["Success"] = "Profile updated.";
        return RedirectToAction(nameof(Edit));
    }

    // ─── Change password ───────────────────────────────────────────────────

    [HttpPost("password")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdatePassword(
        string currentPassword,
        string password,
        string passwordConfirm)
    {
        var staff = await db.Staffs.FindAsync(CurrentStaffId);
        if (staff == null) return NotFound();

        if (!BCrypt.Net.BCrypt.Verify(currentPassword, staff.Password))
        {
            TempData["PwError"] = "Current password is incorrect.";
            return RedirectToAction(nameof(Edit));
        }

        if (password.Length < 6)
        {
            TempData["PwError"] = "Password must be at least 6 characters.";
            return RedirectToAction(nameof(Edit));
        }

        if (password != passwordConfirm)
        {
            TempData["PwError"] = "Passwords do not match.";
            return RedirectToAction(nameof(Edit));
        }

        staff.Password  = BCrypt.Net.BCrypt.HashPassword(password);
        staff.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        TempData["Success"] = "Password updated.";
        return RedirectToAction(nameof(Edit));
    }
}
