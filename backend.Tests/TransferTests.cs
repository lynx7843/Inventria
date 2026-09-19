using Inventria.Controllers;
using Inventria.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventria.Tests;

/// <summary>
/// Transfers: a relocate that spans warehouses, split into Ship and Receive
/// so there is an InTransit gap in between rather than an instant move. The
/// property that matters most here is the one a single-request relocate gets
/// for free and a transfer has to earn - the stock is never counted at both
/// ends, and never counted at neither end for longer than it takes to call
/// Receive.
/// </summary>
public class TransferTests
{
    private static TransfersController ControllerFor(TestDatabase db, string username = "alice") =>
        new(db.Context) { ControllerContext = ApiResult.SignedInAs(username) };

    private static InventoryController InventoryFor(TestDatabase db, string username = "alice") =>
        new(db.Context, new StockReceivingService(db.Context), new StockPickingService(db.Context))
        { ControllerContext = ApiResult.SignedInAs(username) };

    private static Transfer TransferWithLines(TestDatabase db) =>
        db.Context.Transfers.Include(t => t.Lines).Single();

    [Fact]
    public void Creating_a_transfer_for_the_same_warehouse_twice_is_refused()
    {
        using var db = new TestDatabase();
        var warehouse = db.AddWarehouse();
        var item = db.AddItem();
        var bin = db.AddBin(warehouseId: warehouse.Id);

        var result = ControllerFor(db).CreateTransfer(new CreateTransferRequest
        {
            SourceWarehouseId = warehouse.Id,
            DestinationWarehouseId = warehouse.Id,
            Lines = [new TransferLineRequest { ItemId = item.Id, SourceBinId = bin.Id, DestinationBinId = bin.Id, Quantity = 1 }]
        });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void A_line_whose_source_bin_is_in_the_wrong_warehouse_is_refused()
    {
        using var db = new TestDatabase();
        var sourceWarehouse = db.AddWarehouse("Warehouse A");
        var destinationWarehouse = db.AddWarehouse("Warehouse B");
        var item = db.AddItem();
        var wrongBin = db.AddBin(warehouseId: destinationWarehouse.Id);
        var destinationBin = db.AddBin(warehouseId: destinationWarehouse.Id, shelf: "S2");

        var result = ControllerFor(db).CreateTransfer(new CreateTransferRequest
        {
            SourceWarehouseId = sourceWarehouse.Id,
            DestinationWarehouseId = destinationWarehouse.Id,
            Lines = [new TransferLineRequest { ItemId = item.Id, SourceBinId = wrongBin.Id, DestinationBinId = destinationBin.Id, Quantity = 1 }]
        });

        Assert.IsType<BadRequestObjectResult>(result);

        using var check = db.NewContext();
        Assert.Empty(check.Transfers);
    }

    [Fact]
    public void Shipping_deducts_the_source_and_leaves_the_stock_in_neither_balance()
    {
        using var db = new TestDatabase();
        var sourceWarehouse = db.AddWarehouse("Warehouse A");
        var destinationWarehouse = db.AddWarehouse("Warehouse B");
        var item = db.AddItem();
        var sourceBin = db.AddBin(warehouseId: sourceWarehouse.Id);
        var destinationBin = db.AddBin(warehouseId: destinationWarehouse.Id);
        db.AddBalance(item, sourceBin, 20);
        var controller = ControllerFor(db);

        controller.CreateTransfer(new CreateTransferRequest
        {
            SourceWarehouseId = sourceWarehouse.Id,
            DestinationWarehouseId = destinationWarehouse.Id,
            Lines = [new TransferLineRequest { ItemId = item.Id, SourceBinId = sourceBin.Id, DestinationBinId = destinationBin.Id, Quantity = 8 }]
        });
        var transfer = TransferWithLines(db);

        var result = controller.ShipTransfer(transfer.Id);

        Assert.IsType<OkObjectResult>(result);

        using var check = db.NewContext();
        Assert.Equal(12, check.InventoryBalances.Single().Quantity);

        // Not yet arrived - no balance row exists at the destination at all,
        // which is exactly what "counted in neither place" looks like.
        Assert.Empty(check.InventoryBalances.Where(b => b.WarehouseBinId == destinationBin.Id));
        Assert.Equal(TransferStatus.InTransit, check.Transfers.Single().Status);

        var movement = check.StockMovements.Single();
        Assert.Equal("RELOCATE", movement.TransactionType);
        Assert.Equal(-8, movement.QuantityChanged);
    }

    [Fact]
    public void Receiving_adds_to_the_destination_and_completes_the_transfer()
    {
        using var db = new TestDatabase();
        var sourceWarehouse = db.AddWarehouse("Warehouse A");
        var destinationWarehouse = db.AddWarehouse("Warehouse B");
        var item = db.AddItem();
        var sourceBin = db.AddBin(warehouseId: sourceWarehouse.Id);
        var destinationBin = db.AddBin(warehouseId: destinationWarehouse.Id);
        db.AddBalance(item, sourceBin, 20);
        var controller = ControllerFor(db);

        controller.CreateTransfer(new CreateTransferRequest
        {
            SourceWarehouseId = sourceWarehouse.Id,
            DestinationWarehouseId = destinationWarehouse.Id,
            Lines = [new TransferLineRequest { ItemId = item.Id, SourceBinId = sourceBin.Id, DestinationBinId = destinationBin.Id, Quantity = 8 }]
        });
        var transfer = TransferWithLines(db);
        controller.ShipTransfer(transfer.Id);

        var result = controller.ReceiveTransfer(transfer.Id);

        Assert.IsType<OkObjectResult>(result);

        using var check = db.NewContext();
        Assert.Equal(12, check.InventoryBalances.Single(b => b.WarehouseBinId == sourceBin.Id).Quantity);
        Assert.Equal(8, check.InventoryBalances.Single(b => b.WarehouseBinId == destinationBin.Id).Quantity);
        Assert.Equal(TransferStatus.Received, check.Transfers.Single().Status);

        var movements = check.StockMovements.OrderBy(m => m.Id).ToList();
        Assert.Equal(2, movements.Count);
        Assert.Equal(-8, movements[0].QuantityChanged);
        Assert.Equal(8, movements[1].QuantityChanged);
        Assert.All(movements, m => Assert.Equal("RELOCATE", m.TransactionType));
    }

    [Fact]
    public void Shipping_more_than_the_source_holds_is_refused_and_writes_nothing()
    {
        using var db = new TestDatabase();
        var sourceWarehouse = db.AddWarehouse("Warehouse A");
        var destinationWarehouse = db.AddWarehouse("Warehouse B");
        var item = db.AddItem();
        var sourceBin = db.AddBin(warehouseId: sourceWarehouse.Id);
        var destinationBin = db.AddBin(warehouseId: destinationWarehouse.Id);
        db.AddBalance(item, sourceBin, 3);
        var controller = ControllerFor(db);

        controller.CreateTransfer(new CreateTransferRequest
        {
            SourceWarehouseId = sourceWarehouse.Id,
            DestinationWarehouseId = destinationWarehouse.Id,
            Lines = [new TransferLineRequest { ItemId = item.Id, SourceBinId = sourceBin.Id, DestinationBinId = destinationBin.Id, Quantity = 5 }]
        });
        var transfer = TransferWithLines(db);

        var result = controller.ShipTransfer(transfer.Id);

        Assert.IsType<BadRequestObjectResult>(result);

        using var check = db.NewContext();
        Assert.Equal(3, check.InventoryBalances.Single().Quantity);
        Assert.Empty(check.StockMovements);
        Assert.Equal(TransferStatus.Draft, check.Transfers.Single().Status);
    }

    [Fact]
    public void Receiving_before_shipping_is_refused()
    {
        using var db = new TestDatabase();
        var sourceWarehouse = db.AddWarehouse("Warehouse A");
        var destinationWarehouse = db.AddWarehouse("Warehouse B");
        var item = db.AddItem();
        var sourceBin = db.AddBin(warehouseId: sourceWarehouse.Id);
        var destinationBin = db.AddBin(warehouseId: destinationWarehouse.Id);
        db.AddBalance(item, sourceBin, 20);
        var controller = ControllerFor(db);

        controller.CreateTransfer(new CreateTransferRequest
        {
            SourceWarehouseId = sourceWarehouse.Id,
            DestinationWarehouseId = destinationWarehouse.Id,
            Lines = [new TransferLineRequest { ItemId = item.Id, SourceBinId = sourceBin.Id, DestinationBinId = destinationBin.Id, Quantity = 8 }]
        });
        var transfer = TransferWithLines(db);

        var result = controller.ReceiveTransfer(transfer.Id);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public void Cancelling_a_draft_transfer_leaves_stock_untouched()
    {
        using var db = new TestDatabase();
        var sourceWarehouse = db.AddWarehouse("Warehouse A");
        var destinationWarehouse = db.AddWarehouse("Warehouse B");
        var item = db.AddItem();
        var sourceBin = db.AddBin(warehouseId: sourceWarehouse.Id);
        var destinationBin = db.AddBin(warehouseId: destinationWarehouse.Id);
        db.AddBalance(item, sourceBin, 20);
        var controller = ControllerFor(db);

        controller.CreateTransfer(new CreateTransferRequest
        {
            SourceWarehouseId = sourceWarehouse.Id,
            DestinationWarehouseId = destinationWarehouse.Id,
            Lines = [new TransferLineRequest { ItemId = item.Id, SourceBinId = sourceBin.Id, DestinationBinId = destinationBin.Id, Quantity = 8 }]
        });
        var transfer = TransferWithLines(db);

        var result = controller.CancelTransfer(transfer.Id);

        Assert.IsType<OkObjectResult>(result);

        using var check = db.NewContext();
        Assert.Equal(TransferStatus.Cancelled, check.Transfers.Single().Status);
        Assert.Equal(20, check.InventoryBalances.Single().Quantity);
    }

    [Fact]
    public void An_in_transit_transfer_cannot_be_cancelled()
    {
        using var db = new TestDatabase();
        var sourceWarehouse = db.AddWarehouse("Warehouse A");
        var destinationWarehouse = db.AddWarehouse("Warehouse B");
        var item = db.AddItem();
        var sourceBin = db.AddBin(warehouseId: sourceWarehouse.Id);
        var destinationBin = db.AddBin(warehouseId: destinationWarehouse.Id);
        db.AddBalance(item, sourceBin, 20);
        var controller = ControllerFor(db);

        controller.CreateTransfer(new CreateTransferRequest
        {
            SourceWarehouseId = sourceWarehouse.Id,
            DestinationWarehouseId = destinationWarehouse.Id,
            Lines = [new TransferLineRequest { ItemId = item.Id, SourceBinId = sourceBin.Id, DestinationBinId = destinationBin.Id, Quantity = 8 }]
        });
        var transfer = TransferWithLines(db);
        controller.ShipTransfer(transfer.Id);

        var result = controller.CancelTransfer(transfer.Id);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public void Transferring_a_lot_tracked_item_carries_the_lot_to_the_destination()
    {
        using var db = new TestDatabase();
        var sourceWarehouse = db.AddWarehouse("Warehouse A");
        var destinationWarehouse = db.AddWarehouse("Warehouse B");
        var item = db.AddItem(tracksLots: true);
        var sourceBin = db.AddBin(warehouseId: sourceWarehouse.Id);
        var destinationBin = db.AddBin(warehouseId: destinationWarehouse.Id);
        var lot = db.AddLot(item, "LOT-1");
        db.AddLotBalance(item, sourceBin, lot, 15);
        var controller = ControllerFor(db);

        controller.CreateTransfer(new CreateTransferRequest
        {
            SourceWarehouseId = sourceWarehouse.Id,
            DestinationWarehouseId = destinationWarehouse.Id,
            Lines = [new TransferLineRequest { ItemId = item.Id, SourceBinId = sourceBin.Id, DestinationBinId = destinationBin.Id, Quantity = 5, LotNumber = "LOT-1" }]
        });
        var transfer = TransferWithLines(db);
        controller.ShipTransfer(transfer.Id);
        controller.ReceiveTransfer(transfer.Id);

        using var check = db.NewContext();
        Assert.Equal(10, check.InventoryLotBalances.Single(b => b.WarehouseBinId == sourceBin.Id).Quantity);
        Assert.Equal(5, check.InventoryLotBalances.Single(b => b.WarehouseBinId == destinationBin.Id).Quantity);
        Assert.All(check.StockMovements, m => Assert.Equal(lot.Id, m.LotId));
    }

    [Fact]
    public void Transferring_a_lot_tracked_item_without_a_lot_number_is_refused()
    {
        using var db = new TestDatabase();
        var sourceWarehouse = db.AddWarehouse("Warehouse A");
        var destinationWarehouse = db.AddWarehouse("Warehouse B");
        var item = db.AddItem(tracksLots: true);
        var sourceBin = db.AddBin(warehouseId: sourceWarehouse.Id);
        var destinationBin = db.AddBin(warehouseId: destinationWarehouse.Id);

        var result = ControllerFor(db).CreateTransfer(new CreateTransferRequest
        {
            SourceWarehouseId = sourceWarehouse.Id,
            DestinationWarehouseId = destinationWarehouse.Id,
            Lines = [new TransferLineRequest { ItemId = item.Id, SourceBinId = sourceBin.Id, DestinationBinId = destinationBin.Id, Quantity = 5 }]
        });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    // --- THE PLAIN RELOCATE ENDPOINT NOW REFUSES TO CROSS WAREHOUSES --------

    [Fact]
    public void Plain_relocate_across_warehouses_is_refused_in_favor_of_a_transfer()
    {
        using var db = new TestDatabase();
        var sourceWarehouse = db.AddWarehouse("Warehouse A");
        var destinationWarehouse = db.AddWarehouse("Warehouse B");
        var item = db.AddItem();
        var sourceBin = db.AddBin(warehouseId: sourceWarehouse.Id);
        var destinationBin = db.AddBin(warehouseId: destinationWarehouse.Id);
        db.AddBalance(item, sourceBin, 10);

        var result = InventoryFor(db).RelocateStock(new RelocateStockRequest
        {
            ItemId = item.Id,
            SourceBinId = sourceBin.Id,
            DestinationBinId = destinationBin.Id,
            Quantity = 5
        });

        Assert.IsType<BadRequestObjectResult>(result);

        using var check = db.NewContext();
        Assert.Equal(10, check.InventoryBalances.Single().Quantity);
        Assert.Empty(check.StockMovements);
    }
}
