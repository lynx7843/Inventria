using Inventria.Controllers;
using Inventria.Models;
using Microsoft.AspNetCore.Mvc;

namespace Inventria.Tests;

/// <summary>
/// Assembling components into a finished product - one hammer from one handle
/// and one head. The property that matters most is atomicity: a build that
/// cannot be fully supplied has to consume nothing, not half-consume parts for
/// a finished unit that never gets made.
/// </summary>
public class AssemblyTests
{
    private static AssemblyController ControllerFor(TestDatabase db, string username = "alice") =>
        new(db.Context) { ControllerContext = ApiResult.SignedInAs(username) };

    [Fact]
    public void Defining_a_bill_of_materials_for_an_item_that_does_not_exist_is_refused()
    {
        using var db = new TestDatabase();
        var handle = db.AddItem("HANDLE", "Handle");

        var result = ControllerFor(db).SetBom(999, new SetBillOfMaterialsRequest
        {
            Components = [new BillOfMaterialComponentRequest { ComponentItemId = handle.Id, QuantityRequired = 1 }]
        });

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void An_item_cannot_be_a_component_of_itself()
    {
        using var db = new TestDatabase();
        var hammer = db.AddItem("HAMMER", "Hammer");

        var result = ControllerFor(db).SetBom(hammer.Id, new SetBillOfMaterialsRequest
        {
            Components = [new BillOfMaterialComponentRequest { ComponentItemId = hammer.Id, QuantityRequired = 1 }]
        });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void A_lot_tracked_finished_item_cannot_have_a_bill_of_materials_yet()
    {
        using var db = new TestDatabase();
        var hammer = db.AddItem("HAMMER", "Hammer", tracksLots: true);
        var handle = db.AddItem("HANDLE", "Handle");

        var result = ControllerFor(db).SetBom(hammer.Id, new SetBillOfMaterialsRequest
        {
            Components = [new BillOfMaterialComponentRequest { ComponentItemId = handle.Id, QuantityRequired = 1 }]
        });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void A_lot_tracked_component_cannot_be_used_yet()
    {
        using var db = new TestDatabase();
        var hammer = db.AddItem("HAMMER", "Hammer");
        var handle = db.AddItem("HANDLE", "Handle", tracksLots: true);

        var result = ControllerFor(db).SetBom(hammer.Id, new SetBillOfMaterialsRequest
        {
            Components = [new BillOfMaterialComponentRequest { ComponentItemId = handle.Id, QuantityRequired = 1 }]
        });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void Setting_a_bom_replaces_whatever_was_there_before()
    {
        using var db = new TestDatabase();
        var hammer = db.AddItem("HAMMER", "Hammer");
        var handle = db.AddItem("HANDLE", "Handle");
        var head = db.AddItem("HEAD", "Head");
        var controller = ControllerFor(db);

        controller.SetBom(hammer.Id, new SetBillOfMaterialsRequest
        {
            Components = [new BillOfMaterialComponentRequest { ComponentItemId = handle.Id, QuantityRequired = 1 }]
        });

        controller.SetBom(hammer.Id, new SetBillOfMaterialsRequest
        {
            Components =
            [
                new BillOfMaterialComponentRequest { ComponentItemId = handle.Id, QuantityRequired = 1 },
                new BillOfMaterialComponentRequest { ComponentItemId = head.Id, QuantityRequired = 1 }
            ]
        });

        using var check = db.NewContext();
        var bom = check.BillsOfMaterials.Single();
        Assert.Equal(2, check.BillOfMaterialLines.Count(l => l.BillOfMaterialsId == bom.Id));
    }

    [Fact]
    public void Building_consumes_every_component_and_produces_the_finished_item()
    {
        using var db = new TestDatabase();
        var hammer = db.AddItem("HAMMER", "Hammer");
        var handle = db.AddItem("HANDLE", "Handle");
        var head = db.AddItem("HEAD", "Head");
        var bin = db.AddBin();
        db.AddBalance(handle, bin, 10);
        db.AddBalance(head, bin, 10);
        var controller = ControllerFor(db);

        controller.SetBom(hammer.Id, new SetBillOfMaterialsRequest
        {
            Components =
            [
                new BillOfMaterialComponentRequest { ComponentItemId = handle.Id, QuantityRequired = 1 },
                new BillOfMaterialComponentRequest { ComponentItemId = head.Id, QuantityRequired = 1 }
            ]
        });

        var result = controller.BuildAssembly(new BuildAssemblyRequest { ItemId = hammer.Id, WarehouseBinId = bin.Id, Quantity = 3 });

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(3, ApiResult.Number(result, "NewFinishedBalance"));

        using var check = db.NewContext();
        Assert.Equal(7, check.InventoryBalances.Single(b => b.ItemId == handle.Id).Quantity);
        Assert.Equal(7, check.InventoryBalances.Single(b => b.ItemId == head.Id).Quantity);
        Assert.Equal(3, check.InventoryBalances.Single(b => b.ItemId == hammer.Id).Quantity);

        var movements = check.StockMovements.ToList();
        Assert.Equal(3, movements.Count);
        Assert.All(movements, m => Assert.Equal("ASSEMBLE", m.TransactionType));
        Assert.Equal(-3, movements.Single(m => m.ItemId == handle.Id).QuantityChanged);
        Assert.Equal(-3, movements.Single(m => m.ItemId == head.Id).QuantityChanged);
        Assert.Equal(3, movements.Single(m => m.ItemId == hammer.Id).QuantityChanged);
    }

    [Fact]
    public void Building_respects_quantities_greater_than_one_per_unit()
    {
        using var db = new TestDatabase();
        var trike = db.AddItem("TRIKE", "Tricycle");
        var wheel = db.AddItem("WHEEL", "Wheel");
        var bin = db.AddBin();
        db.AddBalance(wheel, bin, 10);
        var controller = ControllerFor(db);

        controller.SetBom(trike.Id, new SetBillOfMaterialsRequest
        {
            Components = [new BillOfMaterialComponentRequest { ComponentItemId = wheel.Id, QuantityRequired = 3 }]
        });

        var result = controller.BuildAssembly(new BuildAssemblyRequest { ItemId = trike.Id, WarehouseBinId = bin.Id, Quantity = 2 });

        Assert.IsType<OkObjectResult>(result);

        using var check = db.NewContext();
        Assert.Equal(4, check.InventoryBalances.Single(b => b.ItemId == wheel.Id).Quantity);
        Assert.Equal(-6, check.StockMovements.Single(m => m.ItemId == wheel.Id).QuantityChanged);
    }

    [Fact]
    public void Building_without_enough_of_one_component_consumes_nothing()
    {
        using var db = new TestDatabase();
        var hammer = db.AddItem("HAMMER", "Hammer");
        var handle = db.AddItem("HANDLE", "Handle");
        var head = db.AddItem("HEAD", "Head");
        var bin = db.AddBin();
        db.AddBalance(handle, bin, 10);
        db.AddBalance(head, bin, 1); // not enough for 3 hammers
        var controller = ControllerFor(db);

        controller.SetBom(hammer.Id, new SetBillOfMaterialsRequest
        {
            Components =
            [
                new BillOfMaterialComponentRequest { ComponentItemId = handle.Id, QuantityRequired = 1 },
                new BillOfMaterialComponentRequest { ComponentItemId = head.Id, QuantityRequired = 1 }
            ]
        });

        var result = controller.BuildAssembly(new BuildAssemblyRequest { ItemId = hammer.Id, WarehouseBinId = bin.Id, Quantity = 3 });

        Assert.IsType<BadRequestObjectResult>(result);

        using var check = db.NewContext();
        Assert.Equal(10, check.InventoryBalances.Single(b => b.ItemId == handle.Id).Quantity);
        Assert.Equal(1, check.InventoryBalances.Single(b => b.ItemId == head.Id).Quantity);
        Assert.Empty(check.InventoryBalances.Where(b => b.ItemId == hammer.Id));
        Assert.Empty(check.StockMovements);
    }

    [Fact]
    public void Building_an_item_with_no_bill_of_materials_is_refused()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin();

        var result = ControllerFor(db).BuildAssembly(new BuildAssemblyRequest { ItemId = item.Id, WarehouseBinId = bin.Id, Quantity = 1 });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void Building_an_archived_finished_item_is_refused()
    {
        using var db = new TestDatabase();
        var hammer = db.AddItem("HAMMER", "Hammer", isArchived: true);
        var handle = db.AddItem("HANDLE", "Handle");
        var bin = db.AddBin();
        db.AddBalance(handle, bin, 10);
        var controller = ControllerFor(db);

        controller.SetBom(hammer.Id, new SetBillOfMaterialsRequest
        {
            Components = [new BillOfMaterialComponentRequest { ComponentItemId = handle.Id, QuantityRequired = 1 }]
        });

        var result = controller.BuildAssembly(new BuildAssemblyRequest { ItemId = hammer.Id, WarehouseBinId = bin.Id, Quantity = 1 });

        Assert.IsType<ConflictObjectResult>(result);
    }
}
