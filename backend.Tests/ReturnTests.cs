using Inventria.Controllers;
using Inventria.Models;
using Microsoft.AspNetCore.Mvc;

namespace Inventria.Tests;

/// <summary>
/// Processing a return. The property that matters most is the one a plain
/// RECEIVE never had to worry about: a damaged unit marked Quarantine or
/// Scrap must never rejoin a sellable balance just because it was logged as
/// having come back.
/// </summary>
public class ReturnTests
{
    private static ReturnsController ControllerFor(TestDatabase db, string username = "alice") =>
        new(db.Context) { ControllerContext = ApiResult.SignedInAs(username) };

    private static ProcessReturnRequest Request(int itemId, int binId, int quantity, string disposition, string condition = ReturnConditionGrade.Good) => new()
    {
        ItemId = itemId,
        WarehouseBinId = binId,
        Quantity = quantity,
        Reason = "Customer changed their mind",
        ConditionGrade = condition,
        Disposition = disposition
    };

    [Fact]
    public void Restocking_a_return_adds_it_to_the_sellable_balance()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin();

        var result = ControllerFor(db).ProcessReturn(Request(item.Id, bin.Id, 4, ReturnDisposition.Restock, ReturnConditionGrade.New));

        Assert.IsType<OkObjectResult>(result);
        Assert.True(ApiResult.Property(ApiResult.Body(result), "Restocked").GetBoolean());
        Assert.Equal(4, ApiResult.Number(result, "NewBalance"));

        using var check = db.NewContext();
        Assert.Equal(4, check.InventoryBalances.Single().Quantity);

        var movement = check.StockMovements.Single();
        Assert.Equal("RETURN", movement.TransactionType);
        Assert.Equal(4, movement.QuantityChanged);
        Assert.Equal(ReturnDisposition.Restock, movement.Disposition);
        Assert.Equal(ReturnConditionGrade.New, movement.ConditionGrade);
        Assert.Equal("Customer changed their mind", movement.ReasonCode);
    }

    [Fact]
    public void Quarantining_a_return_logs_it_without_touching_any_balance()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin();

        var result = ControllerFor(db).ProcessReturn(Request(item.Id, bin.Id, 4, ReturnDisposition.Quarantine, ReturnConditionGrade.Damaged));

        Assert.IsType<OkObjectResult>(result);
        Assert.False(ApiResult.Property(ApiResult.Body(result), "Restocked").GetBoolean());

        using var check = db.NewContext();

        // No balance row at all - not zero, not negative, simply nothing to
        // pick from. This is the whole point: damaged stock a human has not
        // cleared cannot silently become sellable inventory.
        Assert.Empty(check.InventoryBalances);

        var movement = check.StockMovements.Single();
        Assert.Equal("RETURN", movement.TransactionType);
        Assert.Equal(0, movement.QuantityChanged);
        Assert.Equal(ReturnDisposition.Quarantine, movement.Disposition);
        Assert.Equal(ReturnConditionGrade.Damaged, movement.ConditionGrade);
    }

    [Fact]
    public void Scrapping_a_return_logs_it_without_touching_any_balance()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin();

        var result = ControllerFor(db).ProcessReturn(Request(item.Id, bin.Id, 2, ReturnDisposition.Scrap, ReturnConditionGrade.Unsellable));

        Assert.IsType<OkObjectResult>(result);

        using var check = db.NewContext();
        Assert.Empty(check.InventoryBalances);
        Assert.Equal(0, check.StockMovements.Single().QuantityChanged);
        Assert.Equal(ReturnDisposition.Scrap, check.StockMovements.Single().Disposition);
    }

    [Fact]
    public void A_return_without_a_reason_is_refused()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin();

        var request = Request(item.Id, bin.Id, 1, ReturnDisposition.Restock);
        request.Reason = "   ";

        var result = ControllerFor(db).ProcessReturn(request);

        Assert.IsType<BadRequestObjectResult>(result);

        using var check = db.NewContext();
        Assert.Empty(check.StockMovements);
    }

    [Fact]
    public void An_unrecognized_disposition_is_refused()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin();

        var result = ControllerFor(db).ProcessReturn(Request(item.Id, bin.Id, 1, "Discard"));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void An_unrecognized_condition_grade_is_refused()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin();

        var result = ControllerFor(db).ProcessReturn(Request(item.Id, bin.Id, 1, ReturnDisposition.Restock, "Pristine"));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void Restocking_a_lot_tracked_return_without_a_lot_number_is_refused()
    {
        using var db = new TestDatabase();
        var item = db.AddItem(tracksLots: true);
        var bin = db.AddBin();

        var result = ControllerFor(db).ProcessReturn(Request(item.Id, bin.Id, 1, ReturnDisposition.Restock));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void Restocking_a_lot_tracked_return_opens_a_new_lot_if_needed()
    {
        using var db = new TestDatabase();
        var item = db.AddItem(tracksLots: true);
        var bin = db.AddBin();
        var request = Request(item.Id, bin.Id, 6, ReturnDisposition.Restock, ReturnConditionGrade.Good);
        request.LotNumber = "LOT-RETURN-1";

        var result = ControllerFor(db).ProcessReturn(request);

        Assert.IsType<OkObjectResult>(result);

        using var check = db.NewContext();
        var lot = check.Lots.Single();
        Assert.Equal("LOT-RETURN-1", lot.LotNumber);
        Assert.Equal(6, check.InventoryLotBalances.Single().Quantity);
        Assert.Equal(lot.Id, check.StockMovements.Single().LotId);
    }

    [Fact]
    public void Quarantining_a_lot_tracked_return_still_names_the_lot_on_the_movement()
    {
        using var db = new TestDatabase();
        var item = db.AddItem(tracksLots: true);
        var bin = db.AddBin();
        var lot = db.AddLot(item, "LOT-A");
        db.AddLotBalance(item, bin, lot, 5);

        var request = Request(item.Id, bin.Id, 2, ReturnDisposition.Quarantine, ReturnConditionGrade.Damaged);
        request.LotNumber = "LOT-A";

        var result = ControllerFor(db).ProcessReturn(request);

        Assert.IsType<OkObjectResult>(result);

        using var check = db.NewContext();

        // The lot's existing sellable balance is untouched - the returned
        // units are a separate, unresolved fact, not stock deducted from it.
        Assert.Equal(5, check.InventoryLotBalances.Single().Quantity);
        Assert.Equal(lot.Id, check.StockMovements.Single().LotId);
    }

    [Fact]
    public void Returning_against_an_archived_item_is_refused()
    {
        using var db = new TestDatabase();
        var item = db.AddItem(isArchived: true);
        var bin = db.AddBin();

        var result = ControllerFor(db).ProcessReturn(Request(item.Id, bin.Id, 1, ReturnDisposition.Restock, ReturnConditionGrade.New));

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public void Listing_returns_can_filter_by_disposition()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin();
        var controller = ControllerFor(db);

        controller.ProcessReturn(Request(item.Id, bin.Id, 1, ReturnDisposition.Restock, ReturnConditionGrade.New));
        controller.ProcessReturn(Request(item.Id, bin.Id, 1, ReturnDisposition.Scrap, ReturnConditionGrade.Unsellable));

        var result = controller.GetReturns(disposition: ReturnDisposition.Scrap);

        var rows = ApiResult.Property(ApiResult.Body(result), "Returns").EnumerateArray().ToList();
        Assert.Single(rows);
        Assert.Equal(ReturnDisposition.Scrap, ApiResult.Property(rows[0], "Disposition").GetString());
    }
}
