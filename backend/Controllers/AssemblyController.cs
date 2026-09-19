using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Inventria.Models;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace Inventria.Controllers;

/// <summary>
/// Assembling components into a finished product - one hammer consumes one
/// handle and one head. A bill of materials says the ratio; BuildAssembly is
/// the one write that consumes every component and produces the finished
/// item in the same bin, atomically: a build that would leave any component
/// short takes nothing at all, rather than half-consuming parts for a
/// finished unit that never gets made.
///
/// Deliberately scoped to plain, non-lot-tracked items on both sides - a
/// lot-tracked component would need the build to say which batch each part
/// came from, and a lot-tracked finished good would need one assigned to what
/// it produces. Neither is here yet.
/// </summary>
[Authorize]
[Route("api/assembly")]
[ApiController]
public class AssemblyController : ControllerBase
{
    private readonly InventriaDbContext _context;

    public AssemblyController(InventriaDbContext context)
    {
        _context = context;
    }

    private string CurrentUsername => User.FindFirstValue(ClaimTypes.Name)!;

    private static object ComponentSummary(BillOfMaterialLine line) => new
    {
        line.ComponentItemId,
        ComponentName = line.ComponentItem?.Name,
        ComponentSku = line.ComponentItem?.Sku,
        line.QuantityRequired
    };

    private static object BomDetail(BillOfMaterials bom) => new
    {
        bom.ItemId,
        ItemName = bom.Item?.Name,
        ItemSku = bom.Item?.Sku,
        Components = bom.Components.Select(ComponentSummary)
    };

    private IQueryable<BillOfMaterials> DetailQuery() => _context.BillsOfMaterials
        .Include(b => b.Item)
        .Include(b => b.Components).ThenInclude(c => c.ComponentItem);

    [HttpGet("boms")]
    public IActionResult GetBoms()
    {
        var boms = DetailQuery().OrderBy(b => b.Item!.Name).ToList();

        return Ok(new { Boms = boms.Select(BomDetail) });
    }

    [HttpGet("boms/{itemId}")]
    public IActionResult GetBom(int itemId)
    {
        var bom = DetailQuery().FirstOrDefault(b => b.ItemId == itemId);
        if (bom == null) return NotFound(new { Message = "No bill of materials is defined for this item." });

        return Ok(BomDetail(bom));
    }

    // Full replacement, the same shape PurchaseOrdersController.UpdateOrder
    // takes for lines: editing a bill of materials is indistinguishable from
    // not having defined it yet, so there is one endpoint for "define" and
    // "change" rather than two.
    [HttpPut("boms/{itemId}")]
    public IActionResult SetBom(int itemId, [FromBody] SetBillOfMaterialsRequest request)
    {
        var item = _context.Items.Find(itemId);
        if (item == null) return NotFound(new { Message = $"Item with ID {itemId} not found." });

        if (item.TracksLots)
        {
            return BadRequest(new { Message = $"'{item.Name}' tracks lots. Assembly does not support lot-tracked finished items yet." });
        }

        var componentIds = request.Components.Select(c => c.ComponentItemId).ToList();

        if (componentIds.Contains(itemId))
        {
            return BadRequest(new { Message = $"'{item.Name}' cannot be a component of itself." });
        }

        if (componentIds.Count != componentIds.Distinct().Count())
        {
            return BadRequest(new { Message = "Each component can only appear once on a bill of materials." });
        }

        var components = _context.Items.Where(i => componentIds.Contains(i.Id)).ToDictionary(i => i.Id);
        var missingId = componentIds.FirstOrDefault(id => !components.ContainsKey(id));
        if (missingId != 0) return NotFound(new { Message = $"Item with ID {missingId} not found." });

        var lotTrackedComponent = components.Values.FirstOrDefault(i => i.TracksLots);
        if (lotTrackedComponent != null)
        {
            return BadRequest(new { Message = $"'{lotTrackedComponent.Name}' tracks lots. Assembly does not support lot-tracked components yet." });
        }

        var bom = _context.BillsOfMaterials.Include(b => b.Components).FirstOrDefault(b => b.ItemId == itemId);

        if (bom == null)
        {
            bom = new BillOfMaterials { ItemId = itemId };
            _context.BillsOfMaterials.Add(bom);
        }
        else
        {
            _context.BillOfMaterialLines.RemoveRange(bom.Components);
        }

        bom.Components = request.Components.Select(c => new BillOfMaterialLine
        {
            ComponentItemId = c.ComponentItemId,
            QuantityRequired = c.QuantityRequired
        }).ToList();

        _context.SaveChanges();

        return Ok(new { Message = "Bill of materials saved.", Bom = BomDetail(bom) });
    }

    [HttpDelete("boms/{itemId}")]
    public IActionResult DeleteBom(int itemId)
    {
        var bom = _context.BillsOfMaterials.FirstOrDefault(b => b.ItemId == itemId);
        if (bom == null) return NotFound(new { Message = "No bill of materials is defined for this item." });

        _context.BillsOfMaterials.Remove(bom);
        _context.SaveChanges();

        return Ok(new { Message = "Bill of materials removed." });
    }

    private const int MaxConcurrencyAttempts = 8;

    private static void PauseBeforeRetrying(int attempt) =>
        Thread.Sleep(Random.Shared.Next(4, 16) * attempt);

