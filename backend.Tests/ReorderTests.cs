using Inventria.Controllers;
using Inventria.Models;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Inventria.Tests;

/// <summary>
/// Reorder points: what counts as low on stock, how much the system suggests
/// buying, and every place that answer surfaces - the dashboards' count, the
/// reorder report, and the warning a picker gets when they take the last of
/// something.
///
/// All of it comes from one query (<see cref="LowStock"/>), so most of these
/// tests are about that definition holding at each of those places rather than
/// about three separate implementations agreeing by luck.
/// </summary>
public class ReorderTests
{
    private static ReportsController ReportsFor(TestDatabase db) => new(db.Context);

    private static DashboardController DashboardFor(TestDatabase db) => new(db.Context);

    private static InventoryController InventoryFor(TestDatabase db) =>
        new(db.Context) { ControllerContext = ApiResult.SignedInAs("alice") };

    private static List<JsonElement> RowsIn(IActionResult result) =>
        ApiResult.Property(ApiResult.Body(result), "Items").EnumerateArray().ToList();

    private static List<string> NamesIn(IActionResult result) =>
        RowsIn(result).Select(row => ApiResult.Property(row, "Name").GetString()!).ToList();

    private static int Field(JsonElement row, string name) =>
        ApiResult.Property(row, name).GetInt32();

    /// <summary>The single row a one-item report answered with.</summary>
    private static JsonElement OnlyRow(IActionResult result) => Assert.Single(RowsIn(result));

    private static IActionResult Reorder(TestDatabase db, string? category = null, int page = 1, int pageSize = 25) =>
        ReportsFor(db).GetReorder(category, page, pageSize);

    // --- WHAT COUNTS AS LOW ------------------------------------------------

    [Fact]
    public void An_item_with_no_reorder_point_never_counts_as_low()
    {
        using var db = new TestDatabase();
        var item = db.AddItem("SKU-1", "Wrench");
        db.AddBalance(item, db.AddBin(), 0);

        // Zero on the shelf and zero as a reorder point: nobody has said this
        // item needs a level, so an empty shelf is not an alert. This is what
        // keeps every item that predates the column silent.
        Assert.Equal(0, ApiResult.Number(Reorder(db), "TotalCount"));
    }

    [Fact]
    public void An_item_below_its_reorder_point_is_listed()
    {
        using var db = new TestDatabase();
        var item = db.AddItem("SKU-1", "Wrench", reorderPoint: 10);
        db.AddBalance(item, db.AddBin(), 3);

        Assert.Equal(["Wrench"], NamesIn(Reorder(db)));
    }

    [Fact]
    public void An_item_sitting_exactly_on_its_reorder_point_is_listed()
    {
        using var db = new TestDatabase();
        var item = db.AddItem("SKU-1", "Wrench", reorderPoint: 10);
        db.AddBalance(item, db.AddBin(), 10);

        // The reorder point is the level that triggers an order, so reaching it
        // is the trigger. Waiting for stock to fall past it would place every
        // order one unit late.
        Assert.Equal(["Wrench"], NamesIn(Reorder(db)));
    }

    [Fact]
    public void An_item_above_its_reorder_point_is_not_listed()
    {
        using var db = new TestDatabase();
        var item = db.AddItem("SKU-1", "Wrench", reorderPoint: 10);
        db.AddBalance(item, db.AddBin(), 11);

        Assert.Equal(0, ApiResult.Number(Reorder(db), "TotalCount"));
    }

    [Fact]
    public void An_item_with_a_reorder_point_and_no_stock_at_all_is_listed()
    {
        using var db = new TestDatabase();
        // No balance row anywhere - not a row holding zero, no row. The sum has
        // to read that as nothing on the shelves rather than as nothing to say.
        db.AddItem("SKU-1", "Wrench", reorderPoint: 10);

        var row = OnlyRow(Reorder(db));

        Assert.Equal(0, Field(row, "QuantityOnHand"));
        Assert.Equal(10, Field(row, "Shortfall"));
    }

    [Fact]
    public void Stock_is_counted_across_every_bin_the_item_sits_in()
    {
        using var db = new TestDatabase();
        var item = db.AddItem("SKU-1", "Wrench", reorderPoint: 10);
        db.AddBalance(item, db.AddBin(shelf: "S1"), 6);
        db.AddBalance(item, db.AddBin(shelf: "S2"), 6);

        // 6 in one bin looks low on its own; 12 across the warehouse is not. A
        // reorder point is a question about the building, not about a shelf.
        Assert.Equal(0, ApiResult.Number(Reorder(db), "TotalCount"));
    }

