using Inventria.Controllers;
using Inventria.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventria.Tests;

/// <summary>
/// Batched picking: a list of lines walked as one route instead of one bin
/// trip per item. The route itself - sorting by the bin's Zone/Aisle/Shelf -
/// is the point of the feature, so that is what most of these check; the
/// actual stock write is StockPickingService's job and is covered by
/// StockMovementTests.
/// </summary>
public class PickListTests
{
    private static PickListsController ControllerFor(TestDatabase db, string username = "alice") =>
        new(db.Context, new StockPickingService(db.Context)) { ControllerContext = ApiResult.SignedInAs(username) };

    private static PickList ListWithLines(TestDatabase db) =>
        db.Context.PickLists.Include(l => l.Lines).Single();

    [Fact]
    public void Opening_a_list_sorts_its_lines_into_a_zone_aisle_shelf_route()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();

        // Deliberately out of order, so a route that just echoed request order
        // back would pass this test by accident.
        var binZ = db.AddBin(zone: "Z", aisle: "A1", shelf: "S1");
        var binA1 = db.AddBin(zone: "A", aisle: "A1", shelf: "S2", warehouseId: binZ.WarehouseId);
        var binA0 = db.AddBin(zone: "A", aisle: "A1", shelf: "S1", warehouseId: binZ.WarehouseId);

        var controller = ControllerFor(db);
        var result = controller.OpenPickList(new OpenPickListRequest
        {
            Lines =
            [
                new PickListLineRequest { ItemId = item.Id, WarehouseBinId = binZ.Id, Quantity = 1 },
                new PickListLineRequest { ItemId = item.Id, WarehouseBinId = binA1.Id, Quantity = 1 },
                new PickListLineRequest { ItemId = item.Id, WarehouseBinId = binA0.Id, Quantity = 1 }
            ]
        });

        Assert.IsType<OkObjectResult>(result);

