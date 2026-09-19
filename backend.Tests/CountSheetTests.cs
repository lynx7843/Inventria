using Inventria.Controllers;
using Inventria.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventria.Tests;

/// <summary>
/// Cycle counting: the only sanctioned way to correct a miscount. Before this,
/// a shortage found on the shelf had no honest transaction type to be recorded
/// as, so these tests lean on the two things that made that a problem -
/// nothing should be adjustable without a reason, and an adjustment should
/// never look like a pick or a receipt to anything counting those.
/// </summary>
public class CountSheetTests
{
    private static CountSheetsController ControllerFor(TestDatabase db, string username = "alice") =>
        new(db.Context) { ControllerContext = ApiResult.SignedInAs(username) };

    private static CountSheet SheetWithLines(TestDatabase db) =>
        db.Context.CountSheets.Include(s => s.Lines).Single();

    // --- OPENING -----------------------------------------------------------

    [Fact]
    public void Opening_a_count_sheet_snapshots_only_the_named_zones_balances()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var binInZone = db.AddBin(zone: "A", shelf: "S1");
        var binOutsideZone = db.AddBin(zone: "B", shelf: "S1", warehouseId: binInZone.WarehouseId);
        db.AddBalance(item, binInZone, 10);
        db.AddBalance(item, binOutsideZone, 99);

        var result = ControllerFor(db).OpenCountSheet(new OpenCountSheetRequest
        {
            WarehouseId = binInZone.WarehouseId,
            Zone = "A"
        });

        Assert.IsType<OkObjectResult>(result);

