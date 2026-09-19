using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Inventria.Models;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace Inventria.Controllers;

[Authorize]
[Route("api/[controller]")]
[ApiController]
public class InventoryController : ControllerBase
{
    private readonly InventriaDbContext _context;
    private readonly StockReceivingService _receiving;
    private readonly StockPickingService _picking;

    public InventoryController(InventriaDbContext context, StockReceivingService receiving, StockPickingService picking)
    {
        _context = context;
        _receiving = receiving;
        _picking = picking;
    }

    private string CurrentUsername => User.FindFirstValue(ClaimTypes.Name)!;

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private string? LowStockNoticeFor(int itemId)
    {

        var wantsAlerts = _context.Users
            .Where(u => u.Id == CurrentUserId)
            .Select(u => (bool?)u.NotifyLowStock)
            .FirstOrDefault() ?? true;

        if (!wantsAlerts) return null;

        // Deliberately the same query the dashboards count and the reorder report
        // lists, so an item cannot be low here and fine there.
        var line = LowStock.Lines(_context).FirstOrDefault(l => l.ItemId == itemId);
        if (line == null) return null;

        var order = line.SuggestedOrderQuantity > 0
            ? $" Suggested order: {line.SuggestedOrderQuantity} units."
            : " No reorder quantity is set for it, so there is no suggested amount.";

        return $"Low stock: {line.Name} is down to {line.QuantityOnHand} units, "
             + $"at or below its reorder point of {line.ReorderPoint}.{order}";
    }

    private const int MaxConcurrencyAttempts = 8;

    private IActionResult ExecuteStockMove(Func<IActionResult> move)
    {
        for (var attempt = 1; attempt <= MaxConcurrencyAttempts; attempt++)
        {
            try
            {
                return move();
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
                // Someone else created the balance row for this item/bin between
                // our lookup and our insert. A stock move writes no other row that
                // a unique index covers, so this is always that race and is always
                // safe to retry - the retry will find their row.
                _context.ChangeTracker.Clear();
                PauseBeforeRetrying(attempt);
            }
        }

        return Conflict(new { Message = "This stock is being updated by another request. Please try again." });
    }

    // retried is one short transaction, not because they were measured.
    private static void PauseBeforeRetrying(int attempt) =>
        Thread.Sleep(Random.Shared.Next(4, 16) * attempt);

    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 200;

