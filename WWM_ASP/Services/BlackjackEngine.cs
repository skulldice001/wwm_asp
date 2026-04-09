using Microsoft.EntityFrameworkCore;
using WWM_ASP.Data;
using WWM_ASP.Models;

namespace WWM_ASP.Services;

public static class BlackjackEngine
{
    private static readonly string[] Suits = ["spades", "hearts", "diamonds", "clubs"];
    private static readonly string[] Ranks = ["2","3","4","5","6","7","8","9","10","J","Q","K","A"];

    // ── Public API ────────────────────────────────────────────────────────────

    public static async Task<BlackjackRound> StartRoundAsync(AppDbContext db, BlackjackTable table)
    {
        var pivotPlayers = await db.BlackjackTablePlayers
            .Include(p => p.User)
            .Where(p => p.BlackjackTableId == table.Id)
            .OrderBy(p => p.Seat)
            .ToListAsync();

        bool isAiMode = table.IsAiMode;

        BlackjackTablePlayer? dealerPivot = isAiMode
            ? null
            : pivotPlayers.FirstOrDefault(p => p.Role == "dealer");

        var playerPivots = isAiMode
            ? pivotPlayers.OrderBy(p => p.Seat).ToList()
            : pivotPlayers.Where(p => p.Role == "player").OrderBy(p => p.Seat).ToList();

        var players    = new Dictionary<string, BlackjackPlayerState>();
        var turnOrder  = new List<long>();
        int seatNum    = 1;

        foreach (var pivot in playerPivots)
        {
            int seat = isAiMode ? seatNum++ : (int)(pivot.Seat ?? 1);
            string uid = pivot.UserId.ToString();
            players[uid] = new BlackjackPlayerState
            {
                UserId    = pivot.UserId,
                Name      = pivot.User?.Name ?? "?",
                Seat      = seat,
                Cards     = [],
                Bet       = 0,
                BetPlaced = false,
                CanDouble = false,
                Stood     = false,
                Busted    = false,
                Blackjack = false,
                Result    = null,
                Payout    = 0,
            };
            turnOrder.Add(pivot.UserId);
        }

        string dealerName   = isAiMode ? "Dealer (AI)" : (dealerPivot?.User?.Name ?? "Dealer");
        long   dealerUserId = isAiMode ? 0 : (dealerPivot?.UserId ?? 0);

        var state = new BlackjackState
        {
            Phase   = "betting",
            Deck    = [],
            IsAiMode= isAiMode,
            Dealer  = new BlackjackDealerState
            {
                UserId     = dealerUserId,
                Name       = dealerName,
                IsAi       = isAiMode,
                Cards      = [],
                HoleCard   = null,
                Score      = 0,
                Blackjack  = false,
                Busted     = false,
                IsRevealed = false,
            },
            Players           = players,
            TurnOrder         = turnOrder,
            CurrentTurnUserId = null,
        };

        var round = new BlackjackRound
        {
            BlackjackTableId  = table.Id,
            Phase             = "betting",
            CurrentTurnUserId = null,
            CreatedAt         = DateTime.UtcNow,
            UpdatedAt         = DateTime.UtcNow,
        };
        round.State = state;

        db.BlackjackRounds.Add(round);
        await db.SaveChangesAsync();
        return round;
    }

