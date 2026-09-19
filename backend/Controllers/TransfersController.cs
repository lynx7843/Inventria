using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Inventria.Models;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace Inventria.Controllers;

/// <summary>
/// A relocate that spans warehouses. InventoryController's plain
/// POST /inventory/relocate stays instant and single-warehouse - see its own
/// comment - because within one building both legs can be written in the same
/// request. Crossing warehouses can't assume that: the stock is physically on
/// a truck for a while, so this splits the move into Ship (deduct the source)
/// and Receive (add to the destination) as two separate events, with an
/// InTransit state in between where the stock is in neither balance table at
/// once - see Transfer.
/// </summary>
[Authorize]
[Route("api/transfers")]
[ApiController]
public class TransfersController : ControllerBase
{
    private readonly InventriaDbContext _context;

    public TransfersController(InventriaDbContext context)
    {
        _context = context;
    }

    private string CurrentUsername => User.FindFirstValue(ClaimTypes.Name)!;

    private static string? NormalizeLotNumber(string? lotNumber) =>
        string.IsNullOrWhiteSpace(lotNumber) ? null : lotNumber.Trim();

    private static object LineSummary(TransferLine line) => new
    {
        line.Id,
        line.ItemId,
        ItemName = line.Item?.Name,
        ItemSku = line.Item?.Sku,
        line.SourceBinId,
        line.DestinationBinId,
        line.LotNumber,
        line.Quantity,
        line.Shipped,
        line.Received
    };

    private static object TransferDetail(Transfer transfer) => new
    {
        transfer.Id,
        transfer.SourceWarehouseId,
        SourceWarehouseName = transfer.SourceWarehouse?.Name,
        transfer.DestinationWarehouseId,
        DestinationWarehouseName = transfer.DestinationWarehouse?.Name,
        transfer.Status,
        transfer.CreatedAt,
        transfer.CreatedBy,
        transfer.ShippedAt,
        transfer.ShippedBy,
        transfer.ReceivedAt,
        transfer.ReceivedBy,
        Lines = transfer.Lines.Select(LineSummary)
    };

    private IQueryable<Transfer> DetailQuery() => _context.Transfers
        .Include(t => t.SourceWarehouse)
        .Include(t => t.DestinationWarehouse)
        .Include(t => t.Lines).ThenInclude(l => l.Item);

    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 200;

    [HttpGet]
    public IActionResult GetTransfers(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = _context.Transfers
            .Include(t => t.SourceWarehouse)
            .Include(t => t.DestinationWarehouse)
            .Include(t => t.Lines)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(t => t.Status == status);

        query = query.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id);

        var totalCount = query.Count();

        var transfers = query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList()
            .Select(t => new
            {
                t.Id,
                t.SourceWarehouseId,
                SourceWarehouseName = t.SourceWarehouse?.Name,
                t.DestinationWarehouseId,
                DestinationWarehouseName = t.DestinationWarehouse?.Name,
                t.Status,
                t.CreatedAt,
                t.ShippedAt,
                t.ReceivedAt,
                LineCount = t.Lines.Count
            });

