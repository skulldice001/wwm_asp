using Microsoft.EntityFrameworkCore;
using WWM_ASP.Data;
using WWM_ASP.Models;

namespace WWM_ASP.Services;

/// <summary>
/// Handles all Zoo-coin operations with transaction logging.
/// </summary>
public class ZooCoinService(AppDbContext db)
{
    // ─── Adjust coins (admin action) ──────────────────────────────────────
    public async Task<(bool Ok, string Error)> AdjustAsync(
        User   user,
        string type,      // "add" or "deduct"
        int    amount,
        string? note,
        long?  staffId)
    {
        if (amount <= 0)
            return (false, "Amount must be positive.");

        int before = user.ZCoins;

        if (type == "deduct" && amount > before)
            return (false, $"Insufficient Zoo-coins (current: {before:N0}).");

        int after = type == "add" ? before + amount : before - amount;

        user.ZCoins    = after;
        user.UpdatedAt = DateTime.UtcNow;

        db.ZooCoinTransactions.Add(new ZooCoinTransaction
        {
            UserId        = user.Id,
            Type          = type,
            Amount        = amount,
            BalanceBefore = before,
            BalanceAfter  = after,
            Note          = note,
            StaffId       = staffId,
            CreatedAt     = DateTime.UtcNow,
            UpdatedAt     = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();
        return (true, string.Empty);
    }

    // ─── Freeze coins ──────────────────────────────────────────────────────
    public async Task<(bool Ok, string Error)> FreezeAsync(User user, int amount)
    {
        if (amount < 0)
            return (false, "Amount cannot be negative.");

        if (amount > user.ZCoins)
            return (false, "Cannot freeze more than the user's balance.");

        user.ZCoinsFrozen = amount;
        user.UpdatedAt    = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return (true, string.Empty);
    }

    // ─── Transfer between users ───────────────────────────────────────────
    public async Task<(bool Ok, string Error)> TransferAsync(
        User sender,
        User receiver,
        int  amount)
    {
        if (amount <= 0)
            return (false, "Amount must be positive.");

        if (sender.AvailableZCoins < amount)
            return (false, "Insufficient available Zoo-coins.");

        var now = DateTime.UtcNow;

        int senderBefore   = sender.ZCoins;
        int receiverBefore = receiver.ZCoins;

        sender.ZCoins    -= amount;
        receiver.ZCoins  += amount;
        sender.UpdatedAt  = now;
        receiver.UpdatedAt = now;

        db.ZooCoinTransactions.AddRange(
            new ZooCoinTransaction
            {
                UserId        = sender.Id,
                Type          = "transfer_out",
                Amount        = amount,
                BalanceBefore = senderBefore,
                BalanceAfter  = sender.ZCoins,
                Note          = $"Transfer to {receiver.DisplayName}",
                RelatedUserId = receiver.Id,
                CreatedAt     = now,
                UpdatedAt     = now,
            },
            new ZooCoinTransaction
            {
                UserId        = receiver.Id,
                Type          = "transfer_in",
                Amount        = amount,
                BalanceBefore = receiverBefore,
                BalanceAfter  = receiver.ZCoins,
                Note          = $"Transfer from {sender.DisplayName}",
                RelatedUserId = sender.Id,
                CreatedAt     = now,
                UpdatedAt     = now,
            });

        await db.SaveChangesAsync();
        return (true, string.Empty);
    }
}