    public static async Task<(bool Ok, string? Error, BlackjackRound? Round)> PlaceBetAsync(
        AppDbContext db, BlackjackRound round, long userId, long bet)
    {
        using var tx = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable);
        try
        {
            // Re-fetch with lock
            round = await db.BlackjackRounds.FirstAsync(r => r.Id == round.Id);

            if (round.Phase != "betting")
                return (false, "Không trong giai đoạn đặt cược.", null);

            var state = round.State;

            if (!state.Players.ContainsKey(userId.ToString()))
                return (false, "Bạn không ngồi ở bàn này.", null);

            if (state.Players[userId.ToString()].BetPlaced)
                return (false, "Bạn đã đặt cược rồi.", null);

            var table = await db.BlackjackTables.FirstAsync(t => t.Id == round.BlackjackTableId);
            if (bet < table.MinBet || bet > table.MaxBet)
                return (false, $"Cược phải từ {table.MinBet:N0} đến {table.MaxBet:N0} Zoo.", null);

            var user = await db.Users.FirstAsync(u => u.Id == userId);
            long available = user.ZCoins - user.ZCoinsFrozen;
            if (available < bet)
                return (false, "Không đủ Zoo.", null);

            string txType   = state.IsAiMode ? "blackjack_ai_bet" : "blackjack_bet";
            long   balBefore = user.ZCoins;
            user.ZCoins     -= bet;

            db.ZooCoinTransactions.Add(new ZooCoinTransaction
            {
                UserId        = userId,
                Type          = txType,
                Amount        = bet,
                BalanceBefore = balBefore,
                BalanceAfter  = balBefore - bet,
                Note          = $"Blackjack{(state.IsAiMode ? " vs AI" : "")} đặt cược (bàn #{table.Id})",
                CreatedAt     = DateTime.UtcNow,
            });

            state.Players[userId.ToString()].Bet       = bet;
            state.Players[userId.ToString()].BetPlaced = true;

            bool allBet = state.Players.Values.All(p => p.BetPlaced);
            round.SyncFromState(state);
            round.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            if (allBet)
                round = await DealCardsAsync(db, round);

            await tx.CommitAsync();
            return (true, null, round);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public static async Task<(bool Ok, string? Error, BlackjackRound? Round)> ProcessActionAsync(
        AppDbContext db, BlackjackRound round, long userId, string action)
    {
        using var tx = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable);
        try
        {
            round = await db.BlackjackRounds.FirstAsync(r => r.Id == round.Id);

            if (round.Phase != "player_turns")
                return (false, "Không trong lượt của người chơi.", null);

            if (round.CurrentTurnUserId != userId)
                return (false, "Chưa đến lượt của bạn.", null);

            var state = round.State;
            var p = state.Players[userId.ToString()];

            switch (action)
            {
                case "hit":
                    p.Cards.Add(Pop(state.Deck));
                    p.CanDouble = false;
                    if (Score(p.Cards) > 21)
                    {
                        p.Busted = true;
                        p.Stood  = true;
                    }
                    break;

                case "stand":
                    p.Stood = true;
                    break;

                case "double":
                    if (!p.CanDouble)
                        return (false, "Không thể đôi.", null);

                    var user   = await db.Users.FirstAsync(u => u.Id == userId);
                    long avail = user.ZCoins - user.ZCoinsFrozen;
                    if (avail >= p.Bet)
                    {
                        long extra = p.Bet;
                        long balB  = user.ZCoins;
                        user.ZCoins -= extra;
                        db.ZooCoinTransactions.Add(new ZooCoinTransaction
                        {
                            UserId        = userId,
                            Type          = "blackjack_bet",
                            Amount        = extra,
                            BalanceBefore = balB,
                            BalanceAfter  = balB - extra,
                            Note          = $"Blackjack đôi (bàn #{round.BlackjackTableId})",
                            CreatedAt     = DateTime.UtcNow,
                        });
                        p.Bet *= 2;
                    }
                    p.Cards.Add(Pop(state.Deck));
                    p.CanDouble = false;
                    p.Stood     = true;
                    if (Score(p.Cards) > 21)
                        p.Busted = true;
                    break;
            }

            state.Players[userId.ToString()] = p;
            state = AdvanceTurn(state);

            round.SyncFromState(state);
            round.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            if (round.Phase == "dealer_turn")
                round = await InitDealerTurnAsync(db, round);

            await tx.CommitAsync();
            return (true, null, round);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public static async Task<(bool Ok, string? Error, BlackjackRound? Round)> DealerActionAsync(
        AppDbContext db, BlackjackRound round, long userId, string action)
    {
        using var tx = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable);
        try
        {
            round = await db.BlackjackRounds.FirstAsync(r => r.Id == round.Id);

            if (round.Phase != "dealer_turn")
                return (false, "Không trong lượt nhà cái.", null);

            var state = round.State;

            if (state.Dealer.UserId != userId)
                return (false, "Chỉ nhà cái mới có thể thực hiện.", null);

            int score = Score(state.Dealer.Cards);

            if (action == "hit")
            {
                if (score >= 17)
                    return (false, "Nhà cái phải dừng tại 17 trở lên.", null);

                state.Dealer.Cards.Add(Pop(state.Deck));
                int newScore = Score(state.Dealer.Cards);
                state.Dealer.Score = newScore;
                if (newScore > 21)
                    state.Dealer.Busted = true;

                round.SyncFromState(state);
                round.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();

                if (state.Dealer.Busted)
                    round = await ResolveAllAsync(db, round);
            }
            else if (action == "stand")
            {
                if (score < 17)
                    return (false, "Nhà cái phải rút thêm khi dưới 17.", null);

                state.Dealer.Score = score;
                round.SyncFromState(state);
                round.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
                round = await ResolveAllAsync(db, round);
            }

            await tx.CommitAsync();
            return (true, null, round);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public static async Task<BlackjackRound> RunAiDealerAsync(AppDbContext db, BlackjackRound round)
    {
        using var tx = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable);
        try
        {
            round = await db.BlackjackRounds.FirstAsync(r => r.Id == round.Id);

            if (round.Phase != "dealer_turn")
            {
                await tx.CommitAsync();
                return round;
            }

            var state = round.State;

            for (int i = 0; i < 20; i++)
            {
                int sc = Score(state.Dealer.Cards);
                if (sc >= 17) break;
                state.Dealer.Cards.Add(Pop(state.Deck));
                int newSc = Score(state.Dealer.Cards);
                state.Dealer.Score = newSc;
                if (newSc > 21) { state.Dealer.Busted = true; break; }
            }
            state.Dealer.Score = Score(state.Dealer.Cards);
            round.SyncFromState(state);
            round.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            round = await ResolveAllAsync(db, round);
            await tx.CommitAsync();
            return round;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public static async Task<BlackjackRound> HandlePlayerLeaveAsync(
        AppDbContext db, BlackjackRound round, long userId, string pivotRole)
    {
        using var tx = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable);
        try
        {
            round = await db.BlackjackRounds.FirstAsync(r => r.Id == round.Id);

            if (round.Phase == "finished")
            {
                await tx.CommitAsync();
                return round;
            }

            if (pivotRole == "dealer")
            {
                round = await CancelRoundAsync(db, round);
                await tx.CommitAsync();
                return round;
            }

            var state = round.State;
            string uid = userId.ToString();

            if (!state.Players.ContainsKey(uid))
            {
                await tx.CommitAsync();
                return round;
            }

            var p = state.Players[uid];
            if (!p.BetPlaced)
            {
                state.TurnOrder  = state.TurnOrder.Where(u => u != userId).ToList();
                p.BetPlaced = true;
                p.Bet       = 0;
            }
            p.Busted = true;
            p.Stood  = true;
            state.Players[uid] = p;

            if (round.Phase == "betting")
            {
                bool allBet = state.Players.Values.All(x => x.BetPlaced);
                round.SyncFromState(state);
                round.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();

                if (allBet)
                    round = await DealCardsAsync(db, round);

                await tx.CommitAsync();
                return round;
            }

            if (round.Phase == "player_turns" && round.CurrentTurnUserId == userId)
                state = AdvanceTurn(state);

            bool allDone = state.TurnOrder.Count == 0 ||
                state.TurnOrder.All(u => state.Players[u.ToString()].Stood
                                      || state.Players[u.ToString()].Busted);

            if (allDone && round.Phase == "player_turns")
                state.Phase = "dealer_turn";

            round.SyncFromState(state);
            round.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            if (round.Phase == "dealer_turn")
                round = await InitDealerTurnAsync(db, round);

            await tx.CommitAsync();
            return round;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    // ── Client state ──────────────────────────────────────────────────────────

    public static object ClientState(BlackjackRound round, long viewerUserId, string viewerRole, long balance)  // balance is already long
    {
        var s = round.State;
        bool isDealer     = s.Dealer.UserId == viewerUserId;
        bool holeRevealed = round.Phase is "dealer_turn" or "finished";

        // Build dealer cards for client
        var dealerCards = new List<BlackjackCard>(s.Dealer.Cards);
        if (s.Dealer.HoleCard != null)
        {
            if (holeRevealed || isDealer)
                dealerCards.Add(s.Dealer.HoleCard);
            else
                dealerCards.Add(new BlackjackCard { Rank = "back", Suit = "back" });
        }

        var allDealerCards = (holeRevealed || isDealer) && s.Dealer.HoleCard != null
            ? [.. s.Dealer.Cards, s.Dealer.HoleCard]
            : s.Dealer.Cards;
        int dealerScore = Score(allDealerCards!);

        return new
        {
            round_id              = round.Id,
            phase                 = round.Phase,
            dealer                = new
            {
                user_id     = s.Dealer.UserId,
                name        = s.Dealer.Name,
                is_ai       = s.Dealer.IsAi,
                cards       = dealerCards,
                score       = dealerScore,
                blackjack   = s.Dealer.Blackjack,
                busted      = s.Dealer.Busted,
                is_revealed = holeRevealed || isDealer,
            },
            players               = s.Players.Values
                                      .OrderBy(p => p.Seat)
                                      .ToList(),
            turn_order            = s.TurnOrder,
            current_turn_user_id  = round.CurrentTurnUserId,
            my_balance            = balance,
            my_user_id            = viewerUserId,
            my_role               = viewerRole,
            is_ai_mode            = s.IsAiMode,
        };
    }

    // ── Helpers for controller ────────────────────────────────────────────────

    public static async Task<BlackjackRound?> ActiveRoundAsync(AppDbContext db, long tableId)
        => await db.BlackjackRounds
            .Where(r => r.BlackjackTableId == tableId && r.Phase != "finished")
            .OrderByDescending(r => r.Id)
            .FirstOrDefaultAsync();

    // ── Private helpers ───────────────────────────────────────────────────────

    private static async Task<BlackjackRound> DealCardsAsync(AppDbContext db, BlackjackRound round)
    {
        var state = round.State;
        var deck  = FreshDeck();
        var order = state.TurnOrder;

        // Round 1: each player face-up, then dealer face-up
        foreach (var uid in order)
            state.Players[uid.ToString()].Cards.Add(Pop(deck));
        state.Dealer.Cards.Add(Pop(deck));

        // Round 2: each player face-up, then dealer hole card (face-down)
        foreach (var uid in order)
            state.Players[uid.ToString()].Cards.Add(Pop(deck));
        state.Dealer.HoleCard = Pop(deck);

        state.Deck = deck;

        // Score players, detect blackjack
        bool allDone = true;
        foreach (var uid in order)
        {
            var p     = state.Players[uid.ToString()];
            int sc    = Score(p.Cards);
            bool bj   = IsBlackjack(p.Cards);
            p.Blackjack = bj;
            p.CanDouble = !bj;
            if (bj || sc > 21)
                p.Stood = true;
            else
                allDone = false;
            state.Players[uid.ToString()] = p;
        }

        state.Dealer.Score = Score(state.Dealer.Cards);

        if (allDone)
        {
            round.SyncFromState(state);
            round.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return await InitDealerTurnAsync(db, round);
        }

        long? firstActive = NextActiveTurn(state, null);
        state.Phase               = "player_turns";
        state.CurrentTurnUserId   = firstActive;

        round.SyncFromState(state);
        round.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return round;
    }

    private static async Task<BlackjackRound> InitDealerTurnAsync(AppDbContext db, BlackjackRound round)
    {
        var state = round.State;

        if (state.Dealer.HoleCard != null)
        {
            state.Dealer.Cards.Add(state.Dealer.HoleCard);
            state.Dealer.HoleCard = null;
        }
        state.Dealer.IsRevealed = true;

        if (state.Dealer.Cards.Count == 2 && IsBlackjack(state.Dealer.Cards))
            state.Dealer.Blackjack = true;

        state.Dealer.Score = Score(state.Dealer.Cards);
        state.Phase = "dealer_turn";
        state.CurrentTurnUserId = null;

        round.SyncFromState(state);
        round.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        if (state.Dealer.Blackjack)
            return await ResolveAllAsync(db, round);

        return round;
    }

    private static async Task<BlackjackRound> ResolveAllAsync(AppDbContext db, BlackjackRound round)
    {
        var state       = round.State;
        int dealerScore = Score(state.Dealer.Cards);
        bool dealerBj   = state.Dealer.Blackjack;
        bool dealerBust = state.Dealer.Busted;

        foreach (var uid in state.Players.Keys.ToList())
        {
            var p           = state.Players[uid];
            int playerScore = Score(p.Cards);
            bool playerBj   = p.Blackjack;
            long bet        = p.Bet;

            string result;
            long   payout;

            if (p.Busted)
                (result, payout) = ("bust", 0);
            else if (playerBj && dealerBj)
                (result, payout) = ("push", bet);
            else if (playerBj)
                (result, payout) = ("blackjack", bet + (long)Math.Round(bet * 1.5));
            else if (dealerBj)
                (result, payout) = ("lose", 0);
            else if (dealerBust)
                (result, payout) = ("dealer_bust", bet * 2);
            else if (playerScore > dealerScore)
                (result, payout) = ("win", bet * 2);
            else if (playerScore == dealerScore)
                (result, payout) = ("push", bet);
            else
                (result, payout) = ("lose", 0);

            if (state.IsAiMode && payout > 0)
            {
                long profit = payout - bet;
                payout = bet + (long)Math.Floor(profit / 10.0);
                payout = await CapAiPayoutAsync(db, p.UserId, payout, bet);
            }

            p.Result = result;
            p.Payout = payout;
            state.Players[uid] = p;

            if (payout > 0)
            {
                var user      = await db.Users.FirstAsync(u => u.Id == p.UserId);
                long balBefore = user.ZCoins;
                user.ZCoins   += payout;
                string txType = state.IsAiMode ? "blackjack_ai_payout" : "blackjack_payout";
                db.ZooCoinTransactions.Add(new ZooCoinTransaction
                {
                    UserId        = p.UserId,
                    Type          = txType,
                    Amount        = payout,
                    BalanceBefore = balBefore,
                    BalanceAfter  = balBefore + payout,
                    Note          = $"Blackjack{(state.IsAiMode ? " vs AI" : "")}: {result} +{payout} Zoo (bàn #{round.BlackjackTableId})",
                    CreatedAt     = DateTime.UtcNow,
                });
            }
        }

        state.Phase = "finished";
        round.SyncFromState(state);
        round.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return round;
    }

    private static async Task<BlackjackRound> CancelRoundAsync(AppDbContext db, BlackjackRound round)
    {
        var state = round.State;

        foreach (var uid in state.Players.Keys.ToList())
        {
            var p = state.Players[uid];
            if (p.Result != null) continue;
            if (p.Bet > 0)
            {
                var user      = await db.Users.FirstAsync(u => u.Id == p.UserId);
                long balBefore = user.ZCoins;
                user.ZCoins   += p.Bet;
                db.ZooCoinTransactions.Add(new ZooCoinTransaction
                {
                    UserId        = p.UserId,
                    Type          = "blackjack_payout",
                    Amount        = p.Bet,
                    BalanceBefore = balBefore,
                    BalanceAfter  = balBefore + p.Bet,
                    Note          = $"Blackjack hoàn tiền (ván hủy, bàn #{round.BlackjackTableId})",
                    CreatedAt     = DateTime.UtcNow,
                });
            }
            p.Result = "push";
            p.Payout = p.Bet;
            state.Players[uid] = p;
        }

        state.Phase = "finished";
        round.SyncFromState(state);
        round.Phase             = "finished";
        round.CurrentTurnUserId = null;
        round.UpdatedAt         = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return round;
    }

    private static BlackjackState AdvanceTurn(BlackjackState state)
    {
        long? next = NextActiveTurn(state, state.CurrentTurnUserId);
        state.CurrentTurnUserId = next;
        if (next == null)
            state.Phase = "dealer_turn";
        return state;
    }

    private static long? NextActiveTurn(BlackjackState state, long? afterUserId)
    {
        var order = state.TurnOrder;
        int start = afterUserId == null
            ? 0
            : order.IndexOf(afterUserId.Value) + 1;

        for (int i = start; i < order.Count; i++)
        {
            var p = state.Players[order[i].ToString()];
            if (!p.Stood && !p.Busted)
                return order[i];
        }
        return null;
    }

    private static async Task<long> CapAiPayoutAsync(AppDbContext db, long userId, long payout, long bet)
    {
        var  today   = DateTime.UtcNow.Date;
        long payouts = await db.ZooCoinTransactions
            .Where(t => t.UserId == userId
                     && (t.Type == "poker_ai_payout" || t.Type == "blackjack_ai_payout")
                     && t.CreatedAt >= today)
            .SumAsync(t => t.Amount);

        long bets = await db.ZooCoinTransactions
            .Where(t => t.UserId == userId
                     && (t.Type == "poker_ai_bet" || t.Type == "blackjack_ai_bet")
                     && t.CreatedAt >= today)
            .SumAsync(t => t.Amount);

        long netSoFar     = Math.Max(0, payouts - bets);
        long profit       = Math.Max(0, payout - bet);
        long remaining    = Math.Max(0, 5000 - netSoFar);
        long cappedProfit = Math.Min(profit, remaining);
        return bet + cappedProfit;
    }

    // ── Card helpers ──────────────────────────────────────────────────────────

    private static List<BlackjackCard> FreshDeck()
    {
        var deck = new List<BlackjackCard>();
        foreach (var suit in Suits)
            foreach (var rank in Ranks)
                deck.Add(new BlackjackCard { Rank = rank, Suit = suit });

        var rng = new Random();
        for (int i = deck.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }
        return deck;
    }

    private static BlackjackCard Pop(List<BlackjackCard> deck)
    {
        var card = deck[0];
        deck.RemoveAt(0);
        return card;
    }

    public static int Score(List<BlackjackCard> cards)
    {
        int total = 0, aces = 0;
        foreach (var c in cards)
        {
            if (c.Suit == "back") continue;
            switch (c.Rank)
            {
                case "A":
                    aces++;
                    total += 11;
                    break;
                case "J": case "Q": case "K": case "10":
                    total += 10;
                    break;
                default:
                    total += int.Parse(c.Rank);
                    break;
            }
        }
        while (total > 21 && aces > 0) { total -= 10; aces--; }
        return total;
    }

    private static bool IsBlackjack(List<BlackjackCard> cards)
        => cards.Count == 2 && Score(cards) == 21;
}