    // --- HOW MUCH TO ORDER --------------------------------------------------

    [Fact]
    public void With_no_reorder_quantity_the_suggestion_tops_back_up_to_the_point()
    {
        using var db = new TestDatabase();
        var item = db.AddItem("SKU-1", "Wrench", reorderPoint: 10);
        db.AddBalance(item, db.AddBin(), 4);

        var row = OnlyRow(Reorder(db));

        Assert.Equal(6, Field(row, "Shortfall"));
        Assert.Equal(6, Field(row, "SuggestedOrderQuantity"));
    }

    [Fact]
    public void A_reorder_quantity_is_the_lot_size_the_suggestion_is_built_from()
    {
        using var db = new TestDatabase();
        var item = db.AddItem("SKU-1", "Wrench", reorderPoint: 10, reorderQuantity: 50);
        db.AddBalance(item, db.AddBin(), 4);

        // Six units short, but the supplier ships in fifties - so one lot, not
        // six units.
        Assert.Equal(50, Field(OnlyRow(Reorder(db)), "SuggestedOrderQuantity"));
    }

    [Fact]
    public void More_lots_are_suggested_when_one_would_not_clear_the_shortfall()
    {
        using var db = new TestDatabase();
        var item = db.AddItem("SKU-1", "Wrench", reorderPoint: 200, reorderQuantity: 50);
        db.AddBalance(item, db.AddBin(), 20);

        // 180 short in lots of 50 is four lots: three would arrive and leave the
        // item under its point on the day it landed.
        Assert.Equal(200, Field(OnlyRow(Reorder(db)), "SuggestedOrderQuantity"));
    }

    [Fact]
    public void An_exact_multiple_is_not_rounded_up_to_a_spare_lot()
    {
        using var db = new TestDatabase();
        var item = db.AddItem("SKU-1", "Wrench", reorderPoint: 100, reorderQuantity: 25);
        db.AddBalance(item, db.AddBin(), 50);

        Assert.Equal(50, Field(OnlyRow(Reorder(db)), "SuggestedOrderQuantity"));
    }

    [Fact]
    public void An_item_exactly_on_its_point_still_orders_a_whole_lot()
    {
        using var db = new TestDatabase();
        var item = db.AddItem("SKU-1", "Wrench", reorderPoint: 10, reorderQuantity: 50);
        db.AddBalance(item, db.AddBin(), 10);

        // Nothing is missing yet, but the trigger has fired, and the answer to
        // "how much" is a lot rather than the zero the arithmetic alone gives.
        Assert.Equal(50, Field(OnlyRow(Reorder(db)), "SuggestedOrderQuantity"));
    }

    [Fact]
    public void An_item_on_its_point_with_no_lot_size_is_flagged_without_a_quantity()
    {
        using var db = new TestDatabase();
        var item = db.AddItem("SKU-1", "Wrench", reorderPoint: 10);
        db.AddBalance(item, db.AddBin(), 10);

        var result = Reorder(db);

        // The one case where the data cannot answer "how much": stock is exactly
        // at the level that says buy more, and nobody has recorded how much a
        // purchase of this item is. It is listed as low, and left out of the
        // count of lines an order can actually be written from, rather than given
        // an invented figure.
        Assert.Equal(0, Field(OnlyRow(result), "SuggestedOrderQuantity"));
        Assert.Equal(1, ApiResult.Number(result, "TotalCount"));
        Assert.Equal(0, ApiResult.Number(result, "OrderableCount"));
    }

    // --- THE REPORT ---------------------------------------------------------

    [Fact]
    public void The_most_urgent_item_is_the_one_with_least_of_what_it_needs()
    {
        using var db = new TestDatabase();
        var bin = db.AddBin();

        // 450 of 500 is a bigger shortfall in units than 0 of 5, and far less
        // urgent: one has weeks of cover, the other cannot fill an order today.
        var comfortable = db.AddItem("SKU-1", "Pallet Wrap", reorderPoint: 500);
        db.AddBalance(comfortable, bin, 450);

        var stockout = db.AddItem("SKU-2", "Wrench", reorderPoint: 5);
        db.AddBalance(stockout, db.AddBin(shelf: "S2"), 0);

        var halfway = db.AddItem("SKU-3", "Hammer", reorderPoint: 20);
        db.AddBalance(halfway, db.AddBin(shelf: "S3"), 10);

        Assert.Equal(["Wrench", "Hammer", "Pallet Wrap"], NamesIn(Reorder(db)));
    }

