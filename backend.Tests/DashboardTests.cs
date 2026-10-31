using Inventria;
using Inventria.Controllers;
using Inventria.Models;
using Microsoft.AspNetCore.Mvc;

namespace Inventria.Tests;

/// <summary>
/// The figures both dashboards report. These are the numbers people plan around,
/// and every one of them is a sum with a rule about what to leave out.
/// </summary>
public class DashboardTests
{
    private static DashboardController ControllerFor(TestDatabase db, WarehouseClock? clock = null) =>
        new(db.Context, clock ?? WarehouseClock.Utc);

    private static InventoryController InventoryFor(TestDatabase db) =>
        new(db.Context, new StockReceivingService(db.Context), new StockPickingService(db.Context)) { ControllerContext = ApiResult.SignedInAs("alice") };

    // --- EMPLOYEE --------------------------------------------------------

    [Fact]
    public async Task Employee_totals_count_stock_on_hand_and_items_defined()
    {
        using var db = new TestDatabase();
        var wrench = db.AddItem("SKU-1", "Wrench");
        db.AddItem("SKU-2", "Hammer");
        db.AddBalance(wrench, db.AddBin(shelf: "S1"), 12);
        db.AddBalance(wrench, db.AddBin(shelf: "S2"), 8);

        var result = await ControllerFor(db).GetEmployeeStats();

        Assert.Equal(20, ApiResult.Number(result, "UnitsOnHand"));
        Assert.Equal(2, ApiResult.Number(result, "SkusTracked"));
    }

    [Fact]
    public async Task Todays_receipts_and_picks_are_counted_separately_and_positively()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin();
        var inventory = InventoryFor(db);

        inventory.ReceiveStock(new ReceiveStockRequest { ItemId = item.Id, WarehouseBinId = bin.Id, Quantity = 50 });
        inventory.PickStock(new PickStockRequest { ItemId = item.Id, WarehouseBinId = bin.Id, Quantity = 20 });

        var result = await ControllerFor(db).GetEmployeeStats();

        Assert.Equal(50, ApiResult.Number(result, "ReceivedToday"));

