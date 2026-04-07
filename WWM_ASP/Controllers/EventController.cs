using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WWM_ASP.Data;
using WWM_ASP.Models;

namespace WWM_ASP.Controllers;

[Authorize(Policy = "UserAuth")]
[Route("events")]
public class EventController(AppDbContext db) : Controller
{
    private long CurrentUserId =>
        long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // ─── Index ────────────────────────────────────────────────────────────

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var userId = CurrentUserId;

        var events = await db.Events
            .Where(e => e.Status == "upcoming" || e.Status == "ongoing")
            .Include(e => e.EventParticipants)
            .OrderBy(e => e.StartTime)
            .ToListAsync();

        var registeredEventIds = events
            .Where(e => e.EventParticipants.Any(p => p.UserId == userId))
            .Select(e => e.Id)
            .ToHashSet();

        var preferredTimes = events
            .SelectMany(e => e.EventParticipants.Where(p => p.UserId == userId))
            .ToDictionary(p => p.EventId, p => p.PreferredTime);

        ViewBag.RegisteredEventIds = registeredEventIds;
        ViewBag.PreferredTimes     = preferredTimes;

        return View("~/Views/Events/Index.cshtml", events);
    }

    // ─── Register ─────────────────────────────────────────────────────────

    [HttpPost("{id:long}/register")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(long id, string? preferredTime)
    {
        var ev = await db.Events.FindAsync(id);
        if (ev == null || !ev.IsActive)
        {
            TempData["Error"] = "This event is no longer accepting registrations.";
            return RedirectToAction(nameof(Index));
        }

        if (ev.IsGuildWar && string.IsNullOrEmpty(preferredTime))
        {
            TempData["Error"] = "Preferred time is required for Guild War events.";
            return RedirectToAction(nameof(Index));
        }

        var userId  = CurrentUserId;
        var existing = await db.EventParticipants
            .FirstOrDefaultAsync(p => p.EventId == id && p.UserId == userId);

        if (existing != null)
        {
            existing.PreferredTime = preferredTime;
            existing.UpdatedAt     = DateTime.UtcNow;
        }
        else
        {
            db.EventParticipants.Add(new EventParticipant
            {
                EventId       = id,
                UserId        = userId,
                PreferredTime = preferredTime,
                CreatedAt     = DateTime.UtcNow,
                UpdatedAt     = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync();
        TempData["Success"] = "Registration successful.";
        return RedirectToAction(nameof(Index));
    }

    // ─── Unregister ───────────────────────────────────────────────────────

    [HttpPost("{id:long}/unregister")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unregister(long id)
    {
        var ev = await db.Events.FindAsync(id);
        if (ev == null || ev.Status != "upcoming")
        {
            TempData["Error"] = "Cannot cancel registration after the event has started.";
            return RedirectToAction(nameof(Index));
        }

        var userId   = CurrentUserId;
        var existing = await db.EventParticipants
            .FirstOrDefaultAsync(p => p.EventId == id && p.UserId == userId);

        if (existing != null)
        {
            db.EventParticipants.Remove(existing);
            await db.SaveChangesAsync();
        }

        TempData["Success"] = "Registration cancelled.";
        return RedirectToAction(nameof(Index));
    }
}
