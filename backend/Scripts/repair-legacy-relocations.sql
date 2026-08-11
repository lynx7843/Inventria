/*
    Repairs relocations that were logged as a single row.

    Before the relocation fix, moving stock between bins wrote one movement:
    against the SOURCE bin, with a POSITIVE quantity. That row says the opposite
    of what happened - the bin that lost stock was credited with it - and never
    mentions the destination at all, so summing a bin's movements cannot
    reconcile against its InventoryBalance. New relocations write both legs; the
    rows written before that stay wrong until something like this fixes them.

    What this does to each single-legged relocation:

      1. Flips the recorded leg to negative. The bin named on that row is the
         source, and stock left it - that much is certain from the shape of the
         bug, without needing any other evidence.

      2. Writes the missing arrival leg. The destination is NOT recorded
         anywhere, so it is inferred: the bin holding this item whose on-hand
         quantity exceeds what its movements account for by exactly the quantity
         that moved. Where that does not identify exactly one bin, the row is
         left alone and reported, because a guess here would put stock somewhere
         it never went.

    Both legs keep the original timestamp, which is what marks them as halves of
    one move, and the original PerformedBy - the person who made the move is
    still the person who made it, and this script is not a new event.

    Safe to run more than once: a repaired move has two legs and is no longer
    matched. Everything happens in one transaction that rolls back unless every
    item it touched reconciles afterwards.

    Usage:
      sqlcmd -S <server> -U <user> -P <password> -d WarehouseDb \
             -i backend/Scripts/repair-legacy-relocations.sql
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

DECLARE @orphans TABLE (
    MovementId  int,
    ItemId      int,
    SourceBinId int,
    Quantity    int
);

-- A relocation is two rows sharing an item and a timestamp. One row on its own
-- is the old, broken shape.
INSERT INTO @orphans (MovementId, ItemId, SourceBinId, Quantity)
SELECT m.Id, m.ItemId, m.WarehouseBinId, ABS(m.QuantityChanged)
FROM StockMovements m
WHERE m.TransactionType = 'RELOCATE'
  AND m.WarehouseBinId IS NOT NULL
  AND NOT EXISTS (
      SELECT 1
      FROM StockMovements other
      WHERE other.TransactionType = 'RELOCATE'
        AND other.ItemId = m.ItemId
        AND other.Timestamp = m.Timestamp
        AND other.Id <> m.Id
  );

DECLARE @found int = (SELECT COUNT(*) FROM @orphans);
PRINT CONCAT('Single-legged relocations found: ', @found);

DECLARE @repaired int = 0, @skipped int = 0;
DECLARE @movementId int, @itemId int, @sourceBinId int, @quantity int;

DECLARE orphan_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT MovementId, ItemId, SourceBinId, Quantity FROM @orphans;

OPEN orphan_cursor;
FETCH NEXT FROM orphan_cursor INTO @movementId, @itemId, @sourceBinId, @quantity;

WHILE @@FETCH_STATUS = 0
BEGIN
    -- The destination has to be inferred from the stock that arrived somewhere
    -- without the ledger ever saying so. Recomputed per row, so repairing one
    -- move is accounted for when the next is considered.
    DECLARE @candidates int, @destinationBinId int;

    SELECT @candidates = COUNT(*)
    FROM InventoryBalances b
    WHERE b.ItemId = @itemId
      AND b.WarehouseBinId <> @sourceBinId
      AND b.Quantity - ISNULL((
            SELECT SUM(m.QuantityChanged)
            FROM StockMovements m
            WHERE m.ItemId = b.ItemId AND m.WarehouseBinId = b.WarehouseBinId
        ), 0) = @quantity;

    IF @candidates = 1
    BEGIN
        SELECT @destinationBinId = b.WarehouseBinId
        FROM InventoryBalances b
        WHERE b.ItemId = @itemId
          AND b.WarehouseBinId <> @sourceBinId
          AND b.Quantity - ISNULL((
                SELECT SUM(m.QuantityChanged)
                FROM StockMovements m
                WHERE m.ItemId = b.ItemId AND m.WarehouseBinId = b.WarehouseBinId
            ), 0) = @quantity;

        UPDATE StockMovements
        SET QuantityChanged = -@quantity
        WHERE Id = @movementId;

        INSERT INTO StockMovements (ItemId, WarehouseBinId, TransactionType, QuantityChanged, Timestamp, PerformedBy)
        SELECT m.ItemId, @destinationBinId, 'RELOCATE', @quantity, m.Timestamp, m.PerformedBy
        FROM StockMovements m
        WHERE m.Id = @movementId;

        SET @repaired += 1;
        PRINT CONCAT('  Movement ', @movementId, ': item ', @itemId, ', ', @quantity,
                     ' units out of bin ', @sourceBinId, ' and into bin ', @destinationBinId, '.');
    END
    ELSE
    BEGIN
        SET @skipped += 1;
        PRINT CONCAT('  Movement ', @movementId, ': item ', @itemId, ', ', @quantity,
                     ' units out of bin ', @sourceBinId, ' - left alone, ', @candidates,
                     ' bins could be the destination. Needs a person who knows where it went.');
    END

    FETCH NEXT FROM orphan_cursor INTO @movementId, @itemId, @sourceBinId, @quantity;
END

CLOSE orphan_cursor;
DEALLOCATE orphan_cursor;

-- Every bin holding an item this script touched must now agree with its
-- movements. If any does not, the repair made things worse and none of it stands.
DECLARE @unreconciled int = (
    SELECT COUNT(*)
    FROM InventoryBalances b
    WHERE b.ItemId IN (SELECT ItemId FROM @orphans)
      AND b.Quantity <> ISNULL((
            SELECT SUM(m.QuantityChanged)
            FROM StockMovements m
            WHERE m.ItemId = b.ItemId AND m.WarehouseBinId = b.WarehouseBinId
        ), 0)
);

IF @unreconciled > 0 AND @skipped = 0
BEGIN
    PRINT CONCAT('ROLLED BACK: ', @unreconciled, ' bin(s) still do not reconcile.');
    ROLLBACK TRANSACTION;
END
ELSE
BEGIN
    PRINT CONCAT('Repaired: ', @repaired, '. Left alone: ', @skipped, '.');
    COMMIT TRANSACTION;
END
