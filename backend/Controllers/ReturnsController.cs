using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Inventria.Models;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace Inventria.Controllers;

/// <summary>
/// Processing a returned unit - deliberately its own endpoint rather than a
/// call to POST /inventory/receive, because a return needs three things a
/// receive never asks for: why it came back, what condition it came back in,
/// and what happens to it next. That last one is the point: a receive always
/// adds to a sellable balance, but a return only does that when the decision
/// is Restock. Quarantine and Scrap are logged - see StockMovement - without
/// ever touching InventoryBalance/InventoryLotBalance, so a damaged unit
/// cannot rejoin sellable stock just because someone recorded that it came
/// back.
/// </summary>
[Authorize]
[Route("api/returns")]
[ApiController]
public class ReturnsController : ControllerBase
{
    private readonly InventriaDbContext _context;

    public ReturnsController(InventriaDbContext context)
    {
        _context = context;
    }

    private string CurrentUsername => User.FindFirstValue(ClaimTypes.Name)!;

    private static string? NormalizeLotNumber(string? lotNumber) =>
        string.IsNullOrWhiteSpace(lotNumber) ? null : lotNumber.Trim();

    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 200;

    [HttpGet]
    public IActionResult GetReturns(
        [FromQuery] string? disposition,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = _context.StockMovements.Where(m => m.TransactionType == "RETURN");

        if (!string.IsNullOrWhiteSpace(disposition)) query = query.Where(m => m.Disposition == disposition);

        query = query.OrderByDescending(m => m.Timestamp).ThenByDescending(m => m.Id);

        var totalCount = query.Count();

        var rows = query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new
            {
                m.Id,
                m.ItemId,
                // Same "deleted item" fallback as ReportsController.GetMovements -
                // a movement's item can outlive the item's own row.
                ItemName = _context.Items.Where(i => i.Id == m.ItemId).Select(i => i.Name).FirstOrDefault()
                    ?? $"deleted item #{m.ItemId}",
                m.WarehouseBinId,
                m.QuantityChanged,
                Reason = m.ReasonCode,
                m.ConditionGrade,
                m.Disposition,
                m.Timestamp,
                m.PerformedBy
            })
            .ToList();

