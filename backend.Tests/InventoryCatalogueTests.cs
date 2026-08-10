using Inventria.Controllers;
using Inventria.Models;
using Microsoft.AspNetCore.Mvc;

namespace Inventria.Tests;

/// <summary>
/// The master item list: paging, and the rules about deleting something the
/// warehouse still has on a shelf or in its history.
/// </summary>
public class InventoryCatalogueTests
{
    private static InventoryController ControllerFor(TestDatabase db) =>
        new(db.Context) { ControllerContext = ApiResult.SignedInAs("alice") };

    private static List<string> NamesIn(IActionResult result) =>
        ApiResult.Property(ApiResult.Body(result), "Items")
            .EnumerateArray()
            .Select(item => ApiResult.Property(item, "Name").GetString()!)
            .ToList();

    // --- PAGING ----------------------------------------------------------

    [Fact]
    public void The_default_page_holds_at_most_twenty_five_items()
    {
        using var db = new TestDatabase();
        for (var i = 1; i <= 30; i++) db.AddItem($"SKU-{i:D3}", $"Item {i:D3}");

        var result = ControllerFor(db).GetAllItems();

        Assert.Equal(25, NamesIn(result).Count);
        Assert.Equal(30, ApiResult.Number(result, "TotalCount"));
        Assert.Equal(2, ApiResult.Number(result, "TotalPages"));
    }

    [Fact]
    public void The_last_page_holds_the_remainder()
    {
        using var db = new TestDatabase();
        for (var i = 1; i <= 30; i++) db.AddItem($"SKU-{i:D3}", $"Item {i:D3}");

        var result = ControllerFor(db).GetAllItems(page: 2);

        Assert.Equal(5, NamesIn(result).Count);
        Assert.Equal(2, ApiResult.Number(result, "Page"));
    }

    [Fact]
    public void Pages_are_ordered_by_name_and_do_not_overlap()
    {
        using var db = new TestDatabase();
        // Inserted in an order that is not the sorted one, so a query without an
        // ORDER BY has every chance to hand back something else.
        foreach (var name in new[] { "Zinc Bar", "Anvil", "Mallet", "Bolt", "Yarn" })
        {
            db.AddItem($"SKU-{name[..3]}", name);
        }

        var controller = ControllerFor(db);
        var first = NamesIn(controller.GetAllItems(page: 1, pageSize: 2));
        var second = NamesIn(controller.GetAllItems(page: 2, pageSize: 2));
        var third = NamesIn(controller.GetAllItems(page: 3, pageSize: 2));

        Assert.Equal(["Anvil", "Bolt"], first);
        Assert.Equal(["Mallet", "Yarn"], second);
        Assert.Equal(["Zinc Bar"], third);

        // Every item appears exactly once across the pages - the property that
        // paging an unordered query silently breaks.
        Assert.Equal(5, first.Concat(second).Concat(third).Distinct().Count());
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    public void A_page_number_below_one_is_clamped_to_the_first_page(int asked, int expected)
    {
        using var db = new TestDatabase();
        db.AddItem();

        var result = ControllerFor(db).GetAllItems(page: asked);

        Assert.Equal(expected, ApiResult.Number(result, "Page"));
    }

    [Fact]
    public void A_page_size_beyond_the_maximum_is_clamped_rather_than_refused()
    {
        using var db = new TestDatabase();
        db.AddItem();

        var result = ControllerFor(db).GetAllItems(pageSize: 5000);

        Assert.Equal(200, ApiResult.Number(result, "PageSize"));
    }

    [Fact]
    public void A_page_size_below_one_is_clamped_up()
    {
        using var db = new TestDatabase();
        db.AddItem();

        var result = ControllerFor(db).GetAllItems(pageSize: 0);

        Assert.Equal(1, ApiResult.Number(result, "PageSize"));
    }

    [Fact]
    public void An_empty_catalogue_answers_with_an_empty_page_rather_than_an_error()
    {
        using var db = new TestDatabase();

        var result = ControllerFor(db).GetAllItems();

        Assert.Empty(NamesIn(result));
        Assert.Equal(0, ApiResult.Number(result, "TotalCount"));
        Assert.Equal(0, ApiResult.Number(result, "TotalPages"));
    }

    [Fact]
    public void Each_item_carries_its_stock_summed_across_every_bin_holding_it()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        db.AddBalance(item, db.AddBin(shelf: "S1"), 12);
        db.AddBalance(item, db.AddBin(shelf: "S2"), 30);

        var result = ControllerFor(db).GetAllItems();
        var row = ApiResult.Property(ApiResult.Body(result), "Items").EnumerateArray().Single();

        Assert.Equal(42, ApiResult.Property(row, "QuantityOnHand").GetInt32());
    }

