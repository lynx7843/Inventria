using Inventria.Controllers;
using Inventria.Models;
using Microsoft.AspNetCore.Mvc;

namespace Inventria.Tests;

/// <summary>
/// Items that opt into Item.TracksLots keep their stock in
/// InventoryLotBalance, keyed by (item, bin, lot) instead of (item, bin), so
/// that a receive, pick or relocation can be answered against one named
/// batch rather than the item as a whole. Everything an item that does not
/// track lots does is covered by StockMovementTests; this is only the extra
/// behaviour lot tracking adds on top.
/// </summary>
public class LotTrackingTests
{
    private static InventoryController ControllerFor(TestDatabase db, string username = "alice") =>
        new(db.Context, new StockReceivingService(db.Context), new StockPickingService(db.Context)) { ControllerContext = ApiResult.SignedInAs(username) };

    // --- RECEIVE ---------------------------------------------------------

    [Fact]
    public void Receiving_a_lot_tracked_item_without_a_lot_number_is_refused()
    {
        using var db = new TestDatabase();
        var item = db.AddItem(tracksLots: true);
        var bin = db.AddBin();

        var result = ControllerFor(db).ReceiveStock(new ReceiveStockRequest
        {
            ItemId = item.Id,
            WarehouseBinId = bin.Id,
            Quantity = 10
        });

        Assert.IsType<BadRequestObjectResult>(result);

        using var check = db.NewContext();
        Assert.Empty(check.InventoryLotBalances);
        Assert.Empty(check.StockMovements);
    }

    [Fact]
    public void Receiving_a_new_lot_creates_it_and_logs_the_movement_against_it()
    {
        using var db = new TestDatabase();
        var item = db.AddItem(tracksLots: true);
        var bin = db.AddBin();
        var expiry = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var result = ControllerFor(db).ReceiveStock(new ReceiveStockRequest
        {
            ItemId = item.Id,
            WarehouseBinId = bin.Id,
            Quantity = 25,
            LotNumber = " LOT-A ",
            ExpirationDate = expiry
        });

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(25, ApiResult.Number(result, "NewTotalBalance"));

        using var check = db.NewContext();
        var lot = check.Lots.Single();
        // Trimmed the same way a barcode or SKU is - surrounding spaces are
        // invisible in the UI but would make "LOT-A" and " LOT-A " two lots.
        Assert.Equal("LOT-A", lot.LotNumber);
        Assert.Equal(expiry, lot.ExpirationDate);

        var balance = check.InventoryLotBalances.Single();
        Assert.Equal(lot.Id, balance.LotId);
        Assert.Equal(25, balance.Quantity);

        var movement = check.StockMovements.Single();
        Assert.Equal("RECEIVE", movement.TransactionType);
        Assert.Equal(lot.Id, movement.LotId);

        // Receiving a lot-tracked item must not also open an ordinary
        // InventoryBalance row - its stock lives in exactly one table.
        Assert.Empty(check.InventoryBalances);
    }

    [Fact]
    public void Receiving_into_an_existing_lot_adds_to_it_without_creating_a_second_one()
    {
        using var db = new TestDatabase();
        var item = db.AddItem(tracksLots: true);
        var bin = db.AddBin();
        var controller = ControllerFor(db);

        controller.ReceiveStock(new ReceiveStockRequest { ItemId = item.Id, WarehouseBinId = bin.Id, Quantity = 10, LotNumber = "LOT-A" });
        var result = controller.ReceiveStock(new ReceiveStockRequest { ItemId = item.Id, WarehouseBinId = bin.Id, Quantity = 5, LotNumber = "LOT-A" });

        Assert.Equal(15, ApiResult.Number(result, "NewTotalBalance"));

        using var check = db.NewContext();
        Assert.Single(check.Lots);
        Assert.Equal(15, check.InventoryLotBalances.Single().Quantity);
    }