    [HttpPost("build")]
    public IActionResult BuildAssembly([FromBody] BuildAssemblyRequest request)
    {
        if (request.Quantity <= 0)
        {
            return BadRequest(new { Message = "Quantity must be greater than zero." });
        }

        var item = _context.Items.Find(request.ItemId);
        if (item == null) return NotFound(new { Message = $"Item with ID {request.ItemId} not found." });

        if (item.IsArchived)
        {
            return Conflict(new { Message = $"'{item.Name}' is archived and cannot be assembled. Unarchive it first." });
        }

        var bin = _context.WarehouseBins.Find(request.WarehouseBinId);
        if (bin == null) return NotFound(new { Message = $"Warehouse Bin with ID {request.WarehouseBinId} not found." });

        for (var attempt = 1; attempt <= MaxConcurrencyAttempts; attempt++)
        {
            try
            {
                var bom = _context.BillsOfMaterials
                    .Include(b => b.Components).ThenInclude(c => c.ComponentItem)
                    .FirstOrDefault(b => b.ItemId == request.ItemId);

                if (bom == null || bom.Components.Count == 0)
                {
                    return BadRequest(new { Message = $"'{item.Name}' has no bill of materials defined." });
                }

                // Every component is checked before any of them is touched -
                // a build is all-or-nothing, not half a hammer's worth of
                // parts consumed for a hammer that was never produced.
                var shortages = new List<string>();
                var balances = new Dictionary<int, InventoryBalance>();

                foreach (var component in bom.Components)
                {
                    var required = component.QuantityRequired * request.Quantity;
                    var balance = _context.InventoryBalances.FirstOrDefault(b =>
                        b.ItemId == component.ComponentItemId && b.WarehouseBinId == request.WarehouseBinId);

                    if (balance == null || balance.Quantity < required)
                    {
                        shortages.Add($"{component.ComponentItem!.Name} (needs {required}, has {balance?.Quantity ?? 0})");
                    }
                    else
                    {
                        balances[component.ComponentItemId] = balance;
                    }
                }

                if (shortages.Count > 0)
                {
                    return BadRequest(new
                    {
                        Message = $"Not enough stock in Bin {request.WarehouseBinId} to build {request.Quantity} unit(s) of '{item.Name}': {string.Join(", ", shortages)}."
                    });
                }

                foreach (var component in bom.Components)
                {
                    var required = component.QuantityRequired * request.Quantity;
                    balances[component.ComponentItemId].Quantity -= required;

                    _context.StockMovements.Add(new StockMovement
                    {
                        ItemId = component.ComponentItemId,
                        WarehouseBinId = request.WarehouseBinId,
                        TransactionType = "ASSEMBLE",
                        QuantityChanged = -required,
                        Timestamp = DateTime.UtcNow,
                        PerformedBy = CurrentUsername
                    });
                }

                var producedBalance = _context.InventoryBalances.FirstOrDefault(b =>
                    b.ItemId == request.ItemId && b.WarehouseBinId == request.WarehouseBinId);

                if (producedBalance != null)
                {
                    producedBalance.Quantity += request.Quantity;
                }
                else
                {
                    producedBalance = new InventoryBalance
                    {
                        ItemId = request.ItemId,
                        WarehouseBinId = request.WarehouseBinId,
                        Quantity = request.Quantity
                    };
                    _context.InventoryBalances.Add(producedBalance);
                }

                _context.StockMovements.Add(new StockMovement
                {
                    ItemId = request.ItemId,
                    WarehouseBinId = request.WarehouseBinId,
                    TransactionType = "ASSEMBLE",
                    QuantityChanged = request.Quantity,
                    Timestamp = DateTime.UtcNow,
                    PerformedBy = CurrentUsername
                });

                _context.SaveChanges();

                return Ok(new
                {
                    Message = $"Built {request.Quantity} unit(s) of '{item.Name}'.",
                    NewFinishedBalance = producedBalance.Quantity
                });
            }
            catch (DbUpdateConcurrencyException)
            {
                // A balance row we read (a component's or the finished item's)
                // has been updated since; retrying re-reads everything fresh,
                // including the BOM, rather than trusting anything from this
                // attempt.
                _context.ChangeTracker.Clear();
                PauseBeforeRetrying(attempt);
            }
            catch (DbUpdateException ex) when (UniqueConstraint.WasViolated(ex))
            {
                _context.ChangeTracker.Clear();
                PauseBeforeRetrying(attempt);
            }
        }

        return Conflict(new { Message = "This stock is being updated by another request. Please try again." });
    }
}

public class BillOfMaterialComponentRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Choose a component item.")]
    public int ComponentItemId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Quantity required must be a whole number greater than zero.")]
    public int QuantityRequired { get; set; }
}

public class SetBillOfMaterialsRequest
{
    [MinLength(1, ErrorMessage = "A bill of materials needs at least one component.")]
    public List<BillOfMaterialComponentRequest> Components { get; set; } = [];
}

public class BuildAssemblyRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Choose the finished item to build.")]
    public int ItemId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Choose the bin to build in.")]
    public int WarehouseBinId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Quantity must be a whole number greater than zero.")]
    public int Quantity { get; set; }
}