    [Fact]
    public void The_category_filter_is_an_exact_case_insensitive_match()
    {
        using var db = new TestDatabase();
        var tool = db.AddItem("SKU-1", "Wrench", "Tools", reorderPoint: 10);
        var cable = db.AddItem("SKU-2", "Cable", "Electronics", reorderPoint: 10);
        db.AddBalance(tool, db.AddBin(shelf: "S1"), 1);
        db.AddBalance(cable, db.AddBin(shelf: "S2"), 1);

        Assert.Equal(["Wrench"], NamesIn(Reorder(db, category: "tools")));
    }

    [Fact]
    public void The_units_to_order_are_totalled_across_every_page_not_just_this_one()
    {
        using var db = new TestDatabase();
        for (var i = 1; i <= 4; i++)
        {
            var item = db.AddItem($"SKU-{i}", $"Item {i}", reorderPoint: 10);
            db.AddBalance(item, db.AddBin(shelf: $"S{i}"), 0);
        }

        var result = Reorder(db, pageSize: 2);

        // A buyer asking how much they are about to order is not asking about
        // page one.
        Assert.Equal(2, RowsIn(result).Count);
        Assert.Equal(4, ApiResult.Number(result, "TotalCount"));
        Assert.Equal(4, ApiResult.Number(result, "OrderableCount"));
        Assert.Equal(40, ApiResult.Number(result, "TotalUnitsToOrder"));
    }

    [Fact]
    public void An_empty_warehouse_reports_zeros_rather_than_failing()
    {
        using var db = new TestDatabase();

        var result = Reorder(db);

        Assert.Equal(0, ApiResult.Number(result, "TotalCount"));
        Assert.Equal(0, ApiResult.Number(result, "TotalUnitsToOrder"));
        Assert.Equal(0, ApiResult.Number(result, "OrderableCount"));
    }

    [Fact]
    public void The_page_size_ceiling_is_the_same_two_hundred_the_other_reports_use()
    {
        using var db = new TestDatabase();

        var result = Reorder(db, pageSize: 5000);

        Assert.Equal(200, ApiResult.Number(result, "PageSize"));
    }

    // --- THE DASHBOARD TILES ------------------------------------------------

    [Fact]
    public async Task Both_dashboards_count_the_same_low_items()
    {
        using var db = new TestDatabase();
        var low = db.AddItem("SKU-1", "Wrench", reorderPoint: 10);
        db.AddBalance(low, db.AddBin(shelf: "S1"), 2);

        var fine = db.AddItem("SKU-2", "Hammer", reorderPoint: 10);
        db.AddBalance(fine, db.AddBin(shelf: "S2"), 40);

        var untracked = db.AddItem("SKU-3", "Cable");
        db.AddBalance(untracked, db.AddBin(shelf: "S3"), 0);

        var employee = await DashboardFor(db).GetEmployeeStats();
        var admin = await DashboardFor(db).GetAdminStats();

        Assert.Equal(1, ApiResult.Number(employee, "LowStockCount"));
        Assert.Equal(1, ApiResult.Number(admin, "LowStockCount"));
        Assert.Equal(1, ApiResult.Number(Reorder(db), "TotalCount"));
    }

    // --- THE MASTER LIST ----------------------------------------------------

    [Fact]
    public void Reorder_levels_survive_a_create_and_come_back_on_the_item()
    {
        using var db = new TestDatabase();

        InventoryFor(db).CreateItem(new ItemRequest
        {
            Sku = "SKU-1",
            Name = "Wrench",
            Category = "Tools",
            ReorderPoint = 25,
            ReorderQuantity = 100
        });

        var row = Assert.Single(RowsIn(InventoryFor(db).GetAllItems()));

        Assert.Equal(25, Field(row, "ReorderPoint"));
        Assert.Equal(100, Field(row, "ReorderQuantity"));
    }

