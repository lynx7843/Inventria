using Inventria.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventria;

/// <summary>What a receive actually did, for a caller to report back.</summary>
public sealed class StockReceipt
{
    public required int NewTotalBalance { get; init; }
}

/// <summary>
/// The one place stock is added to a bin - InventoryController's POST /receive
/// and PurchaseOrdersController's line-receive endpoint both call this rather
/// than each writing InventoryBalance/InventoryLotBalance and StockMovement
/// rows themselves. The reason is the retry loop: two concurrent receives
/// against the same item/bin race on InventoryBalance.RowVersion, and getting
/// that race right once here is the whole point of not getting it right twice
/// in two endpoints.
///
/// Callers are expected to have already checked the things that produce a
/// specific error message - the item exists and is not archived, the bin
/// exists, a lot number is given when the item requires one. This service
/// only does the write and the retry; a caller passing it a bad state finds
/// out from the database, not from a sentence written for a warehouse worker.
/// </summary>
public sealed class StockReceivingService
{
    private const int MaxConcurrencyAttempts = 8;

    private readonly InventriaDbContext _context;

    public StockReceivingService(InventriaDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Adds <paramref name="quantity"/> of <paramref name="item"/> to
    /// <paramref name="warehouseBinId"/>, logs the movement, and retries the
    /// concurrency conflicts a busy bin can throw. Returns null only once
    /// every retry has been exhausted - the caller's answer to that is the
    /// same 409 ExecuteStockMove already gave.
    /// </summary>
    public StockReceipt? Receive(
        Item item,
        int warehouseBinId,
        int quantity,
        string performedBy,
        string? lotNumber,
        DateTime? expirationDate)
    {
        for (var attempt = 1; attempt <= MaxConcurrencyAttempts; attempt++)
        {
            try
            {
                return ReceiveOnce(item, warehouseBinId, quantity, performedBy, lotNumber, expirationDate);
            }
            catch (DbUpdateConcurrencyException)
            {
                // A balance row we read has been updated since; its RowVersion
                // no longer matches and the UPDATE matched no rows.
                _context.ChangeTracker.Clear();
                PauseBeforeRetrying(attempt);
            }
            catch (DbUpdateException ex) when (UniqueConstraint.WasViolated(ex))
            {
                // Someone else created the balance row for this item/bin (or
                // item/bin/lot) between our lookup and our insert. A receive
                // writes no other row a unique index covers, so this is
                // always that race and is always safe to retry - the retry
                // will find their row.
                _context.ChangeTracker.Clear();
                PauseBeforeRetrying(attempt);
            }
        }

        return null;
    }

    private StockReceipt ReceiveOnce(
        Item item, int warehouseBinId, int quantity, string performedBy, string? lotNumber, DateTime? expirationDate)
    {
        if (item.TracksLots)
        {
            var lot = FindOrOpenLot(item.Id, lotNumber!, expirationDate);

            var lotBalance = lot.Id != 0
                ? _context.InventoryLotBalances.FirstOrDefault(b =>
                    b.ItemId == item.Id && b.WarehouseBinId == warehouseBinId && b.LotId == lot.Id)
                : null;

            if (lotBalance != null)
            {
                lotBalance.Quantity += quantity;
            }
            else
            {
                lotBalance = new InventoryLotBalance
                {
                    ItemId = item.Id,
                    WarehouseBinId = warehouseBinId,
                    Quantity = quantity,
                    Lot = lot
                };
                _context.InventoryLotBalances.Add(lotBalance);
            }

            _context.StockMovements.Add(new StockMovement
            {
                ItemId = item.Id,
                WarehouseBinId = warehouseBinId,
                TransactionType = "RECEIVE",
                QuantityChanged = quantity,
                Timestamp = DateTime.UtcNow,
                PerformedBy = performedBy,
                Lot = lot
            });

            _context.SaveChanges();
            return new StockReceipt { NewTotalBalance = lotBalance.Quantity };
        }

        var balance = _context.InventoryBalances
            .FirstOrDefault(b => b.ItemId == item.Id && b.WarehouseBinId == warehouseBinId);

        if (balance != null)
        {
            balance.Quantity += quantity;
        }
        else
        {
            balance = new InventoryBalance
            {
                ItemId = item.Id,
                WarehouseBinId = warehouseBinId,
                Quantity = quantity
            };
            _context.InventoryBalances.Add(balance);
        }

        _context.StockMovements.Add(new StockMovement
        {
            ItemId = item.Id,
            WarehouseBinId = warehouseBinId,
            TransactionType = "RECEIVE",
            QuantityChanged = quantity,
            Timestamp = DateTime.UtcNow,
            PerformedBy = performedBy
        });

        _context.SaveChanges();
        return new StockReceipt { NewTotalBalance = balance.Quantity };
    }

    // Finds the lot a receive is adding to, or opens a new one for a lot
    // number this item has not seen before. Only ever called for an item with
    // TracksLots set, and only from inside a move that will save it - a
    // freshly created lot has no Id until SaveChanges runs, which is why
    // everything downstream (the balance row, the movement) hangs it off the
    // Lot navigation instead of reading LotId up front.
    private Lot FindOrOpenLot(int itemId, string lotNumber, DateTime? expirationDate)
    {
        var lot = _context.Lots.FirstOrDefault(l => l.ItemId == itemId && l.LotNumber == lotNumber);
        if (lot != null) return lot;

        lot = new Lot { ItemId = itemId, LotNumber = lotNumber, ExpirationDate = expirationDate };
        _context.Lots.Add(lot);
        return lot;
    }

    // retried is one short transaction, not because they were measured.
    private static void PauseBeforeRetrying(int attempt) =>
        Thread.Sleep(Random.Shared.Next(4, 16) * attempt);
}
