using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Inventria.Models;

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
        _context.SaveChanges();

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

        _context.SaveChanges();

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
            PerformedBy = request.PerformedBy
        };
        _context.StockMovements.Add(movement);

        // 6. Commit both changes to SQL Server simultaneously
        _context.SaveChanges();

        return Ok(new {
            Message = $"Successfully received {request.Quantity} units of {item.Name} into {bin.Zone}-{bin.Aisle}-{bin.Shelf}.",
            NewTotalBalance = balance.Quantity
        });
    }

    [HttpPost("pick")]
    public IActionResult PickStock([FromBody] PickStockRequest request)
    {
        if (request.Quantity <= 0)
        {
            return BadRequest(new { Message = "Quantity must be greater than zero." });
        }

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
            PerformedBy = request.PerformedBy
        };
        _context.StockMovements.Add(movement);

        _context.SaveChanges();

        return Ok(new { 
            Message = $"Successfully picked {request.Quantity} units from Bin {request.WarehouseBinId}.",
            RemainingBalance = balance.Quantity
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
            PerformedBy = request.PerformedBy
        };
        _context.StockMovements.Add(movement);

        _context.SaveChanges();

        return Ok(new { 
            Message = $"Successfully relocated {request.Quantity} units from Bin {request.SourceBinId} to Bin {request.DestinationBinId}." 
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

public class ReceiveStockRequest
{
    public int ItemId { get; set; }
    public int WarehouseBinId { get; set; }
    public int Quantity { get; set; }
    public string PerformedBy { get; set; } = string.Empty;
}

public class PickStockRequest
{
    public int ItemId { get; set; }
    public int WarehouseBinId { get; set; }
    public int Quantity { get; set; }
    public string PerformedBy { get; set; } = string.Empty;
}

public class RelocateStockRequest
{
    public int ItemId { get; set; }
    public int SourceBinId { get; set; }
    public int DestinationBinId { get; set; }
    public int Quantity { get; set; }
    public string PerformedBy { get; set; } = string.Empty;
}