        return Ok(new
        {
            Returns = rows,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    private const int MaxConcurrencyAttempts = 8;

    private static void PauseBeforeRetrying(int attempt) =>
        Thread.Sleep(Random.Shared.Next(4, 16) * attempt);

    [HttpPost]
    public IActionResult ProcessReturn([FromBody] ProcessReturnRequest request)
    {
        if (request.Quantity <= 0)
        {
            return BadRequest(new { Message = "Quantity must be greater than zero." });
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(new { Message = "A reason is required." });
        }

        if (request.Disposition is not (ReturnDisposition.Restock or ReturnDisposition.Quarantine or ReturnDisposition.Scrap))
        {
            return BadRequest(new { Message = $"'{request.Disposition}' is not a recognized decision. Choose Restock, Quarantine, or Scrap." });
        }

        if (request.ConditionGrade is not
            (ReturnConditionGrade.New or ReturnConditionGrade.Good or ReturnConditionGrade.Damaged or ReturnConditionGrade.Unsellable))
        {
            return BadRequest(new { Message = $"'{request.ConditionGrade}' is not a recognized condition grade." });
        }

        var item = _context.Items.Find(request.ItemId);
        if (item == null) return NotFound(new { Message = $"Item with ID {request.ItemId} not found." });

        if (item.IsArchived)
        {
            return Conflict(new { Message = $"'{item.Name}' is archived. Unarchive it before processing a return against it." });
        }

        var bin = _context.WarehouseBins.Find(request.WarehouseBinId);
        if (bin == null) return NotFound(new { Message = $"Warehouse Bin with ID {request.WarehouseBinId} not found." });

        var lotNumber = NormalizeLotNumber(request.LotNumber);
        if (item.TracksLots && lotNumber == null)
        {
            return BadRequest(new { Message = $"'{item.Name}' tracks lots. Give the lot/batch number this return belongs to." });
        }

        var reason = request.Reason.Trim();

        // Restock is the only decision that puts stock back where a pick can
        // reach it. This is the one difference between a return and a plain
        // receive: a receive always restocks, a return only does when someone
        // has actually said the goods are fit to sell again.
        var restocking = request.Disposition == ReturnDisposition.Restock;

        for (var attempt = 1; attempt <= MaxConcurrencyAttempts; attempt++)
        {
            try
            {
                // Set via the Lot navigation, not a LotId scalar read off
                // lot.Id up front - a freshly created lot has no Id until
                // SaveChanges runs, the same reason
                // StockReceivingService.FindOrOpenLot works this way.
                Lot? lotForMovement = null;
                int? newBalance = null;

                if (item.TracksLots)
                {
                    var lot = _context.Lots.FirstOrDefault(l => l.ItemId == item.Id && l.LotNumber == lotNumber);

                    if (restocking)
                    {
                        if (lot == null)
                        {
                            lot = new Lot { ItemId = item.Id, LotNumber = lotNumber!, ExpirationDate = request.ExpirationDate };
                            _context.Lots.Add(lot);
                        }

                        var lotBalance = lot.Id != 0
                            ? _context.InventoryLotBalances.FirstOrDefault(b =>
                                b.ItemId == item.Id && b.WarehouseBinId == request.WarehouseBinId && b.LotId == lot.Id)
                            : null;

                        if (lotBalance != null)
                        {
                            lotBalance.Quantity += request.Quantity;
                        }
                        else
                        {
                            lotBalance = new InventoryLotBalance
                            {
                                ItemId = item.Id, WarehouseBinId = request.WarehouseBinId, Quantity = request.Quantity, Lot = lot
                            };
                            _context.InventoryLotBalances.Add(lotBalance);
                        }

                        newBalance = lotBalance.Quantity;
                    }

                    // Quarantined or scrapped: no balance to touch, but the
                    // lot is still worth naming on the movement when it is
                    // one this item has seen before - a recall needs to know
                    // a damaged unit of LOT-2026-01 came back even though it
                    // never rejoined stock.
                    lotForMovement = lot;
                }
                else if (restocking)
                {
                    var balance = _context.InventoryBalances.FirstOrDefault(b =>
                        b.ItemId == item.Id && b.WarehouseBinId == request.WarehouseBinId);

                    if (balance != null)
                    {
                        balance.Quantity += request.Quantity;
                    }
                    else
                    {
                        balance = new InventoryBalance { ItemId = item.Id, WarehouseBinId = request.WarehouseBinId, Quantity = request.Quantity };
                        _context.InventoryBalances.Add(balance);
                    }

                    newBalance = balance.Quantity;
                }

                _context.StockMovements.Add(new StockMovement
                {
                    ItemId = item.Id,
                    WarehouseBinId = request.WarehouseBinId,
                    TransactionType = "RETURN",
                    QuantityChanged = restocking ? request.Quantity : 0,
                    Timestamp = DateTime.UtcNow,
                    PerformedBy = CurrentUsername,
                    Lot = lotForMovement,
                    ReasonCode = reason,
                    ConditionGrade = request.ConditionGrade,
                    Disposition = request.Disposition
                });

                _context.SaveChanges();

                return Ok(new
                {
                    Message = restocking
                        ? $"Returned {request.Quantity} unit(s) of '{item.Name}' and restocked them."
                        : $"Returned {request.Quantity} unit(s) of '{item.Name}' and sent them to {request.Disposition}.",
                    Restocked = restocking,
                    NewBalance = newBalance
                });
            }
            catch (DbUpdateConcurrencyException)
            {
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

public class ProcessReturnRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Choose an item.")]
    public int ItemId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Choose the bin to process this return in.")]
    public int WarehouseBinId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Quantity must be a whole number greater than zero.")]
    public int Quantity { get; set; }

    [NotBlank(ErrorMessage = "A reason is required.")]
    [StringLength(500, ErrorMessage = "Reason cannot be longer than 500 characters.")]
    public string Reason { get; set; } = string.Empty;

    // One of ReturnConditionGrade's values.
    [NotBlank(ErrorMessage = "A condition grade is required.")]
    public string ConditionGrade { get; set; } = string.Empty;

    // One of ReturnDisposition's values.
    [NotBlank(ErrorMessage = "A decision is required.")]
    public string Disposition { get; set; } = string.Empty;

    // Required only when the item tracks lots - same as ReceiveStockRequest.
    [StringLength(64, ErrorMessage = "Lot/batch number cannot be longer than 64 characters.")]
    public string? LotNumber { get; set; }

    // Only read the first time this (item, lot number) pair is seen, and only
    // when restocking opens a new lot - same as ReceiveStockRequest.
    public DateTime? ExpirationDate { get; set; }
}
