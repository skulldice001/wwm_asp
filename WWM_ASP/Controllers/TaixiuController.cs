using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WWM_ASP.Data;
using WWM_ASP.Hubs;
using WWM_ASP.Models;
using WWM_ASP.Services;

namespace WWM_ASP.Controllers;

[Authorize(AuthenticationSchemes = "user")]
[Route("entertainment/taixiu")]
public class TaixiuController(
    AppDbContext db,
    IHubContext<TaixiuHub> hub,
    IServiceScopeFactory scopeFactory) : Controller
{
    private long UserId => long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // ── GET /entertainment/taixiu — lobby ────────────────────────────────────

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var tables = await db.TaixiuTables.ToListAsync();

        if (Request.Headers.Accept.ToString().Contains("application/json"))
            return Json(new { tables });

        ViewData["ActivePage"] = "entertainment";
        return View("~/Views/Entertainment/Taixiu.cshtml", tables);
    }

    // ── POST /entertainment/taixiu — create table ────────────────────────────

    [HttpPost("")]
    public async Task<IActionResult> Create([FromBody] CreateTableRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return Json(new { errors = new { name = new[] { "Nhập tên bàn." } } });

        var table = new TaixiuTable
        {
            Name           = req.Name.Trim()[..Math.Min(60, req.Name.Trim().Length)],
            MinBet         = Math.Max(1, req.MinBet),
            MaxBet         = Math.Max(1, req.MaxBet),
            MaxPlayers     = Math.Clamp(req.MaxPlayers, 2, 100),
            CurrentPlayers = 0,
            Status         = "waiting",
            CreatedAt      = DateTime.UtcNow,
            UpdatedAt      = DateTime.UtcNow,
        };
        db.TaixiuTables.Add(table);
        await db.SaveChangesAsync();

        // Auto-join creator
        db.TaixiuTablePlayers.Add(new TaixiuTablePlayer
        {
            TaixiuTableId = table.Id,
            UserId        = UserId,
            JoinedAt      = DateTime.UtcNow,
        });
        table.CurrentPlayers = 1;
        table.Status         = "playing";
        table.UpdatedAt      = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Broadcast lobby update
        await hub.Clients.Group("taixiu-lobby").SendAsync("LobbyEvent", new { table });

        // Start first round
        var game = await TaixiuEngine.StartRoundAsync(db, table);
        TaixiuEngine.ScheduleAutoRoll(table.Id, game.Id, game.State.BetDeadlineAt!.Value, scopeFactory, hub);

        return Json(new { redirect = Url.Action("Show", new { id = table.Id }) });
    }

    // ── GET /entertainment/taixiu/{id} — room ────────────────────────────────

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Show(long id)
    {
        var table = await db.TaixiuTables.FindAsync(id);
        if (table == null) return NotFound();

        ViewData["ActivePage"] = "entertainment";
        return View("~/Views/Entertainment/TaixiuRoom.cshtml", table);
    }

    // ── POST /entertainment/taixiu/{id}/join ─────────────────────────────────

    [HttpPost("{id:long}/join")]
    public async Task<IActionResult> Join(long id)
    {
        var table = await db.TaixiuTables.FindAsync(id);
        if (table == null) return Json(new { message = "Không tìm thấy bàn." }, 404);

        // Already at this table — idempotent
        if (await db.TaixiuTablePlayers.AnyAsync(p => p.TaixiuTableId == id && p.UserId == UserId))
            return Json(new { redirect = Url.Action("Show", new { id }) });

        // Remove from another table first
        var otherEntry = await db.TaixiuTablePlayers
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.UserId == UserId && p.TaixiuTableId != id);

        if (otherEntry != null)
        {
            var otherTable = await db.TaixiuTables.FindAsync(otherEntry.TaixiuTableId);
            db.TaixiuTablePlayers.Remove(otherEntry);
            if (otherTable != null)
            {
                otherTable.CurrentPlayers = await db.TaixiuTablePlayers.CountAsync(p => p.TaixiuTableId == otherTable.Id) - 1;
                otherTable.Status = otherTable.CurrentPlayers > 0 ? "playing" : "waiting";
                otherTable.UpdatedAt = DateTime.UtcNow;
                await hub.Clients.Group("taixiu-lobby").SendAsync("LobbyEvent", new { table = otherTable });
            }
        }

        if (table.CurrentPlayers >= table.MaxPlayers)
            return Json(new { message = "Bàn đã đầy." });

        db.TaixiuTablePlayers.Add(new TaixiuTablePlayer
        {
            TaixiuTableId = id,
            UserId        = UserId,
            JoinedAt      = DateTime.UtcNow,
        });
        table.CurrentPlayers++;
        table.Status     = "playing";
        table.UpdatedAt  = DateTime.UtcNow;
        await db.SaveChangesAsync();

        await hub.Clients.Group("taixiu-lobby").SendAsync("LobbyEvent", new { table });

        // Start round if none running
        var game = await TaixiuEngine.ActiveGameAsync(db, id);
        if (game == null || game.State.Phase == "result")
        {
            game = await TaixiuEngine.StartRoundAsync(db, table);
            TaixiuEngine.ScheduleAutoRoll(table.Id, game.Id, game.State.BetDeadlineAt!.Value, scopeFactory, hub);
        }

        return Json(new { redirect = Url.Action("Show", new { id }) });
    }

    // ── POST /entertainment/taixiu/{id}/leave ────────────────────────────────

    [HttpPost("{id:long}/leave")]
    public async Task<IActionResult> Leave(long id)
    {
        var entry = await db.TaixiuTablePlayers
            .FirstOrDefaultAsync(p => p.TaixiuTableId == id && p.UserId == UserId);

        if (entry != null)
        {
            db.TaixiuTablePlayers.Remove(entry);
            await db.SaveChangesAsync();
        }

        var table = await db.TaixiuTables.FindAsync(id);
        if (table != null)
        {
            table.CurrentPlayers = await db.TaixiuTablePlayers.CountAsync(p => p.TaixiuTableId == id);
            if (table.CurrentPlayers <= 0)
            {
                table.Status    = "closed";
                table.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
                await hub.Clients.Group("taixiu-lobby").SendAsync("LobbyEvent", new { table, closed = true });
                db.TaixiuTables.Remove(table);
                await db.SaveChangesAsync();
            }
            else
            {
                table.Status    = "playing";
                table.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
                await hub.Clients.Group("taixiu-lobby").SendAsync("LobbyEvent", new { table });
            }
        }

        return Redirect("/entertainment/taixiu");
    }

    // ── GET /entertainment/taixiu/{id}/state ─────────────────────────────────

    [HttpGet("{id:long}/state")]
    public async Task<IActionResult> State(long id)
    {
        var table = await db.TaixiuTables.FindAsync(id);
        if (table == null) return NotFound();

        var game  = await TaixiuEngine.ActiveGameAsync(db, id);
        var user  = await db.Users.FindAsync(UserId);
        long avail = user?.AvailableZCoins ?? 0;

        if (game == null)
        {
            return Json(new
            {
                game    = (object?)null,
                z_coins = avail,
                table   = new { min_bet = table.MinBet, max_bet = table.MaxBet },
            });
        }

        return Json(new
        {
            game    = TaixiuEngine.ClientState(game, UserId),
            z_coins = avail,
            table   = new { min_bet = table.MinBet, max_bet = table.MaxBet },
        });
    }

    // ── POST /entertainment/taixiu/{id}/bet ──────────────────────────────────

    [HttpPost("{id:long}/bet")]
    public async Task<IActionResult> Bet(long id, [FromBody] BetRequest req)
    {
        var table = await db.TaixiuTables.FindAsync(id);
        if (table == null) return Json(new { error = "Không tìm thấy bàn." });

        var game = await TaixiuEngine.ActiveGameAsync(db, id);
        if (game == null)
            return Json(new { error = "Không có ván đang chơi." });

        var (ok, error, updatedGame) = await TaixiuEngine.PlaceBetAsync(db, game, UserId, req.Choice, req.Amount, table);
        if (!ok) return Json(new { error });

        // Broadcast updated bets (without per-user my_bet)
        var broadcastState = TaixiuEngine.ClientState(updatedGame!);
        await hub.Clients.Group($"taixiu-room-{id}")
            .SendAsync("RoomEvent", new { type = "bet_placed", state = broadcastState });

        return Json(new { state = TaixiuEngine.ClientState(updatedGame!, UserId) });
    }

    // ── GET /entertainment/taixiu/{id}/chat ──────────────────────────────────

    [HttpGet("{id:long}/chat")]
    public async Task<IActionResult> ChatMessages(long id)
    {
        var messages = await db.TaixiuMessages
            .Where(m => m.TableId == id)
            .Include(m => m.User)
            .OrderByDescending(m => m.Id)
            .Take(100)
            .OrderBy(m => m.Id)
            .Select(m => new
            {
                id      = m.Id,
                user_id = m.UserId,
                name    = m.User != null ? (m.User.IngameName ?? m.User.Account) : "Unknown",
                avatar  = m.User != null ? m.User.AvatarUrl : null,
                message = m.Message,
                time    = m.CreatedAt.HasValue ? m.CreatedAt.Value.ToLocalTime().ToString("HH:mm") : "",
            })
            .ToListAsync();

        return Json(new { messages });
    }

    // ── POST /entertainment/taixiu/{id}/chat ─────────────────────────────────

    [HttpPost("{id:long}/chat")]
    public async Task<IActionResult> SendChat(long id, [FromBody] ChatRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Message))
            return Json(new { error = "Empty message." });

        // Must be at table
        bool atTable = await db.TaixiuTablePlayers.AnyAsync(p => p.TaixiuTableId == id && p.UserId == UserId);
        if (!atTable)
            return Json(new { error = "Not at table." });

        var user = await db.Users.FindAsync(UserId);
        var msg  = new TaixiuMessage
        {
            TableId   = id,
            UserId    = UserId,
            Message   = req.Message.Trim()[..Math.Min(500, req.Message.Trim().Length)],
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.TaixiuMessages.Add(msg);
        await db.SaveChangesAsync();

        var payload = new
        {
            id      = msg.Id,
            user_id = UserId,
            name    = user?.IngameName ?? user?.Account ?? "Unknown",
            avatar  = user?.AvatarUrl,
            message = msg.Message,
            time    = msg.CreatedAt?.ToLocalTime().ToString("HH:mm") ?? "",
        };

        await hub.Clients.Group($"taixiu-room-{id}")
            .SendAsync("RoomEvent", new { type = "chat_message", chat_message = payload });

        return Json(new { message = payload });
    }

    // ─── Request DTOs ─────────────────────────────────────────────────────────

    public record CreateTableRequest(string Name, long MinBet, long MaxBet, int MaxPlayers);
    public record BetRequest(string Choice, int Amount);
    public record ChatRequest(string Message);
}
