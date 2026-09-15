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

    public InventoryController(InventriaDbContext context)
    {
        _context = context;
    }

    // The only trustworthy answer to "who did this" is the signed token. Taking
    // it from the request body let any authenticated caller stamp a colleague's
    // name on a movement, which is the one field the audit log rests on.
    // [Authorize] guarantees an authenticated principal, so the claim is present.
    private string CurrentUsername => User.FindFirstValue(ClaimTypes.Name)!;

    // The account behind the token, for reading its notification preferences.
    // Same claim UserProfileController resolves the caller from, and [Authorize]
    // guarantees it is present.
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>
    /// The sentence to hand back when a pick has just taken an item to or below
    /// its reorder point, or null when it has not - or when the person who did
    /// the picking has turned low-stock alerts off.
    /// </summary>
    /// <remarks>
    /// This is where a low-stock alert is worth delivering: the moment stock
    /// falls, to the person who just made it fall, while they are still standing
    /// at the shelf. A receive can only push an item further from its point and a
    /// relocation moves units between bins without changing the total, so neither
    /// can start an alert that this has not already raised.
    ///
    /// It reads the item's total across every bin rather than the bin just picked
    /// from, because a reorder point is a question about the warehouse, not about
    /// one shelf: an item with 2 left in this bin and 400 in the next one does
    /// not need buying.
    /// </remarks>
    private string? LowStockNoticeFor(int itemId)
    {
        // The preference lives on the account row. A token naming an id with no
        // row behind it is not a reason to withhold a warning about stock, so the
        // default the User model declares - on - applies.
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

    // How many times a stock move re-runs after losing a race. Conflicts only
    // happen when two requests touch the same item/bin at the same moment; past
    // this many losses the bin is hot enough that the caller should be told to
    // try again rather than kept waiting.
    //
    // Four was not enough in practice. Eight scanners receiving into one bin at
    // once had two of them give up and answer 409 - not a lost unit, the ledger
    // stayed exact, but a person told to do their job again for no reason they
    // can see. The losers of each round all retried on the same instant and
    // collided again, so the pause below matters more than the count does.
    private const int MaxConcurrencyAttempts = 8;

    // Runs a stock move that reads balances and then saves them, retrying from
    // scratch if another request changed the same rows in between.
    //
    // Each attempt must re-read the balances it depends on, which is why the
    // change tracker is cleared between attempts: a retry has to see the winner's
    // new quantity and re-run its own "enough stock?" check against it, and it
    // has to drop the StockMovement the failed attempt had queued up. A single
    // SaveChanges is already one transaction, so a failed attempt writes nothing.
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

    // Everyone who lost the same round is holding the same stale read and is
    // ready to retry at the same moment, so retrying immediately reproduces the
    // pile-up that caused the loss. Waiting a random few milliseconds, growing
    // with each attempt, is what breaks the tie - without it, extra attempts
    // mostly buy extra collisions. The numbers are small because the work being
    // retried is one short transaction, not because they were measured.
    private static void PauseBeforeRetrying(int attempt) =>
        Thread.Sleep(Random.Shared.Next(4, 16) * attempt);

    // What a caller gets when it asks for a page without saying how big, and the
    // most it can ask for in one go. The ceiling is the point of the exercise: a
    // catalogue grows without anyone deciding it should, and this used to answer
    // with all of it - every row, each carrying its own balance lookup - which
    // gets slower for every item the warehouse has ever stocked.
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 200;

    [HttpGet]
    public IActionResult GetAllItems([FromQuery] int page = 1, [FromQuery] int pageSize = DefaultPageSize)
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
        var query = _context.Items.OrderBy(i => i.Name).ThenBy(i => i.Id);

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
                QuantityOnHand = _context.InventoryBalances
                    .Where(b => b.ItemId == i.Id)
                    .Sum(b => (int?)b.Quantity) ?? 0
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
            Barcode = NormalizeBarcode(request.Barcode)
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

        // 3. Verify the destination warehouse bin exists
        var bin = _context.WarehouseBins.Find(request.WarehouseBinId);
        if (bin == null)
        {
            return NotFound(new { Message = $"Warehouse Bin with ID {request.WarehouseBinId} not found." });
        }

        return ExecuteStockMove(() =>
        {
            // 4. Update or Create the Inventory Balance
            var balance = _context.InventoryBalances
                .FirstOrDefault(b => b.ItemId == request.ItemId && b.WarehouseBinId == request.WarehouseBinId);

            if (balance != null)
            {
                // If the item is already in this bin, just add to the existing quantity
                balance.Quantity += request.Quantity;
            }
            else
            {
                // If this is the first time this item is placed in this bin, create a new record
                balance = new InventoryBalance
                {
                    ItemId = request.ItemId,
                    WarehouseBinId = request.WarehouseBinId,
                    Quantity = request.Quantity
                };
                _context.InventoryBalances.Add(balance);
            }

            // 5. Log the Stock Movement for auditing
            var movement = new StockMovement
            {
                ItemId = request.ItemId,
                WarehouseBinId = request.WarehouseBinId,
                TransactionType = "RECEIVE",
                QuantityChanged = request.Quantity,
                Timestamp = DateTime.UtcNow,
                PerformedBy = CurrentUsername
            };
            _context.StockMovements.Add(movement);

            // 6. Commit both changes to SQL Server simultaneously
            _context.SaveChanges();

            return Ok(new {
                Message = $"Successfully received {request.Quantity} units of {item.Name} into {bin.Zone}-{bin.Aisle}-{bin.Shelf}.",
                NewTotalBalance = balance.Quantity
            });
        });
    }

    [HttpPost("pick")]
    public IActionResult PickStock([FromBody] PickStockRequest request)
    {
        if (request.Quantity <= 0)
        {
            return BadRequest(new { Message = "Quantity must be greater than zero." });
        }

        return ExecuteStockMove(() =>
        {
            // Check if the inventory balance record exists for this item in this specific bin
            var balance = _context.InventoryBalances
                .FirstOrDefault(b => b.ItemId == request.ItemId && b.WarehouseBinId == request.WarehouseBinId);

            if (balance == null || balance.Quantity < request.Quantity)
            {
                return BadRequest(new { Message = "Insufficient stock available in the specified bin to fulfill this pick." });
            }

            // Deduct the inventory
            balance.Quantity -= request.Quantity;

            // If the bin hits exactly 0, we can choose to remove the row or leave it at 0. Let's keep it to preserve tracking history.

            // Log the movement as a "PICK"
            var movement = new StockMovement
            {
                ItemId = request.ItemId,
                WarehouseBinId = request.WarehouseBinId,
                TransactionType = "PICK",
                QuantityChanged = -request.Quantity, // Negative value signifies stock reduction
                Timestamp = DateTime.UtcNow,
                PerformedBy = CurrentUsername
            };
            _context.StockMovements.Add(movement);

            _context.SaveChanges();

            // Read after the save, so it describes the shelf as it now stands
            // rather than as it stood before the units left it.
            return Ok(new {
                Message = $"Successfully picked {request.Quantity} units from Bin {request.WarehouseBinId}.",
                RemainingBalance = balance.Quantity,
                LowStockWarning = LowStockNoticeFor(request.ItemId)
            });
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

        return ExecuteStockMove(() =>
        {
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
            var timestamp = DateTime.UtcNow;

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
}

public class PickStockRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Choose an item to pick.")]
    public int ItemId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Choose the bin to pick from.")]
    public int WarehouseBinId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Quantity must be a whole number greater than zero.")]
    public int Quantity { get; set; }
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
}