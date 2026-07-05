using Microsoft.AspNetCore.Mvc;
using Inventria.Models;

namespace Inventria.Controllers;

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
}

// Add this at the bottom of the file
public class ReceiveStockRequest
{
    public int ItemId { get; set; }
    public int WarehouseBinId { get; set; }
    public int Quantity { get; set; }
    public string PerformedBy { get; set; } = string.Empty;
}