        var sheet = SheetWithLines(db);
        var line = Assert.Single(sheet.Lines);
        Assert.Equal(binInZone.Id, line.WarehouseBinId);
        Assert.Equal(10, line.ExpectedQuantity);
        Assert.Null(line.CountedQuantity);
    }

    [Fact]
    public void Opening_a_count_sheet_for_a_zone_with_no_stock_is_refused()
    {
        using var db = new TestDatabase();
        var bin = db.AddBin(zone: "Empty Zone");

        var result = ControllerFor(db).OpenCountSheet(new OpenCountSheetRequest
        {
            WarehouseId = bin.WarehouseId,
            Zone = "Empty Zone"
        });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void Opening_a_second_sheet_for_a_zone_already_open_is_refused()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin(zone: "A");
        db.AddBalance(item, bin, 5);
        var controller = ControllerFor(db);

        controller.OpenCountSheet(new OpenCountSheetRequest { WarehouseId = bin.WarehouseId, Zone = "A" });
        var result = controller.OpenCountSheet(new OpenCountSheetRequest { WarehouseId = bin.WarehouseId, Zone = "A" });

        Assert.IsType<ConflictObjectResult>(result);

        using var check = db.NewContext();
        Assert.Single(check.CountSheets);
    }

    [Fact]
    public void A_cancelled_sheet_frees_its_zone_to_be_counted_again()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin(zone: "A");
        db.AddBalance(item, bin, 5);
        var controller = ControllerFor(db);

        controller.OpenCountSheet(new OpenCountSheetRequest { WarehouseId = bin.WarehouseId, Zone = "A" });
        controller.CancelCountSheet(SheetWithLines(db).Id);

        var result = controller.OpenCountSheet(new OpenCountSheetRequest { WarehouseId = bin.WarehouseId, Zone = "A" });

        Assert.IsType<OkObjectResult>(result);
    }

    // --- RECORDING AND REVIEWING --------------------------------------------

    [Fact]
    public void Posting_before_every_line_is_counted_is_refused()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin(zone: "A");
        db.AddBalance(item, bin, 5);
        var controller = ControllerFor(db);
        controller.OpenCountSheet(new OpenCountSheetRequest { WarehouseId = bin.WarehouseId, Zone = "A" });

        var result = controller.PostCountSheet(SheetWithLines(db).Id);

        Assert.IsType<BadRequestObjectResult>(result);

        using var check = db.NewContext();
        Assert.Empty(check.StockMovements);
        Assert.Equal(CountSheetStatus.Open, check.CountSheets.Single().Status);
    }

    [Fact]
    public void Posting_a_variance_with_no_reason_code_is_refused()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin(zone: "A");
        db.AddBalance(item, bin, 10);
        var controller = ControllerFor(db);
        controller.OpenCountSheet(new OpenCountSheetRequest { WarehouseId = bin.WarehouseId, Zone = "A" });
        var line = SheetWithLines(db).Lines.Single();

        controller.RecordCount(line.CountSheetId, line.Id, new RecordCountRequest { CountedQuantity = 9 });
        var result = controller.PostCountSheet(line.CountSheetId);

        Assert.IsType<BadRequestObjectResult>(result);

        using var check = db.NewContext();
        Assert.Empty(check.StockMovements);
    }

    [Fact]
    public void A_count_that_matches_the_books_needs_no_reason_and_posts_no_movement()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin(zone: "A");
        db.AddBalance(item, bin, 10);
        var controller = ControllerFor(db);
        controller.OpenCountSheet(new OpenCountSheetRequest { WarehouseId = bin.WarehouseId, Zone = "A" });
        var line = SheetWithLines(db).Lines.Single();

        controller.RecordCount(line.CountSheetId, line.Id, new RecordCountRequest { CountedQuantity = 10 });
        var result = controller.PostCountSheet(line.CountSheetId);

        Assert.IsType<OkObjectResult>(result);

        using var check = db.NewContext();
        Assert.Empty(check.StockMovements);
        Assert.Equal(10, check.InventoryBalances.Single().Quantity);
        Assert.Equal(CountSheetStatus.Posted, check.CountSheets.Single().Status);
    }

    // --- POSTING -------------------------------------------------------------

    [Fact]
    public void Posting_a_shortage_writes_a_negative_ADJUST_and_reduces_the_balance()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin(zone: "A");
        db.AddBalance(item, bin, 10);
        var controller = ControllerFor(db);
        controller.OpenCountSheet(new OpenCountSheetRequest { WarehouseId = bin.WarehouseId, Zone = "A" });
        var line = SheetWithLines(db).Lines.Single();

        controller.RecordCount(line.CountSheetId, line.Id,
            new RecordCountRequest { CountedQuantity = 7, ReasonCode = "Shelf came up short on the physical count" });

        var result = controller.PostCountSheet(line.CountSheetId);

        Assert.IsType<OkObjectResult>(result);

        using var check = db.NewContext();
        Assert.Equal(7, check.InventoryBalances.Single().Quantity);

        var movement = check.StockMovements.Single(m => m.TransactionType == "ADJUST");
        Assert.Equal(-3, movement.QuantityChanged);
        Assert.Equal("Shelf came up short on the physical count", movement.ReasonCode);
        Assert.Equal("alice", movement.PerformedBy);

        Assert.Equal(CountSheetStatus.Posted, check.CountSheets.Single().Status);
    }

    [Fact]
    public void Posting_a_surplus_writes_a_positive_ADJUST_and_increases_the_balance()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin(zone: "A");
        db.AddBalance(item, bin, 10);
        var controller = ControllerFor(db);
        controller.OpenCountSheet(new OpenCountSheetRequest { WarehouseId = bin.WarehouseId, Zone = "A" });
        var line = SheetWithLines(db).Lines.Single();

        controller.RecordCount(line.CountSheetId, line.Id,
            new RecordCountRequest { CountedQuantity = 14, ReasonCode = "Found extra units mis-shelved from another bin" });

        var result = controller.PostCountSheet(line.CountSheetId);

        Assert.IsType<OkObjectResult>(result);

        using var check = db.NewContext();
        Assert.Equal(14, check.InventoryBalances.Single().Quantity);
        Assert.Equal(4, check.StockMovements.Single(m => m.TransactionType == "ADJUST").QuantityChanged);
    }

    [Fact]
    public void Counting_a_lot_tracked_items_balance_adjusts_that_lot_and_no_other()
    {
        using var db = new TestDatabase();
        var item = db.AddItem(tracksLots: true);
        var bin = db.AddBin(zone: "A");
        var lot = db.AddLot(item, "LOT-1");
        var otherLot = db.AddLot(item, "LOT-2");
        db.AddLotBalance(item, bin, lot, 20);
        db.AddLotBalance(item, bin, otherLot, 20);
        var controller = ControllerFor(db);

        controller.OpenCountSheet(new OpenCountSheetRequest { WarehouseId = bin.WarehouseId, Zone = "A" });
        var sheet = SheetWithLines(db);
        var line = sheet.Lines.Single(l => l.LotId == lot.Id);

        controller.RecordCount(line.CountSheetId, line.Id,
            new RecordCountRequest { CountedQuantity = 18, ReasonCode = "Two units damaged and discarded" });
        controller.RecordCount(line.CountSheetId, sheet.Lines.Single(l => l.LotId == otherLot.Id).Id,
            new RecordCountRequest { CountedQuantity = 20 });

        var result = controller.PostCountSheet(line.CountSheetId);

        Assert.IsType<OkObjectResult>(result);

        using var check = db.NewContext();
        Assert.Equal(18, check.InventoryLotBalances.Single(b => b.LotId == lot.Id).Quantity);
        Assert.Equal(20, check.InventoryLotBalances.Single(b => b.LotId == otherLot.Id).Quantity);

        var movement = check.StockMovements.Single(m => m.TransactionType == "ADJUST");
        Assert.Equal(-2, movement.QuantityChanged);
        Assert.Equal(lot.Id, movement.LotId);
    }

    // --- ITEMS FOUND OUTSIDE THE SNAPSHOT ------------------------------------

    [Fact]
    public void An_item_found_with_no_recorded_balance_can_be_added_and_posts_as_a_surplus()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var otherItem = db.AddItem("SKU-2", "Hammer");
        var bin = db.AddBin(zone: "A");
        db.AddBalance(item, bin, 5);
        var controller = ControllerFor(db);

        controller.OpenCountSheet(new OpenCountSheetRequest { WarehouseId = bin.WarehouseId, Zone = "A" });
        var sheet = SheetWithLines(db);

        var addResult = controller.AddLine(sheet.Id,
            new AddCountSheetLineRequest { ItemId = otherItem.Id, WarehouseBinId = bin.Id });
        Assert.IsType<OkObjectResult>(addResult);

        sheet = SheetWithLines(db);
        var expectedLine = sheet.Lines.Single(l => l.ItemId == item.Id);
        var foundLine = sheet.Lines.Single(l => l.ItemId == otherItem.Id);
        Assert.Equal(0, foundLine.ExpectedQuantity);

        controller.RecordCount(sheet.Id, expectedLine.Id, new RecordCountRequest { CountedQuantity = 5 });
        controller.RecordCount(sheet.Id, foundLine.Id,
            new RecordCountRequest { CountedQuantity = 4, ReasonCode = "Found unlabeled stock while counting the zone" });

        var result = controller.PostCountSheet(sheet.Id);

        Assert.IsType<OkObjectResult>(result);

        using var check = db.NewContext();
        Assert.Equal(4, check.InventoryBalances.Single(b => b.ItemId == otherItem.Id).Quantity);
        Assert.Equal(4, check.StockMovements.Single(m => m.ItemId == otherItem.Id).QuantityChanged);
    }

    [Fact]
    public void A_bin_outside_the_sheets_zone_cannot_be_added_to_it()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var otherItem = db.AddItem("SKU-2", "Hammer");
        var bin = db.AddBin(zone: "A");
        var outsideBin = db.AddBin(zone: "B", warehouseId: bin.WarehouseId);
        db.AddBalance(item, bin, 5);
        var controller = ControllerFor(db);

        controller.OpenCountSheet(new OpenCountSheetRequest { WarehouseId = bin.WarehouseId, Zone = "A" });
        var sheet = SheetWithLines(db);

        var result = controller.AddLine(sheet.Id,
            new AddCountSheetLineRequest { ItemId = otherItem.Id, WarehouseBinId = outsideBin.Id });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    // --- CANCELLING ------------------------------------------------------

    [Fact]
    public void Cancelling_a_sheet_leaves_balances_untouched()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin(zone: "A");
        db.AddBalance(item, bin, 5);
        var controller = ControllerFor(db);
        controller.OpenCountSheet(new OpenCountSheetRequest { WarehouseId = bin.WarehouseId, Zone = "A" });

        var result = controller.CancelCountSheet(SheetWithLines(db).Id);

        Assert.IsType<OkObjectResult>(result);

        using var check = db.NewContext();
        Assert.Equal(CountSheetStatus.Cancelled, check.CountSheets.Single().Status);
        Assert.Equal(5, check.InventoryBalances.Single().Quantity);
        Assert.Empty(check.StockMovements);
    }
}