    [Fact]
    public void Two_different_lots_of_the_same_item_in_the_same_bin_are_kept_separate()
    {
        using var db = new TestDatabase();
        var item = db.AddItem(tracksLots: true);
        var bin = db.AddBin();
        var controller = ControllerFor(db);

        controller.ReceiveStock(new ReceiveStockRequest { ItemId = item.Id, WarehouseBinId = bin.Id, Quantity = 10, LotNumber = "LOT-A" });
        controller.ReceiveStock(new ReceiveStockRequest { ItemId = item.Id, WarehouseBinId = bin.Id, Quantity = 7, LotNumber = "LOT-B" });

        using var check = db.NewContext();
        Assert.Equal(2, check.Lots.Count());
        Assert.Equal(2, check.InventoryLotBalances.Count());
        Assert.Equal(17, check.InventoryLotBalances.Sum(b => b.Quantity));
    }

    [Fact]
    public void An_item_that_does_not_track_lots_ignores_a_lot_number_and_uses_the_ordinary_balance()
    {
        using var db = new TestDatabase();
        var item = db.AddItem(tracksLots: false);
        var bin = db.AddBin();

        var result = ControllerFor(db).ReceiveStock(new ReceiveStockRequest
        {
            ItemId = item.Id,
            WarehouseBinId = bin.Id,
            Quantity = 10,
            LotNumber = "LOT-A"
        });

        Assert.IsType<OkObjectResult>(result);

        using var check = db.NewContext();
        Assert.Equal(10, check.InventoryBalances.Single().Quantity);
        Assert.Empty(check.InventoryLotBalances);
        Assert.Empty(check.Lots);
    }

    // --- PICK --------------------------------------------------------

    [Fact]
    public void Picking_a_lot_tracked_item_without_naming_a_lot_is_refused()
    {
        using var db = new TestDatabase();
        var item = db.AddItem(tracksLots: true);
        var bin = db.AddBin();
        var lot = db.AddLot(item);
        db.AddLotBalance(item, bin, lot, 20);

        var result = ControllerFor(db).PickStock(new PickStockRequest
        {
            ItemId = item.Id,
            WarehouseBinId = bin.Id,
            Quantity = 5
        });

        Assert.IsType<BadRequestObjectResult>(result);

        using var check = db.NewContext();
        Assert.Equal(20, check.InventoryLotBalances.Single().Quantity);
    }

    [Fact]
    public void Picking_from_a_named_lot_deducts_only_that_lot()
    {
        using var db = new TestDatabase();
        var item = db.AddItem(tracksLots: true);
        var bin = db.AddBin();
        var lotA = db.AddLot(item, "LOT-A");
        var lotB = db.AddLot(item, "LOT-B");
        db.AddLotBalance(item, bin, lotA, 20);
        db.AddLotBalance(item, bin, lotB, 20);

        var result = ControllerFor(db).PickStock(new PickStockRequest
        {
            ItemId = item.Id,
            WarehouseBinId = bin.Id,
            Quantity = 8,
            LotNumber = "LOT-A"
        });

        Assert.Equal(12, ApiResult.Number(result, "RemainingBalance"));

        using var check = db.NewContext();
        Assert.Equal(12, check.InventoryLotBalances.Single(b => b.LotId == lotA.Id).Quantity);
        Assert.Equal(20, check.InventoryLotBalances.Single(b => b.LotId == lotB.Id).Quantity);

        var movement = check.StockMovements.Single();
        Assert.Equal(lotA.Id, movement.LotId);
        Assert.Equal(-8, movement.QuantityChanged);
    }

    [Fact]
    public void Picking_more_than_a_lot_holds_is_refused_even_if_another_lot_of_the_same_item_has_enough()
    {
        using var db = new TestDatabase();
        var item = db.AddItem(tracksLots: true);
        var bin = db.AddBin();
        var lotA = db.AddLot(item, "LOT-A");
        var lotB = db.AddLot(item, "LOT-B");
        db.AddLotBalance(item, bin, lotA, 3);
        db.AddLotBalance(item, bin, lotB, 100);

        var result = ControllerFor(db).PickStock(new PickStockRequest
        {
            ItemId = item.Id,
            WarehouseBinId = bin.Id,
            Quantity = 4,
            LotNumber = "LOT-A"
        });

        Assert.IsType<BadRequestObjectResult>(result);

        using var check = db.NewContext();
        Assert.Equal(3, check.InventoryLotBalances.Single(b => b.LotId == lotA.Id).Quantity);
        Assert.Empty(check.StockMovements);
    }

