using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Inventria.Models;
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

    // How many times a stock move re-runs after losing a race. Conflicts only
    // happen when two requests touch the same item/bin at the same moment, so a
    // handful of attempts is plenty; past that the bin is hot enough that the
    // caller should be told to try again rather than kept waiting.
    private const int MaxConcurrencyAttempts = 4;

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
            }
            catch (DbUpdateException ex) when (UniqueConstraint.WasViolated(ex))
            {
                // Someone else created the balance row for this item/bin between
                // our lookup and our insert. A stock move writes no other row that
                // a unique index covers, so this is always that race and is always
                // safe to retry - the retry will find their row.
                _context.ChangeTracker.Clear();
            }
        }

        return Conflict(new { Message = "This stock is being updated by another request. Please try again." });
    }

    [HttpGet]
    public IActionResult GetAllItems()
    {
        // Fetches all items from the SQL Server database
        var items = _context.Items.ToList();
        return Ok(items);
    }

    // --- MASTER ITEM CRUD OPERATIONS ---

    [HttpPost("items")]
    public IActionResult CreateItem([FromBody] ItemRequest request)
    {
        var newItem = new Item
        {
            Sku = request.Sku,
            Name = request.Name,
            Category = request.Category
        };

        _context.Items.Add(newItem);

        try
        {
            _context.SaveChanges();
        }
        catch (DbUpdateException ex) when (UniqueConstraint.WasViolated(ex))
        {
            return BadRequest(new { Message = $"SKU '{request.Sku}' is already used by another item." });
        }

        return Ok(new { Message = "Item created successfully.", Item = newItem });
    }

    [HttpPut("items/{id}")]
    public IActionResult UpdateItem(int id, [FromBody] ItemRequest request)
    {
        var item = _context.Items.Find(id);
        if (item == null) return NotFound(new { Message = "Item not found." });

        item.Sku = request.Sku;
        item.Name = request.Name;
        item.Category = request.Category;

        try
        {
            _context.SaveChanges();
        }
        catch (DbUpdateException ex) when (UniqueConstraint.WasViolated(ex))
        {
            return BadRequest(new { Message = $"SKU '{request.Sku}' is already used by another item." });
        }

        return Ok(new { Message = "Item updated successfully." });
    }

    [HttpDelete("items/{id}")]
    public IActionResult DeleteItem(int id)
    {
        var item = _context.Items.Find(id);
        if (item == null) return NotFound(new { Message = "Item not found." });

        // Note: In a production system, you might want to prevent deletion if the item 
        // has existing InventoryBalances, or use a "IsActive" flag instead of hard deletion.
        _context.Items.Remove(item);
        _context.SaveChanges();

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

            return Ok(new {
                Message = $"Successfully picked {request.Quantity} units from Bin {request.WarehouseBinId}.",
                RemainingBalance = balance.Quantity
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

            // Log the movement as a "RELOCATE"
            var movement = new StockMovement
            {
                ItemId = request.ItemId,
                WarehouseBinId = request.SourceBinId, // Log where it started
                TransactionType = "RELOCATE",
                QuantityChanged = request.Quantity,
                Timestamp = DateTime.UtcNow,
                PerformedBy = CurrentUsername
            };
            _context.StockMovements.Add(movement);

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
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
}

// None of these carry a PerformedBy: attribution comes from the caller's token,
// so there is deliberately no field for a client to set it with.
public class ReceiveStockRequest
{
    public int ItemId { get; set; }
    public int WarehouseBinId { get; set; }
    public int Quantity { get; set; }
}

public class PickStockRequest
{
    public int ItemId { get; set; }
    public int WarehouseBinId { get; set; }
    public int Quantity { get; set; }
}

public class RelocateStockRequest
{
    public int ItemId { get; set; }
    public int SourceBinId { get; set; }
    public int DestinationBinId { get; set; }
    public int Quantity { get; set; }
}