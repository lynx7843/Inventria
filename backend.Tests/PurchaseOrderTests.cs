using Inventria.Controllers;
using Inventria.Models;
using Microsoft.AspNetCore.Mvc;

namespace Inventria.Tests;

/// <summary>
/// Purchase orders: the answer to "what is on order" that receiving used to
/// have no memory of. Receiving against a line has to go through the exact
/// path StockMovementTests already covers for POST /receive - these tests
/// check the order bookkeeping on top of that, not the stock-move mechanics
/// underneath it again.
/// </summary>
public class PurchaseOrderTests
{
    private static PurchaseOrdersController ControllerFor(TestDatabase db, string username = "alice") =>
        new(db.Context, new StockReceivingService(db.Context)) { ControllerContext = ApiResult.SignedInAs(username) };

    private static PurchaseOrderRequest OneLineRequest(int supplierId, int itemId, int quantity = 10) => new()
    {
        SupplierId = supplierId,
        Lines = [new PurchaseOrderLineRequest { ItemId = itemId, QuantityOrdered = quantity }]
    };

    private static int IdOf(IActionResult createResult) =>
        ApiResult.Property(ApiResult.Property(ApiResult.Body(createResult), "PurchaseOrder"), "Id").GetInt32();

    // --- CREATE ------------------------------------------------------------

    [Fact]
    public void Creating_an_order_starts_it_as_a_draft()
    {
        using var db = new TestDatabase();
        var supplier = db.AddSupplier();
        var item = db.AddItem();

        var result = ControllerFor(db).CreateOrder(OneLineRequest(supplier.Id, item.Id, 20));

        Assert.IsType<OkObjectResult>(result);
        using var check = db.NewContext();
        var order = check.PurchaseOrders.Single();
        Assert.Equal(PurchaseOrderStatus.Draft, order.Status);
        Assert.Null(order.OrderedAt);

        var line = check.PurchaseOrderLines.Single();
        Assert.Equal(20, line.QuantityOrdered);
        Assert.Equal(0, line.QuantityReceived);
    }

    [Fact]
    public void Creating_an_order_for_a_supplier_that_does_not_exist_is_refused()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();

        var result = ControllerFor(db).CreateOrder(OneLineRequest(999, item.Id));

