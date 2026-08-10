using Inventria.Controllers;
using Inventria.Models;
using Microsoft.AspNetCore.Mvc;

namespace Inventria.Tests;

/// <summary>
/// Receiving, picking and relocating: the three ways stock moves, and the ledger
/// they leave behind. This is where the money is - a balance that drifts from
/// its movements is a warehouse that cannot be counted.
/// </summary>
public class StockMovementTests
{
    private static InventoryController ControllerFor(TestDatabase db, string username = "alice") =>
        new(db.Context) { ControllerContext = ApiResult.SignedInAs(username) };

    // --- RECEIVE ---------------------------------------------------------

    [Fact]
    public void Receiving_stock_creates_the_balance_and_logs_the_movement()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin();

        var result = ControllerFor(db).ReceiveStock(new ReceiveStockRequest
        {
            ItemId = item.Id,
            WarehouseBinId = bin.Id,
            Quantity = 25
        });

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(25, ApiResult.Number(result, "NewTotalBalance"));

        using var check = db.NewContext();
        Assert.Equal(25, check.InventoryBalances.Single().Quantity);

        var movement = check.StockMovements.Single();
        Assert.Equal("RECEIVE", movement.TransactionType);
        Assert.Equal(25, movement.QuantityChanged);
        Assert.Equal(bin.Id, movement.WarehouseBinId);
    }

    [Fact]
    public void Receiving_into_a_bin_that_already_holds_the_item_adds_to_it()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin();
        db.AddBalance(item, bin, 10);

        var result = ControllerFor(db).ReceiveStock(new ReceiveStockRequest
        {
            ItemId = item.Id,
            WarehouseBinId = bin.Id,
            Quantity = 5
        });

        Assert.Equal(15, ApiResult.Number(result, "NewTotalBalance"));

        using var check = db.NewContext();
        Assert.Equal(15, check.InventoryBalances.Single().Quantity);
    }

    [Fact]
    public void A_movement_is_stamped_with_the_signed_in_user_not_the_request()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin();

        ControllerFor(db, username: "bob").ReceiveStock(new ReceiveStockRequest
        {
            ItemId = item.Id,
            WarehouseBinId = bin.Id,
            Quantity = 1
        });

        using var check = db.NewContext();
        Assert.Equal("bob", check.StockMovements.Single().PerformedBy);
    }

    [Fact]
    public void Receiving_into_a_bin_that_does_not_exist_is_refused()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();

        var result = ControllerFor(db).ReceiveStock(new ReceiveStockRequest
        {
            ItemId = item.Id,
            WarehouseBinId = 999,
            Quantity = 5
        });

        Assert.IsType<NotFoundObjectResult>(result);
        Assert.Contains("999", ApiResult.Message(result));

        using var check = db.NewContext();
        Assert.Empty(check.StockMovements);
    }

    [Fact]
    public void Receiving_an_item_that_does_not_exist_is_refused()
    {
        using var db = new TestDatabase();
        var bin = db.AddBin();

        var result = ControllerFor(db).ReceiveStock(new ReceiveStockRequest
        {
            ItemId = 999,
            WarehouseBinId = bin.Id,
            Quantity = 5
        });

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Receiving_a_quantity_that_is_not_positive_is_refused(int quantity)
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin();

        var result = ControllerFor(db).ReceiveStock(new ReceiveStockRequest
        {
            ItemId = item.Id,
            WarehouseBinId = bin.Id,
            Quantity = quantity
        });

        Assert.IsType<BadRequestObjectResult>(result);

        using var check = db.NewContext();
        Assert.Empty(check.InventoryBalances);
    }

    // --- PICK ------------------------------------------------------------

    [Fact]
    public void Picking_deducts_the_stock_and_logs_a_negative_movement()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin();
        db.AddBalance(item, bin, 20);

        var result = ControllerFor(db).PickStock(new PickStockRequest
        {
            ItemId = item.Id,
            WarehouseBinId = bin.Id,
            Quantity = 8
        });

        Assert.Equal(12, ApiResult.Number(result, "RemainingBalance"));

        using var check = db.NewContext();
        Assert.Equal(12, check.InventoryBalances.Single().Quantity);

        // Negative, because that is the direction the stock moved. Anything that
        // sums a bin's movements depends on the sign being the truth.
        Assert.Equal(-8, check.StockMovements.Single().QuantityChanged);
    }

    [Fact]
    public void Picking_more_than_the_bin_holds_is_refused_and_writes_nothing()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin();
        db.AddBalance(item, bin, 3);

        var result = ControllerFor(db).PickStock(new PickStockRequest
        {
            ItemId = item.Id,
            WarehouseBinId = bin.Id,
            Quantity = 4
        });

        Assert.IsType<BadRequestObjectResult>(result);

        using var check = db.NewContext();
        Assert.Equal(3, check.InventoryBalances.Single().Quantity);
        Assert.Empty(check.StockMovements);
    }

    [Fact]
    public void Picking_from_a_bin_holding_none_of_the_item_is_refused()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin();

        var result = ControllerFor(db).PickStock(new PickStockRequest
        {
            ItemId = item.Id,
            WarehouseBinId = bin.Id,
            Quantity = 1
        });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    // --- RELOCATE --------------------------------------------------------

    [Fact]
    public void Relocating_moves_the_stock_between_bins()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var source = db.AddBin(shelf: "S1");
        var destination = db.AddBin(shelf: "S2");
        db.AddBalance(item, source, 30);

        var result = ControllerFor(db).RelocateStock(new RelocateStockRequest
        {
            ItemId = item.Id,
            SourceBinId = source.Id,
            DestinationBinId = destination.Id,
            Quantity = 12
        });

        Assert.IsType<OkObjectResult>(result);

        using var check = db.NewContext();
        Assert.Equal(18, check.InventoryBalances.Single(b => b.WarehouseBinId == source.Id).Quantity);
        Assert.Equal(12, check.InventoryBalances.Single(b => b.WarehouseBinId == destination.Id).Quantity);
    }

    [Fact]
    public void Relocating_writes_both_legs_so_movements_still_add_up_to_the_balances()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var source = db.AddBin(shelf: "S1");
        var destination = db.AddBin(shelf: "S2");
        var controller = ControllerFor(db);

        // Received rather than seeded straight into the balance: this test is
        // about movements adding up to balances, so the opening stock has to
        // arrive through the ledger like everything else.
        controller.ReceiveStock(new ReceiveStockRequest
        {
            ItemId = item.Id,
            WarehouseBinId = source.Id,
            Quantity = 30
        });

        controller.RelocateStock(new RelocateStockRequest
        {
            ItemId = item.Id,
            SourceBinId = source.Id,
            DestinationBinId = destination.Id,
            Quantity = 12
        });

        using var check = db.NewContext();
        var movements = check.StockMovements.Where(m => m.TransactionType == "RELOCATE").ToList();

        Assert.Equal(2, movements.Count);
        Assert.Equal(-12, movements.Single(m => m.WarehouseBinId == source.Id).QuantityChanged);
        Assert.Equal(12, movements.Single(m => m.WarehouseBinId == destination.Id).QuantityChanged);

        // The two halves of one move share a timestamp - that is what marks them
        // as a pair to anything reading the log later.
        Assert.Equal(movements[0].Timestamp, movements[1].Timestamp);

        // And the ledger reconciles: for every bin, the movements recorded
        // against it sum to the quantity sitting in it. This is the property the
        // single-row relocation used to break.
        foreach (var balance in check.InventoryBalances)
        {
            var fromMovements = check.StockMovements
                .Where(m => m.ItemId == balance.ItemId && m.WarehouseBinId == balance.WarehouseBinId)
                .Sum(m => m.QuantityChanged);

            Assert.Equal(balance.Quantity, fromMovements);
        }
    }

    [Fact]
    public void Relocating_into_the_same_bin_is_refused()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin();
        db.AddBalance(item, bin, 10);

        var result = ControllerFor(db).RelocateStock(new RelocateStockRequest
        {
            ItemId = item.Id,
            SourceBinId = bin.Id,
            DestinationBinId = bin.Id,
            Quantity = 5
        });

        Assert.IsType<BadRequestObjectResult>(result);

        using var check = db.NewContext();
        Assert.Empty(check.StockMovements);
    }

    [Fact]
    public void Relocating_to_a_bin_that_does_not_exist_is_refused_and_leaves_the_source_alone()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var source = db.AddBin();
        db.AddBalance(item, source, 10);

        var result = ControllerFor(db).RelocateStock(new RelocateStockRequest
        {
            ItemId = item.Id,
            SourceBinId = source.Id,
            DestinationBinId = 999,
            Quantity = 5
        });

        Assert.IsType<NotFoundObjectResult>(result);

        using var check = db.NewContext();
        Assert.Equal(10, check.InventoryBalances.Single().Quantity);
        Assert.Empty(check.StockMovements);
    }

    [Fact]
    public void Relocating_more_than_the_source_holds_is_refused()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var source = db.AddBin(shelf: "S1");
        var destination = db.AddBin(shelf: "S2");
        db.AddBalance(item, source, 2);

        var result = ControllerFor(db).RelocateStock(new RelocateStockRequest
        {
            ItemId = item.Id,
            SourceBinId = source.Id,
            DestinationBinId = destination.Id,
            Quantity = 3
        });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    // --- THE LEDGER ------------------------------------------------------

    [Fact]
    public void Timestamps_come_back_as_UTC_so_they_serialize_with_a_Z()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin();

        ControllerFor(db).ReceiveStock(new ReceiveStockRequest
        {
            ItemId = item.Id,
            WarehouseBinId = bin.Id,
            Quantity = 1
        });

        // Read through a fresh context, so this is what the column gives back
        // rather than the value still sitting in the change tracker. Unspecified
        // here is what made the dashboard read every movement in local time.
        using var check = db.NewContext();
        var timestamp = check.StockMovements.Single().Timestamp;

        Assert.Equal(DateTimeKind.Utc, timestamp.Kind);
        Assert.EndsWith("Z", System.Text.Json.JsonSerializer.Serialize(timestamp).Trim('"'));
    }

    [Fact]
    public void A_sequence_of_moves_leaves_every_bin_reconciling_with_its_movements()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var a = db.AddBin(shelf: "S1");
        var b = db.AddBin(shelf: "S2");
        var controller = ControllerFor(db);

        controller.ReceiveStock(new ReceiveStockRequest { ItemId = item.Id, WarehouseBinId = a.Id, Quantity = 100 });
        controller.PickStock(new PickStockRequest { ItemId = item.Id, WarehouseBinId = a.Id, Quantity = 30 });
        controller.RelocateStock(new RelocateStockRequest { ItemId = item.Id, SourceBinId = a.Id, DestinationBinId = b.Id, Quantity = 40 });
        controller.PickStock(new PickStockRequest { ItemId = item.Id, WarehouseBinId = b.Id, Quantity = 15 });

        using var check = db.NewContext();

        Assert.Equal(30, check.InventoryBalances.Single(x => x.WarehouseBinId == a.Id).Quantity);
        Assert.Equal(25, check.InventoryBalances.Single(x => x.WarehouseBinId == b.Id).Quantity);

        // Nothing was created or destroyed on the way: what is on the shelves is
        // what the ledger says arrived, minus what it says left.
        Assert.Equal(
            check.InventoryBalances.Sum(x => x.Quantity),
            check.StockMovements.Sum(m => m.QuantityChanged));
    }
}
