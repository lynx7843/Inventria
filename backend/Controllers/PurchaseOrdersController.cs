using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Inventria.Models;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace Inventria.Controllers;

/// <summary>
/// What is on order, and what has arrived against it. Before this, receiving
/// stock had no memory of what was expected - see StockReceivingService for
/// the write path this shares with InventoryController's plain POST /receive,
/// which stays exactly as it worked before this controller existed.
/// </summary>
[Authorize]
[Route("api/purchase-orders")]
[ApiController]
public class PurchaseOrdersController : ControllerBase
{
    private readonly InventriaDbContext _context;
    private readonly StockReceivingService _receiving;

    public PurchaseOrdersController(InventriaDbContext context, StockReceivingService receiving)
    {
        _context = context;
        _receiving = receiving;
    }

    private string CurrentUsername => User.FindFirstValue(ClaimTypes.Name)!;

    private static string? NormalizeLotNumber(string? lotNumber) =>
        string.IsNullOrWhiteSpace(lotNumber) ? null : lotNumber.Trim();

    // A line is short of what it ordered until QuantityReceived catches up -
    // the same fact GetOrder reports per line and RecomputeStatus reports for
    // the order as a whole.
    private static bool IsFullyReceived(PurchaseOrderLine line) => line.QuantityReceived >= line.QuantityOrdered;

    // Called after any receive against a line. An order becomes Received only
    // once every line is - one short line keeps the whole order
    // PartiallyReceived, because "is this delivery short" has to stay
    // answerable from the order's own status, not just from reading every
    // line under it.
    private static void RecomputeStatus(PurchaseOrder order)
    {
        if (order.Lines.All(IsFullyReceived))
        {
            order.Status = PurchaseOrderStatus.Received;
            order.ReceivedAt ??= DateTime.UtcNow;
        }
        else
        {
            order.Status = PurchaseOrderStatus.PartiallyReceived;
        }
    }

    private static object LineSummary(PurchaseOrderLine line) => new
    {
        line.Id,
        line.ItemId,
        ItemName = line.Item?.Name,
        ItemSku = line.Item?.Sku,
        line.QuantityOrdered,
        line.QuantityReceived,
        QuantityRemaining = Math.Max(0, line.QuantityOrdered - line.QuantityReceived),
        line.UnitCost
    };

    private static object OrderDetail(PurchaseOrder order) => new
    {
        order.Id,
        order.SupplierId,
        SupplierName = order.Supplier?.Name,
        order.Status,
        order.CreatedAt,
        order.CreatedBy,
        order.OrderedAt,
        order.ReceivedAt,
        order.ExpectedDate,
        order.Notes,
        Lines = order.Lines.Select(LineSummary)
    };

    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 200;

    // What is on order - every PO that isn't a Draft nobody has sent yet or
    // Cancelled, unless the caller specifically asks to see those too.
    [HttpGet]
    public IActionResult GetOrders(
        [FromQuery] string? status,
        [FromQuery] int? supplierId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = _context.PurchaseOrders
            .Include(o => o.Supplier)
            .Include(o => o.Lines)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(o => o.Status == status);
        if (supplierId.HasValue) query = query.Where(o => o.SupplierId == supplierId.Value);

        query = query.OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Id);

        var totalCount = query.Count();

        var orders = query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList()
            .Select(o => new
            {
                o.Id,
                o.SupplierId,
                SupplierName = o.Supplier?.Name,
                o.Status,
                o.CreatedAt,
                o.OrderedAt,
                o.ReceivedAt,
                o.ExpectedDate,
                LineCount = o.Lines.Count,
                TotalOrdered = o.Lines.Sum(l => l.QuantityOrdered),
                TotalReceived = o.Lines.Sum(l => l.QuantityReceived)
            });

