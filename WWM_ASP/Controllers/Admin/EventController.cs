using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WWM_ASP.Data;
using WWM_ASP.Models;
using WWM_ASP.Services;

namespace WWM_ASP.Controllers.Admin;

[Authorize(Policy = "StaffAuth")]
[Route("admin/events")]
public class EventController(AppDbContext db, ZooCoinService coins) : Controller
{
    private long CurrentStaffId =>
        long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private bool IsAdmin =>
        User.HasClaim("role", "master") || User.HasClaim("role", "admin");

    // ─── Index ────────────────────────────────────────────────────────────

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var events = await db.Events.IgnoreQueryFilters()
            .Where(e => e.DeletedAt == null)
            .Include(e => e.Creator)
            .Include(e => e.EventParticipants)
            .OrderByDescending(e => e.StartTime)
            .ToListAsync();

        return View("~/Views/Admin/Events/Index.cshtml", events);
    }

    // ─── Create ───────────────────────────────────────────────────────────

    [HttpGet("create")]
    [Authorize(Policy = "AdminRole")]
    public async Task<IActionResult> Create(string? type)
    {
        ViewBag.InitialType = type ?? "";
        ViewBag.Users = await db.Users.OrderBy(u => u.Account).ToListAsync();
        return View("~/Views/Admin/Events/Create.cshtml");
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "AdminRole")]
    public async Task<IActionResult> Store(
        string    title,
        string?   description,
        string    type,
        string?   rules,
        string?   rewards,
        DateTime  startTime,
        DateTime? endTime,
        string?   location,
        string    status,
        long[]?   participantIds,
        // Lucky Draw
        string[]? prizeNames,
        string[]? prizeDescs,
        int[]?    prizeCoins)
    {
        if (!IsAdmin) return Forbid();

        var ev = new Event
        {
            Title       = title,
            Description = description,
            Type        = type,
            Rules       = rules,
            Rewards     = rewards,
            StartTime   = startTime,
            EndTime     = endTime,
            Location    = location,
            Status      = status,
            CreatedBy   = CurrentStaffId,
            CreatedAt   = DateTime.UtcNow,
            UpdatedAt   = DateTime.UtcNow,
        };

        if (type == Event.TypeLuckyDraw && prizeNames?.Length > 0)
        {
            var prizes = prizeNames.Select((name, i) => new Dictionary<string, object?>
            {
                ["name"]             = name,
                ["description"]      = prizeDescs?.ElementAtOrDefault(i) ?? "",
                ["zoo_coin_amount"]  = (object?)(prizeCoins?.ElementAtOrDefault(i) ?? 0),
            }).ToList();

            ev.LuckyDrawDataJson = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["draw_at"]  = endTime?.ToString("o"),
                ["drawn_at"] = null,
                ["prizes"]   = prizes,
                ["winners"]  = new List<object>(),
            });
        }

        db.Events.Add(ev);
        await db.SaveChangesAsync();

        if (participantIds?.Length > 0)
        {
            foreach (var uid in participantIds)
            {
                db.EventParticipants.Add(new EventParticipant
                {
                    EventId   = ev.Id,
                    UserId    = uid,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                });
            }
            await db.SaveChangesAsync();
        }

        TempData["Success"] = $"Event '{ev.Title}' created.";
        return RedirectToAction(nameof(Index));
    }

    // ─── Edit ─────────────────────────────────────────────────────────────

    [HttpGet("{id:long}/edit")]
    [Authorize(Policy = "AdminRole")]
    public async Task<IActionResult> Edit(long id)
    {
        var ev = await db.Events.IgnoreQueryFilters()
            .Include(e => e.EventParticipants)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (ev == null) return NotFound();
        if (ev.Status is "completed" or "cancelled")
        {
            TempData["Error"] = "Cannot edit a completed or cancelled event.";
            return RedirectToAction(nameof(Index));
        }

        ViewBag.Users = await db.Users.OrderBy(u => u.Account).ToListAsync();
        ViewBag.ParticipantIds = ev.EventParticipants.Select(p => p.UserId).ToList();
        return View("~/Views/Admin/Events/Edit.cshtml", ev);
    }

    [HttpPost("{id:long}/edit")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "AdminRole")]
    public async Task<IActionResult> Update(
        long      id,
        string    title,
        string?   description,
        string?   rules,
        string?   rewards,
        DateTime  startTime,
        DateTime? endTime,
        string?   location,
        string    status,
        long[]?   participantIds)
    {
        if (!IsAdmin) return Forbid();

        var ev = await db.Events.IgnoreQueryFilters()
            .Include(e => e.EventParticipants)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (ev == null) return NotFound();
        if (ev.Status is "completed" or "cancelled") return Forbid();

        ev.Title       = title;
        ev.Description = description;
        ev.Rules       = rules;
        ev.Rewards     = rewards;
        ev.StartTime   = startTime;
        ev.EndTime     = endTime;
        ev.Location    = location;
        ev.Status      = status;
        ev.UpdatedAt   = DateTime.UtcNow;

        // Sync participants
        var existingIds = ev.EventParticipants.Select(p => p.UserId).ToHashSet();
        var newIds      = (participantIds ?? []).ToHashSet();

        var toAdd    = newIds.Except(existingIds);
        var toRemove = ev.EventParticipants.Where(p => !newIds.Contains(p.UserId)).ToList();

        db.EventParticipants.RemoveRange(toRemove);
        foreach (var uid in toAdd)
        {
            db.EventParticipants.Add(new EventParticipant
            {
                EventId = id, UserId = uid,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync();
        TempData["Success"] = "Event updated.";
        return RedirectToAction(nameof(Index));
    }

    // ─── Complete ─────────────────────────────────────────────────────────

    [HttpPost("{id:long}/complete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(long id)
    {
        var ev = await db.Events.FindAsync(id);
        if (ev == null) return NotFound();
        if (ev.Status is "completed" or "cancelled") return RedirectToAction(nameof(Index));

        ev.Status    = "completed";
        ev.EndTime ??= DateTime.UtcNow;
        ev.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        TempData["Success"] = "Event marked as completed.";
        return RedirectToAction(nameof(Index));
    }

    // ─── Delete ───────────────────────────────────────────────────────────

    [HttpPost("{id:long}/delete")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "AdminRole")]
    public async Task<IActionResult> Destroy(long id)
    {
        var ev = await db.Events.FindAsync(id);
        if (ev == null) return NotFound();
        if (ev.Status is "completed" or "cancelled")
        {
            TempData["Error"] = "Cannot delete a completed or cancelled event.";
            return RedirectToAction(nameof(Index));
        }

        ev.DeletedAt = DateTime.UtcNow;
        ev.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        TempData["Success"] = "Event deleted.";
        return RedirectToAction(nameof(Index));
    }

    // ─── Participants ─────────────────────────────────────────────────────

    [HttpGet("{id:long}/participants")]
    public async Task<IActionResult> Participants(long id)
    {
        var ev = await db.Events.IgnoreQueryFilters()
            .Include(e => e.EventParticipants)
                .ThenInclude(p => p.User)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (ev == null) return NotFound();

        ViewBag.Event = ev;
        return View("~/Views/Admin/Events/Participants.cshtml", ev.EventParticipants.ToList());
    }

    // ─── Lucky Draw ───────────────────────────────────────────────────────

    [HttpGet("{id:long}/lucky-draw")]
    public async Task<IActionResult> LuckyDrawResult(long id)
    {
        var ev = await db.Events.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == id);

        if (ev == null || !ev.IsLuckyDraw) return NotFound();
        return View("~/Views/Admin/Events/LuckyDrawResult.cshtml", ev);
    }

    [HttpPost("{id:long}/run-draw")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "AdminRole")]
    public async Task<IActionResult> RunDraw(long id)
    {
        if (!IsAdmin) return Forbid();

        var ev = await db.Events.IgnoreQueryFilters()
            .Include(e => e.EventParticipants)
                .ThenInclude(p => p.User)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (ev == null || !ev.IsLuckyDraw) return NotFound();
        if (ev.LuckyDrawDrawn)
        {
            TempData["Error"] = "Draw has already been run.";
            return RedirectToAction(nameof(LuckyDrawResult), new { id });
        }

        var data = ev.LuckyDrawData;
        if (data == null)
        {
            TempData["Error"] = "No lucky draw data configured.";
            return RedirectToAction(nameof(LuckyDrawResult), new { id });
        }

        var participants = ev.EventParticipants.ToList();
        if (!participants.Any())
        {
            TempData["Error"] = "No participants yet. Cannot run draw.";
            return RedirectToAction(nameof(LuckyDrawResult), new { id });
        }

        var prizesJson = data.GetValueOrDefault("prizes")?.ToString() ?? "[]";
        var prizes     = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(prizesJson) ?? [];

        if (prizes.Count == 0)
        {
            TempData["Error"] = "No prizes configured.";
            return RedirectToAction(nameof(LuckyDrawResult), new { id });
        }

        // Shuffle participants, pick one per prize (no duplicate winners)
        var pool    = participants.OrderBy(_ => Guid.NewGuid()).ToList();
        var winners = new List<Dictionary<string, object?>>();

        for (int i = 0; i < prizes.Count && pool.Count > 0; i++)
        {
            var winner   = pool[0];
            pool.RemoveAt(0);
            var prize    = prizes[i];
            var coinAmt  = int.TryParse(prize.GetValueOrDefault("zoo_coin_amount")?.ToString(), out var c) ? c : 0;

            if (coinAmt > 0 && winner.User != null)
            {
                await coins.AdjustAsync(winner.User, "add", coinAmt,
                    $"Lucky Draw prize: {prize.GetValueOrDefault("name")} — {ev.Title}",
                    null);
            }

            winners.Add(new Dictionary<string, object?>
            {
                ["rank"]            = i + 1,
                ["prize_name"]      = prize.GetValueOrDefault("name")?.ToString() ?? "",
                ["prize_desc"]      = prize.GetValueOrDefault("description")?.ToString() ?? "",
                ["zoo_coin_amount"] = coinAmt,
                ["user_id"]         = winner.UserId,
                ["username"]        = winner.User?.DisplayName ?? "",
                ["ingame_name"]     = winner.User?.IngameName ?? winner.User?.DisplayName ?? "",
            });
        }

        data["drawn_at"] = DateTime.UtcNow.ToString("o");
        data["winners"]  = winners;
        ev.LuckyDrawDataJson = JsonSerializer.Serialize(data);
        ev.Status    = "completed";
        ev.EndTime   = DateTime.UtcNow;
        ev.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        TempData["Success"] = $"Draw complete! {winners.Count} winner(s) selected.";
        return RedirectToAction(nameof(LuckyDrawResult), new { id });
    }
}