    [Fact]
    public void An_item_nobody_has_stocked_reports_zero_rather_than_nothing()
    {
        using var db = new TestDatabase();
        db.AddItem();

        var result = ControllerFor(db).GetAllItems();
        var row = ApiResult.Property(ApiResult.Body(result), "Items").EnumerateArray().Single();

        Assert.Equal(0, ApiResult.Property(row, "QuantityOnHand").GetInt32());
    }

    // --- CREATE AND UPDATE -----------------------------------------------

    [Fact]
    public void Creating_an_item_trims_what_it_stores()
    {
        using var db = new TestDatabase();

        var result = ControllerFor(db).CreateItem(new ItemRequest
        {
            Sku = "  SKU-1  ",
            Name = "  Steel Wrench ",
            Category = " Tools "
        });

        Assert.IsType<OkObjectResult>(result);

        using var check = db.NewContext();
        var item = check.Items.Single();

        // Surrounding spaces are invisible in the UI but not to the unique
        // index, so " SKU-1" would sit beside "SKU-1" as a second,
        // indistinguishable code.
        Assert.Equal("SKU-1", item.Sku);
        Assert.Equal("Steel Wrench", item.Name);
        Assert.Equal("Tools", item.Category);
    }

    [Fact]
    public void Updating_an_item_that_does_not_exist_is_a_not_found()
    {
        using var db = new TestDatabase();

        var result = ControllerFor(db).UpdateItem(999, new ItemRequest
        {
            Sku = "SKU-1",
            Name = "Steel Wrench",
            Category = "Tools"
        });

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // --- DELETE ----------------------------------------------------------

    [Fact]
    public void An_item_with_stock_on_a_shelf_cannot_be_deleted()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        db.AddBalance(item, db.AddBin(), 5);

        var result = ControllerFor(db).DeleteItem(item.Id);

        Assert.IsType<ConflictObjectResult>(result);

        // The message has to say what is in the way, or there is nothing to act on.
        var message = ApiResult.Message(result);
        Assert.Contains("5 units", message);
        Assert.Contains("1 bin", message);

        using var check = db.NewContext();
        Assert.Single(check.Items);
    }

    [Fact]
    public void An_item_with_movement_history_cannot_be_deleted_even_at_zero_stock()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin();
        var controller = ControllerFor(db);

        controller.ReceiveStock(new ReceiveStockRequest { ItemId = item.Id, WarehouseBinId = bin.Id, Quantity = 4 });
        controller.PickStock(new PickStockRequest { ItemId = item.Id, WarehouseBinId = bin.Id, Quantity = 4 });

        var result = controller.DeleteItem(item.Id);

        // Deleting it would strand the audit history that says those four units
        // arrived and left.
        Assert.IsType<ConflictObjectResult>(result);
        Assert.Contains("movement", ApiResult.Message(result));

        using var check = db.NewContext();
        Assert.Single(check.Items);
        Assert.Equal(2, check.StockMovements.Count());
    }

    [Fact]
    public void An_item_nothing_references_can_be_deleted()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();

        var result = ControllerFor(db).DeleteItem(item.Id);

        Assert.IsType<OkObjectResult>(result);

        using var check = db.NewContext();
        Assert.Empty(check.Items);
    }

    [Fact]
    public void Deleting_an_item_that_does_not_exist_is_a_not_found()
    {
        using var db = new TestDatabase();

        var result = ControllerFor(db).DeleteItem(999);

        Assert.IsType<NotFoundObjectResult>(result);
    }
}