    [Fact]
    public void Picking_a_lot_number_that_does_not_exist_for_the_item_is_refused()
    {
        using var db = new TestDatabase();
        var item = db.AddItem(tracksLots: true);
        var bin = db.AddBin();
        db.AddLotBalance(item, bin, db.AddLot(item, "LOT-A"), 20);

        var result = ControllerFor(db).PickStock(new PickStockRequest
        {
            ItemId = item.Id,
            WarehouseBinId = bin.Id,
            Quantity = 1,
            LotNumber = "LOT-NOPE"
        });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    // --- RELOCATE ------------------------------------------------------

    [Fact]
    public void Relocating_a_lot_moves_only_that_lots_stock_between_bins()
    {
        using var db = new TestDatabase();
        var item = db.AddItem(tracksLots: true);
        var source = db.AddBin(shelf: "S1");
        var destination = db.AddBin(shelf: "S2");
        var lot = db.AddLot(item, "LOT-A");
        db.AddLotBalance(item, source, lot, 30);

        var result = ControllerFor(db).RelocateStock(new RelocateStockRequest
        {
            ItemId = item.Id,
            SourceBinId = source.Id,
            DestinationBinId = destination.Id,
            Quantity = 12,
            LotNumber = "LOT-A"
        });

        Assert.IsType<OkObjectResult>(result);

        using var check = db.NewContext();
        Assert.Equal(18, check.InventoryLotBalances.Single(b => b.WarehouseBinId == source.Id).Quantity);
        Assert.Equal(12, check.InventoryLotBalances.Single(b => b.WarehouseBinId == destination.Id).Quantity);

        var movements = check.StockMovements.Where(m => m.TransactionType == "RELOCATE").ToList();
        Assert.All(movements, m => Assert.Equal(lot.Id, m.LotId));
    }

    [Fact]
    public void Relocating_a_lot_tracked_item_without_naming_a_lot_is_refused()
    {
        using var db = new TestDatabase();
        var item = db.AddItem(tracksLots: true);
        var source = db.AddBin(shelf: "S1");
        var destination = db.AddBin(shelf: "S2");
        db.AddLotBalance(item, source, db.AddLot(item), 10);

        var result = ControllerFor(db).RelocateStock(new RelocateStockRequest
        {
            ItemId = item.Id,
            SourceBinId = source.Id,
            DestinationBinId = destination.Id,
            Quantity = 5
        });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    // --- CATALOGUE -------------------------------------------------------

    [Fact]
    public void Quantity_on_hand_includes_lot_balances_for_a_lot_tracked_item()
    {
        using var db = new TestDatabase();
        var item = db.AddItem(tracksLots: true);
        var binA = db.AddBin(shelf: "S1");
        var binB = db.AddBin(shelf: "S2");
        db.AddLotBalance(item, binA, db.AddLot(item, "LOT-A"), 10);
        db.AddLotBalance(item, binB, db.AddLot(item, "LOT-B"), 15);

        var result = ControllerFor(db).GetAllItems();

        var items = ApiResult.Property(ApiResult.Body(result), "Items");
        var first = items.EnumerateArray().Single();
        Assert.Equal(25, ApiResult.Property(first, "QuantityOnHand").GetInt32());
    }

    [Fact]
    public void Deleting_a_lot_tracked_item_with_stock_is_refused()
    {
        using var db = new TestDatabase();
        var item = db.AddItem(tracksLots: true);
        var bin = db.AddBin();
        db.AddLotBalance(item, bin, db.AddLot(item), 5);

        var result = ControllerFor(db).DeleteItem(item.Id);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public void Turning_off_lot_tracking_while_a_lot_still_holds_stock_is_refused()
    {
        using var db = new TestDatabase();
        var item = db.AddItem(tracksLots: true, sku: "SKU-9", name: "Widget", category: "Parts");
        var bin = db.AddBin();
        db.AddLotBalance(item, bin, db.AddLot(item), 5);

        var result = ControllerFor(db).UpdateItem(item.Id, new ItemRequest
        {
            Sku = "SKU-9",
            Name = "Widget",
            Category = "Parts",
            TracksLots = false
        });

        Assert.IsType<ConflictObjectResult>(result);

        using var check = db.NewContext();
        Assert.True(check.Items.Single().TracksLots);
    }
}
