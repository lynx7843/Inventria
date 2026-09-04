using Inventria.Controllers;
using Inventria.Models;

namespace Inventria.Tests;

/// <summary>
/// The four reports built over InventoryBalances and StockMovements. None of
/// these write anything, so every test here is about what the query includes,
/// excludes, and orders - not about anything being saved.
/// </summary>
public class ReportsTests
{
    private static ReportsController ControllerFor(TestDatabase db) => new(db.Context);

    // --- STOCK ON HAND -----------------------------------------------------

    [Fact]
    public void Stock_on_hand_lists_one_row_per_item_per_bin_with_a_running_total()
    {
        using var db = new TestDatabase();
        var wrench = db.AddItem("SKU-1", "Wrench");
        db.AddBalance(wrench, db.AddBin(shelf: "S1"), 12);
        db.AddBalance(wrench, db.AddBin(shelf: "S2"), 8);

        var result = ControllerFor(db).GetStockOnHand(category: null, zone: null, page: 1, pageSize: 25);

        Assert.Equal(2, ApiResult.Number(result, "TotalCount"));
        Assert.Equal(20, ApiResult.Number(result, "TotalUnits"));
    }

    [Fact]
    public void Category_filter_is_an_exact_case_insensitive_match()
    {
        using var db = new TestDatabase();
        var tool = db.AddItem("SKU-1", "Wrench", "Tools");
        var cable = db.AddItem("SKU-2", "Cable", "Electronics");
        db.AddBalance(tool, db.AddBin(shelf: "S1"), 5);
        db.AddBalance(cable, db.AddBin(shelf: "S2"), 9);

        var result = ControllerFor(db).GetStockOnHand(category: "tools", zone: null, page: 1, pageSize: 25);

        Assert.Equal(1, ApiResult.Number(result, "TotalCount"));
        Assert.Equal(5, ApiResult.Number(result, "TotalUnits"));
    }

    [Fact]
    public void Zone_filter_narrows_to_that_zones_balances()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        db.AddBalance(item, db.AddBin(zone: "Cold Storage", shelf: "S1"), 30);
        db.AddBalance(item, db.AddBin(zone: "Dry Goods", shelf: "S2"), 40);

        var result = ControllerFor(db).GetStockOnHand(category: null, zone: "Dry Goods", page: 1, pageSize: 25);

