using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WWM_ASP.Data;
using WWM_ASP.Hubs;
using WWM_ASP.Models;

namespace WWM_ASP.Services;

/// <summary>
/// Core game logic for Tài Xỉu (Sic Bo) — ported from PHP TaixiuEngine.
/// </summary>
public static class TaixiuEngine
{
    public const int BETTING_DURATION = 30; // seconds
    public const int RESULT_DURATION  = 8;  // seconds

    // Pending auto-roll timers keyed by "{tableId}-{gameId}"
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, CancellationTokenSource>
        _timers = new();

    // ─── Start new round ────────────────────────────────────────────────────

    public static async Task<TaixiuGame> StartRoundAsync(AppDbContext db, TaixiuTable table)
    {
        var betDeadline = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + BETTING_DURATION;

        var state = new TaixiuState
        {
            Phase          = "betting",
            BetDeadlineAt  = betDeadline,
            Log            = [$"Ván mới bắt đầu. Đặt cược trong {BETTING_DURATION} giây."],
        };

        var game = new TaixiuGame
        {
            TaixiuTableId = table.Id,
            CreatedAt     = DateTime.UtcNow,
            UpdatedAt     = DateTime.UtcNow,
        };
        game.State = state;

        db.TaixiuGames.Add(game);
        await db.SaveChangesAsync();
        return game;
    }

    // ─── Place (or replace) a bet ────────────────────────────────────────────