        Assert.IsType<NotFoundObjectResult>(result);
        using var check = db.NewContext();
        Assert.Empty(check.PurchaseOrders);
    }

    [Fact]
    public void Creating_an_order_for_an_item_that_does_not_exist_is_refused()
    {
        using var db = new TestDatabase();
        var supplier = db.AddSupplier();

        var result = ControllerFor(db).CreateOrder(OneLineRequest(supplier.Id, 999));

        Assert.IsType<NotFoundObjectResult>(result);
        using var check = db.NewContext();
        Assert.Empty(check.PurchaseOrders);
    }

    // --- PLACE ---------------------------------------------------------------

    [Fact]
    public void Placing_a_draft_moves_it_to_ordered()
    {
        using var db = new TestDatabase();
        var supplier = db.AddSupplier();
        var item = db.AddItem();
        var controller = ControllerFor(db);

        var created = controller.CreateOrder(OneLineRequest(supplier.Id, item.Id));
        var id = IdOf(created);

        var result = controller.PlaceOrder(id);

        Assert.IsType<OkObjectResult>(result);
        using var check = db.NewContext();
        var order = check.PurchaseOrders.Single();
        Assert.Equal(PurchaseOrderStatus.Ordered, order.Status);
        Assert.NotNull(order.OrderedAt);
    }

    [Fact]
    public void Placing_an_order_that_is_already_placed_is_refused()
    {
        using var db = new TestDatabase();
        var supplier = db.AddSupplier();
        var item = db.AddItem();
        var controller = ControllerFor(db);

        var created = controller.CreateOrder(OneLineRequest(supplier.Id, item.Id));
        var id = IdOf(created);
        controller.PlaceOrder(id);

        var result = controller.PlaceOrder(id);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public void Placing_an_order_with_no_lines_is_refused()
    {
        using var db = new TestDatabase();
        var supplier = db.AddSupplier();
        using var context = db.Context;
        var order = new PurchaseOrder { SupplierId = supplier.Id, CreatedBy = "alice" };
        context.PurchaseOrders.Add(order);
        context.SaveChanges();

        var result = ControllerFor(db).PlaceOrder(order.Id);

        Assert.IsType<BadRequestObjectResult>(result);
        using var check = db.NewContext();
        Assert.Equal(PurchaseOrderStatus.Draft, check.PurchaseOrders.Single().Status);
    }

    [Fact]
    public void Placing_an_order_that_does_not_exist_is_a_not_found()
    {
        using var db = new TestDatabase();

        Assert.IsType<NotFoundObjectResult>(ControllerFor(db).PlaceOrder(999));
    }

    // --- RECEIVE -------------------------------------------------------------

    private static int CreateAndPlace(PurchaseOrdersController controller, int supplierId, int itemId, int quantity)
    {
        var created = controller.CreateOrder(OneLineRequest(supplierId, itemId, quantity));
        var id = IdOf(created);
        controller.PlaceOrder(id);
        return id;
    }

    private static int FirstLineIdOf(TestDatabase db, int orderId)
    {
        using var context = db.NewContext();
        return context.PurchaseOrderLines.Single(l => l.PurchaseOrderId == orderId).Id;
    }

    [Fact]
    public void Receiving_the_full_ordered_quantity_marks_the_order_received_and_writes_real_stock()
    {
        using var db = new TestDatabase();
        var supplier = db.AddSupplier();
        var item = db.AddItem();
        var bin = db.AddBin();
        var controller = ControllerFor(db);

        var orderId = CreateAndPlace(controller, supplier.Id, item.Id, 20);
        var lineId = FirstLineIdOf(db, orderId);

        var result = controller.ReceiveLine(orderId, lineId, new ReceivePurchaseOrderLineRequest
        {
            WarehouseBinId = bin.Id,
            Quantity = 20
        });

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(20, ApiResult.Number(result, "NewTotalBalance"));

        using var check = db.NewContext();

        // The same tables StockMovementTests checks after a plain /receive -
        // this is what "the same code path" actually means, not just that
        // both endpoints return 200.
        Assert.Equal(20, check.InventoryBalances.Single().Quantity);
        var movement = check.StockMovements.Single();
        Assert.Equal("RECEIVE", movement.TransactionType);
        Assert.Equal(20, movement.QuantityChanged);

        var order = check.PurchaseOrders.Single();
        Assert.Equal(PurchaseOrderStatus.Received, order.Status);
        Assert.NotNull(order.ReceivedAt);
        Assert.Equal(20, check.PurchaseOrderLines.Single().QuantityReceived);
    }

    [Fact]
    public void Receiving_less_than_ordered_leaves_the_order_partially_received()
    {
        using var db = new TestDatabase();
        var supplier = db.AddSupplier();
        var item = db.AddItem();
        var bin = db.AddBin();
        var controller = ControllerFor(db);

        var orderId = CreateAndPlace(controller, supplier.Id, item.Id, 20);
        var lineId = FirstLineIdOf(db, orderId);

        var result = controller.ReceiveLine(orderId, lineId, new ReceivePurchaseOrderLineRequest
        {
            WarehouseBinId = bin.Id,
            Quantity = 12
        });

        Assert.IsType<OkObjectResult>(result);

        using var check = db.NewContext();
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, check.PurchaseOrders.Single().Status);
        Assert.Equal(12, check.PurchaseOrderLines.Single().QuantityReceived);
        Assert.Null(check.PurchaseOrders.Single().ReceivedAt);
    }

    [Fact]
    public void A_second_delivery_tops_the_line_up_to_fully_received()
    {
        using var db = new TestDatabase();
        var supplier = db.AddSupplier();
        var item = db.AddItem();
        var bin = db.AddBin();
        var controller = ControllerFor(db);

        var orderId = CreateAndPlace(controller, supplier.Id, item.Id, 20);
        var lineId = FirstLineIdOf(db, orderId);

        controller.ReceiveLine(orderId, lineId, new ReceivePurchaseOrderLineRequest { WarehouseBinId = bin.Id, Quantity = 12 });
        var result = controller.ReceiveLine(orderId, lineId, new ReceivePurchaseOrderLineRequest { WarehouseBinId = bin.Id, Quantity = 8 });

        Assert.IsType<OkObjectResult>(result);

        using var check = db.NewContext();
        Assert.Equal(PurchaseOrderStatus.Received, check.PurchaseOrders.Single().Status);
        Assert.Equal(20, check.PurchaseOrderLines.Single().QuantityReceived);

        // Two deliveries, one line, one balance - the ledger still reconciles.
        Assert.Equal(20, check.InventoryBalances.Single().Quantity);
        Assert.Equal(2, check.StockMovements.Count());
    }

    [Fact]
    public void Receiving_more_than_the_line_still_owes_is_refused()
    {
        using var db = new TestDatabase();
        var supplier = db.AddSupplier();
        var item = db.AddItem();
        var bin = db.AddBin();
        var controller = ControllerFor(db);

        var orderId = CreateAndPlace(controller, supplier.Id, item.Id, 10);
        var lineId = FirstLineIdOf(db, orderId);

        var result = controller.ReceiveLine(orderId, lineId, new ReceivePurchaseOrderLineRequest
        {
            WarehouseBinId = bin.Id,
            Quantity = 15
        });

        Assert.IsType<BadRequestObjectResult>(result);

        using var check = db.NewContext();
        Assert.Empty(check.InventoryBalances);
        Assert.Equal(0, check.PurchaseOrderLines.Single().QuantityReceived);
    }

    [Fact]
    public void Receiving_against_a_draft_order_is_refused()
    {
        using var db = new TestDatabase();
        var supplier = db.AddSupplier();
        var item = db.AddItem();
        var bin = db.AddBin();
        var controller = ControllerFor(db);

        var created = controller.CreateOrder(OneLineRequest(supplier.Id, item.Id));
        var orderId = IdOf(created);
        var lineId = FirstLineIdOf(db, orderId);

        var result = controller.ReceiveLine(orderId, lineId, new ReceivePurchaseOrderLineRequest
        {
            WarehouseBinId = bin.Id,
            Quantity = 5
        });

        Assert.IsType<ConflictObjectResult>(result);
        using var check = db.NewContext();
        Assert.Empty(check.InventoryBalances);
    }

    [Fact]
    public void Receiving_against_a_cancelled_order_is_refused()
    {
        using var db = new TestDatabase();
        var supplier = db.AddSupplier();
        var item = db.AddItem();
        var bin = db.AddBin();
        var controller = ControllerFor(db);

        var orderId = CreateAndPlace(controller, supplier.Id, item.Id, 10);
        var lineId = FirstLineIdOf(db, orderId);
        controller.CancelOrder(orderId);

        var result = controller.ReceiveLine(orderId, lineId, new ReceivePurchaseOrderLineRequest
        {
            WarehouseBinId = bin.Id,
            Quantity = 5
        });

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public void Receiving_into_a_bin_that_does_not_exist_is_refused_and_touches_nothing()
    {
        using var db = new TestDatabase();
        var supplier = db.AddSupplier();
        var item = db.AddItem();
        var controller = ControllerFor(db);

        var orderId = CreateAndPlace(controller, supplier.Id, item.Id, 10);
        var lineId = FirstLineIdOf(db, orderId);

        var result = controller.ReceiveLine(orderId, lineId, new ReceivePurchaseOrderLineRequest
        {
            WarehouseBinId = 999,
            Quantity = 5
        });

        Assert.IsType<NotFoundObjectResult>(result);
        using var check = db.NewContext();
        Assert.Equal(0, check.PurchaseOrderLines.Single().QuantityReceived);
    }

    // --- CANCEL ----------------------------------------------------------------

    [Fact]
    public void Cancelling_a_draft_is_allowed()
    {
        using var db = new TestDatabase();
        var supplier = db.AddSupplier();
        var item = db.AddItem();
        var controller = ControllerFor(db);

        var created = controller.CreateOrder(OneLineRequest(supplier.Id, item.Id));
        var id = IdOf(created);

        var result = controller.CancelOrder(id);

        Assert.IsType<OkObjectResult>(result);
        using var check = db.NewContext();
        Assert.Equal(PurchaseOrderStatus.Cancelled, check.PurchaseOrders.Single().Status);
    }

    [Fact]
    public void Cancelling_an_order_that_is_already_fully_received_is_refused()
    {
        using var db = new TestDatabase();
        var supplier = db.AddSupplier();
        var item = db.AddItem();
        var bin = db.AddBin();
        var controller = ControllerFor(db);

        var orderId = CreateAndPlace(controller, supplier.Id, item.Id, 5);
        var lineId = FirstLineIdOf(db, orderId);
        controller.ReceiveLine(orderId, lineId, new ReceivePurchaseOrderLineRequest { WarehouseBinId = bin.Id, Quantity = 5 });

        var result = controller.CancelOrder(orderId);

        Assert.IsType<ConflictObjectResult>(result);
    }

    // --- EDIT (DRAFT ONLY) -------------------------------------------------

    [Fact]
    public void Editing_a_draft_replaces_its_lines()
    {
        using var db = new TestDatabase();
        var supplier = db.AddSupplier();
        var itemA = db.AddItem(sku: "SKU-A");
        var itemB = db.AddItem(sku: "SKU-B");
        var controller = ControllerFor(db);

        var created = controller.CreateOrder(OneLineRequest(supplier.Id, itemA.Id, 10));
        var id = IdOf(created);

        var result = controller.UpdateOrder(id, new PurchaseOrderRequest
        {
            SupplierId = supplier.Id,
            Lines = [new PurchaseOrderLineRequest { ItemId = itemB.Id, QuantityOrdered = 7 }]
        });

        Assert.IsType<OkObjectResult>(result);
        using var check = db.NewContext();
        var line = check.PurchaseOrderLines.Single();
        Assert.Equal(itemB.Id, line.ItemId);
        Assert.Equal(7, line.QuantityOrdered);
    }

    [Fact]
    public void Editing_an_order_that_has_already_been_placed_is_refused()
    {
        using var db = new TestDatabase();
        var supplier = db.AddSupplier();
        var item = db.AddItem();
        var controller = ControllerFor(db);

        var orderId = CreateAndPlace(controller, supplier.Id, item.Id, 10);

        var result = controller.UpdateOrder(orderId, OneLineRequest(supplier.Id, item.Id, 999));

        Assert.IsType<ConflictObjectResult>(result);
        using var check = db.NewContext();
        Assert.Equal(10, check.PurchaseOrderLines.Single().QuantityOrdered);
    }

    // --- THE LEDGER --------------------------------------------------------

    [Fact]
    public void Timestamps_come_back_as_UTC_so_they_serialize_with_a_Z()
    {
        using var db = new TestDatabase();
        var supplier = db.AddSupplier();
        var item = db.AddItem();
        var controller = ControllerFor(db);

        controller.CreateOrder(OneLineRequest(supplier.Id, item.Id));

        // Same bug StockMovementTests guards against for the audit log -
        // datetime2 forgets Kind on the way back from SQL Server, and without
        // InventriaDbContext's conversion a CreatedAt logged at 09:42 UTC reads
        // as 09:42 wherever the viewer's browser happens to be.
        using var check = db.NewContext();
        var createdAt = check.PurchaseOrders.Single().CreatedAt;

        Assert.Equal(DateTimeKind.Utc, createdAt.Kind);
        Assert.EndsWith("Z", System.Text.Json.JsonSerializer.Serialize(createdAt).Trim('"'));
    }

    // --- LISTING -------------------------------------------------------------

    [Fact]
    public void Listing_orders_can_be_filtered_by_status()
    {
        using var db = new TestDatabase();
        var supplier = db.AddSupplier();
        var item = db.AddItem();
        var controller = ControllerFor(db);

        controller.CreateOrder(OneLineRequest(supplier.Id, item.Id));
        CreateAndPlace(controller, supplier.Id, item.Id, 5);

        var result = controller.GetOrders(status: PurchaseOrderStatus.Ordered, supplierId: null, page: 1, pageSize: 25);

        var orders = ApiResult.Property(ApiResult.Body(result), "Orders");
        Assert.Equal(1, orders.GetArrayLength());
    }
}
