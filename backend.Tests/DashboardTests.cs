using Inventria.Controllers;
using Inventria.Models;

namespace Inventria.Tests;

/// <summary>
/// The figures both dashboards report. These are the numbers people plan around,
/// and every one of them is a sum with a rule about what to leave out.
/// </summary>
public class DashboardTests
{
    private static DashboardController ControllerFor(TestDatabase db) => new(db.Context);

    private static InventoryController InventoryFor(TestDatabase db) =>
        new(db.Context) { ControllerContext = ApiResult.SignedInAs("alice") };

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
}