        // Picks are stored negative; a day's picking is a positive count of
        // units that left.
        Assert.Equal(20, ApiResult.Number(result, "PickedToday"));
    }

    [Fact]
    public async Task A_relocation_is_not_a_receipt_or_a_pick()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var source = db.AddBin(shelf: "S1");
        var destination = db.AddBin(shelf: "S2");
        var inventory = InventoryFor(db);

        inventory.ReceiveStock(new ReceiveStockRequest { ItemId = item.Id, WarehouseBinId = source.Id, Quantity = 40 });
        inventory.RelocateStock(new RelocateStockRequest
        {
            ItemId = item.Id,
            SourceBinId = source.Id,
            DestinationBinId = destination.Id,
            Quantity = 15
        });

        var result = await ControllerFor(db).GetEmployeeStats();

        // Shuffling stock between shelves is not goods arriving or leaving, and
        // counting its legs would make a tidy-up look like a day's work.
        Assert.Equal(40, ApiResult.Number(result, "ReceivedToday"));
        Assert.Equal(0, ApiResult.Number(result, "PickedToday"));
    }

    [Fact]
    public async Task Yesterdays_movements_are_not_todays()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin();

        db.Context.StockMovements.Add(new StockMovement
        {
            ItemId = item.Id,
            WarehouseBinId = bin.Id,
            TransactionType = "RECEIVE",
            QuantityChanged = 99,
            Timestamp = DateTime.UtcNow.AddDays(-1),
            PerformedBy = "alice"
        });
        db.Context.SaveChanges();

        var result = await ControllerFor(db).GetEmployeeStats();

        Assert.Equal(0, ApiResult.Number(result, "ReceivedToday"));
    }

    /// <summary>A clock stopped at <paramref name="utcNow"/>, for asking what "today" is.</summary>
    private sealed class FrozenTime(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    private static void AddMovement(TestDatabase db, Item item, WarehouseBin bin, string type, int quantity, DateTime utcTimestamp)
    {
        db.Context.StockMovements.Add(new StockMovement
        {
            ItemId = item.Id,
            WarehouseBinId = bin.Id,
            TransactionType = type,
            QuantityChanged = quantity,
            Timestamp = utcTimestamp,
            PerformedBy = "alice"
        });
        db.Context.SaveChanges();
    }

    [Fact]
    public async Task Today_is_the_warehouses_own_day_not_the_utc_one()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin();

        // Ten hours ahead of UTC - Sydney's offset outside daylight saving, as
        // a fixed zone so the test does not depend on a tzdata release. It is
        // 08:00 on the 26th in the warehouse while UTC is still on the 25th.
        var sydney = TimeZoneInfo.CreateCustomTimeZone(
            "Test/UtcPlus10", TimeSpan.FromHours(10), "UTC+10", "UTC+10");
        var now = new DateTime(2026, 9, 25, 22, 0, 0, DateTimeKind.Utc);
        var clock = new WarehouseClock(sydney, new FrozenTime(now));

        // 01:00 on the 26th on the warehouse floor. Under a UTC day boundary
        // this was filed as yesterday's work - the whole complaint.
        AddMovement(db, item, bin, "RECEIVE", 70, new DateTime(2026, 9, 25, 15, 0, 0, DateTimeKind.Utc));

        // 23:00 on the 25th on the floor: genuinely yesterday, even though UTC
        // still calls it today. Counting it would just move the error an hour.
        AddMovement(db, item, bin, "RECEIVE", 5, new DateTime(2026, 9, 25, 13, 0, 0, DateTimeKind.Utc));

        var result = await ControllerFor(db, clock).GetEmployeeStats();

        Assert.Equal(70, ApiResult.Number(result, "ReceivedToday"));
    }

    [Fact]
    public async Task A_warehouse_behind_utc_does_not_count_tomorrows_work_yet()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin();

        // The other direction: Los Angeles in winter, where UTC has already
        // rolled over to a day the warehouse has not reached.
        var losAngeles = TimeZoneInfo.CreateCustomTimeZone(
            "Test/UtcMinus8", TimeSpan.FromHours(-8), "UTC-8", "UTC-8");
        var now = new DateTime(2026, 1, 15, 2, 0, 0, DateTimeKind.Utc);
        var clock = new WarehouseClock(losAngeles, new FrozenTime(now));

        // Midday on the 14th locally - squarely inside the warehouse's day, but
        // before UTC's, which did not start until 16:00 local. A UTC boundary
        // throws away the whole day shift here.
        AddMovement(db, item, bin, "RECEIVE", 40, new DateTime(2026, 1, 14, 20, 0, 0, DateTimeKind.Utc));

        // 18:00 on the 14th locally: today by either clock.
        AddMovement(db, item, bin, "RECEIVE", 6, new DateTime(2026, 1, 15, 2, 0, 0, DateTimeKind.Utc));

        // 22:00 on the 13th locally: yesterday by either clock.
        AddMovement(db, item, bin, "RECEIVE", 9, new DateTime(2026, 1, 14, 6, 0, 0, DateTimeKind.Utc));

        var result = await ControllerFor(db, clock).GetEmployeeStats();

        Assert.Equal(46, ApiResult.Number(result, "ReceivedToday"));
    }

    [Fact]
    public async Task An_adjustment_does_not_inflate_received_or_picked_today()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin();

        // A cycle-count correction, not a receipt or a pick - see
        // StockMovement.TransactionType. Faking a PICK to record a shortfall is
        // exactly what ADJUST exists to make unnecessary, so both directions are
        // checked here: neither should move either counter at all.
        db.Context.StockMovements.Add(new StockMovement
        {
            ItemId = item.Id,
            WarehouseBinId = bin.Id,
            TransactionType = "ADJUST",
            QuantityChanged = -5,
            Timestamp = DateTime.UtcNow,
            PerformedBy = "alice",
            ReasonCode = "Cycle count shortage"
        });
        db.Context.StockMovements.Add(new StockMovement
        {
            ItemId = item.Id,
            WarehouseBinId = bin.Id,
            TransactionType = "ADJUST",
            QuantityChanged = 5,
            Timestamp = DateTime.UtcNow,
            PerformedBy = "alice",
            ReasonCode = "Cycle count surplus"
        });
        db.Context.SaveChanges();

        var result = await ControllerFor(db).GetEmployeeStats();

        Assert.Equal(0, ApiResult.Number(result, "ReceivedToday"));
        Assert.Equal(0, ApiResult.Number(result, "PickedToday"));
    }

    [Fact]
    public async Task A_restocked_return_is_not_counted_as_todays_receipts()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin();

        // A customer return, not a supplier delivery - see StockMovement and
        // ReturnsController. Lumping it into ReceivedToday would credit a
        // shift with a delivery nobody at the dock actually took in.
        db.Context.StockMovements.Add(new StockMovement
        {
            ItemId = item.Id,
            WarehouseBinId = bin.Id,
            TransactionType = "RETURN",
            QuantityChanged = 5,
            Timestamp = DateTime.UtcNow,
            PerformedBy = "alice",
            ReasonCode = "Customer changed their mind",
            ConditionGrade = "New",
            Disposition = "Restock"
        });
        db.Context.SaveChanges();

        var result = await ControllerFor(db).GetEmployeeStats();

        Assert.Equal(0, ApiResult.Number(result, "ReceivedToday"));
        Assert.Equal(0, ApiResult.Number(result, "PickedToday"));
    }

    [Fact]
    public async Task An_empty_warehouse_reports_zeros_rather_than_failing()
    {
        using var db = new TestDatabase();

        var result = await ControllerFor(db).GetEmployeeStats();

        Assert.Equal(0, ApiResult.Number(result, "UnitsOnHand"));
        Assert.Equal(0, ApiResult.Number(result, "SkusTracked"));
        Assert.Equal(0, ApiResult.Number(result, "ReceivedToday"));
        Assert.Equal(0, ApiResult.Number(result, "PickedToday"));
    }

    // --- ADMIN -----------------------------------------------------------

    [Fact]
    public async Task Monthly_throughput_counts_relocated_units_once_not_twice()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var source = db.AddBin(shelf: "S1");
        var destination = db.AddBin(shelf: "S2");
        var inventory = InventoryFor(db);

        inventory.ReceiveStock(new ReceiveStockRequest { ItemId = item.Id, WarehouseBinId = source.Id, Quantity = 100 });
        inventory.RelocateStock(new RelocateStockRequest
        {
            ItemId = item.Id,
            SourceBinId = source.Id,
            DestinationBinId = destination.Id,
            Quantity = 30
        });

        var result = await ControllerFor(db).GetAdminStats();

        // A relocation writes both of its legs, so adding up every row would
        // count those 30 units twice: 100 received + 30 moved = 130.
        Assert.Equal(130, ApiResult.Number(result, "MonthlyThroughput"));
    }

    [Fact]
    public async Task Monthly_throughput_does_not_count_adjustments()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin();
        var inventory = InventoryFor(db);

        inventory.ReceiveStock(new ReceiveStockRequest { ItemId = item.Id, WarehouseBinId = bin.Id, Quantity = 50 });

        // A cycle-count correction - nothing arrived at the dock or left it, so
        // it should not read as a day's throughput any more than the quiet leg
        // of a RELOCATE does.
        db.Context.StockMovements.Add(new StockMovement
        {
            ItemId = item.Id,
            WarehouseBinId = bin.Id,
            TransactionType = "ADJUST",
            QuantityChanged = -3,
            Timestamp = DateTime.UtcNow,
            PerformedBy = "alice",
            ReasonCode = "Cycle count shortage"
        });
        db.Context.SaveChanges();

        var result = await ControllerFor(db).GetAdminStats();

        Assert.Equal(50, ApiResult.Number(result, "MonthlyThroughput"));
    }

    [Fact]
    public async Task Monthly_throughput_does_not_count_assembly_movements()
    {
        using var db = new TestDatabase();
        var handle = db.AddItem("HANDLE", "Handle");
        var hammer = db.AddItem("HAMMER", "Hammer");
        var bin = db.AddBin();
        var inventory = InventoryFor(db);

        inventory.ReceiveStock(new ReceiveStockRequest { ItemId = handle.Id, WarehouseBinId = bin.Id, Quantity = 50 });

        // Turning components already on the books into a finished item still
        // on the books - nothing arrived at the dock or left it, so it should
        // not read as a day's throughput any more than an ADJUST does.
        db.Context.StockMovements.Add(new StockMovement
        {
            ItemId = handle.Id,
            WarehouseBinId = bin.Id,
            TransactionType = "ASSEMBLE",
            QuantityChanged = -5,
            Timestamp = DateTime.UtcNow,
            PerformedBy = "alice"
        });
        db.Context.StockMovements.Add(new StockMovement
        {
            ItemId = hammer.Id,
            WarehouseBinId = bin.Id,
            TransactionType = "ASSEMBLE",
            QuantityChanged = 5,
            Timestamp = DateTime.UtcNow,
            PerformedBy = "alice"
        });
        db.Context.SaveChanges();

        var result = await ControllerFor(db).GetAdminStats();

        Assert.Equal(50, ApiResult.Number(result, "MonthlyThroughput"));
    }

    [Fact]
    public async Task Movements_older_than_thirty_days_fall_out_of_throughput()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin();

        db.Context.StockMovements.Add(new StockMovement
        {
            ItemId = item.Id,
            WarehouseBinId = bin.Id,
            TransactionType = "RECEIVE",
            QuantityChanged = 500,
            Timestamp = DateTime.UtcNow.AddDays(-31),
            PerformedBy = "alice"
        });
        db.Context.SaveChanges();

        var result = await ControllerFor(db).GetAdminStats();

        Assert.Equal(0, ApiResult.Number(result, "MonthlyThroughput"));
    }

    [Fact]
    public async Task Recent_activity_names_an_item_that_has_since_been_deleted()
    {
        using var db = new TestDatabase();
        var bin = db.AddBin();

        // A movement left behind by a delete that happened before the foreign
        // keys stopped them. The join finds nothing, and a blank where the item
        // name goes tells the reader less than the id does.
        db.AddLegacyOrphanedMovement(missingItemId: 4242, binId: bin.Id, quantity: 5);

        var result = await ControllerFor(db).GetAdminStats();
        var entry = ApiResult.Property(ApiResult.Body(result), "RecentActivity").EnumerateArray().Single();

        Assert.Equal("deleted item #4242", ApiResult.Property(entry, "ItemName").GetString());
    }

    [Fact]
    public async Task Category_distribution_counts_items_per_category()
    {
        using var db = new TestDatabase();
        db.AddItem("SKU-1", "Wrench", "Tools");
        db.AddItem("SKU-2", "Hammer", "Tools");
        db.AddItem("SKU-3", "Cable", "Electronics");

        var result = await ControllerFor(db).GetAdminStats();
        var distribution = ApiResult.Property(ApiResult.Body(result), "Distribution").EnumerateArray().ToList();

        Assert.Equal(3, ApiResult.Number(result, "TotalUniqueItems"));
        Assert.Equal(2, distribution.Count);
        Assert.Equal(2, ApiResult.Property(
            distribution.Single(d => ApiResult.Property(d, "Category").GetString() == "Tools"), "Count").GetInt32());
    }

    // --- STOCK BY CATEGORY (both dashboards) -----------------------------

    private static List<(string Category, int Units)> StockByCategory(IActionResult result) =>
        ApiResult.Property(ApiResult.Body(result), "StockByCategory").EnumerateArray()
            .Select(c => (ApiResult.Property(c, "Category").GetString()!, ApiResult.Property(c, "Units").GetInt32()))
            .ToList();

    [Fact]
    public async Task Stock_by_category_sums_units_on_hand_including_lots_largest_first()
    {
        using var db = new TestDatabase();
        var wrench = db.AddItem("SKU-1", "Wrench", "Tools");
        var hammer = db.AddItem("SKU-2", "Hammer", "Tools");
        var milk = db.AddItem("SKU-3", "Milk", "Dairy", tracksLots: true);
        var shelf1 = db.AddBin(shelf: "S1");
        var shelf2 = db.AddBin(shelf: "S2");

        db.AddBalance(wrench, shelf1, 12);
        db.AddBalance(wrench, shelf2, 8);
        db.AddBalance(hammer, shelf1, 5);
        db.AddLotBalance(milk, shelf1, db.AddLot(milk, "LOT-A"), 30);
        db.AddLotBalance(milk, shelf2, db.AddLot(milk, "LOT-B"), 10);

        var result = await ControllerFor(db).GetAdminStats();

        Assert.Equal([("Dairy", 40), ("Tools", 25)], StockByCategory(result));
    }

    [Fact]
    public async Task Stock_by_category_leaves_out_categories_with_nothing_on_hand()
    {
        using var db = new TestDatabase();
        var wrench = db.AddItem("SKU-1", "Wrench", "Tools");
        db.AddItem("SKU-2", "Cable", "Electronics");
        var bin = db.AddBin();
        db.AddBalance(wrench, bin, 7);
        db.AddBalance(db.AddItem("SKU-3", "Fuse", "Electrical"), bin, 0);

        var result = await ControllerFor(db).GetEmployeeStats();

        Assert.Equal([("Tools", 7)], StockByCategory(result));
    }

    // --- STOCK HISTORY (both dashboards) ---------------------------------

    private static List<int> LevelsFor(IActionResult result, string sku) =>
        ApiResult.Property(
                ApiResult.Property(ApiResult.Body(result), "Series").EnumerateArray()
                    .Single(s => ApiResult.Property(s, "Sku").GetString() == sku),
                "Levels")
            .EnumerateArray().Select(l => l.GetInt32()).ToList();

    [Fact]
    public async Task Stock_history_rebuilds_each_days_closing_level_from_todays_balance()
    {
        using var db = new TestDatabase();
        var now = new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
        var clock = new WarehouseClock(TimeZoneInfo.Utc, new FrozenTime(now));
        var item = db.AddItem("SKU-1");
        var bin = db.AddBin(shelf: "S1");
        var other = db.AddBin(shelf: "S2");

        // 50 in on the 26th, 20 out on the 29th, 10 out this morning: 20 left.
        AddMovement(db, item, bin, "RECEIVE", 50, new DateTime(2026, 10, 26, 9, 0, 0, DateTimeKind.Utc));
        AddMovement(db, item, bin, "PICK", -20, new DateTime(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc));
        // A shuffle between shelves on the 30th - not a change in how many there are.
        AddMovement(db, item, bin, "RELOCATE", -5, new DateTime(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc));
        AddMovement(db, item, other, "RELOCATE", 5, new DateTime(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc));
        AddMovement(db, item, bin, "PICK", -10, new DateTime(2026, 10, 31, 8, 0, 0, DateTimeKind.Utc));
        db.AddBalance(item, bin, 15);
        db.AddBalance(item, other, 5);

        var result = await ControllerFor(db, clock).GetStockHistory(days: 7);
        var dates = ApiResult.Property(ApiResult.Body(result), "Dates").EnumerateArray()
            .Select(d => d.GetString()).ToList();

        Assert.Equal("2026-10-25", dates.First());
        Assert.Equal("2026-10-31", dates.Last());
        //            25  26  27  28  29  30  31
        Assert.Equal([0, 50, 50, 50, 30, 30, 20], LevelsFor(result, "SKU-1"));
    }

    [Fact]
    public async Task Stock_history_covers_every_live_item_and_clamps_the_window()
    {
        using var db = new TestDatabase();
        db.AddItem("SKU-1");
        db.AddItem("SKU-2", "Hammer");
        db.AddItem("SKU-3", "Old Hammer", isArchived: true);

        var result = await ControllerFor(db).GetStockHistory(days: 1);
        var body = ApiResult.Body(result);

        Assert.Equal(7, ApiResult.Property(body, "Dates").GetArrayLength());
        Assert.Equal(["SKU-1", "SKU-2"], ApiResult.Property(body, "Series").EnumerateArray()
            .Select(s => ApiResult.Property(s, "Sku").GetString()).ToList());
        Assert.Equal([0, 0, 0, 0, 0, 0, 0], LevelsFor(result, "SKU-2"));
    }
}