        var lines = ListWithLines(db).Lines.OrderBy(l => l.Sequence).ToList();
        Assert.Equal(3, lines.Count);
        Assert.Equal(binA0.Id, lines[0].WarehouseBinId);
        Assert.Equal(binA1.Id, lines[1].WarehouseBinId);
        Assert.Equal(binZ.Id, lines[2].WarehouseBinId);
        Assert.Equal([1, 2, 3], lines.Select(l => l.Sequence));
    }

    [Fact]
    public void Opening_a_list_for_an_item_that_does_not_exist_is_refused()
    {
        using var db = new TestDatabase();
        var bin = db.AddBin();

        var result = ControllerFor(db).OpenPickList(new OpenPickListRequest
        {
            Lines = [new PickListLineRequest { ItemId = 999, WarehouseBinId = bin.Id, Quantity = 1 }]
        });

        Assert.IsType<NotFoundObjectResult>(result);

        using var check = db.NewContext();
        Assert.Empty(check.PickLists);
    }

    [Fact]
    public void Opening_a_list_for_a_lot_tracked_item_without_a_lot_number_is_refused()
    {
        using var db = new TestDatabase();
        var item = db.AddItem(tracksLots: true);
        var bin = db.AddBin();

        var result = ControllerFor(db).OpenPickList(new OpenPickListRequest
        {
            Lines = [new PickListLineRequest { ItemId = item.Id, WarehouseBinId = bin.Id, Quantity = 1 }]
        });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void Picking_a_line_deducts_stock_and_marks_it_off()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin(zone: "A");
        db.AddBalance(item, bin, 20);
        var controller = ControllerFor(db);

        controller.OpenPickList(new OpenPickListRequest
        {
            Lines = [new PickListLineRequest { ItemId = item.Id, WarehouseBinId = bin.Id, Quantity = 8 }]
        });
        var line = ListWithLines(db).Lines.Single();

        var result = controller.PickLine(line.PickListId, line.Id, new PickPickListLineRequest());

        Assert.IsType<OkObjectResult>(result);

        using var check = db.NewContext();
        Assert.Equal(12, check.InventoryBalances.Single().Quantity);
        Assert.Equal("PICK", check.StockMovements.Single().TransactionType);
        Assert.Equal(-8, check.StockMovements.Single().QuantityChanged);
        Assert.Equal(8, check.PickListLines.Single().QuantityPicked);
        Assert.Equal(PickListStatus.Completed, check.PickLists.Single().Status);
    }

    [Fact]
    public void Picking_a_line_answers_with_the_item_and_bin_each_line_names()
    {
        using var db = new TestDatabase();
        var item = db.AddItem(sku: "SKU-7", name: "Industrial Fan");
        var bin = db.AddBin(zone: "Bulk Storage", aisle: "B3", shelf: "S4");
        db.AddBalance(item, bin, 20);

        ControllerFor(db).OpenPickList(new OpenPickListRequest
        {
            Lines = [new PickListLineRequest { ItemId = item.Id, WarehouseBinId = bin.Id, Quantity = 8 }]
        });
        var line = ListWithLines(db).Lines.Single();

        // A context of its own, with nothing already tracked in it. The one the
        // list was built through has the item and the bin in its change tracker,
        // so EF fixes up every line's navigation properties there whether the
        // query asked for them or not - and a controller over that context
        // answers correctly even when it has not loaded them. A picker's second
        // request arrives at a fresh context, which is where a response built
        // from unloaded navigations turns into nulls on the wire.
        using var fresh = db.NewContext();
        var controller = new PickListsController(fresh, new StockPickingService(fresh))
        {
            ControllerContext = ApiResult.SignedInAs("alice")
        };

        var result = controller.PickLine(line.PickListId, line.Id, new PickPickListLineRequest { Quantity = 3 });

        Assert.IsType<OkObjectResult>(result);

        var answered = ApiResult.Property(
            ApiResult.Property(ApiResult.Body(result), "PickList"), "Lines").EnumerateArray().Single();

        Assert.Equal("Industrial Fan", ApiResult.Property(answered, "ItemName").GetString());
        Assert.Equal("SKU-7", ApiResult.Property(answered, "ItemSku").GetString());
        Assert.Equal("Bulk Storage", ApiResult.Property(answered, "BinZone").GetString());
        Assert.Equal("B3", ApiResult.Property(answered, "BinAisle").GetString());
        Assert.Equal("S4", ApiResult.Property(answered, "BinShelf").GetString());
    }

    [Fact]
    public void A_list_stays_open_until_every_line_is_fully_picked()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var binA = db.AddBin(zone: "A", shelf: "S1");
        var binB = db.AddBin(zone: "A", shelf: "S2", warehouseId: binA.WarehouseId);
        db.AddBalance(item, binA, 10);
        db.AddBalance(item, binB, 10);
        var controller = ControllerFor(db);

        controller.OpenPickList(new OpenPickListRequest
        {
            Lines =
            [
                new PickListLineRequest { ItemId = item.Id, WarehouseBinId = binA.Id, Quantity = 5 },
                new PickListLineRequest { ItemId = item.Id, WarehouseBinId = binB.Id, Quantity = 5 }
            ]
        });
        var list = ListWithLines(db);
        var firstLine = list.Lines.OrderBy(l => l.Sequence).First();

        controller.PickLine(list.Id, firstLine.Id, new PickPickListLineRequest());

        using var midway = db.NewContext();
        Assert.Equal(PickListStatus.Open, midway.PickLists.Single().Status);

        var secondLine = list.Lines.OrderBy(l => l.Sequence).Last();
        controller.PickLine(list.Id, secondLine.Id, new PickPickListLineRequest());

        using var check = db.NewContext();
        Assert.Equal(PickListStatus.Completed, check.PickLists.Single().Status);
    }

    [Fact]
    public void A_short_pick_can_take_less_than_the_line_asks_for_and_leaves_the_list_open()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin(zone: "A");
        db.AddBalance(item, bin, 20);
        var controller = ControllerFor(db);

        controller.OpenPickList(new OpenPickListRequest
        {
            Lines = [new PickListLineRequest { ItemId = item.Id, WarehouseBinId = bin.Id, Quantity = 10 }]
        });
        var line = ListWithLines(db).Lines.Single();

        var result = controller.PickLine(line.PickListId, line.Id, new PickPickListLineRequest { Quantity = 6 });

        Assert.IsType<OkObjectResult>(result);

        using var check = db.NewContext();
        Assert.Equal(6, check.PickListLines.Single().QuantityPicked);
        Assert.Equal(PickListStatus.Open, check.PickLists.Single().Status);
        Assert.Equal(14, check.InventoryBalances.Single().Quantity);
    }

    [Fact]
    public void Picking_more_than_the_line_still_owes_is_refused()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin(zone: "A");
        db.AddBalance(item, bin, 20);
        var controller = ControllerFor(db);

        controller.OpenPickList(new OpenPickListRequest
        {
            Lines = [new PickListLineRequest { ItemId = item.Id, WarehouseBinId = bin.Id, Quantity = 5 }]
        });
        var line = ListWithLines(db).Lines.Single();

        var result = controller.PickLine(line.PickListId, line.Id, new PickPickListLineRequest { Quantity = 6 });

        Assert.IsType<BadRequestObjectResult>(result);

        using var check = db.NewContext();
        Assert.Equal(0, check.PickListLines.Single().QuantityPicked);
        Assert.Equal(20, check.InventoryBalances.Single().Quantity);
    }

    [Fact]
    public void Picking_more_than_the_bin_actually_holds_is_refused_and_writes_nothing()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin(zone: "A");
        db.AddBalance(item, bin, 3);
        var controller = ControllerFor(db);

        controller.OpenPickList(new OpenPickListRequest
        {
            Lines = [new PickListLineRequest { ItemId = item.Id, WarehouseBinId = bin.Id, Quantity = 3 }]
        });
        var line = ListWithLines(db).Lines.Single();

        // The list was opened against a snapshot of what the line asked for,
        // not what is on the shelf right now - a pick against a bin that has
        // since come up short still has to fail like any other pick.
        db.Context.InventoryBalances.Single().Quantity = 1;
        db.Context.SaveChanges();

        var result = controller.PickLine(line.PickListId, line.Id, new PickPickListLineRequest());

        Assert.IsType<BadRequestObjectResult>(result);

        using var check = db.NewContext();
        Assert.Equal(0, check.PickListLines.Single().QuantityPicked);
        Assert.Empty(check.StockMovements);
    }

    [Fact]
    public void Picking_a_line_on_a_cancelled_list_is_refused()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin(zone: "A");
        db.AddBalance(item, bin, 10);
        var controller = ControllerFor(db);

        controller.OpenPickList(new OpenPickListRequest
        {
            Lines = [new PickListLineRequest { ItemId = item.Id, WarehouseBinId = bin.Id, Quantity = 5 }]
        });
        var list = ListWithLines(db);
        controller.CancelPickList(list.Id);

        var result = controller.PickLine(list.Id, list.Lines.Single().Id, new PickPickListLineRequest());

        Assert.IsType<ConflictObjectResult>(result);

        using var check = db.NewContext();
        Assert.Equal(10, check.InventoryBalances.Single().Quantity);
    }

    [Fact]
    public void Picking_a_lot_tracked_line_deducts_the_named_lot()
    {
        using var db = new TestDatabase();
        var item = db.AddItem(tracksLots: true);
        var bin = db.AddBin(zone: "A");
        var lot = db.AddLot(item, "LOT-1");
        db.AddLotBalance(item, bin, lot, 15);
        var controller = ControllerFor(db);

        controller.OpenPickList(new OpenPickListRequest
        {
            Lines = [new PickListLineRequest { ItemId = item.Id, WarehouseBinId = bin.Id, Quantity = 5, LotNumber = "LOT-1" }]
        });
        var line = ListWithLines(db).Lines.Single();

        var result = controller.PickLine(line.PickListId, line.Id, new PickPickListLineRequest());

        Assert.IsType<OkObjectResult>(result);

        using var check = db.NewContext();
        Assert.Equal(10, check.InventoryLotBalances.Single().Quantity);
        Assert.Equal(lot.Id, check.StockMovements.Single().LotId);
    }
}