        return Ok(new
        {
            Transfers = transfers,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    [HttpGet("{id}")]
    public IActionResult GetTransfer(int id)
    {
        var transfer = DetailQuery().FirstOrDefault(t => t.Id == id);
        if (transfer == null) return NotFound(new { Message = "Transfer not found." });

        return Ok(TransferDetail(transfer));
    }

    [HttpPost]
    public IActionResult CreateTransfer([FromBody] CreateTransferRequest request)
    {
        if (request.SourceWarehouseId == request.DestinationWarehouseId)
        {
            return BadRequest(new { Message = "Source and destination warehouses cannot be the same - use a plain relocate within one warehouse instead." });
        }

        var sourceWarehouse = _context.Warehouses.Find(request.SourceWarehouseId);
        if (sourceWarehouse == null) return NotFound(new { Message = $"Warehouse with ID {request.SourceWarehouseId} not found." });

        var destinationWarehouse = _context.Warehouses.Find(request.DestinationWarehouseId);
        if (destinationWarehouse == null) return NotFound(new { Message = $"Warehouse with ID {request.DestinationWarehouseId} not found." });

        var itemIds = request.Lines.Select(l => l.ItemId).Distinct().ToList();
        var items = _context.Items.Where(i => itemIds.Contains(i.Id)).ToDictionary(i => i.Id);
        var missingItemId = itemIds.FirstOrDefault(id => !items.ContainsKey(id));
        if (missingItemId != 0)
        {
            return NotFound(new { Message = $"Item with ID {missingItemId} not found." });
        }

        var lines = new List<TransferLine>();
        foreach (var requestLine in request.Lines)
        {
            var item = items[requestLine.ItemId];

            var sourceBin = _context.WarehouseBins.Find(requestLine.SourceBinId);
            if (sourceBin == null) return NotFound(new { Message = $"Warehouse Bin with ID {requestLine.SourceBinId} not found." });
            if (sourceBin.WarehouseId != request.SourceWarehouseId)
            {
                return BadRequest(new { Message = $"Bin {requestLine.SourceBinId} is not in '{sourceWarehouse.Name}'." });
            }

            var destinationBin = _context.WarehouseBins.Find(requestLine.DestinationBinId);
            if (destinationBin == null) return NotFound(new { Message = $"Warehouse Bin with ID {requestLine.DestinationBinId} not found." });
            if (destinationBin.WarehouseId != request.DestinationWarehouseId)
            {
                return BadRequest(new { Message = $"Bin {requestLine.DestinationBinId} is not in '{destinationWarehouse.Name}'." });
            }

            var lotNumber = NormalizeLotNumber(requestLine.LotNumber);
            if (item.TracksLots && lotNumber == null)
            {
                return BadRequest(new { Message = $"'{item.Name}' tracks lots. Choose the lot/batch number to transfer." });
            }

            lines.Add(new TransferLine
            {
                ItemId = requestLine.ItemId,
                SourceBinId = requestLine.SourceBinId,
                DestinationBinId = requestLine.DestinationBinId,
                Quantity = requestLine.Quantity,
                LotNumber = lotNumber
            });
        }

        var transfer = new Transfer
        {
            SourceWarehouseId = request.SourceWarehouseId,
            DestinationWarehouseId = request.DestinationWarehouseId,
            CreatedBy = CurrentUsername,
            Lines = lines
        };

        _context.Transfers.Add(transfer);
        _context.SaveChanges();

        return Ok(new { Message = $"Transfer created with {lines.Count} line(s).", Transfer = TransferDetail(transfer) });
    }

    [HttpPost("{id}/cancel")]
    public IActionResult CancelTransfer(int id)
    {
        var transfer = _context.Transfers.Find(id);
        if (transfer == null) return NotFound(new { Message = "Transfer not found." });

        if (transfer.Status != TransferStatus.Draft)
        {
            return Conflict(new { Message = $"This transfer is {transfer.Status} - stock has already left the source warehouse, so it can no longer be cancelled outright." });
        }

        transfer.Status = TransferStatus.Cancelled;
        _context.SaveChanges();

        return Ok(new { Message = "Transfer cancelled." });
    }

    private const int MaxConcurrencyAttempts = 8;

    private static void PauseBeforeRetrying(int attempt) =>
        Thread.Sleep(Random.Shared.Next(4, 16) * attempt);

    private enum ShipOutcome { Success, InsufficientStock, ConcurrencyExhausted }

    // Deducts one line from its source bin and marks it Shipped, so a retried
    // ShipTransfer (the caller's answer to ConcurrencyExhausted) skips a line
    // already shipped instead of deducting it twice.
    private ShipOutcome Ship(int lineId, int itemId, bool tracksLots, int sourceBinId, int quantity, string? lotNumber)
    {
        for (var attempt = 1; attempt <= MaxConcurrencyAttempts; attempt++)
        {
            try
            {
                if (tracksLots)
                {
                    var lot = _context.Lots.FirstOrDefault(l => l.ItemId == itemId && l.LotNumber == lotNumber);
                    var lotBalance = lot == null
                        ? null
                        : _context.InventoryLotBalances.FirstOrDefault(b =>
                            b.ItemId == itemId && b.WarehouseBinId == sourceBinId && b.LotId == lot.Id);

                    if (lotBalance == null || lotBalance.Quantity < quantity) return ShipOutcome.InsufficientStock;

                    lotBalance.Quantity -= quantity;

                    _context.StockMovements.Add(new StockMovement
                    {
                        ItemId = itemId,
                        WarehouseBinId = sourceBinId,
                        TransactionType = "RELOCATE",
                        QuantityChanged = -quantity,
                        Timestamp = DateTime.UtcNow,
                        PerformedBy = CurrentUsername,
                        LotId = lot!.Id
                    });
                }
                else
                {
                    var balance = _context.InventoryBalances.FirstOrDefault(b =>
                        b.ItemId == itemId && b.WarehouseBinId == sourceBinId);

                    if (balance == null || balance.Quantity < quantity) return ShipOutcome.InsufficientStock;

                    balance.Quantity -= quantity;

                    _context.StockMovements.Add(new StockMovement
                    {
                        ItemId = itemId,
                        WarehouseBinId = sourceBinId,
                        TransactionType = "RELOCATE",
                        QuantityChanged = -quantity,
                        Timestamp = DateTime.UtcNow,
                        PerformedBy = CurrentUsername
                    });
                }

                var line = _context.TransferLines.Find(lineId)!;
                line.Shipped = true;

                _context.SaveChanges();
                return ShipOutcome.Success;
            }
            catch (DbUpdateConcurrencyException)
            {
                _context.ChangeTracker.Clear();
                PauseBeforeRetrying(attempt);
            }
        }

        return ShipOutcome.ConcurrencyExhausted;
    }

    // Every line's stock leaves its source bin here - what physically happens
    // when the truck pulls out. Only lines not already Shipped are touched,
    // which is what makes a retry after a partial failure safe.
    [HttpPost("{id}/ship")]
    public IActionResult ShipTransfer(int id)
    {
        var transfer = _context.Transfers.Include(t => t.Lines).FirstOrDefault(t => t.Id == id);
        if (transfer == null) return NotFound(new { Message = "Transfer not found." });

        if (transfer.Status != TransferStatus.Draft)
        {
            return Conflict(new { Message = $"This transfer is already {transfer.Status}." });
        }

        var toShip = transfer.Lines.Where(l => !l.Shipped).ToList();

        foreach (var line in toShip)
        {
            var item = _context.Items.Find(line.ItemId)!;
            var outcome = Ship(line.Id, line.ItemId, item.TracksLots, line.SourceBinId, line.Quantity, line.LotNumber);

            if (outcome == ShipOutcome.InsufficientStock)
            {
                return BadRequest(new
                {
                    Message = item.TracksLots
                        ? $"Insufficient stock in lot '{line.LotNumber}' in Bin {line.SourceBinId} to ship this transfer."
                        : $"Insufficient stock in Bin {line.SourceBinId} to ship this transfer."
                });
            }

            if (outcome == ShipOutcome.ConcurrencyExhausted)
            {
                return Conflict(new { Message = "This stock is being updated by another request. Please try shipping this transfer again." });
            }
        }

        var shipped = DetailQuery().First(t => t.Id == id);
        shipped.Status = TransferStatus.InTransit;
        shipped.ShippedAt = DateTime.UtcNow;
        shipped.ShippedBy = CurrentUsername;
        _context.SaveChanges();

        return Ok(new { Message = "Transfer shipped.", Transfer = TransferDetail(shipped) });
    }

    // Adds one line's stock to its destination bin and marks it Received -
    // the same idempotency trick Ship uses above.
    private bool Receive(int lineId, int itemId, bool tracksLots, int destinationBinId, int quantity, string? lotNumber)
    {
        for (var attempt = 1; attempt <= MaxConcurrencyAttempts; attempt++)
        {
            try
            {
                if (tracksLots)
                {
                    // The lot already exists: a transfer can only ship a lot
                    // that was actually on the shelf at the source, and a
                    // balance cannot exist for a lot that was never created.
                    var lot = _context.Lots.First(l => l.ItemId == itemId && l.LotNumber == lotNumber);

                    var lotBalance = _context.InventoryLotBalances.FirstOrDefault(b =>
                        b.ItemId == itemId && b.WarehouseBinId == destinationBinId && b.LotId == lot.Id);

                    if (lotBalance != null)
                    {
                        lotBalance.Quantity += quantity;
                    }
                    else
                    {
                        _context.InventoryLotBalances.Add(new InventoryLotBalance
                        {
                            ItemId = itemId, WarehouseBinId = destinationBinId, LotId = lot.Id, Quantity = quantity
                        });
                    }

                    _context.StockMovements.Add(new StockMovement
                    {
                        ItemId = itemId,
                        WarehouseBinId = destinationBinId,
                        TransactionType = "RELOCATE",
                        QuantityChanged = quantity,
                        Timestamp = DateTime.UtcNow,
                        PerformedBy = CurrentUsername,
                        LotId = lot.Id
                    });
                }
                else
                {
                    var balance = _context.InventoryBalances.FirstOrDefault(b =>
                        b.ItemId == itemId && b.WarehouseBinId == destinationBinId);

                    if (balance != null)
                    {
                        balance.Quantity += quantity;
                    }
                    else
                    {
                        _context.InventoryBalances.Add(new InventoryBalance
                        {
                            ItemId = itemId, WarehouseBinId = destinationBinId, Quantity = quantity
                        });
                    }

                    _context.StockMovements.Add(new StockMovement
                    {
                        ItemId = itemId,
                        WarehouseBinId = destinationBinId,
                        TransactionType = "RELOCATE",
                        QuantityChanged = quantity,
                        Timestamp = DateTime.UtcNow,
                        PerformedBy = CurrentUsername
                    });
                }

                var line = _context.TransferLines.Find(lineId)!;
                line.Received = true;

                _context.SaveChanges();
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                _context.ChangeTracker.Clear();
                PauseBeforeRetrying(attempt);
            }
            catch (DbUpdateException ex) when (UniqueConstraint.WasViolated(ex))
            {
                _context.ChangeTracker.Clear();
                PauseBeforeRetrying(attempt);
            }
        }

        return false;
    }

    // Every line's stock arrives in its destination bin here - what
    // physically happens when the truck is unloaded. Only reachable once
    // ShipTransfer has already removed the stock from the source, so nothing
    // here can be short: unlike a plain receive, a transfer's destination leg
    // never fails on quantity.
    [HttpPost("{id}/receive")]
    public IActionResult ReceiveTransfer(int id)
    {
        var transfer = _context.Transfers.Include(t => t.Lines).FirstOrDefault(t => t.Id == id);
        if (transfer == null) return NotFound(new { Message = "Transfer not found." });

        if (transfer.Status != TransferStatus.InTransit)
        {
            return Conflict(new { Message = $"This transfer is {transfer.Status}, not in transit." });
        }

        var toReceive = transfer.Lines.Where(l => !l.Received).ToList();

        foreach (var line in toReceive)
        {
            var item = _context.Items.Find(line.ItemId)!;
            var received = Receive(line.Id, line.ItemId, item.TracksLots, line.DestinationBinId, line.Quantity, line.LotNumber);

            if (!received)
            {
                return Conflict(new { Message = "This stock is being updated by another request. Please try receiving this transfer again." });
            }
        }

        var arrived = DetailQuery().First(t => t.Id == id);
        arrived.Status = TransferStatus.Received;
        arrived.ReceivedAt = DateTime.UtcNow;
        arrived.ReceivedBy = CurrentUsername;
        _context.SaveChanges();

        return Ok(new { Message = "Transfer received.", Transfer = TransferDetail(arrived) });
    }
}

public class TransferLineRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Choose an item for each line.")]
    public int ItemId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Choose the bin the stock is shipping from.")]
    public int SourceBinId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Choose the bin the stock is arriving into.")]
    public int DestinationBinId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Quantity must be a whole number greater than zero.")]
    public int Quantity { get; set; }

    // Required only when the item tracks lots - same as RelocateStockRequest.
    [StringLength(64, ErrorMessage = "Lot/batch number cannot be longer than 64 characters.")]
    public string? LotNumber { get; set; }
}

public class CreateTransferRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Choose the source warehouse.")]
    public int SourceWarehouseId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Choose the destination warehouse.")]
    public int DestinationWarehouseId { get; set; }

    [MinLength(1, ErrorMessage = "A transfer needs at least one line.")]
    public List<TransferLineRequest> Lines { get; set; } = [];
}
