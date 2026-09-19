using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Inventria.Models;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace Inventria.Controllers;

/// <summary>
/// Cycle counting: the only way to correct a miscount without faking a PICK.
/// Opening a sheet snapshots what the books say is on the shelves in one zone;
/// recording counts against it is just data entry; posting is the one place
/// that snapshot turns into ADJUST movements against InventoryBalance /
/// InventoryLotBalance, each carrying the reason the count differed. See
/// StockMovement.TransactionType and DashboardController for why ADJUST is
/// deliberately left out of every figure that sums RECEIVE/PICK - a correction
/// is not a day's throughput.
/// </summary>
[Authorize]
[Route("api/count-sheets")]
[ApiController]
public class CountSheetsController : ControllerBase
{
    private readonly InventriaDbContext _context;

    public CountSheetsController(InventriaDbContext context)
    {
        _context = context;
    }

    private string CurrentUsername => User.FindFirstValue(ClaimTypes.Name)!;

    private static string? NormalizeLotNumber(string? lotNumber) =>
        string.IsNullOrWhiteSpace(lotNumber) ? null : lotNumber.Trim();

    private static int? Variance(CountSheetLine line) =>
        line.CountedQuantity.HasValue ? line.CountedQuantity.Value - line.ExpectedQuantity : null;

    private static object LineSummary(CountSheetLine line) => new
    {
        line.Id,
        line.ItemId,
        ItemName = line.Item?.Name,
        ItemSku = line.Item?.Sku,
        line.WarehouseBinId,
        BinZone = line.WarehouseBin?.Zone,
        BinAisle = line.WarehouseBin?.Aisle,
        BinShelf = line.WarehouseBin?.Shelf,
        line.LotId,
        LotNumber = line.Lot?.LotNumber,
        line.ExpectedQuantity,
        line.CountedQuantity,
        Variance = Variance(line),
        line.ReasonCode
    };

    private static object SheetDetail(CountSheet sheet) => new
    {
        sheet.Id,
        sheet.WarehouseId,
        WarehouseName = sheet.Warehouse?.Name,
        sheet.Zone,
        sheet.Status,
        sheet.OpenedAt,
        sheet.OpenedBy,
        sheet.PostedAt,
        sheet.PostedBy,
        Lines = sheet.Lines.Select(LineSummary)
    };

    private IQueryable<CountSheet> DetailQuery() => _context.CountSheets
        .Include(s => s.Warehouse)
        .Include(s => s.Lines).ThenInclude(l => l.Item)
        .Include(s => s.Lines).ThenInclude(l => l.WarehouseBin)
        .Include(s => s.Lines).ThenInclude(l => l.Lot);

    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 200;