        return Ok(new
        {
            Orders = orders,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    [HttpGet("{id}")]
    public IActionResult GetOrder(int id)
    {
        var order = _context.PurchaseOrders
            .Include(o => o.Supplier)
            .Include(o => o.Lines).ThenInclude(l => l.Item)
            .FirstOrDefault(o => o.Id == id);

        if (order == null) return NotFound(new { Message = "Purchase order not found." });

        return Ok(OrderDetail(order));
    }

    [HttpPost]
    public IActionResult CreateOrder([FromBody] PurchaseOrderRequest request)
    {
        if (!_context.Suppliers.Any(s => s.Id == request.SupplierId))
        {
            return NotFound(new { Message = $"Supplier with ID {request.SupplierId} not found." });
        }

        var itemIds = request.Lines.Select(l => l.ItemId).ToList();
        var missingItemId = itemIds.FirstOrDefault(itemId => !_context.Items.Any(i => i.Id == itemId));
        if (missingItemId != 0)
        {
            return NotFound(new { Message = $"Item with ID {missingItemId} not found." });
        }

        var order = new PurchaseOrder
        {
            SupplierId = request.SupplierId,
            ExpectedDate = request.ExpectedDate,
            Notes = request.Notes?.Trim(),
            CreatedBy = CurrentUsername,
            Lines = request.Lines.Select(l => new PurchaseOrderLine
            {
                ItemId = l.ItemId,
                QuantityOrdered = l.QuantityOrdered,
                UnitCost = l.UnitCost
            }).ToList()
        };

        _context.PurchaseOrders.Add(order);
        _context.SaveChanges();

        return Ok(new { Message = "Purchase order created.", PurchaseOrder = OrderDetail(order) });
    }

    // Full replacement of a Draft's supplier, dates, notes and lines - the
    // same shape CreateOrder takes, because editing a Draft is indistinguishable
    // from not having placed it yet. Once an order is Ordered, its lines are
    // what the supplier is fulfilling against and stop being editable; Cancel
    // and re-create is the path for "actually, not those".
    [HttpPut("{id}")]
    public IActionResult UpdateOrder(int id, [FromBody] PurchaseOrderRequest request)
    {
        var order = _context.PurchaseOrders
            .Include(o => o.Lines)
            .FirstOrDefault(o => o.Id == id);

        if (order == null) return NotFound(new { Message = "Purchase order not found." });

        if (order.Status != PurchaseOrderStatus.Draft)
        {
            return Conflict(new { Message = $"This purchase order is {order.Status} and can no longer be edited." });
        }

        if (!_context.Suppliers.Any(s => s.Id == request.SupplierId))
        {
            return NotFound(new { Message = $"Supplier with ID {request.SupplierId} not found." });
        }

        var itemIds = request.Lines.Select(l => l.ItemId).ToList();
        var missingItemId = itemIds.FirstOrDefault(itemId => !_context.Items.Any(i => i.Id == itemId));
        if (missingItemId != 0)
        {
            return NotFound(new { Message = $"Item with ID {missingItemId} not found." });
        }

        order.SupplierId = request.SupplierId;
        order.ExpectedDate = request.ExpectedDate;
        order.Notes = request.Notes?.Trim();

        _context.PurchaseOrderLines.RemoveRange(order.Lines);
        order.Lines = request.Lines.Select(l => new PurchaseOrderLine
        {
            ItemId = l.ItemId,
            QuantityOrdered = l.QuantityOrdered,
            UnitCost = l.UnitCost
        }).ToList();

        _context.SaveChanges();

        return Ok(new { Message = "Purchase order updated.", PurchaseOrder = OrderDetail(order) });
    }

    [HttpPost("{id}/place")]
    public IActionResult PlaceOrder(int id)
    {
        var order = _context.PurchaseOrders
            .Include(o => o.Lines)
            .FirstOrDefault(o => o.Id == id);

        if (order == null) return NotFound(new { Message = "Purchase order not found." });

        if (order.Status != PurchaseOrderStatus.Draft)
        {
            return Conflict(new { Message = $"This purchase order is already {order.Status}." });
        }

        if (order.Lines.Count == 0)
        {
            return BadRequest(new { Message = "Add at least one line before placing this order." });
        }

        order.Status = PurchaseOrderStatus.Ordered;
        order.OrderedAt = DateTime.UtcNow;
        _context.SaveChanges();

        return Ok(new { Message = "Purchase order placed.", PurchaseOrder = OrderDetail(order) });
    }

    [HttpPost("{id}/cancel")]
    public IActionResult CancelOrder(int id)
    {
        var order = _context.PurchaseOrders.Find(id);
        if (order == null) return NotFound(new { Message = "Purchase order not found." });

        if (order.Status is PurchaseOrderStatus.Received or PurchaseOrderStatus.Cancelled)
        {
            return Conflict(new { Message = $"This purchase order is already {order.Status} and cannot be cancelled." });
        }

        order.Status = PurchaseOrderStatus.Cancelled;
        _context.SaveChanges();

        return Ok(new { Message = "Purchase order cancelled." });
    }

    // Receives against one line of a placed order, through the exact code
    // path POST /api/inventory/receive uses - see StockReceivingService. The
    // line's own bookkeeping (QuantityReceived, and the order's Status once
    // every line agrees) is a second save after the stock write succeeds:
    // the balance and movement rows are the ones a warehouse audit actually
    // depends on, so those are what the retry in StockReceivingService
    // protects, and they exist whether or not this second save does.
    [HttpPost("{id}/lines/{lineId}/receive")]
    public IActionResult ReceiveLine(int id, int lineId, [FromBody] ReceivePurchaseOrderLineRequest request)
    {
        var order = _context.PurchaseOrders
            .Include(o => o.Lines)
            .FirstOrDefault(o => o.Id == id);

        if (order == null) return NotFound(new { Message = "Purchase order not found." });

        var line = order.Lines.FirstOrDefault(l => l.Id == lineId);
        if (line == null) return NotFound(new { Message = "Purchase order line not found." });

        if (order.Status is not (PurchaseOrderStatus.Ordered or PurchaseOrderStatus.PartiallyReceived))
        {
            return Conflict(new { Message = $"This purchase order is {order.Status} and cannot receive stock." });
        }

        var remaining = line.QuantityOrdered - line.QuantityReceived;
        if (request.Quantity > remaining)
        {
            return BadRequest(new { Message = $"Receiving {request.Quantity} would exceed the {line.QuantityOrdered} ordered ({line.QuantityReceived} already received, {remaining} remaining). Use the plain receive screen to record an overage." });
        }

        var item = _context.Items.Find(line.ItemId);
        if (item == null) return NotFound(new { Message = $"Item with ID {line.ItemId} not found." });

        if (item.IsArchived)
        {
            return Conflict(new { Message = $"'{item.Name}' is archived and cannot receive new stock. Unarchive it first." });
        }

        var bin = _context.WarehouseBins.Find(request.WarehouseBinId);
        if (bin == null) return NotFound(new { Message = $"Warehouse Bin with ID {request.WarehouseBinId} not found." });

        var lotNumber = NormalizeLotNumber(request.LotNumber);
        if (item.TracksLots && lotNumber == null)
        {
            return BadRequest(new { Message = $"'{item.Name}' tracks lots. Give the lot/batch number this stock belongs to." });
        }

        var receipt = _receiving.Receive(item, request.WarehouseBinId, request.Quantity, CurrentUsername, lotNumber, request.ExpirationDate);
        if (receipt == null)
        {
            return Conflict(new { Message = "This stock is being updated by another request. Please try again." });
        }

        line.QuantityReceived += request.Quantity;
        RecomputeStatus(order);
        _context.SaveChanges();

        return Ok(new
        {
            Message = $"Successfully received {request.Quantity} units of {item.Name} into {bin.Zone}-{bin.Aisle}-{bin.Shelf}.",
            NewTotalBalance = receipt.NewTotalBalance,
            PurchaseOrder = OrderDetail(order)
        });
    }
}

public class PurchaseOrderLineRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Choose an item for each line.")]
    public int ItemId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Quantity ordered must be a whole number greater than zero.")]
    public int QuantityOrdered { get; set; }

    [Range(typeof(decimal), "0", "79228162514264337593543950335", ErrorMessage = "Unit cost cannot be negative.")]
    public decimal? UnitCost { get; set; }
}

public class PurchaseOrderRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Choose a supplier.")]
    public int SupplierId { get; set; }

    public DateTime? ExpectedDate { get; set; }

    [StringLength(1000, ErrorMessage = "Notes cannot be longer than 1000 characters.")]
    public string? Notes { get; set; }

    [MinLength(1, ErrorMessage = "A purchase order needs at least one line.")]
    public List<PurchaseOrderLineRequest> Lines { get; set; } = [];
}

public class ReceivePurchaseOrderLineRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Choose the bin the stock is going into.")]
    public int WarehouseBinId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Quantity must be a whole number greater than zero.")]
    public int Quantity { get; set; }

    // Required only when the item tracks lots - same as ReceiveStockRequest.
    [StringLength(64, ErrorMessage = "Lot/batch number cannot be longer than 64 characters.")]
    public string? LotNumber { get; set; }

    public DateTime? ExpirationDate { get; set; }
}
