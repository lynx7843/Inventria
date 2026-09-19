using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Inventria.Models;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace Inventria.Controllers;

/// <summary>
/// Batches picks that used to be one item at a time into a single walking
/// route through the warehouse. Bins already carry Zone/Aisle/Shelf, so
/// opening a list is just sorting the requested lines by that address once,
/// up front - see OpenPickList. Each line is then fulfilled through
/// StockPickingService, the same write POST /api/inventory/pick uses.
/// </summary>
[Authorize]
[Route("api/pick-lists")]
[ApiController]
public class PickListsController : ControllerBase
{
    private readonly InventriaDbContext _context;
    private readonly StockPickingService _picking;

    public PickListsController(InventriaDbContext context, StockPickingService picking)
    {
        _context = context;
        _picking = picking;
    }

    private string CurrentUsername => User.FindFirstValue(ClaimTypes.Name)!;

    private static string? NormalizeLotNumber(string? lotNumber) =>
        string.IsNullOrWhiteSpace(lotNumber) ? null : lotNumber.Trim();

    private static object LineSummary(PickListLine line) => new
    {
        line.Id,
        line.Sequence,
        line.ItemId,
        ItemName = line.Item?.Name,
        ItemSku = line.Item?.Sku,
        line.WarehouseBinId,
        BinZone = line.WarehouseBin?.Zone,
        BinAisle = line.WarehouseBin?.Aisle,
        BinShelf = line.WarehouseBin?.Shelf,
        line.LotNumber,
        line.QuantityRequested,
        line.QuantityPicked,
        QuantityRemaining = Math.Max(0, line.QuantityRequested - line.QuantityPicked)
    };

    private static object ListDetail(PickList list) => new
    {
        list.Id,
        list.Status,
        list.CreatedAt,
        list.CreatedBy,
        list.CompletedAt,
        Lines = list.Lines.OrderBy(l => l.Sequence).Select(LineSummary)
    };

    private IQueryable<PickList> DetailQuery() => _context.PickLists
        .Include(l => l.Lines).ThenInclude(l => l.Item)
        .Include(l => l.Lines).ThenInclude(l => l.WarehouseBin);

    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 200;

    [HttpGet]
    public IActionResult GetPickLists(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = _context.PickLists.Include(l => l.Lines).AsQueryable();

        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(l => l.Status == status);

        query = query.OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.Id);

        var totalCount = query.Count();

        var lists = query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList()
            .Select(l => new
            {
                l.Id,
                l.Status,
                l.CreatedAt,
                l.CreatedBy,
                l.CompletedAt,
                LineCount = l.Lines.Count,
                LinesRemaining = l.Lines.Count(x => x.QuantityPicked < x.QuantityRequested)
            });