    [HttpGet]
    public IActionResult GetCountSheets(
        [FromQuery] string? status,
        [FromQuery] int? warehouseId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = _context.CountSheets
            .Include(s => s.Warehouse)
            .Include(s => s.Lines)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(s => s.Status == status);
        if (warehouseId.HasValue) query = query.Where(s => s.WarehouseId == warehouseId.Value);

        query = query.OrderByDescending(s => s.OpenedAt).ThenByDescending(s => s.Id);

        var totalCount = query.Count();

        var sheets = query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList()
            .Select(s => new
            {
                s.Id,
                s.WarehouseId,
                WarehouseName = s.Warehouse?.Name,
                s.Zone,
                s.Status,
                s.OpenedAt,
                s.OpenedBy,
                s.PostedAt,
                LineCount = s.Lines.Count,
                CountedLineCount = s.Lines.Count(l => l.CountedQuantity.HasValue),
                VarianceLineCount = s.Lines.Count(l => l.CountedQuantity.HasValue && l.CountedQuantity.Value != l.ExpectedQuantity)
            });

        return Ok(new
        {
            CountSheets = sheets,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    [HttpGet("{id}")]
    public IActionResult GetCountSheet(int id)
    {
        var sheet = DetailQuery().FirstOrDefault(s => s.Id == id);
        if (sheet == null) return NotFound(new { Message = "Count sheet not found." });

        return Ok(SheetDetail(sheet));
    }

    // Snapshots every non-lot and lot balance in the zone's bins into fresh
    // lines. Anything found on the shelf that isn't among them - stock the
    // books have no record of in this zone at all - has no line to record a
    // count against yet; AddLine below is how one gets added.
    [HttpPost]
    public IActionResult OpenCountSheet([FromBody] OpenCountSheetRequest request)
    {
        var warehouse = _context.Warehouses.Find(request.WarehouseId);
        if (warehouse == null) return NotFound(new { Message = $"Warehouse with ID {request.WarehouseId} not found." });

        var zone = request.Zone.Trim();

        var binIds = _context.WarehouseBins
            .Where(b => b.WarehouseId == request.WarehouseId && b.Zone == zone)
            .Select(b => b.Id)
            .ToList();

        if (binIds.Count == 0)
        {
            return NotFound(new { Message = $"'{zone}' has no bins in '{warehouse.Name}'." });
        }

        // Two open sheets for the same zone would double-count whatever the
        // second one finds - the first sheet's adjustments have not happened
        // yet, so its lines are not a live view the second could compare
        // against.
        var alreadyOpen = _context.CountSheets.Any(s =>
            s.WarehouseId == request.WarehouseId && s.Zone == zone && s.Status == CountSheetStatus.Open);
        if (alreadyOpen)
        {
            return Conflict(new { Message = $"'{zone}' already has an open count sheet. Post or cancel it before opening another." });
        }

        var lines = _context.InventoryBalances
            .Where(b => binIds.Contains(b.WarehouseBinId))
            .Select(b => new CountSheetLine
            {
                ItemId = b.ItemId,
                WarehouseBinId = b.WarehouseBinId,
                ExpectedQuantity = b.Quantity
            })
            .ToList();

        lines.AddRange(_context.InventoryLotBalances
            .Where(b => binIds.Contains(b.WarehouseBinId))
            .Select(b => new CountSheetLine
            {
                ItemId = b.ItemId,
                WarehouseBinId = b.WarehouseBinId,
                LotId = b.LotId,
                ExpectedQuantity = b.Quantity
            }));

        if (lines.Count == 0)
        {
            return BadRequest(new { Message = $"'{zone}' has no stock recorded to count." });
        }

        var sheet = new CountSheet
        {
            WarehouseId = request.WarehouseId,
            Zone = zone,
            OpenedBy = CurrentUsername,
            Lines = lines
        };

        _context.CountSheets.Add(sheet);
        _context.SaveChanges();

        return Ok(new { Message = $"Count sheet opened for '{zone}' with {lines.Count} line(s).", CountSheet = SheetDetail(sheet) });
    }

    // Adds a line for stock found in the zone that the snapshot did not
    // already carry - the books showing nothing is itself a count of zero,
    // not a reason to leave a surplus unrecordable.
    [HttpPost("{id}/lines")]
    public IActionResult AddLine(int id, [FromBody] AddCountSheetLineRequest request)
    {
        var sheet = _context.CountSheets.Include(s => s.Lines).FirstOrDefault(s => s.Id == id);
        if (sheet == null) return NotFound(new { Message = "Count sheet not found." });

        if (sheet.Status != CountSheetStatus.Open)
        {
            return Conflict(new { Message = $"This count sheet is {sheet.Status} and can no longer be changed." });
        }

        var item = _context.Items.Find(request.ItemId);
        if (item == null) return NotFound(new { Message = $"Item with ID {request.ItemId} not found." });

        var bin = _context.WarehouseBins.Find(request.WarehouseBinId);
        if (bin == null) return NotFound(new { Message = $"Warehouse Bin with ID {request.WarehouseBinId} not found." });

        if (bin.WarehouseId != sheet.WarehouseId || bin.Zone != sheet.Zone)
        {
            return BadRequest(new { Message = $"Bin {request.WarehouseBinId} is not in '{sheet.Zone}', the zone this sheet is counting." });
        }

        int? lotId = null;
        if (item.TracksLots)
        {
            var lotNumber = NormalizeLotNumber(request.LotNumber);
            if (lotNumber == null)
            {
                return BadRequest(new { Message = $"'{item.Name}' tracks lots. Give the lot/batch number found." });
            }

            var lot = _context.Lots.FirstOrDefault(l => l.ItemId == item.Id && l.LotNumber == lotNumber);
            if (lot == null)
            {
                return NotFound(new { Message = $"No lot '{lotNumber}' is on record for '{item.Name}'." });
            }

            lotId = lot.Id;
        }

        var duplicate = sheet.Lines.Any(l => l.ItemId == item.Id && l.WarehouseBinId == bin.Id && l.LotId == lotId);
        if (duplicate)
        {
            return Conflict(new { Message = $"'{item.Name}' is already on this sheet for that bin." });
        }

        var line = new CountSheetLine
        {
            CountSheetId = sheet.Id,
            ItemId = item.Id,
            WarehouseBinId = bin.Id,
            LotId = lotId,
            ExpectedQuantity = 0
        };

        sheet.Lines.Add(line);
        _context.SaveChanges();

        return Ok(new { Message = "Line added.", CountSheet = SheetDetail(sheet) });
    }

    [HttpPut("{id}/lines/{lineId}")]
    public IActionResult RecordCount(int id, int lineId, [FromBody] RecordCountRequest request)
    {
        var sheet = _context.CountSheets.Include(s => s.Lines).FirstOrDefault(s => s.Id == id);
        if (sheet == null) return NotFound(new { Message = "Count sheet not found." });

        if (sheet.Status != CountSheetStatus.Open)
        {
            return Conflict(new { Message = $"This count sheet is {sheet.Status} and can no longer be recorded against." });
        }

        var line = sheet.Lines.FirstOrDefault(l => l.Id == lineId);
        if (line == null) return NotFound(new { Message = "Count sheet line not found." });

        line.CountedQuantity = request.CountedQuantity;
        line.ReasonCode = string.IsNullOrWhiteSpace(request.ReasonCode) ? null : request.ReasonCode.Trim();

        _context.SaveChanges();

        return Ok(new { Message = "Count recorded.", CountSheet = SheetDetail(sheet) });
    }

    [HttpPost("{id}/cancel")]
    public IActionResult CancelCountSheet(int id)
    {
        var sheet = _context.CountSheets.Find(id);
        if (sheet == null) return NotFound(new { Message = "Count sheet not found." });

        if (sheet.Status != CountSheetStatus.Open)
        {
            return Conflict(new { Message = $"This count sheet is already {sheet.Status}." });
        }

        sheet.Status = CountSheetStatus.Cancelled;
        _context.SaveChanges();

        return Ok(new { Message = "Count sheet cancelled." });
    }

    private const int MaxConcurrencyAttempts = 8;

    // retried is one short transaction, not because they were measured - same
    // reasoning as InventoryController.PauseBeforeRetrying.
    private static void PauseBeforeRetrying(int attempt) =>
        Thread.Sleep(Random.Shared.Next(4, 16) * attempt);

    // Writes one line's discrepancy as an ADJUST movement and folds it into
    // the balance it was counted against, then advances that line's
    // ExpectedQuantity to match what was counted. That last part is what
    // makes posting idempotent: if this sheet's Post call is retried (the
    // same "stock changed underneath us, try again" story every other stock
    // write in this app tells), a line already settled here has Expected ==
    // Counted and PostCountSheet's own toAdjust filter skips it, so it is
    // never adjusted twice. Returns false only once every retry against a
    // concurrency conflict is exhausted.
    private bool ApplyAdjustment(int lineId, int itemId, int warehouseBinId, int? lotId, int delta, string? reasonCode)
    {
        for (var attempt = 1; attempt <= MaxConcurrencyAttempts; attempt++)
        {
            try
            {
                if (lotId.HasValue)
                {
                    var lotBalance = _context.InventoryLotBalances.FirstOrDefault(b =>
                        b.ItemId == itemId && b.WarehouseBinId == warehouseBinId && b.LotId == lotId.Value);

                    if (lotBalance != null)
                    {
                        lotBalance.Quantity += delta;
                    }
                    else
                    {
                        _context.InventoryLotBalances.Add(new InventoryLotBalance
                        {
                            ItemId = itemId,
                            WarehouseBinId = warehouseBinId,
                            LotId = lotId.Value,
                            Quantity = delta
                        });
                    }
                }
                else
                {
                    var balance = _context.InventoryBalances.FirstOrDefault(b =>
                        b.ItemId == itemId && b.WarehouseBinId == warehouseBinId);

                    if (balance != null)
                    {
                        balance.Quantity += delta;
                    }
                    else
                    {
                        _context.InventoryBalances.Add(new InventoryBalance
                        {
                            ItemId = itemId,
                            WarehouseBinId = warehouseBinId,
                            Quantity = delta
                        });
                    }
                }

                _context.StockMovements.Add(new StockMovement
                {
                    ItemId = itemId,
                    WarehouseBinId = warehouseBinId,
                    TransactionType = "ADJUST",
                    QuantityChanged = delta,
                    Timestamp = DateTime.UtcNow,
                    PerformedBy = CurrentUsername,
                    LotId = lotId,
                    ReasonCode = reasonCode
                });

                var line = _context.CountSheetLines.Find(lineId)!;
                line.ExpectedQuantity = line.CountedQuantity!.Value;

                _context.SaveChanges();
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                // A balance row we read has been updated since; its RowVersion no
                // longer matches and the UPDATE matched no rows.
                _context.ChangeTracker.Clear();
                PauseBeforeRetrying(attempt);
            }
            catch (DbUpdateException ex) when (UniqueConstraint.WasViolated(ex))
            {
                // Someone else created the balance row for this item/bin (or
                // item/bin/lot) between our lookup and our insert.
                _context.ChangeTracker.Clear();
                PauseBeforeRetrying(attempt);
            }
        }

        return false;
    }

    [HttpPost("{id}/post")]
    public IActionResult PostCountSheet(int id)
    {
        var sheet = _context.CountSheets.Include(s => s.Lines).FirstOrDefault(s => s.Id == id);
        if (sheet == null) return NotFound(new { Message = "Count sheet not found." });

        if (sheet.Status != CountSheetStatus.Open)
        {
            return Conflict(new { Message = $"This count sheet is already {sheet.Status}." });
        }

        var uncounted = sheet.Lines.Count(l => l.CountedQuantity == null);
        if (uncounted > 0)
        {
            return BadRequest(new { Message = $"{uncounted} line(s) have not been counted yet." });
        }

        var missingReason = sheet.Lines.Any(l =>
            l.CountedQuantity!.Value != l.ExpectedQuantity && string.IsNullOrWhiteSpace(l.ReasonCode));
        if (missingReason)
        {
            return BadRequest(new { Message = "Every line whose count differs from the system needs a reason code before this can be posted." });
        }

        // Read off the tracked graph before any retry below clears it - see
        // ApplyAdjustment.
        var toAdjust = sheet.Lines
            .Where(l => l.CountedQuantity!.Value != l.ExpectedQuantity)
            .Select(l => new
            {
                LineId = l.Id,
                l.ItemId,
                l.WarehouseBinId,
                l.LotId,
                l.ReasonCode,
                Delta = l.CountedQuantity!.Value - l.ExpectedQuantity
            })
            .ToList();

        foreach (var adjustment in toAdjust)
        {
            var applied = ApplyAdjustment(
                adjustment.LineId, adjustment.ItemId, adjustment.WarehouseBinId, adjustment.LotId,
                adjustment.Delta, adjustment.ReasonCode);

            if (!applied)
            {
                return Conflict(new { Message = "This stock is being updated by another request. Please try posting this count sheet again." });
            }
        }

        // ApplyAdjustment may have cleared the change tracker, which would
        // detach `sheet` along with everything it clears - re-read it fresh
        // rather than trust whatever state it is in now.
        var posted = DetailQuery().First(s => s.Id == id);
        posted.Status = CountSheetStatus.Posted;
        posted.PostedAt = DateTime.UtcNow;
        posted.PostedBy = CurrentUsername;
        _context.SaveChanges();

        return Ok(new { Message = "Count sheet posted.", CountSheet = SheetDetail(posted) });
    }
}

public class OpenCountSheetRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Choose the warehouse to count.")]
    public int WarehouseId { get; set; }

    [NotBlank(ErrorMessage = "Zone is required.")]
    [StringLength(64, ErrorMessage = "Zone cannot be longer than 64 characters.")]
    public string Zone { get; set; } = string.Empty;
}

public class AddCountSheetLineRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Choose an item.")]
    public int ItemId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Choose the bin the stock was found in.")]
    public int WarehouseBinId { get; set; }

    [StringLength(64, ErrorMessage = "Lot/batch number cannot be longer than 64 characters.")]
    public string? LotNumber { get; set; }
}

public class RecordCountRequest
{
    [Range(0, int.MaxValue, ErrorMessage = "Counted quantity cannot be negative.")]
    public int CountedQuantity { get; set; }

    [StringLength(200, ErrorMessage = "Reason code cannot be longer than 200 characters.")]
    public string? ReasonCode { get; set; }
}
