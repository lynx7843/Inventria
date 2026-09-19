using Inventria.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Inventria.Tests;

/// <summary>
/// Bins are the locations everything else refers to. Without one, nothing can be
/// received - which is why they need somewhere to be created from - and deleting
/// one out from under stock would strand it.
/// </summary>
public class WarehouseBinTests
{
    private static WarehouseBinsController ControllerFor(TestDatabase db) => new(db.Context);

    private static InventoryController InventoryFor(TestDatabase db) =>
        new(db.Context, new StockReceivingService(db.Context), new StockPickingService(db.Context)) { ControllerContext = ApiResult.SignedInAs("alice") };

    [Fact]
    public void Creating_a_bin_trims_its_address()
    {
        using var db = new TestDatabase();
        db.AddWarehouse();

        var result = ControllerFor(db).CreateBin(new WarehouseBinRequest
        {
            Zone = " Electronics ",
            Aisle = " A1 ",
            Shelf = " S3 "
        });

        Assert.IsType<OkObjectResult>(result);

        using var check = db.NewContext();
        var bin = check.WarehouseBins.Single();

        Assert.Equal("Electronics", bin.Zone);
        Assert.Equal("A1", bin.Aisle);
        Assert.Equal("S3", bin.Shelf);
    }

    [Fact]
    public void Bins_are_listed_in_the_order_someone_would_walk_them()
    {
        using var db = new TestDatabase();
        db.AddBin("Tools", "B2", "S1");
        db.AddBin("Cold Storage", "A1", "S2");
        db.AddBin("Cold Storage", "A1", "S1");

        var result = ControllerFor(db).GetBins();
        var addresses = ApiResult.Body(result)
            .EnumerateArray()
            .Select(bin =>
                $"{ApiResult.Property(bin, "Zone").GetString()}-" +
                $"{ApiResult.Property(bin, "Aisle").GetString()}-" +
                $"{ApiResult.Property(bin, "Shelf").GetString()}")
            .ToList();

        Assert.Equal(["Cold Storage-A1-S1", "Cold Storage-A1-S2", "Tools-B2-S1"], addresses);
    }

    [Fact]
    public void Updating_a_bin_that_does_not_exist_is_a_not_found()
    {
        using var db = new TestDatabase();

        var result = ControllerFor(db).UpdateBin(999, new WarehouseBinRequest
        {
            Zone = "Electronics",
            Aisle = "A1",
            Shelf = "S1"
        });

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void A_bin_holding_stock_cannot_be_deleted()
    {
        using var db = new TestDatabase();
        var bin = db.AddBin();
        db.AddBalance(db.AddItem(), bin, 7);

        var result = ControllerFor(db).DeleteBin(bin.Id);

        Assert.IsType<ConflictObjectResult>(result);
        Assert.Contains("7 units", ApiResult.Message(result));

        // Retiring a location is a decision about the warehouse map, not
        // permission to write off the goods standing on that shelf.
        using var check = db.NewContext();
        Assert.Single(check.WarehouseBins);
        Assert.Equal(7, check.InventoryBalances.Single().Quantity);
    }

    [Fact]
    public void A_bin_with_movement_history_cannot_be_deleted_even_when_empty()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin();
        var inventory = InventoryFor(db);

        inventory.ReceiveStock(new ReceiveStockRequest { ItemId = item.Id, WarehouseBinId = bin.Id, Quantity = 3 });
        inventory.PickStock(new PickStockRequest { ItemId = item.Id, WarehouseBinId = bin.Id, Quantity = 3 });

        var result = ControllerFor(db).DeleteBin(bin.Id);

        Assert.IsType<ConflictObjectResult>(result);
        Assert.Contains("movement", ApiResult.Message(result));
    }

    [Fact]
    public void An_unused_bin_can_be_deleted()
    {
        using var db = new TestDatabase();
        var bin = db.AddBin();

        var result = ControllerFor(db).DeleteBin(bin.Id);

        Assert.IsType<OkObjectResult>(result);

        using var check = db.NewContext();
        Assert.Empty(check.WarehouseBins);
    }

    [Fact]
    public void Deleting_a_bin_that_does_not_exist_is_a_not_found()
    {
        using var db = new TestDatabase();

        var result = ControllerFor(db).DeleteBin(999);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // --- WAREHOUSES --------------------------------------------------------

    [Fact]
    public void Creating_a_bin_with_no_warehouse_named_lands_in_the_default_one()
    {
        using var db = new TestDatabase();
        var warehouse = db.AddWarehouse();

        var result = ControllerFor(db).CreateBin(new WarehouseBinRequest
        {
            Zone = "Electronics",
            Aisle = "A1",
            Shelf = "S1"
        });

        Assert.IsType<OkObjectResult>(result);

        using var check = db.NewContext();
        Assert.Equal(warehouse.Id, check.WarehouseBins.Single().WarehouseId);
    }

    [Fact]
    public void Creating_a_bin_in_a_warehouse_that_does_not_exist_is_a_not_found()
    {
        using var db = new TestDatabase();

        var result = ControllerFor(db).CreateBin(new WarehouseBinRequest
        {
            WarehouseId = 999,
            Zone = "Electronics",
            Aisle = "A1",
            Shelf = "S1"
        });

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void The_same_address_is_allowed_in_two_different_warehouses()
    {
        using var db = new TestDatabase();
        var warehouseA = db.AddWarehouse("Warehouse A");
        var warehouseB = db.AddWarehouse("Warehouse B");
        var controller = ControllerFor(db);

        controller.CreateBin(new WarehouseBinRequest { WarehouseId = warehouseA.Id, Zone = "Electronics", Aisle = "A1", Shelf = "S1" });
        var result = controller.CreateBin(new WarehouseBinRequest { WarehouseId = warehouseB.Id, Zone = "Electronics", Aisle = "A1", Shelf = "S1" });

        Assert.IsType<OkObjectResult>(result);

        using var check = db.NewContext();
        Assert.Equal(2, check.WarehouseBins.Count());
    }

    // A controller-level duplicate-address test belongs at the SQL Server test
    // pass, not here - see TestDatabase's note on why SQLite's unique-violation
    // exception is not one UniqueConstraint.WasViolated recognises.
    // SchemaConstraintTests covers the same-warehouse collision at the schema
    // level instead.

    [Fact]
    public void Moving_a_bin_to_a_warehouse_that_does_not_exist_is_a_not_found()
    {
        using var db = new TestDatabase();
        var bin = db.AddBin();

        var result = ControllerFor(db).UpdateBin(bin.Id, new WarehouseBinRequest
        {
            WarehouseId = 999,
            Zone = bin.Zone,
            Aisle = bin.Aisle,
            Shelf = bin.Shelf
        });

        Assert.IsType<NotFoundObjectResult>(result);
    }
}
