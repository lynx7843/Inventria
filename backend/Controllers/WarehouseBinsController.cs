using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Inventria.Models;
using System.ComponentModel.DataAnnotations;

namespace Inventria.Controllers;

// Bins are where every stock movement lands, so receiving needs one to exist
// before it can do anything. Same audience as the items they hold: anyone signed
// in maintains the warehouse map, only user administration is Admin-only.
[Authorize]
[Route("api/[controller]")]
[ApiController]
public class WarehouseBinsController : ControllerBase
{
    private readonly InventriaDbContext _context;

    public WarehouseBinsController(InventriaDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public IActionResult GetBins()
    {
        // Ordered by warehouse first, then by address within it, so the
        // pickers that render this read like a walk through one building at
        // a time rather than like insert order.
        var bins = _context.WarehouseBins
            .OrderBy(b => b.WarehouseId)
            .ThenBy(b => b.Zone)
            .ThenBy(b => b.Aisle)
            .ThenBy(b => b.Shelf)
            .ToList();

        return Ok(bins);
    }

    [HttpPost]
    public IActionResult CreateBin([FromBody] WarehouseBinRequest request)
    {
        var (warehouseId, error) = ResolveWarehouseId(request.WarehouseId);
        if (error != null) return error;

        // Surrounding spaces are invisible in the UI but not to the unique
        // index, so "A1 " would be accepted as a second, indistinguishable A1.
        var bin = new WarehouseBin
        {
            WarehouseId = warehouseId,
            Zone = request.Zone.Trim(),
            Aisle = request.Aisle.Trim(),
            Shelf = request.Shelf.Trim()
        };

        _context.WarehouseBins.Add(bin);

        try
        {
            _context.SaveChanges();
        }
        catch (DbUpdateException ex) when (UniqueConstraint.WasViolated(ex))
        {
            return BadRequest(new { Message = $"Bin {Describe(bin)} already exists in that warehouse." });
        }

        return Ok(new { Message = "Bin created successfully.", Bin = bin });
    }

    [HttpPut("{id}")]
    public IActionResult UpdateBin(int id, [FromBody] WarehouseBinRequest request)
    {
        var bin = _context.WarehouseBins.Find(id);
        if (bin == null) return NotFound(new { Message = "Bin not found." });

        var (warehouseId, error) = ResolveWarehouseId(request.WarehouseId);
        if (error != null) return error;

        bin.WarehouseId = warehouseId;
        bin.Zone = request.Zone.Trim();
        bin.Aisle = request.Aisle.Trim();
        bin.Shelf = request.Shelf.Trim();

        try
        {
            _context.SaveChanges();
        }
        catch (DbUpdateException ex) when (UniqueConstraint.WasViolated(ex))
        {
            return BadRequest(new { Message = $"Bin {Describe(bin)} already exists in that warehouse." });
        }

        return Ok(new { Message = "Bin updated successfully." });
    }

    [HttpDelete("{id}")]
    public IActionResult DeleteBin(int id)
    {
        var bin = _context.WarehouseBins.Find(id);
        if (bin == null) return NotFound(new { Message = "Bin not found." });

        // The foreign keys are what actually refuse a delete that would strand
        // stock or audit history; these two checks exist to say which of them is
        // in the way instead of returning a bare constraint failure.
        var quantities = _context.InventoryBalances
            .Where(b => b.WarehouseBinId == id && b.Quantity != 0)
            .Select(b => b.Quantity)
            .ToList();

        if (quantities.Count > 0)
        {
            return Conflict(new { Message = $"Bin {Describe(bin)} holds {quantities.Sum()} units across {quantities.Count} item(s). Move or pick the stock out before deleting it." });
        }

        var lotQuantities = _context.InventoryLotBalances
            .Where(b => b.WarehouseBinId == id && b.Quantity != 0)
            .Select(b => b.Quantity)
            .ToList();

        if (lotQuantities.Count > 0)
        {
            return Conflict(new { Message = $"Bin {Describe(bin)} holds {lotQuantities.Sum()} units across {lotQuantities.Count} lot(s). Move or pick the stock out before deleting it." });
        }

        var movementCount = _context.StockMovements.Count(m => m.WarehouseBinId == id);
        if (movementCount > 0)
        {
            return Conflict(new { Message = $"Bin {Describe(bin)} has {movementCount} recorded stock movement(s). Deleting it would destroy that audit history." });
        }

        _context.WarehouseBins.Remove(bin);

        try
        {
            _context.SaveChanges();
        }
        catch (DbUpdateException ex) when (ForeignKeyConstraint.WasViolated(ex))
        {
            // Stock landed in this bin between the checks above and the delete.
            return Conflict(new { Message = $"Bin {Describe(bin)} has just been used in a stock movement and can no longer be deleted." });
        }

        return Ok(new { Message = "Bin deleted successfully." });
    }

    // The address as people say it, and as the stock movement messages print it.
    private static string Describe(WarehouseBin bin) => $"{bin.Zone}-{bin.Aisle}-{bin.Shelf}";

    // Zero means the caller did not name a warehouse - an older client, or
    // one that has never needed to think about more than one building - so
    // it resolves to whichever warehouse was created first, which for every
    // installation that has not deliberately added a second site is the one
    // "Main Warehouse" the AddWarehouse migration seeded. A non-zero id is
    // checked against the table rather than trusted, the same way every
    // other foreign key on this controller's requests is.
    private (int WarehouseId, IActionResult? Error) ResolveWarehouseId(int requestedWarehouseId)
    {
        if (requestedWarehouseId == 0)
        {
            var defaultWarehouseId = _context.Warehouses.OrderBy(w => w.Id).Select(w => w.Id).FirstOrDefault();
            return defaultWarehouseId == 0
                ? (0, NotFound(new { Message = "No warehouse exists yet." }))
                : (defaultWarehouseId, null);
        }

        if (!_context.Warehouses.Any(w => w.Id == requestedWarehouseId))
        {
            return (0, NotFound(new { Message = $"Warehouse with ID {requestedWarehouseId} not found." }));
        }

        return (requestedWarehouseId, null);
    }
}

public class WarehouseBinRequest
{
    // Zero, the default, means "not specified" rather than an error - see
    // WarehouseBinsController.ResolveWarehouseId. A single-site install only
    // ever has the one warehouse the AddWarehouse migration seeded, and a
    // client that has never heard of warehouses (which is every client
    // today - see AddWarehouse) should keep creating bins exactly as before
    // rather than being made to look one up and send its id first.
    public int WarehouseId { get; set; }

    // The lengths are the widths of the columns, and the columns are bounded
    // because the unique index over all four needs them to be. Without the
    // limits an over-long value is a truncation error from SQL Server, which
    // reaches the caller as a 500 rather than as "that is too long".
    [NotBlank(ErrorMessage = "Zone is required.")]
    [StringLength(64, ErrorMessage = "Zone cannot be longer than 64 characters.")]
    public string Zone { get; set; } = string.Empty;

    [NotBlank(ErrorMessage = "Aisle is required.")]
    [StringLength(32, ErrorMessage = "Aisle cannot be longer than 32 characters.")]
    public string Aisle { get; set; } = string.Empty;

    [NotBlank(ErrorMessage = "Shelf is required.")]
    [StringLength(32, ErrorMessage = "Shelf cannot be longer than 32 characters.")]
    public string Shelf { get; set; } = string.Empty;
}