    public static async Task<(bool Ok, string? Error, TaixiuGame? Game)> PlaceBetAsync(
        AppDbContext db,
        TaixiuGame game,
        long userId,
        string choice,
        int amount,
        TaixiuTable table)
    {
        var state = game.State;

        if (state.Phase != "betting")
            return (false, "Đã hết thời gian đặt cược.", null);

        if (choice != "tai" && choice != "xiu")
            return (false, "Lựa chọn không hợp lệ.", null);

        if (amount < table.MinBet || amount > table.MaxBet)
            return (false, $"Cược từ {table.MinBet:N0} đến {table.MaxBet:N0} Zoo.", null);

        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return (false, "Không tìm thấy người dùng.", null);

            var prevBet = state.Bets.FirstOrDefault(b => b.UserId == userId);
            int netCost = amount - (prevBet?.Amount ?? 0);

            if (user.AvailableZCoins < netCost)
                return (false, "Không đủ Zoo.", null);

            // Refund previous bet if replacing
            if (prevBet != null)
            {
                user.ZCoins += prevBet.Amount;
                state.Bets.Remove(prevBet);
            }

            // Deduct new bet
            int balBefore = user.ZCoins;
            user.ZCoins  -= amount;
            user.UpdatedAt = DateTime.UtcNow;

            db.ZooCoinTransactions.Add(new ZooCoinTransaction
            {
                UserId        = userId,
                Type          = "taixiu_bet",
                Amount        = amount,
                BalanceBefore = balBefore,
                BalanceAfter  = user.ZCoins,
                Note          = "Tài Xỉu cược",
                CreatedAt     = DateTime.UtcNow,
                UpdatedAt     = DateTime.UtcNow,
            });

            var label = choice == "tai" ? "Tài" : "Xỉu";
            state.Bets.Add(new TaixiuBetEntry
            {
                UserId = userId,
                Name   = user.IngameName ?? user.Account ?? "Unknown",
                Choice = choice,
                Amount = amount,
            });
            state.Log.Add($"{user.IngameName ?? user.Account} cược {amount:N0} Zoo vào {label}.");

            game.State     = state;
            game.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();
            await tx.CommitAsync();

            return (true, null, game);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    // ─── Roll dice and settle bets ───────────────────────────────────────────

    public static async Task<TaixiuGame> RollAsync(AppDbContext db, TaixiuGame game)
    {
        var state = game.State;
        if (state.Phase != "betting") return game;

        var rng   = Random.Shared;
        var dice  = new[] { rng.Next(1, 7), rng.Next(1, 7), rng.Next(1, 7) };
        int sum   = dice[0] + dice[1] + dice[2];
        bool triple = dice[0] == dice[1] && dice[1] == dice[2];

        string outcome = triple ? "triple" : (sum <= 10 ? "xiu" : "tai");

        string outcomeLabel = outcome switch
        {
            "triple" => "Ba bằng nhau — Nhà cái thắng",
            "tai"    => $"Tài ({sum})",
            "xiu"    => $"Xỉu ({sum})",
            _        => "",
        };

        long resultDeadline = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + RESULT_DURATION;

        // Settle bets
        foreach (var bet in state.Bets)
        {
            if (triple)
            {
                bet.Result = "triple";
                bet.Payout = 0;
            }
            else if (bet.Choice == outcome)
            {
                bet.Result = "win";
                bet.Payout = bet.Amount * 2;
            }
            else
            {
                bet.Result = "lose";
                bet.Payout = 0;
            }
        }

        state.Phase             = "result";
        state.Dice              = dice;
        state.Sum               = sum;
        state.Outcome           = outcome;
        state.ResultDeadlineAt  = resultDeadline;
        state.Log.Add($"Xúc xắc: {dice[0]}-{dice[1]}-{dice[2]} = {sum} → {outcomeLabel}");

        game.State     = state;
        game.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Pay out winners
        foreach (var bet in state.Bets.Where(b => b.Result == "win" && b.Payout > 0))
        {
            var user = await db.Users.FindAsync(bet.UserId);
            if (user == null) continue;

            int balBefore = user.ZCoins;
            user.ZCoins  += bet.Payout!.Value;
            user.UpdatedAt = DateTime.UtcNow;

            db.ZooCoinTransactions.Add(new ZooCoinTransaction
            {
                UserId        = bet.UserId,
                Type          = "taixiu_payout",
                Amount        = bet.Payout.Value,
                BalanceBefore = balBefore,
                BalanceAfter  = user.ZCoins,
                Note          = "Tài Xỉu thắng",
                CreatedAt     = DateTime.UtcNow,
                UpdatedAt     = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync();

        return await db.TaixiuGames.FindAsync(game.Id) ?? game;
    }

    // ─── Build client-safe state payload ────────────────────────────────────

    public static object ClientState(TaixiuGame game, long? viewerUserId = null)
    {
        var state  = game.State;
        var myBet  = viewerUserId.HasValue
            ? state.Bets.FirstOrDefault(b => b.UserId == viewerUserId.Value)
            : null;

        long? deadline = state.Phase == "betting"
            ? state.BetDeadlineAt
            : state.ResultDeadlineAt;

        return new
        {
            game_id  = game.Id,
            phase    = state.Phase,
            dice     = state.Dice,
            sum      = state.Sum,
            outcome  = state.Outcome,
            deadline,
            bets     = state.Bets,
            my_bet   = myBet,
            log      = state.Log.TakeLast(8).ToArray(),
        };
    }

    // ─── Get active game for table ───────────────────────────────────────────

    public static async Task<TaixiuGame?> ActiveGameAsync(AppDbContext db, long tableId)
        => await db.TaixiuGames
            .Where(g => g.TaixiuTableId == tableId)
            .OrderByDescending(g => g.Id)
            .FirstOrDefaultAsync();

    // ─── Auto-roll timer (fire-and-forget background task) ──────────────────

    public static void ScheduleAutoRoll(
        long tableId, long gameId, long betDeadline,
        IServiceScopeFactory scopeFactory,
        IHubContext<TaixiuHub> hub)
    {
        var key = $"{tableId}-{gameId}";
        if (_timers.TryRemove(key, out var old)) old.Cancel();

        var cts = new CancellationTokenSource();
        _timers[key] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                int delayMs = (int)Math.Max(0, betDeadline - DateTimeOffset.UtcNow.ToUnixTimeSeconds()) * 1000;
                await Task.Delay(delayMs, cts.Token);

                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var game = await db.TaixiuGames.FindAsync(gameId);
                if (game == null) return;
                if (game.State.Phase != "betting") return;
                if (game.State.BetDeadlineAt != betDeadline) return; // stale guard

                game = await RollAsync(db, game);

                var clientState = ClientState(game);
                await hub.Clients.Group($"taixiu-room-{tableId}")
                    .SendAsync("RoomEvent", new { type = "dice_rolled", state = clientState });

                // Schedule next round after result phase
                long resultDeadline = game.State.ResultDeadlineAt
                    ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds() + RESULT_DURATION;

                ScheduleNextRound(tableId, resultDeadline, scopeFactory, hub);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[TaixiuEngine] AutoRoll error: {ex.Message}");
            }
        }, cts.Token);
    }

    private static void ScheduleNextRound(
        long tableId, long resultDeadline,
        IServiceScopeFactory scopeFactory,
        IHubContext<TaixiuHub> hub)
    {
        _ = Task.Run(async () =>
        {
            int delayMs = (int)Math.Max(0, resultDeadline - DateTimeOffset.UtcNow.ToUnixTimeSeconds()) * 1000;
            await Task.Delay(delayMs);

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Check players still present
            bool hasPlayers = await db.TaixiuTablePlayers.AnyAsync(p => p.TaixiuTableId == tableId);
            if (!hasPlayers) return;

            var table = await db.TaixiuTables.FindAsync(tableId);
            if (table == null) return;

            var game = await StartRoundAsync(db, table);

            var clientState = ClientState(game);
            await hub.Clients.Group($"taixiu-room-{tableId}")
                .SendAsync("RoomEvent", new { type = "round_started", state = clientState });

            ScheduleAutoRoll(tableId, game.Id, game.State.BetDeadlineAt!.Value, scopeFactory, hub);
        });
    }
}
