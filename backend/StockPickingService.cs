using Inventria.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventria;

public enum PickOutcome
{
    Success,
    InsufficientStock
}

/// <summary>What a pick actually did, for a caller to report back.</summary>
public sealed class PickResult
{
    public required PickOutcome Outcome { get; init; }
    public int RemainingBalance { get; init; }
    public int? LotId { get; init; }
}

/// <summary>
/// The one place stock is deducted from a bin - InventoryController's POST
/// /pick and PickListsController's per-line pick both call this rather than
/// each writing InventoryBalance/InventoryLotBalance and StockMovement rows
/// themselves. Same reasoning as StockReceivingService: the retry loop around
/// InventoryBalance.RowVersion only has to be got right once.
/// </summary>
public sealed class StockPickingService
{
    private const int MaxConcurrencyAttempts = 8;

    private readonly InventriaDbContext _context;

    public StockPickingService(InventriaDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Deducts <paramref name="quantity"/> of <paramref name="itemId"/> from
    /// <paramref name="warehouseBinId"/> and logs the movement. Returns null
    /// only once every retry against a concurrency conflict is exhausted -
    /// the caller's answer to that is the same 409 every other stock write in
    /// this app gives.
    /// </summary>
    public PickResult? Pick(
        int itemId,
        bool tracksLots,
        int warehouseBinId,
        int quantity,
        string performedBy,
        string? lotNumber)
    {
        for (var attempt = 1; attempt <= MaxConcurrencyAttempts; attempt++)
        {
            try
            {
                return PickOnce(itemId, tracksLots, warehouseBinId, quantity, performedBy, lotNumber);
            }
            catch (DbUpdateConcurrencyException)
            {
                _context.ChangeTracker.Clear();
                PauseBeforeRetrying(attempt);
            }
        }

        return null;
    }

    private PickResult PickOnce(
        int itemId, bool tracksLots, int warehouseBinId, int quantity, string performedBy, string? lotNumber)
    {
        if (tracksLots)
        {
            var lot = _context.Lots.FirstOrDefault(l => l.ItemId == itemId && l.LotNumber == lotNumber);
            var lotBalance = lot == null
                ? null
                : _context.InventoryLotBalances.FirstOrDefault(b =>
                    b.ItemId == itemId && b.WarehouseBinId == warehouseBinId && b.LotId == lot.Id);

            if (lotBalance == null || lotBalance.Quantity < quantity)
            {
                return new PickResult { Outcome = PickOutcome.InsufficientStock };
            }

            lotBalance.Quantity -= quantity;

            _context.StockMovements.Add(new StockMovement
            {
                ItemId = itemId,
                WarehouseBinId = warehouseBinId,
                TransactionType = "PICK",
                QuantityChanged = -quantity,
                Timestamp = DateTime.UtcNow,
                PerformedBy = performedBy,
                LotId = lot!.Id
            });

            _context.SaveChanges();
            return new PickResult { Outcome = PickOutcome.Success, RemainingBalance = lotBalance.Quantity, LotId = lot.Id };
        }

        var balance = _context.InventoryBalances
            .FirstOrDefault(b => b.ItemId == itemId && b.WarehouseBinId == warehouseBinId);

        if (balance == null || balance.Quantity < quantity)
        {
            return new PickResult { Outcome = PickOutcome.InsufficientStock };
        }

        balance.Quantity -= quantity;

        _context.StockMovements.Add(new StockMovement
        {
            ItemId = itemId,
            WarehouseBinId = warehouseBinId,
            TransactionType = "PICK",
            QuantityChanged = -quantity,
            Timestamp = DateTime.UtcNow,
            PerformedBy = performedBy
        });

        _context.SaveChanges();
        return new PickResult { Outcome = PickOutcome.Success, RemainingBalance = balance.Quantity };
    }

    // retried is one short transaction, not because they were measured.
    private static void PauseBeforeRetrying(int attempt) =>
        Thread.Sleep(Random.Shared.Next(4, 16) * attempt);
}