        return Ok(new
        {
            PickLists = lists,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    [HttpGet("{id}")]
    public IActionResult GetPickList(int id)
    {
        var list = DetailQuery().FirstOrDefault(l => l.Id == id);
        if (list == null) return NotFound(new { Message = "Pick list not found." });

        return Ok(ListDetail(list));
    }

    // Sorts the requested lines into a walking route once, by the bin address
    // that already exists for exactly this - Zone, then Aisle, then Shelf,
    // within a warehouse. Sequence is fixed from here on: it describes the
    // route someone starts walking, and changing it mid-pick would send them
    // back the way they came.
    [HttpPost]
    public IActionResult OpenPickList([FromBody] OpenPickListRequest request)
    {
        var itemIds = request.Lines.Select(l => l.ItemId).Distinct().ToList();
        var items = _context.Items.Where(i => itemIds.Contains(i.Id)).ToDictionary(i => i.Id);
        var missingItemId = itemIds.FirstOrDefault(id => !items.ContainsKey(id));
        if (missingItemId != 0)
        {
            return NotFound(new { Message = $"Item with ID {missingItemId} not found." });
        }

        var binIds = request.Lines.Select(l => l.WarehouseBinId).Distinct().ToList();
        var bins = _context.WarehouseBins.Where(b => binIds.Contains(b.Id)).ToDictionary(b => b.Id);
        var missingBinId = binIds.FirstOrDefault(id => !bins.ContainsKey(id));
        if (missingBinId != 0)
        {
            return NotFound(new { Message = $"Warehouse Bin with ID {missingBinId} not found." });
        }

        foreach (var requestLine in request.Lines)
        {
            var item = items[requestLine.ItemId];
            if (item.TracksLots && NormalizeLotNumber(requestLine.LotNumber) == null)
            {
                return BadRequest(new { Message = $"'{item.Name}' tracks lots. Choose the lot/batch number to pick from." });
            }
        }

        var route = request.Lines
            .Select(requestLine => new { Line = requestLine, Bin = bins[requestLine.WarehouseBinId] })
            .OrderBy(x => x.Bin.WarehouseId)
            .ThenBy(x => x.Bin.Zone)
            .ThenBy(x => x.Bin.Aisle)
            .ThenBy(x => x.Bin.Shelf)
            .ToList();

        var pickList = new PickList
        {
            CreatedBy = CurrentUsername,
            Lines = route.Select((x, index) => new PickListLine
            {
                ItemId = x.Line.ItemId,
                WarehouseBinId = x.Line.WarehouseBinId,
                QuantityRequested = x.Line.Quantity,
                LotNumber = NormalizeLotNumber(x.Line.LotNumber),
                Sequence = index + 1
            }).ToList()
        };

        _context.PickLists.Add(pickList);
        _context.SaveChanges();

        return Ok(new { Message = $"Pick list opened with {pickList.Lines.Count} line(s).", PickList = ListDetail(pickList) });
    }

    [HttpPost("{id}/cancel")]
    public IActionResult CancelPickList(int id)
    {
        var list = _context.PickLists.Find(id);
        if (list == null) return NotFound(new { Message = "Pick list not found." });

        if (list.Status != PickListStatus.Open)
        {
            return Conflict(new { Message = $"This pick list is already {list.Status}." });
        }

        list.Status = PickListStatus.Cancelled;
        _context.SaveChanges();

        return Ok(new { Message = "Pick list cancelled." });
    }

    // Checks one line off, in whatever order the phone happens to reach it in
    // - Sequence is a suggested route, not an enforced one, since a picker who
    // finds an aisle blocked still needs to be able to pick the next line.
    [HttpPost("{id}/lines/{lineId}/pick")]
    public IActionResult PickLine(int id, int lineId, [FromBody] PickPickListLineRequest request)
    {
        var list = _context.PickLists.Include(l => l.Lines).FirstOrDefault(l => l.Id == id);
        if (list == null) return NotFound(new { Message = "Pick list not found." });

        if (list.Status != PickListStatus.Open)
        {
            return Conflict(new { Message = $"This pick list is {list.Status} and can no longer be picked against." });
        }

        var line = list.Lines.FirstOrDefault(l => l.Id == lineId);
        if (line == null) return NotFound(new { Message = "Pick list line not found." });

        var remaining = line.QuantityRequested - line.QuantityPicked;
        if (remaining <= 0)
        {
            return Conflict(new { Message = "This line has already been fully picked." });
        }

        var quantity = request.Quantity ?? remaining;
        if (quantity <= 0)
        {
            return BadRequest(new { Message = "Quantity must be a whole number greater than zero." });
        }

        if (quantity > remaining)
        {
            return BadRequest(new { Message = $"Picking {quantity} would exceed the {remaining} still owed on this line." });
        }

        var item = _context.Items.Find(line.ItemId);
        if (item == null) return NotFound(new { Message = $"Item with ID {line.ItemId} not found." });

        var result = _picking.Pick(line.ItemId, item.TracksLots, line.WarehouseBinId, quantity, CurrentUsername, line.LotNumber);

        if (result == null)
        {
            return Conflict(new { Message = "This stock is being updated by another request. Please try again." });
        }

        if (result.Outcome == PickOutcome.InsufficientStock)
        {
            return BadRequest(new
            {
                Message = item.TracksLots
                    ? $"Insufficient stock in lot '{line.LotNumber}' in Bin {line.WarehouseBinId} to fulfill this pick."
                    : $"Insufficient stock in Bin {line.WarehouseBinId} to fulfill this pick."
            });
        }

        line.QuantityPicked += quantity;

        if (list.Lines.All(l => l.QuantityPicked >= l.QuantityRequested))
        {
            list.Status = PickListStatus.Completed;
            list.CompletedAt = DateTime.UtcNow;
        }

        _context.SaveChanges();

        return Ok(new
        {
            Message = $"Picked {quantity} units.",
            QuantityRemaining = line.QuantityRequested - line.QuantityPicked,
            PickList = ListDetail(list)
        });
    }
}

public class PickListLineRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Choose an item for each line.")]
    public int ItemId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Choose the bin to pick from for each line.")]
    public int WarehouseBinId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Quantity must be a whole number greater than zero.")]
    public int Quantity { get; set; }

    // Required only when the item tracks lots - same as PickStockRequest.
    [StringLength(64, ErrorMessage = "Lot/batch number cannot be longer than 64 characters.")]
    public string? LotNumber { get; set; }
}

public class OpenPickListRequest
{
    [MinLength(1, ErrorMessage = "A pick list needs at least one line.")]
    public List<PickListLineRequest> Lines { get; set; } = [];
}

public class PickPickListLineRequest
{
    // Null picks whatever remains on the line - the common case, a picker
    // who found the full amount. Given only to record a short pick, the same
    // way a real shelf sometimes comes up short.
    [Range(1, int.MaxValue, ErrorMessage = "Quantity must be a whole number greater than zero.")]
    public int? Quantity { get; set; }
}