    [HttpGet]
    public IActionResult GetAllItems(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        [FromQuery] bool includeArchived = false)
    {
        // Clamped rather than rejected: page 0 and a page size of 5000 are a
        // caller asking for the nearest sensible thing, not a malformed request,
        // and answering them with the first page of 200 is more useful than a
        // 400 they have to write code to handle.
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        // Ordered because paging demands it: without an ORDER BY, SQL Server is
        // free to return rows in any order it likes, and "any order it likes"
        // can differ between two queries - so an item could appear on both page
        // one and page two, or on neither. By name because that is the column
        // people read down; by Id after it so items sharing a name still have a
        // fixed order.
        var query = _context.Items.AsQueryable();

        // Defaults to hiding archived items, but only filters when the caller
        // hasn't asked for them - an explicit opt-in rather than a second
        // endpoint, so the archive can still be browsed without inventing a
        // new route for it. Archived items stay fully queryable everywhere
        // else (movement history, reports) - this filter is the catalogue's
        // alone.
        if (!includeArchived) query = query.Where(i => !i.IsArchived);

        query = query.OrderBy(i => i.Name).ThenBy(i => i.Id);

        var totalCount = query.Count();

        // Each item carries what is actually on the shelves for it, summed across
        // bins because an item is normally in several: the dashboards want "how
        // many of these do we have", not "how many are in one particular bin".
        // That sum is a lookup per row, which is the other reason to send a page
        // of them rather than the catalogue.
        var items = query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(i => new
            {
                i.Id,
                i.Sku,
                i.Name,
                i.Category,
                // Carried on every row so the tables that list items can mark
                // the low ones without a second request per row. Zero means the
                // item is not tracked for reordering; see Item.
                i.ReorderPoint,
                i.ReorderQuantity,
                i.UnitOfMeasure,
                i.UnitsPerPack,
                i.UnitCost,
                i.SalePrice,
                i.Barcode,
                i.IsArchived,
                i.TracksLots,
                // A lot-tracked item keeps no rows in InventoryBalance at
                // all - its stock lives in InventoryLotBalance instead - so
                // summing both is what makes this column mean the same
                // thing regardless of which path an item takes. Exactly one
                // of the two sums is ever non-zero for a given item.
                QuantityOnHand = (_context.InventoryBalances
                        .Where(b => b.ItemId == i.Id)
                        .Sum(b => (int?)b.Quantity) ?? 0)
                    + (_context.InventoryLotBalances
                        .Where(b => b.ItemId == i.Id)
                        .Sum(b => (int?)b.Quantity) ?? 0)
            })
            .ToList();

        return Ok(new
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    // Blank isn't a barcode, it's the absence of one - and unlike Sku/Name,
    // this field has to actually become null on blank input, not just
    // trimmed, because the filtered unique index only ignores rows where the
    // column is NULL. An empty string would still collide with every other
    // empty string.
    private static string? NormalizeBarcode(string? barcode) =>
        string.IsNullOrWhiteSpace(barcode) ? null : barcode.Trim();

    // Same reasoning as NormalizeBarcode: blank is "no lot number given",
    // not a lot number that happens to be blank.
    private static string? NormalizeLotNumber(string? lotNumber) =>
        string.IsNullOrWhiteSpace(lotNumber) ? null : lotNumber.Trim();

    // --- MASTER ITEM CRUD OPERATIONS ---

    [HttpPost("items")]
    public IActionResult CreateItem([FromBody] ItemRequest request)
    {
        // Surrounding spaces are invisible in the UI but not to the unique index,
        // so " SKU-1" would be accepted as a second, indistinguishable SKU-1.
        var sku = request.Sku.Trim();

        var newItem = new Item
        {
            Sku = sku,
            Name = request.Name.Trim(),
            Category = request.Category.Trim(),
            ReorderPoint = request.ReorderPoint,
            ReorderQuantity = request.ReorderQuantity,
            UnitOfMeasure = string.IsNullOrWhiteSpace(request.UnitOfMeasure) ? "unit" : request.UnitOfMeasure.Trim(),
            UnitsPerPack = request.UnitsPerPack,
            UnitCost = request.UnitCost,
            SalePrice = request.SalePrice,
            Barcode = NormalizeBarcode(request.Barcode),
            IsArchived = request.IsArchived,
            TracksLots = request.TracksLots
        };

        _context.Items.Add(newItem);

        try
        {
            _context.SaveChanges();
        }
        catch (DbUpdateException ex) when (UniqueConstraint.WasViolated(ex))
        {
            return BadRequest(new { Message = $"SKU '{sku}' or barcode is already used by another item." });
        }

        return Ok(new { Message = "Item created successfully.", Item = newItem });
    }

    [HttpPut("items/{id}")]
    public IActionResult UpdateItem(int id, [FromBody] ItemRequest request)
    {
        var item = _context.Items.Find(id);
        if (item == null) return NotFound(new { Message = "Item not found." });

        // Turning lot tracking off would leave whatever is sitting in
        // InventoryLotBalance stranded from the path receive/pick/relocate
        // would use from then on - a future receive would open a plain
        // InventoryBalance row alongside lots nobody can pick from through
        // the now-untracked flow. Refused while any lot still holds stock,
        // the same way deleting an item with stock on hand is refused.
        if (item.TracksLots && !request.TracksLots)
        {
            var lotUnits = _context.InventoryLotBalances
                .Where(b => b.ItemId == id && b.Quantity != 0)
                .Select(b => b.Quantity)
                .ToList();

            if (lotUnits.Count > 0)
            {
                return Conflict(new { Message = $"'{item.Name}' has {lotUnits.Sum()} units on hand across {lotUnits.Count} lot(s). Move or pick that stock out before turning off lot tracking." });
            }
        }

        item.Sku = request.Sku.Trim();
        item.Name = request.Name.Trim();
        item.Category = request.Category.Trim();
        item.ReorderPoint = request.ReorderPoint;
        item.ReorderQuantity = request.ReorderQuantity;
        item.UnitOfMeasure = string.IsNullOrWhiteSpace(request.UnitOfMeasure) ? "unit" : request.UnitOfMeasure.Trim();
        item.UnitsPerPack = request.UnitsPerPack;
        item.UnitCost = request.UnitCost;
        item.SalePrice = request.SalePrice;
        item.Barcode = NormalizeBarcode(request.Barcode);
        item.IsArchived = request.IsArchived;
        item.TracksLots = request.TracksLots;

        try
        {
            _context.SaveChanges();
        }
        catch (DbUpdateException ex) when (UniqueConstraint.WasViolated(ex))
        {
            return BadRequest(new { Message = $"SKU '{item.Sku}' or barcode is already used by another item." });
        }

        return Ok(new { Message = "Item updated successfully." });
    }

    [HttpDelete("items/{id}")]
    public IActionResult DeleteItem(int id)
    {
        var item = _context.Items.Find(id);
        if (item == null) return NotFound(new { Message = "Item not found." });

        // Deleting an item used to take its stock down with it - the balance rows
        // cascaded - and leave its movements behind pointing at an Id that no
        // longer resolves. Both foreign keys now refuse the delete, so these two
        // checks exist to say which one is in the way rather than to enforce
        // anything: the database is what actually holds the line.
        var quantities = _context.InventoryBalances
            .Where(b => b.ItemId == id && b.Quantity != 0)
            .Select(b => b.Quantity)
            .ToList();

        if (quantities.Count > 0)
        {
            return Conflict(new { Message = $"'{item.Name}' has {quantities.Sum()} units on hand across {quantities.Count} bin(s). Move or pick the stock out before deleting it." });
        }

        var lotQuantities = _context.InventoryLotBalances
            .Where(b => b.ItemId == id && b.Quantity != 0)
            .Select(b => b.Quantity)
            .ToList();

        if (lotQuantities.Count > 0)
        {
            return Conflict(new { Message = $"'{item.Name}' has {lotQuantities.Sum()} units on hand across {lotQuantities.Count} lot(s). Move or pick the stock out before deleting it." });
        }

        var movementCount = _context.StockMovements.Count(m => m.ItemId == id);
        if (movementCount > 0)
        {
            return Conflict(new { Message = $"'{item.Name}' has {movementCount} recorded stock movement(s). Deleting it would destroy that audit history." });
        }

        _context.Items.Remove(item);

        try
        {
            _context.SaveChanges();
        }
        catch (DbUpdateException ex) when (ForeignKeyConstraint.WasViolated(ex))
        {
            // Stock landed on this item between the checks above and the delete.
            return Conflict(new { Message = $"'{item.Name}' has just been used in a stock movement and can no longer be deleted." });
        }

        return Ok(new { Message = "Item deleted successfully." });
    }

    [HttpPost("receive")]
    public IActionResult ReceiveStock([FromBody] ReceiveStockRequest request)
    {
        // 1. Validate the quantity
        if (request.Quantity <= 0)
        {
            return BadRequest(new { Message = "Quantity must be greater than zero." });
        }

        // 2. Verify the item actually exists in the master list
        var item = _context.Items.Find(request.ItemId);
        if (item == null)
        {
            return NotFound(new { Message = $"Item with ID {request.ItemId} not found." });
        }

        // An archived item is retired, not deleted - it stays intact for
        // history and reports, but new stock arriving for it would be the
        // catalogue quietly un-retiring it. Unarchive it first if that is
        // really what is meant.
        if (item.IsArchived)
        {
            return Conflict(new { Message = $"'{item.Name}' is archived and cannot receive new stock. Unarchive it first." });
        }

        // 3. Verify the destination warehouse bin exists
        var bin = _context.WarehouseBins.Find(request.WarehouseBinId);
        if (bin == null)
        {
            return NotFound(new { Message = $"Warehouse Bin with ID {request.WarehouseBinId} not found." });
        }

        // A lot-tracked item cannot receive into the plain, unlabelled path -
        // every unit on its shelves has to belong to a named batch, or a
        // later recall has no way to say which units are which.
        var lotNumber = NormalizeLotNumber(request.LotNumber);
        if (item.TracksLots && lotNumber == null)
        {
            return BadRequest(new { Message = $"'{item.Name}' tracks lots. Give the lot/batch number this stock belongs to." });
        }

        // The retry loop around InventoryBalance.RowVersion lives in the
        // service now, shared with PurchaseOrdersController's line-receive
        // endpoint - see StockReceivingService for why that has to be one
        // copy, not two.
        var receipt = _receiving.Receive(item, request.WarehouseBinId, request.Quantity, CurrentUsername, lotNumber, request.ExpirationDate);

        if (receipt == null)
        {
            return Conflict(new { Message = "This stock is being updated by another request. Please try again." });
        }

        return Ok(new
        {
            Message = $"Successfully received {request.Quantity} units of {item.Name} into {bin.Zone}-{bin.Aisle}-{bin.Shelf}.",
            NewTotalBalance = receipt.NewTotalBalance
        });
    }

    [HttpPost("pick")]
    public IActionResult PickStock([FromBody] PickStockRequest request)
    {
        if (request.Quantity <= 0)
        {
            return BadRequest(new { Message = "Quantity must be greater than zero." });
        }

        var item = _context.Items.Find(request.ItemId);
        if (item == null)
        {
            return NotFound(new { Message = $"Item with ID {request.ItemId} not found." });
        }

        // A lot-tracked item's stock is only ever addressable by lot - "pick
        // 8 of this item from this bin" is not a complete instruction once
        // the bin can hold more than one batch of it.
        var lotNumber = NormalizeLotNumber(request.LotNumber);
        if (item.TracksLots && lotNumber == null)
        {
            return BadRequest(new { Message = $"'{item.Name}' tracks lots. Choose the lot/batch number to pick from." });
        }

        var result = _picking.Pick(item.Id, item.TracksLots, request.WarehouseBinId, request.Quantity, CurrentUsername, lotNumber);

        if (result == null)
        {
            return Conflict(new { Message = "This stock is being updated by another request. Please try again." });
        }

        if (result.Outcome == PickOutcome.InsufficientStock)
        {
            return BadRequest(new
            {
                Message = item.TracksLots
                    ? $"Insufficient stock in lot '{lotNumber}' in the specified bin to fulfill this pick."
                    : "Insufficient stock available in the specified bin to fulfill this pick."
            });
        }

        return Ok(new
        {
            Message = item.TracksLots
                ? $"Successfully picked {request.Quantity} units of lot '{lotNumber}' from Bin {request.WarehouseBinId}."
                : $"Successfully picked {request.Quantity} units from Bin {request.WarehouseBinId}.",
            RemainingBalance = result.RemainingBalance,
            LowStockWarning = LowStockNoticeFor(request.ItemId)
        });
    }

    [HttpPost("relocate")]
    public IActionResult RelocateStock([FromBody] RelocateStockRequest request)
    {
        if (request.Quantity <= 0)
        {
            return BadRequest(new { Message = "Quantity must be greater than zero." });
        }

        if (request.SourceBinId == request.DestinationBinId)
        {
            return BadRequest(new { Message = "Source and destination bins cannot be the same." });
        }

        var sourceBin = _context.WarehouseBins.Find(request.SourceBinId);
        if (sourceBin == null)
        {
            return NotFound(new { Message = $"Warehouse Bin with ID {request.SourceBinId} not found." });
        }

        var destinationBinForWarehouseCheck = _context.WarehouseBins.Find(request.DestinationBinId);
        if (destinationBinForWarehouseCheck != null && destinationBinForWarehouseCheck.WarehouseId != sourceBin.WarehouseId)
        {
            // This endpoint writes both legs in one request, which only makes
            // sense when the stock never leaves the building - within one
            // warehouse there is no gap for it to be missing from the books
            // during. Crossing warehouses needs the gap a Transfer's
            // InTransit state accounts for; see TransfersController.
            return BadRequest(new { Message = "Source and destination bins are in different warehouses. Use a Transfer to move stock between warehouses." });
        }

        var item = _context.Items.Find(request.ItemId);
        if (item == null)
        {
            return NotFound(new { Message = $"Item with ID {request.ItemId} not found." });
        }

        // A relocation of a lot-tracked item is still a move of one specific
        // batch - the shelf it lands on has to know which lot arrived, the
        // same as a receive does.
        var lotNumber = NormalizeLotNumber(request.LotNumber);
        if (item.TracksLots && lotNumber == null)
        {
            return BadRequest(new { Message = $"'{item.Name}' tracks lots. Choose the lot/batch number to relocate." });
        }

        return ExecuteStockMove(() =>
        {
            var timestamp = DateTime.UtcNow;

            if (item.TracksLots)
            {
                var lot = _context.Lots.FirstOrDefault(l => l.ItemId == request.ItemId && l.LotNumber == lotNumber);
                var sourceLotBalance = lot == null
                    ? null
                    : _context.InventoryLotBalances.FirstOrDefault(b =>
                        b.ItemId == request.ItemId && b.WarehouseBinId == request.SourceBinId && b.LotId == lot.Id);

                if (sourceLotBalance == null || sourceLotBalance.Quantity < request.Quantity)
                {
                    return BadRequest(new { Message = $"Insufficient stock in lot '{lotNumber}' in source bin for relocation." });
                }

                var destinationLotBinExists = _context.WarehouseBins.Any(b => b.Id == request.DestinationBinId);
                if (!destinationLotBinExists)
                {
                    return NotFound(new { Message = $"Destination Bin with ID {request.DestinationBinId} does not exist." });
                }

                sourceLotBalance.Quantity -= request.Quantity;

                var destLotBalance = _context.InventoryLotBalances.FirstOrDefault(b =>
                    b.ItemId == request.ItemId && b.WarehouseBinId == request.DestinationBinId && b.LotId == lot!.Id);

                if (destLotBalance != null)
                {
                    destLotBalance.Quantity += request.Quantity;
                }
                else
                {
                    destLotBalance = new InventoryLotBalance
                    {
                        ItemId = request.ItemId,
                        WarehouseBinId = request.DestinationBinId,
                        Quantity = request.Quantity,
                        LotId = lot!.Id
                    };
                    _context.InventoryLotBalances.Add(destLotBalance);
                }

                _context.StockMovements.Add(new StockMovement
                {
                    ItemId = request.ItemId,
                    WarehouseBinId = request.SourceBinId,
                    TransactionType = "RELOCATE",
                    QuantityChanged = -request.Quantity,
                    Timestamp = timestamp,
                    PerformedBy = CurrentUsername,
                    LotId = lot!.Id
                });

                _context.StockMovements.Add(new StockMovement
                {
                    ItemId = request.ItemId,
                    WarehouseBinId = request.DestinationBinId,
                    TransactionType = "RELOCATE",
                    QuantityChanged = request.Quantity,
                    Timestamp = timestamp,
                    PerformedBy = CurrentUsername,
                    LotId = lot!.Id
                });

                _context.SaveChanges();

                return Ok(new {
                    Message = $"Successfully relocated {request.Quantity} units of lot '{lotNumber}' from Bin {request.SourceBinId} to Bin {request.DestinationBinId}."
                });
            }

            // Verify source bin has enough stock
            var sourceBalance = _context.InventoryBalances
                .FirstOrDefault(b => b.ItemId == request.ItemId && b.WarehouseBinId == request.SourceBinId);

            if (sourceBalance == null || sourceBalance.Quantity < request.Quantity)
            {
                return BadRequest(new { Message = "Insufficient stock in source bin for relocation." });
            }

            // Verify destination bin exists
            var destinationBinExists = _context.WarehouseBins.Any(b => b.Id == request.DestinationBinId);
            if (!destinationBinExists)
            {
                return NotFound(new { Message = $"Destination Bin with ID {request.DestinationBinId} does not exist." });
            }

            // Deduct from source bin
            sourceBalance.Quantity -= request.Quantity;

            // Add to destination bin
            var destBalance = _context.InventoryBalances
                .FirstOrDefault(b => b.ItemId == request.ItemId && b.WarehouseBinId == request.DestinationBinId);

            if (destBalance != null)
            {
                destBalance.Quantity += request.Quantity;
            }
            else
            {
                destBalance = new InventoryBalance
                {
                    ItemId = request.ItemId,
                    WarehouseBinId = request.DestinationBinId,
                    Quantity = request.Quantity
                };
                _context.InventoryBalances.Add(destBalance);
            }

            // A relocation is two entries in the ledger, not one. A single row
            // against the source bin with a positive quantity recorded the
            // opposite of what happened - the bin that lost stock was credited
            // with it - and never mentioned the destination at all, so summing a
            // bin's movements could not reconcile against its InventoryBalance.
            // One row per side, signed the way the balance moved, keeps that sum
            // honest for anything that adds up movements without knowing what a
            // relocation is. Both legs share a timestamp, which is what marks
            // them as the two halves of one move.
            _context.StockMovements.Add(new StockMovement
            {
                ItemId = request.ItemId,
                WarehouseBinId = request.SourceBinId,
                TransactionType = "RELOCATE",
                QuantityChanged = -request.Quantity, // Left the source bin
                Timestamp = timestamp,
                PerformedBy = CurrentUsername
            });

            _context.StockMovements.Add(new StockMovement
            {
                ItemId = request.ItemId,
                WarehouseBinId = request.DestinationBinId,
                TransactionType = "RELOCATE",
                QuantityChanged = request.Quantity, // Arrived in the destination bin
                Timestamp = timestamp,
                PerformedBy = CurrentUsername
            });

            _context.SaveChanges();

            return Ok(new {
                Message = $"Successfully relocated {request.Quantity} units from Bin {request.SourceBinId} to Bin {request.DestinationBinId}."
            });
        });
    }
}

// Add these request DTO classes at the very bottom of the file
public class ItemRequest
{
    // 64 characters because that is the width of the column, and the column is
    // that width because a unique index needs a bounded one. Without the limit
    // an over-long SKU is a truncation error from SQL Server, which reaches the
    // caller as a 500 rather than as "that is too long".
    [NotBlank(ErrorMessage = "SKU is required.")]
    [StringLength(64, ErrorMessage = "SKU cannot be longer than 64 characters.")]
    public string Sku { get; set; } = string.Empty;

    // Name and Category are nvarchar(max) in the database, so these lengths are
    // not a storage limit - they are the point past which a value stops being a
    // product name and starts being pasted junk that breaks every table it is
    // rendered in.
    [NotBlank(ErrorMessage = "Product name is required.")]
    [StringLength(200, ErrorMessage = "Product name cannot be longer than 200 characters.")]
    public string Name { get; set; } = string.Empty;

    [NotBlank(ErrorMessage = "Category is required.")]
    [StringLength(100, ErrorMessage = "Category cannot be longer than 100 characters.")]
    public string Category { get; set; } = string.Empty;

    // Both default to zero, which is what "not tracked for reordering" is
    // spelled as, so a caller that has never heard of these fields - an older
    // client, a script written against the previous shape - keeps working and
    // keeps the item out of the alerts. Negative is refused rather than clamped:
    // a reorder point below zero is not an unusual choice, it is a mistake, and
    // silently storing zero would hide it.
    [Range(0, int.MaxValue, ErrorMessage = "Reorder point cannot be negative.")]
    public int ReorderPoint { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Reorder quantity cannot be negative.")]
    public int ReorderQuantity { get; set; }

    // Defaults to "unit" rather than being required: an older client that has
    // never heard of this field sends no value for it, and that should mean
    // "unit", not a validation error on a request that used to be fine.
    [StringLength(32, ErrorMessage = "Unit of measure cannot be longer than 32 characters.")]
    public string UnitOfMeasure { get; set; } = "unit";

    // Null means "no pack grouping for this item", which is most of them, so
    // it is optional. When given, a pack of zero or fewer is not a pack.
    [Range(1, int.MaxValue, ErrorMessage = "Units per pack must be at least 1.")]
    public int? UnitsPerPack { get; set; }

    // Both null means "not priced yet" - an item can be catalogued before
    // anyone has costed or priced it, and leaving these unset keeps it out of
    // the valuation total rather than counting it as free.
    [Range(typeof(decimal), "0", "79228162514264337593543950335", ErrorMessage = "Unit cost cannot be negative.")]
    public decimal? UnitCost { get; set; }

    [Range(typeof(decimal), "0", "79228162514264337593543950335", ErrorMessage = "Sale price cannot be negative.")]
    public decimal? SalePrice { get; set; }

    // Null/blank means "not barcoded yet" - the column stays that way too;
    // see NormalizeBarcode. Same length as Sku: it is stored the same way
    // and needs the same bound for the same reason, a unique index.
    [StringLength(64, ErrorMessage = "Barcode cannot be longer than 64 characters.")]
    public string? Barcode { get; set; }

    // Defaults to false so a client that has never heard of archiving keeps
    // creating and editing items exactly as before. This is also how an item
    // gets archived and unarchived: through the same edit form as everything
    // else, rather than a separate endpoint for one boolean.
    public bool IsArchived { get; set; }

    // Opts the item into the lot-tracking path - see Item.TracksLots. False
    // by default for the same reason IsArchived is: a client that has never
    // heard of this field keeps creating and editing items exactly as before.
    public bool TracksLots { get; set; }
}

// None of these carry a PerformedBy: attribution comes from the caller's token,
// so there is deliberately no field for a client to set it with.
//
// The ranges below run before the action does, so a request that names nothing
// is answered with what to choose rather than with the result of looking it up:
// a missing id arrives as 0, and "Item with ID 0 not found" describes a search
// for a row nobody asked for. Ids are also the fields most likely to arrive as 0
// by accident - an unselected dropdown, a field a script forgot to fill.
public class ReceiveStockRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Choose an item to receive.")]
    public int ItemId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Choose the bin the stock is going into.")]
    public int WarehouseBinId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Quantity must be a whole number greater than zero.")]
    public int Quantity { get; set; }

    // Required only when the item tracks lots - see
    // InventoryController.ReceiveStock. Ignored otherwise, the same way an
    // older client that has never heard of lots keeps working: it simply
    // never sends this field.
    [StringLength(64, ErrorMessage = "Lot/batch number cannot be longer than 64 characters.")]
    public string? LotNumber { get; set; }

    // Only read the first time a given (item, lot number) pair is received -
    // see FindOrOpenLot. A later receive of the same lot does not get to
    // change when it expires.
    public DateTime? ExpirationDate { get; set; }
}

public class PickStockRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Choose an item to pick.")]
    public int ItemId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Choose the bin to pick from.")]
    public int WarehouseBinId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Quantity must be a whole number greater than zero.")]
    public int Quantity { get; set; }

    // Required only when the item tracks lots, to say which batch to pick
    // from - see InventoryController.PickStock.
    [StringLength(64, ErrorMessage = "Lot/batch number cannot be longer than 64 characters.")]
    public string? LotNumber { get; set; }
}

public class RelocateStockRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Choose an item to move.")]
    public int ItemId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Choose the bin the stock is coming from.")]
    public int SourceBinId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Choose the bin the stock is going to.")]
    public int DestinationBinId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Quantity must be a whole number greater than zero.")]
    public int Quantity { get; set; }

    // Required only when the item tracks lots, to say which batch is moving -
    // see InventoryController.RelocateStock.
    [StringLength(64, ErrorMessage = "Lot/batch number cannot be longer than 64 characters.")]
    public string? LotNumber { get; set; }
}