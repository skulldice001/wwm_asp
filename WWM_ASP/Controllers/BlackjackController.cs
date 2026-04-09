using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WWM_ASP.Data;
using WWM_ASP.Hubs;
using WWM_ASP.Models;
using WWM_ASP.Services;

namespace WWM_ASP.Controllers;

[Authorize(AuthenticationSchemes = "user")]
[Route("entertainment/blackjack")]
public class BlackjackController(
    AppDbContext db,
    IHubContext<BlackjackHub> hub,
    IMemoryCache cache) : Controller
{
    private long UserId => long.Parse(User.FindFirst("id")!.Value);

    // ── Lobby ─────────────────────────────────────────────────────────────────

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var tables = await db.BlackjackTables
            .Where(t => t.Status != "closed")
            .OrderByDescending(t => t.CurrentPlayers)
            .ToListAsync();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == UserId);
        ViewBag.Balance = user?.ZCoins ?? 0;
        ViewBag.UserId  = UserId;
        return View("~/Views/Entertainment/Blackjack.cshtml", tables);
    }

    // ── Table management ──────────────────────────────────────────────────────

    [HttpPost("create")]
    public async Task<IActionResult> Create([FromBody] CreateTableRequest req)
    {
        if (req.MaxBet < req.MinBet)
            return Json(new { error = "max_bet phải >= min_bet" });

        var table = new BlackjackTable
        {
            Name           = req.Name,
            MinBet         = req.MinBet,
            MaxBet         = req.MaxBet,
            MaxPlayers     = req.MaxPlayers,
            Status         = "playing",
            CurrentPlayers = 1,
            IsPreset       = false,
            IsAiMode       = false,
            CreatedAt      = DateTime.UtcNow,
            UpdatedAt      = DateTime.UtcNow,
        };
        db.BlackjackTables.Add(table);
        await db.SaveChangesAsync();

        db.BlackjackTablePlayers.Add(new BlackjackTablePlayer
        {
            BlackjackTableId = table.Id,
            UserId           = UserId,
            JoinedAt         = DateTime.UtcNow,
            Role             = "player",
        });
        await db.SaveChangesAsync();

        await hub.Clients.Group("blackjack-lobby").SendAsync("LobbyEvent", new
        {
            type  = "table_created",
            table = TableDto(table),
        });

        return Json(new { redirect = Url.Action("Show", new { id = table.Id }) });
    }

    [HttpPost("ai")]
    public async Task<IActionResult> CreateAi()
    {
        var user = await db.Users.FirstAsync(u => u.Id == UserId);

        var table = new BlackjackTable
        {
            Name           = $"{user.Name} vs AI",
            MinBet         = 1,
            MaxBet         = 100,
            MaxPlayers     = 1,
            Status         = "playing",
            CurrentPlayers = 0,
            IsPreset       = false,
            IsAiMode       = true,
            CreatedAt      = DateTime.UtcNow,
            UpdatedAt      = DateTime.UtcNow,
        };
        db.BlackjackTables.Add(table);
        await db.SaveChangesAsync();

        db.BlackjackTablePlayers.Add(new BlackjackTablePlayer
        {
            BlackjackTableId = table.Id,
            UserId           = UserId,
            JoinedAt         = DateTime.UtcNow,
            Role             = "player",
            Seat             = 1,
        });
        table.CurrentPlayers = 1;
        await db.SaveChangesAsync();

        return Json(new { redirect = Url.Action("Show", new { id = table.Id }) });
    }

    [HttpPost("{id:long}/join")]
    public async Task<IActionResult> Join(long id)
    {
        var table = await db.BlackjackTables.FindAsync(id);
        if (table == null) return NotFound();

        // Already in this table?
        if (await db.BlackjackTablePlayers.AnyAsync(p => p.BlackjackTableId == id && p.UserId == UserId))
            return Json(new { redirect = Url.Action("Show", new { id }) });

        // Leave any other table first
        var otherPivot = await db.BlackjackTablePlayers
            .Include(p => p.Table)
            .FirstOrDefaultAsync(p => p.UserId == UserId && p.BlackjackTableId != id);
        if (otherPivot?.Table != null)
        {
            await RemovePlayerFromTable(otherPivot.Table, UserId);
        }

        // Check player slots (not counting dealer)
        int playerCount = await db.BlackjackTablePlayers
            .CountAsync(p => p.BlackjackTableId == id && p.Role == "player");
        if (playerCount >= table.MaxPlayers)
            return Json(new { error = "Bàn đã đầy." });

        byte? seat = null;
        if (table.IsAiMode)
        {
            var maxSeat = await db.BlackjackTablePlayers
                .Where(p => p.BlackjackTableId == id)
                .MaxAsync(p => (byte?)p.Seat) ?? 0;
            seat = (byte)(maxSeat + 1);
        }

        db.BlackjackTablePlayers.Add(new BlackjackTablePlayer
        {
            BlackjackTableId = id,
            UserId           = UserId,
            JoinedAt         = DateTime.UtcNow,
            Role             = "player",
            Seat             = seat,
        });
        table.CurrentPlayers++;
        table.Status     = "playing";
        table.UpdatedAt  = DateTime.UtcNow;
        await db.SaveChangesAsync();

        await BroadcastLobbyTable(table);
        await BroadcastRoomPlayers(table);

        return Json(new { redirect = Url.Action("Show", new { id }) });
    }

    [HttpPost("{id:long}/leave")]
    public async Task<IActionResult> Leave(long id)
    {
        var table = await db.BlackjackTables.FindAsync(id);
        if (table == null) return NotFound();

        var pivot = await db.BlackjackTablePlayers
            .FirstOrDefaultAsync(p => p.BlackjackTableId == id && p.UserId == UserId);
        string pivotRole = pivot?.Role ?? "player";

        // Handle active round
        var round = await BlackjackEngine.ActiveRoundAsync(db, id);
        if (round != null)
        {
            var user = await db.Users.FirstAsync(u => u.Id == UserId);
            round = await BlackjackEngine.HandlePlayerLeaveAsync(db, round, UserId, pivotRole);

            string evtType = round.Phase == "finished" ? "round_finished" : "player_acted";
            long balance   = user.ZCoins - user.ZCoinsFrozen;
            await hub.Clients.Group($"blackjack-room-{id}").SendAsync("RoomEvent", new
            {
                type  = evtType,
                round = BlackjackEngine.ClientState(round, UserId, pivotRole, balance),
            });
        }

        if (pivot != null) db.BlackjackTablePlayers.Remove(pivot);
        table.CurrentPlayers = Math.Max(0, table.CurrentPlayers - 1);

        if (table.CurrentPlayers <= 0 && !table.IsPreset)
        {
            table.Status = "closed";
            await db.SaveChangesAsync();
            await hub.Clients.Group("blackjack-lobby").SendAsync("LobbyEvent", new
            { type = "table_removed", table_id = id });
            return Json(new { redirect = Url.Action("Index") });
        }

        table.Status    = table.CurrentPlayers <= 0 ? "waiting" : "playing";
        table.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        await BroadcastLobbyTable(table);
        await BroadcastRoomPlayers(table);

        return Json(new { redirect = Url.Action("Index") });
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Show(long id)
    {
        var table = await db.BlackjackTables.FindAsync(id);
        if (table == null) return NotFound();

        var pivot = await db.BlackjackTablePlayers
            .FirstOrDefaultAsync(p => p.BlackjackTableId == id && p.UserId == UserId);

        if (pivot == null)
            return RedirectToAction("Index");

        ViewBag.MyRole = pivot.Role ?? "player";
        ViewBag.MySeat = pivot.Seat;
        return View("~/Views/Entertainment/BlackjackRoom.cshtml", table);
    }

    // ── Role / Ready ──────────────────────────────────────────────────────────

    [HttpPost("{id:long}/role")]
    public async Task<IActionResult> ChooseRole(long id, [FromBody] ChooseRoleRequest req)
    {
        var table = await db.BlackjackTables.FindAsync(id);
        if (table == null) return NotFound();

        var pivot = await db.BlackjackTablePlayers
            .FirstOrDefaultAsync(p => p.BlackjackTableId == id && p.UserId == UserId);
        if (pivot == null) return Json(new { error = "Bạn không ở bàn này." });

        string role = req.Role;
        byte   seat = role == "dealer" ? (byte)0 : (byte)req.Seat;

        if (role == "dealer")
        {
            bool dealerExists = await db.BlackjackTablePlayers
                .AnyAsync(p => p.BlackjackTableId == id && p.Role == "dealer" && p.UserId != UserId);
            if (dealerExists)
                return Json(new { error = "Đã có người làm nhà cái rồi." });
        }
        else
        {
            if (seat < 1 || seat > 7)
                return Json(new { error = "Số ghế không hợp lệ." });
            bool seatTaken = await db.BlackjackTablePlayers
                .AnyAsync(p => p.BlackjackTableId == id && p.Seat == seat && p.UserId != UserId);
            if (seatTaken)
                return Json(new { error = "Ghế này đã có người ngồi." });
        }

        pivot.Role    = role;
        pivot.Seat    = seat;
        pivot.IsReady = false;
        await db.SaveChangesAsync();

        await BroadcastRoomPlayers(table);
        var players = await GetPlayersData(id);
        return Json(new { players });
    }

    [HttpPost("{id:long}/ready")]
    public async Task<IActionResult> Ready(long id)
    {
        var table = await db.BlackjackTables.FindAsync(id);
        if (table == null) return NotFound();

        var pivot = await db.BlackjackTablePlayers
            .FirstOrDefaultAsync(p => p.BlackjackTableId == id && p.UserId == UserId);
        if (pivot == null) return Json(new { error = "Bạn không ở bàn này." });

        if (!table.IsAiMode && (pivot.Role == null || (pivot.Role == "player" && pivot.Seat == null)))
            return Json(new { error = "Hãy chọn vai và ghế trước." });

        pivot.IsReady = !pivot.IsReady;
        await db.SaveChangesAsync();

        var players = await GetPlayersData(id);
        long? countdownAt = null;

        if (table.IsAiMode)
        {
            bool anyReady = players.Any(p => p.IsReady);
            if (anyReady)
            {
                countdownAt = DateTimeOffset.UtcNow.AddSeconds(3).ToUnixTimeSeconds();
                cache.Set($"bj_countdown_{id}", countdownAt, TimeSpan.FromSeconds(30));
                await hub.Clients.Group($"blackjack-room-{id}").SendAsync("RoomEvent", new
                { type = "countdown_start", players, countdown_at = countdownAt });
            }
            else
            {
                cache.Remove($"bj_countdown_{id}");
                await hub.Clients.Group($"blackjack-room-{id}").SendAsync("RoomEvent", new
                { type = "ready_update", players });
            }
        }
        else
        {
            var dealer  = players.FirstOrDefault(p => p.Role == "dealer" && p.IsReady);
            int readyCt = players.Count(p => p.Role == "player" && p.IsReady);
            if (dealer != null && readyCt >= 1)
            {
                countdownAt = DateTimeOffset.UtcNow.AddSeconds(5).ToUnixTimeSeconds();
                cache.Set($"bj_countdown_{id}", countdownAt, TimeSpan.FromSeconds(30));
                await hub.Clients.Group($"blackjack-room-{id}").SendAsync("RoomEvent", new
                { type = "countdown_start", players, countdown_at = countdownAt });
            }
            else
            {
                cache.Remove($"bj_countdown_{id}");
                await hub.Clients.Group($"blackjack-room-{id}").SendAsync("RoomEvent", new
                { type = "ready_update", players });
            }
        }

        return Json(new { players, countdown_at = countdownAt });
    }

    // ── Gameplay ──────────────────────────────────────────────────────────────

    [HttpPost("{id:long}/game/start")]
    public async Task<IActionResult> StartRound(long id)
    {
        var table = await db.BlackjackTables.FindAsync(id);
        if (table == null) return NotFound();

        var pivot = await db.BlackjackTablePlayers
            .FirstOrDefaultAsync(p => p.BlackjackTableId == id && p.UserId == UserId);

        if (!table.IsAiMode && (pivot == null || pivot.Role != "dealer"))
            return Json(new { error = "Chỉ nhà cái mới bắt đầu được." });

        var players = await GetPlayersData(id);
        bool readyDealer = table.IsAiMode || players.Any(p => p.Role == "dealer" && p.IsReady);
        int  readyCount  = table.IsAiMode
            ? players.Count(p => p.IsReady)
            : players.Count(p => p.Role == "player" && p.IsReady);

        if (!readyDealer || readyCount < 1)
            return Json(new { error = "Chưa đủ người sẵn sàng." });

        // Clear existing unfinished rounds
        var oldRounds = await db.BlackjackRounds
            .Where(r => r.BlackjackTableId == id && r.Phase != "finished")
            .ToListAsync();
        db.BlackjackRounds.RemoveRange(oldRounds);
        cache.Remove($"bj_countdown_{id}");

        // Reset ready flags
        var allPivots = await db.BlackjackTablePlayers
            .Where(p => p.BlackjackTableId == id)
            .ToListAsync();
        foreach (var p in allPivots) p.IsReady = false;
        await db.SaveChangesAsync();

        var round = await BlackjackEngine.StartRoundAsync(db, table);
        var user  = await db.Users.FirstAsync(u => u.Id == UserId);
        string myRole = pivot?.Role ?? "player";
        long balance  = user.ZCoins - user.ZCoinsFrozen;

        await hub.Clients.Group($"blackjack-room-{id}").SendAsync("RoomEvent", new
        {
            type  = "round_started",
            round = BlackjackEngine.ClientState(round, UserId, myRole, balance),
        });

        return Json(new { round = BlackjackEngine.ClientState(round, UserId, myRole, balance) });
    }

    [HttpGet("{id:long}/game/state")]
    public async Task<IActionResult> State(long id)
    {
        var table = await db.BlackjackTables.FindAsync(id);
        if (table == null) return NotFound();

        var user  = await db.Users.FirstAsync(u => u.Id == UserId);
        long bal  = user.ZCoins - user.ZCoinsFrozen;

        var pivot = await db.BlackjackTablePlayers
            .FirstOrDefaultAsync(p => p.BlackjackTableId == id && p.UserId == UserId);

        var baseInfo = new
        {
            z_coins    = bal,
            min_bet    = table.MinBet,
            max_bet    = table.MaxBet,
            my_role    = pivot?.Role,
            my_seat    = pivot?.Seat,
            is_ai_mode = table.IsAiMode,
        };

        var round = await BlackjackEngine.ActiveRoundAsync(db, id);
        if (round == null)
        {
            var players     = await GetPlayersData(id);
            cache.TryGetValue($"bj_countdown_{id}", out long? cdAt);
            return Json(new
            {
                baseInfo.z_coins,
                baseInfo.min_bet,
                baseInfo.max_bet,
                baseInfo.my_role,
                baseInfo.my_seat,
                baseInfo.is_ai_mode,
                phase        = "lobby",
                players,
                countdown_at = cdAt,
            });
        }

        string role = pivot?.Role ?? "player";
        return Json(new
        {
            baseInfo.z_coins,
            baseInfo.min_bet,
            baseInfo.max_bet,
            baseInfo.my_role,
            baseInfo.my_seat,
            baseInfo.is_ai_mode,
            round = BlackjackEngine.ClientState(round, UserId, role, bal),
        });
    }

    [HttpPost("{id:long}/game/deal")]
    public async Task<IActionResult> Deal(long id, [FromBody] DealRequest req)
    {
        var table = await db.BlackjackTables.FindAsync(id);
        if (table == null) return NotFound();

        var round = await BlackjackEngine.ActiveRoundAsync(db, id);
        if (round == null || round.Phase != "betting")
            return Json(new { error = "Không trong giai đoạn đặt cược." });

        var result = await BlackjackEngine.PlaceBetAsync(db, round, UserId, req.Bet);
        if (!result.Ok)
            return Json(new { error = result.Error });

        round = result.Round!;
        var pivot = await db.BlackjackTablePlayers
            .FirstOrDefaultAsync(p => p.BlackjackTableId == id && p.UserId == UserId);
        var user = await db.Users.FirstAsync(u => u.Id == UserId);
        long bal  = user.ZCoins - user.ZCoinsFrozen;
        string role = pivot?.Role ?? "player";

        string evtType = round.Phase == "player_turns" ? "cards_dealt" : "bet_placed";

        await hub.Clients.Group($"blackjack-room-{id}").SendAsync("RoomEvent", new
        {
            type  = evtType,
            round = BlackjackEngine.ClientState(round, UserId, role, bal),
        });

        return Json(new { round = BlackjackEngine.ClientState(round, UserId, role, bal) });
    }

    [HttpPost("{id:long}/game/action")]
    public async Task<IActionResult> Action(long id, [FromBody] ActionRequest req)
    {
        if (req.Action is not ("hit" or "stand" or "double"))
            return Json(new { error = "Action không hợp lệ." });

        var table = await db.BlackjackTables.FindAsync(id);
        if (table == null) return NotFound();

        var round = await BlackjackEngine.ActiveRoundAsync(db, id);
        if (round == null)
            return Json(new { error = "Không có ván đang chơi." });

        var result = await BlackjackEngine.ProcessActionAsync(db, round, UserId, req.Action);
        if (!result.Ok)
            return Json(new { error = result.Error });

        round = result.Round!;

        if (table.IsAiMode && round.Phase == "dealer_turn")
            round = await BlackjackEngine.RunAiDealerAsync(db, round);

        var pivot = await db.BlackjackTablePlayers
            .FirstOrDefaultAsync(p => p.BlackjackTableId == id && p.UserId == UserId);
        var user = await db.Users.FirstAsync(u => u.Id == UserId);
        long bal  = user.ZCoins - user.ZCoinsFrozen;
        string role = pivot?.Role ?? "player";

        string evtType = round.Phase switch
        {
            "dealer_turn" => "dealer_turn",
            "finished"    => "round_finished",
            _             => "player_acted",
        };

        await hub.Clients.Group($"blackjack-room-{id}").SendAsync("RoomEvent", new
        {
            type  = evtType,
            round = BlackjackEngine.ClientState(round, UserId, role, bal),
        });

        return Json(new { round = BlackjackEngine.ClientState(round, UserId, role, bal) });
    }

    [HttpPost("{id:long}/game/dealer-action")]
    public async Task<IActionResult> DealerAction(long id, [FromBody] ActionRequest req)
    {
        if (req.Action is not ("hit" or "stand"))
            return Json(new { error = "Action không hợp lệ." });

        var round = await BlackjackEngine.ActiveRoundAsync(db, id);
        if (round == null) return Json(new { error = "Không có ván đang chơi." });

        var result = await BlackjackEngine.DealerActionAsync(db, round, UserId, req.Action);
        if (!result.Ok)
            return Json(new { error = result.Error });

        round = result.Round!;
        var pivot = await db.BlackjackTablePlayers
            .FirstOrDefaultAsync(p => p.BlackjackTableId == id && p.UserId == UserId);
        var user = await db.Users.FirstAsync(u => u.Id == UserId);
        long bal  = user.ZCoins - user.ZCoinsFrozen;
        string role = pivot?.Role ?? "dealer";

        string evtType = round.Phase == "finished" ? "round_finished" : "dealer_turn";
        await hub.Clients.Group($"blackjack-room-{id}").SendAsync("RoomEvent", new
        {
            type  = evtType,
            round = BlackjackEngine.ClientState(round, UserId, role, bal),
        });

        return Json(new { round = BlackjackEngine.ClientState(round, UserId, role, bal) });
    }

    [HttpPost("{id:long}/game/next")]
    public async Task<IActionResult> NextRound(long id)
    {
        var table = await db.BlackjackTables.FindAsync(id);
        if (table == null) return NotFound();

        var pivot = await db.BlackjackTablePlayers
            .FirstOrDefaultAsync(p => p.BlackjackTableId == id && p.UserId == UserId);

        if (!table.IsAiMode && (pivot == null || pivot.Role != "dealer"))
            return Json(new { error = "Chỉ nhà cái mới có thể bắt ván mới." });

        var round = await BlackjackEngine.ActiveRoundAsync(db, id);
        if (round == null || round.Phase != "finished")
            return Json(new { error = "Ván chưa kết thúc." });

        var newRound = await BlackjackEngine.StartRoundAsync(db, table);
        var user     = await db.Users.FirstAsync(u => u.Id == UserId);
        long bal     = user.ZCoins - user.ZCoinsFrozen;
        string role  = pivot?.Role ?? "player";

        await hub.Clients.Group($"blackjack-room-{id}").SendAsync("RoomEvent", new
        {
            type  = "round_started",
            round = BlackjackEngine.ClientState(newRound, UserId, role, bal),
        });

        return Json(new { round = BlackjackEngine.ClientState(newRound, UserId, role, bal) });
    }

    // ── Chat ──────────────────────────────────────────────────────────────────

    [HttpGet("{id:long}/chat")]
    public async Task<IActionResult> ChatMessages(long id)
    {
        var messages = await db.BlackjackMessages
            .Include(m => m.User)
            .Where(m => m.TableId == id)
            .OrderByDescending(m => m.CreatedAt)
            .Take(100)
            .ToListAsync();

        return Json(new
        {
            messages = messages.AsEnumerable().Reverse().Select(m => new
            {
                id      = m.Id,
                user_id = m.UserId,
                name    = m.User?.Name ?? "Unknown",
                avatar  = m.User?.DiscordAvatar,
                message = m.Message,
                time    = m.CreatedAt.ToString("HH:mm"),
            })
        });
    }

    [HttpPost("{id:long}/chat")]
    public async Task<IActionResult> SendChat(long id, [FromBody] ChatRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Message))
            return Json(new { error = "Tin nhắn không được trống." });

        bool atTable = await db.BlackjackTablePlayers
            .AnyAsync(p => p.BlackjackTableId == id && p.UserId == UserId);
        if (!atTable) return Json(new { error = "Bạn không ở bàn này." });

        var user = await db.Users.FirstAsync(u => u.Id == UserId);

        var msg = new BlackjackMessage
        {
            TableId   = id,
            UserId    = UserId,
            Message   = req.Message[..Math.Min(req.Message.Length, 500)],
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.BlackjackMessages.Add(msg);
        await db.SaveChangesAsync();

        var payload = new
        {
            id      = msg.Id,
            user_id = UserId,
            name    = user.Name,
            avatar  = user.DiscordAvatar,
            message = msg.Message,
            time    = msg.CreatedAt.ToString("HH:mm"),
        };

        await hub.Clients.Group($"blackjack-room-{id}").SendAsync("RoomEvent", new
        { type = "chat_message", chat = payload });

        return Json(new { message = payload });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task RemovePlayerFromTable(BlackjackTable table, long userId)
    {
        var p = await db.BlackjackTablePlayers
            .FirstOrDefaultAsync(x => x.BlackjackTableId == table.Id && x.UserId == userId);
        if (p == null) return;
        db.BlackjackTablePlayers.Remove(p);
        table.CurrentPlayers = Math.Max(0, table.CurrentPlayers - 1);
        table.Status = table.CurrentPlayers <= 0 ? "waiting" : "playing";
        table.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await BroadcastLobbyTable(table);
    }

    private async Task<List<PlayerDto>> GetPlayersData(long tableId)
    {
        return await db.BlackjackTablePlayers
            .Include(p => p.User)
            .Where(p => p.BlackjackTableId == tableId)
            .Select(p => new PlayerDto
            {
                Id      = p.UserId,
                Name    = p.User!.Name ?? "",
                Avatar  = p.User.DiscordAvatar,
                IsReady = p.IsReady,
                Role    = p.Role,
                Seat    = p.Seat,
            })
            .ToListAsync();
    }

    private async Task BroadcastLobbyTable(BlackjackTable table)
    {
        await hub.Clients.Group("blackjack-lobby").SendAsync("LobbyEvent", new
        {
            type  = "table_updated",
            table = TableDto(table),
        });
    }

    private async Task BroadcastRoomPlayers(BlackjackTable table)
    {
        var players = await GetPlayersData(table.Id);
        await hub.Clients.Group($"blackjack-room-{table.Id}").SendAsync("RoomEvent", new
        { type = "ready_update", players });
    }

    private static object TableDto(BlackjackTable t) => new
    {
        id             = t.Id,
        name           = t.Name,
        min_bet        = t.MinBet,
        max_bet        = t.MaxBet,
        current_players= t.CurrentPlayers,
        max_players    = t.MaxPlayers,
        status         = t.Status,
        is_ai_mode     = t.IsAiMode,
    };

    // ── DTOs ──────────────────────────────────────────────────────────────────

    public record CreateTableRequest(string Name, long MinBet, long MaxBet, int MaxPlayers);
    public record ChooseRoleRequest(string Role, int Seat);
    public record DealRequest(long Bet);  // long matches ZCoins type
    public record ActionRequest(string Action);
    public record ChatRequest(string Message);

    public class PlayerDto
    {
        public long    Id      { get; set; }
        public string  Name    { get; set; } = "";
        public string? Avatar  { get; set; }
        public bool    IsReady { get; set; }
        public string  Role    { get; set; } = "player";
        public byte?   Seat    { get; set; }
    }
}