        Assert.Equal(1, ApiResult.Number(result, "TotalCount"));
        Assert.Equal(40, ApiResult.Number(result, "TotalUnits"));
    }

    [Fact]
    public void An_empty_warehouse_reports_zeros_rather_than_failing()
    {
        using var db = new TestDatabase();

        var result = ControllerFor(db).GetStockOnHand(category: null, zone: null, page: 1, pageSize: 25);

        Assert.Equal(0, ApiResult.Number(result, "TotalCount"));
        Assert.Equal(0, ApiResult.Number(result, "TotalUnits"));
    }

    // --- MOVEMENTS -----------------------------------------------------------

    [Fact]
    public void Movements_are_returned_newest_first()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin();

        db.Context.StockMovements.Add(new StockMovement
        {
            ItemId = item.Id, WarehouseBinId = bin.Id, TransactionType = "RECEIVE",
            QuantityChanged = 10, Timestamp = DateTime.UtcNow.AddDays(-2), PerformedBy = "alice"
        });
        db.Context.StockMovements.Add(new StockMovement
        {
            ItemId = item.Id, WarehouseBinId = bin.Id, TransactionType = "PICK",
            QuantityChanged = -4, Timestamp = DateTime.UtcNow.AddDays(-1), PerformedBy = "alice"
        });
        db.Context.SaveChanges();

        var result = ControllerFor(db).GetMovements(
            from: null, to: null, type: null, itemId: null, performedBy: null, page: 1, pageSize: 25);
        var rows = ApiResult.Property(ApiResult.Body(result), "Items").EnumerateArray().ToList();

        Assert.Equal("PICK", ApiResult.Property(rows[0], "TransactionType").GetString());
        Assert.Equal("RECEIVE", ApiResult.Property(rows[1], "TransactionType").GetString());
    }

    [Fact]
    public void Type_filter_is_an_exact_case_insensitive_match()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin();
        var inventory = new InventoryController(db.Context) { ControllerContext = ApiResult.SignedInAs("alice") };

        inventory.ReceiveStock(new ReceiveStockRequest { ItemId = item.Id, WarehouseBinId = bin.Id, Quantity = 10 });
        inventory.PickStock(new PickStockRequest { ItemId = item.Id, WarehouseBinId = bin.Id, Quantity = 4 });

        var result = ControllerFor(db).GetMovements(
            from: null, to: null, type: "pick", itemId: null, performedBy: null, page: 1, pageSize: 25);

        Assert.Equal(1, ApiResult.Number(result, "TotalCount"));
    }

    [Fact]
    public void Date_range_excludes_movements_outside_it()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin();

        db.Context.StockMovements.Add(new StockMovement
        {
            ItemId = item.Id, WarehouseBinId = bin.Id, TransactionType = "RECEIVE",
            QuantityChanged = 10, Timestamp = DateTime.UtcNow.AddDays(-40), PerformedBy = "alice"
        });
        db.Context.SaveChanges();

        var result = ControllerFor(db).GetMovements(
            from: DateTime.UtcNow.AddDays(-10), to: null, type: null, itemId: null, performedBy: null,
            page: 1, pageSize: 25);

        Assert.Equal(0, ApiResult.Number(result, "TotalCount"));
    }

    [Fact]
    public void ItemId_and_performedBy_filters_narrow_the_results()
    {
        using var db = new TestDatabase();
        var wrench = db.AddItem("SKU-1", "Wrench");
        var hammer = db.AddItem("SKU-2", "Hammer");
        var bin = db.AddBin();

        db.Context.StockMovements.Add(new StockMovement
        {
            ItemId = wrench.Id, WarehouseBinId = bin.Id, TransactionType = "RECEIVE",
            QuantityChanged = 10, Timestamp = DateTime.UtcNow, PerformedBy = "alice"
        });
        db.Context.StockMovements.Add(new StockMovement
        {
            ItemId = hammer.Id, WarehouseBinId = bin.Id, TransactionType = "RECEIVE",
            QuantityChanged = 5, Timestamp = DateTime.UtcNow, PerformedBy = "bob"
        });
        db.Context.SaveChanges();

        var byItem = ControllerFor(db).GetMovements(
            from: null, to: null, type: null, itemId: wrench.Id, performedBy: null, page: 1, pageSize: 25);
        Assert.Equal(1, ApiResult.Number(byItem, "TotalCount"));

        var byUser = ControllerFor(db).GetMovements(
            from: null, to: null, type: null, itemId: null, performedBy: "BOB", page: 1, pageSize: 25);
        Assert.Equal(1, ApiResult.Number(byUser, "TotalCount"));
    }

    [Fact]
    public void Movements_name_an_item_that_has_since_been_deleted()
    {
        using var db = new TestDatabase();
        var bin = db.AddBin();
        db.AddLegacyOrphanedMovement(missingItemId: 4242, binId: bin.Id, quantity: 5);

        var result = ControllerFor(db).GetMovements(
            from: null, to: null, type: null, itemId: null, performedBy: null, page: 1, pageSize: 25);
        var row = ApiResult.Property(ApiResult.Body(result), "Items").EnumerateArray().Single();

        Assert.Equal("deleted item #4242", ApiResult.Property(row, "ItemName").GetString());
    }

    // --- DEAD STOCK ----------------------------------------------------------

    [Fact]
    public void Dead_stock_excludes_items_moved_recently()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        db.AddBalance(item, db.AddBin(), 10);
        db.Context.StockMovements.Add(new StockMovement
        {
            ItemId = item.Id, TransactionType = "RECEIVE", QuantityChanged = 10,
            Timestamp = DateTime.UtcNow.AddDays(-5), PerformedBy = "alice"
        });
        db.Context.SaveChanges();

        var result = ControllerFor(db).GetDeadStock(days: 90, page: 1, pageSize: 25);

        Assert.Equal(0, ApiResult.Number(result, "TotalCount"));
    }

    [Fact]
    public void Dead_stock_includes_items_whose_last_movement_is_past_the_window()
    {
        using var db = new TestDatabase();
        var item = db.AddItem("SKU-1", "Stale Widget");
        db.AddBalance(item, db.AddBin(), 10);
        db.Context.StockMovements.Add(new StockMovement
        {
            ItemId = item.Id, TransactionType = "RECEIVE", QuantityChanged = 10,
            Timestamp = DateTime.UtcNow.AddDays(-120), PerformedBy = "alice"
        });
        db.Context.SaveChanges();

        var result = ControllerFor(db).GetDeadStock(days: 90, page: 1, pageSize: 25);
        var row = ApiResult.Property(ApiResult.Body(result), "Items").EnumerateArray().Single();

        Assert.Equal("Stale Widget", ApiResult.Property(row, "Name").GetString());
    }

    [Fact]
    public void Dead_stock_includes_items_that_have_never_moved_at_all()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        // Stock landed on the shelf directly (a seeded balance, no movement row) -
        // the same shortcut TestDatabase.AddBalance takes elsewhere in this suite.
        db.AddBalance(item, db.AddBin(), 10);

        var result = ControllerFor(db).GetDeadStock(days: 90, page: 1, pageSize: 25);

        Assert.Equal(1, ApiResult.Number(result, "TotalCount"));
    }

    [Fact]
    public void Dead_stock_excludes_items_with_nothing_on_the_shelf()
    {
        using var db = new TestDatabase();
        // No balance at all: zero units on hand is not money sitting anywhere.
        db.AddItem();

        var result = ControllerFor(db).GetDeadStock(days: 90, page: 1, pageSize: 25);

        Assert.Equal(0, ApiResult.Number(result, "TotalCount"));
    }

    // --- VELOCITY --------------------------------------------------------------

    [Fact]
    public void Velocity_counts_receives_as_in_and_picks_as_out()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin();
        var inventory = new InventoryController(db.Context) { ControllerContext = ApiResult.SignedInAs("alice") };

        inventory.ReceiveStock(new ReceiveStockRequest { ItemId = item.Id, WarehouseBinId = bin.Id, Quantity = 50 });
        inventory.PickStock(new PickStockRequest { ItemId = item.Id, WarehouseBinId = bin.Id, Quantity = 20 });

        var result = ControllerFor(db).GetVelocity(days: 30, page: 1, pageSize: 25);
        var row = ApiResult.Property(ApiResult.Body(result), "Items").EnumerateArray().Single();

        Assert.Equal(50, ApiResult.Property(row, "UnitsIn").GetInt32());
        Assert.Equal(20, ApiResult.Property(row, "UnitsOut").GetInt32());
        Assert.Equal(30, ApiResult.Property(row, "NetChange").GetInt32());
    }

    [Fact]
    public void A_relocation_moves_no_velocity_needle()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var source = db.AddBin(shelf: "S1");
        var destination = db.AddBin(shelf: "S2");
        var inventory = new InventoryController(db.Context) { ControllerContext = ApiResult.SignedInAs("alice") };

        inventory.ReceiveStock(new ReceiveStockRequest { ItemId = item.Id, WarehouseBinId = source.Id, Quantity = 40 });
        inventory.RelocateStock(new RelocateStockRequest
        {
            ItemId = item.Id, SourceBinId = source.Id, DestinationBinId = destination.Id, Quantity = 15
        });

        var result = ControllerFor(db).GetVelocity(days: 30, page: 1, pageSize: 25);
        var row = ApiResult.Property(ApiResult.Body(result), "Items").EnumerateArray().Single();

        // 40 received; the relocation's two legs must not add to either side.
        Assert.Equal(40, ApiResult.Property(row, "UnitsIn").GetInt32());
        Assert.Equal(0, ApiResult.Property(row, "UnitsOut").GetInt32());
    }

    [Fact]
    public void Items_with_no_movement_in_the_window_still_appear_with_zeros()
    {
        using var db = new TestDatabase();
        db.AddItem("SKU-1", "Untouched Widget");

        var result = ControllerFor(db).GetVelocity(days: 30, page: 1, pageSize: 25);
        var row = ApiResult.Property(ApiResult.Body(result), "Items").EnumerateArray().Single();

        Assert.Equal(0, ApiResult.Property(row, "UnitsIn").GetInt32());
        Assert.Equal(0, ApiResult.Property(row, "UnitsOut").GetInt32());
    }

    [Fact]
    public void Movements_older_than_the_window_do_not_count_toward_velocity()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();

        db.Context.StockMovements.Add(new StockMovement
        {
            ItemId = item.Id, TransactionType = "RECEIVE", QuantityChanged = 500,
            Timestamp = DateTime.UtcNow.AddDays(-31), PerformedBy = "alice"
        });
        db.Context.SaveChanges();

        var result = ControllerFor(db).GetVelocity(days: 30, page: 1, pageSize: 25);
        var row = ApiResult.Property(ApiResult.Body(result), "Items").EnumerateArray().Single();

        Assert.Equal(0, ApiResult.Property(row, "UnitsIn").GetInt32());
    }
}