    [Fact]
    public void An_edit_can_change_the_levels_and_can_turn_tracking_back_off()
    {
        using var db = new TestDatabase();
        var item = db.AddItem("SKU-1", "Wrench", reorderPoint: 25, reorderQuantity: 100);

        InventoryFor(db).UpdateItem(item.Id, new ItemRequest
        {
            Sku = "SKU-1",
            Name = "Wrench",
            Category = "Tools",
            ReorderPoint = 0,
            ReorderQuantity = 0
        });

        var saved = db.NewContext().Items.Single();

        Assert.Equal(0, saved.ReorderPoint);
        Assert.Equal(0, saved.ReorderQuantity);
    }

    // --- THE WARNING A PICKER SEES ------------------------------------------

    /// <summary>The `LowStockWarning` a pick answers with, or null.</summary>
    private static string? WarningIn(IActionResult result)
    {
        var field = ApiResult.Property(ApiResult.Body(result), "LowStockWarning");
        return field.ValueKind == JsonValueKind.Null ? null : field.GetString();
    }

    [Fact]
    public void A_pick_that_takes_an_item_to_its_reorder_point_says_so()
    {
        using var db = new TestDatabase();
        db.AddUser();
        var item = db.AddItem("SKU-1", "Wrench", reorderPoint: 10, reorderQuantity: 50);
        var bin = db.AddBin();
        db.AddBalance(item, bin, 30);

        var result = InventoryFor(db).PickStock(new PickStockRequest
        {
            ItemId = item.Id,
            WarehouseBinId = bin.Id,
            Quantity = 25
        });

        var warning = WarningIn(result);

        Assert.NotNull(warning);
        Assert.Contains("Wrench", warning);
        // The state after the pick, not before it: 5 left, not 30.
        Assert.Contains("5 units", warning);
        Assert.Contains("50", warning);
    }

    [Fact]
    public void A_pick_that_leaves_plenty_on_the_shelf_says_nothing()
    {
        using var db = new TestDatabase();
        db.AddUser();
        var item = db.AddItem("SKU-1", "Wrench", reorderPoint: 10);
        var bin = db.AddBin();
        db.AddBalance(item, bin, 100);

        var result = InventoryFor(db).PickStock(new PickStockRequest
        {
            ItemId = item.Id,
            WarehouseBinId = bin.Id,
            Quantity = 5
        });

        Assert.Null(WarningIn(result));
    }

    [Fact]
    public void Emptying_one_bin_of_an_item_stocked_elsewhere_is_not_a_warning()
    {
        using var db = new TestDatabase();
        db.AddUser();
        var item = db.AddItem("SKU-1", "Wrench", reorderPoint: 10);
        var picked = db.AddBin(shelf: "S1");
        db.AddBalance(item, picked, 8);
        db.AddBalance(item, db.AddBin(shelf: "S2"), 400);

        var result = InventoryFor(db).PickStock(new PickStockRequest
        {
            ItemId = item.Id,
            WarehouseBinId = picked.Id,
            Quantity = 8
        });

        // The shelf is bare and the warehouse is not. Alerting here would train
        // people to ignore the alert.
        Assert.Null(WarningIn(result));
    }

    [Fact]
    public void An_untracked_item_never_warns_however_far_it_is_picked_down()
    {
        using var db = new TestDatabase();
        db.AddUser();
        var item = db.AddItem("SKU-1", "Wrench");
        var bin = db.AddBin();
        db.AddBalance(item, bin, 5);

        var result = InventoryFor(db).PickStock(new PickStockRequest
        {
            ItemId = item.Id,
            WarehouseBinId = bin.Id,
            Quantity = 5
        });

        Assert.Null(WarningIn(result));
    }

    [Fact]
    public void Turning_low_stock_alerts_off_withholds_the_warning()
    {
        using var db = new TestDatabase();
        db.AddUser(notifyLowStock: false);
        var item = db.AddItem("SKU-1", "Wrench", reorderPoint: 10);
        var bin = db.AddBin();
        db.AddBalance(item, bin, 12);

        var result = InventoryFor(db).PickStock(new PickStockRequest
        {
            ItemId = item.Id,
            WarehouseBinId = bin.Id,
            Quantity = 5
        });

        // The Settings toggle finally decides something. The pick still goes
        // through and the item is still low - the reorder report and the
        // dashboard tile are unaffected by one person's preference.
        Assert.Null(WarningIn(result));
        Assert.Equal(1, ApiResult.Number(Reorder(db), "TotalCount"));
    }
}
