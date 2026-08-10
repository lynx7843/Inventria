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
        // Ordered by address so the pickers that render this read like a walk
        // through the warehouse rather than like insert order.
        var bins = _context.WarehouseBins
            .OrderBy(b => b.Zone)
            .ThenBy(b => b.Aisle)
            .ThenBy(b => b.Shelf)
            .ToList();

        return Ok(bins);
    }

    [HttpPost]
    public IActionResult CreateBin([FromBody] WarehouseBinRequest request)
    {
        // Surrounding spaces are invisible in the UI but not to the unique
        // index, so "A1 " would be accepted as a second, indistinguishable A1.
        var bin = new WarehouseBin
        {
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
            return BadRequest(new { Message = $"Bin {Describe(bin)} already exists." });
        }

        return Ok(new { Message = "Bin created successfully.", Bin = bin });
    }

    [HttpPut("{id}")]
    public IActionResult UpdateBin(int id, [FromBody] WarehouseBinRequest request)
    {
        var bin = _context.WarehouseBins.Find(id);
        if (bin == null) return NotFound(new { Message = "Bin not found." });

        bin.Zone = request.Zone.Trim();
        bin.Aisle = request.Aisle.Trim();
        bin.Shelf = request.Shelf.Trim();

        try
        {
            _context.SaveChanges();
        }
        catch (DbUpdateException ex) when (UniqueConstraint.WasViolated(ex))
        {
            return BadRequest(new { Message = $"Bin {Describe(bin)} already exists." });
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
}

public class WarehouseBinRequest
{
    // The lengths are the widths of the columns, and the columns are bounded
    // because the unique index over all three needs them to be. Without the
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
