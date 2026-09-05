using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Inventria.Models;

namespace Inventria.Controllers;

// Every figure here is read off tables the rest of the app already writes -
// InventoryBalances and StockMovements - so this controller never touches
// SaveChanges. Same audience as Inventory and Bins: reports on the catalogue
// aren't an Admin secret, so no role restriction beyond being signed in.
[Authorize]
[Route("api/[controller]")]
[ApiController]
public class ReportsController : ControllerBase
{
    private readonly InventriaDbContext _context;

    public ReportsController(InventriaDbContext context)
    {
        _context = context;
    }

    // Same ceiling as InventoryController.GetAllItems, for the same reason: a
    // caller asking for page 0 or a page size of 5000 is asking for the
    // nearest sensible thing, not sending a malformed request.
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 200;

    private static (int page, int pageSize) ClampPaging(int page, int pageSize)
        => (Math.Max(page, 1), Math.Clamp(pageSize, 1, MaxPageSize));

    // "How many days back" ranges used by dead-stock and velocity. Zero or a
    // negative number would make the window mean nothing (or run backwards),
    // so it is clamped rather than rejected, the same way paging is above.
    // The upper end is a generous ten years - long enough that no real report
    // hits it, short enough that it still bounds the query.
    private const int MinLookbackDays = 1;
    private const int MaxLookbackDays = 3650;

    private static int ClampDays(int days) => Math.Clamp(days, MinLookbackDays, MaxLookbackDays);

    [HttpGet("stock-on-hand")]
    public IActionResult GetStockOnHand(
        [FromQuery] string? category,
        [FromQuery] string? zone,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize)
    {
        (page, pageSize) = ClampPaging(page, pageSize);

        var query = _context.InventoryBalances
            .Select(b => new
            {
                b.ItemId,
                b.Item!.Sku,
                b.Item.Name,
                b.Item.Category,
                b.WarehouseBinId,
                b.WarehouseBin!.Zone,
                b.WarehouseBin.Aisle,
                b.WarehouseBin.Shelf,
                b.Quantity
            });

        // Exact, case-insensitive match rather than a substring search: both
        // fields come from a fixed set of values a dropdown offers, the same
        // way the reports page's own category filter works today, not free text
        // someone is searching within.
        if (!string.IsNullOrWhiteSpace(category))
        {
            var trimmed = category.Trim();
            query = query.Where(b => b.Category.ToLower() == trimmed.ToLower());
        }

        if (!string.IsNullOrWhiteSpace(zone))
        {
            var trimmed = zone.Trim();
            query = query.Where(b => b.Zone.ToLower() == trimmed.ToLower());
        }

        // Grouped by item first so the report reads the way a picker walks a
        // report - one item, then everywhere it lives - rather than in
        // whatever order the balance table happens to store rows.
        query = query.OrderBy(b => b.Name).ThenBy(b => b.Zone).ThenBy(b => b.Aisle).ThenBy(b => b.Shelf);

        var totalCount = query.Count();
        var totalUnits = query.Sum(b => (int?)b.Quantity) ?? 0;

        var rows = query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Ok(new
        {
            Items = rows,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
            TotalUnits = totalUnits
        });
    }

    [HttpGet("movements")]
    public IActionResult GetMovements(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? type,
        [FromQuery] int? itemId,
        [FromQuery] string? performedBy,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize)
    {
        (page, pageSize) = ClampPaging(page, pageSize);

        var query = _context.StockMovements.AsQueryable();

        // `from`/`to` arrive with no offset, the same as every other DateTime
        // this app reads from a query string, and Timestamp's value converter
        // already treats an unspecified Kind as UTC - so no adjustment is
        // needed here to compare on the same clock the column is written with.
        if (from.HasValue) query = query.Where(m => m.Timestamp >= from.Value);
        if (to.HasValue) query = query.Where(m => m.Timestamp <= to.Value);

        // The stored values are the fixed set "RECEIVE"/"PICK"/"RELOCATE", so
        // this is an exact match once case is normalized, not a search.
        if (!string.IsNullOrWhiteSpace(type))
        {
            var normalized = type.Trim();
            query = query.Where(m => m.TransactionType.ToUpper() == normalized.ToUpper());
        }

        if (itemId.HasValue) query = query.Where(m => m.ItemId == itemId.Value);

        if (!string.IsNullOrWhiteSpace(performedBy))
        {
            var trimmed = performedBy.Trim();
            query = query.Where(m => m.PerformedBy.ToLower() == trimmed.ToLower());
        }

        // Newest first: this is an audit trail, and the reason someone opens
        // it is almost always "what just happened", not "what happened first".
        query = query.OrderByDescending(m => m.Timestamp).ThenByDescending(m => m.Id);

        var totalCount = query.Count();

        var rows = query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new
            {
                m.Id,
                m.ItemId,
                // A movement's foreign keys now refuse to let the row they
                // point at be deleted, but rows written before that
                // constraint existed can still name an Id that resolves to
                // nothing - the same gap DashboardController's recent-activity
                // feed papers over, and for the same reason.
                ItemName = _context.Items.Where(i => i.Id == m.ItemId).Select(i => i.Name).FirstOrDefault()
                    ?? $"deleted item #{m.ItemId}",
                Sku = _context.Items.Where(i => i.Id == m.ItemId).Select(i => i.Sku).FirstOrDefault(),
                m.WarehouseBinId,
                Zone = _context.WarehouseBins.Where(b => b.Id == m.WarehouseBinId).Select(b => b.Zone).FirstOrDefault(),
                Aisle = _context.WarehouseBins.Where(b => b.Id == m.WarehouseBinId).Select(b => b.Aisle).FirstOrDefault(),
                Shelf = _context.WarehouseBins.Where(b => b.Id == m.WarehouseBinId).Select(b => b.Shelf).FirstOrDefault(),
                m.TransactionType,
                m.QuantityChanged,
                m.Timestamp,
                m.PerformedBy
            })
            .ToList();

        return Ok(new
        {
            Items = rows,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    [HttpGet("dead-stock")]
    public IActionResult GetDeadStock(
        [FromQuery] int days = 90,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize)
    {
        (page, pageSize) = ClampPaging(page, pageSize);
        days = ClampDays(days);
        var cutoff = DateTime.UtcNow.AddDays(-days);

        var lastMovement = _context.StockMovements
            .GroupBy(m => m.ItemId)
            .Select(g => new { ItemId = g.Key, LastMovementAt = g.Max(m => m.Timestamp) });

        // Projected before it is filtered: EF Core can push a WHERE over these
        // plain columns down to SQL, but folding the filter into the join
        // itself - checking quantityOnHand and lm's nullability in the same
        // predicate - is a shape it refuses to translate.
        var projected =
            from item in _context.Items
            join lm in lastMovement on item.Id equals lm.ItemId into lmJoin
            from lm in lmJoin.DefaultIfEmpty()
            select new
            {
                item.Id,
                item.Sku,
                item.Name,
                item.Category,
                QuantityOnHand = _context.InventoryBalances
                    .Where(b => b.ItemId == item.Id)
                    .Sum(b => (int?)b.Quantity) ?? 0,
                LastMovementAt = (DateTime?)(lm == null ? null : lm.LastMovementAt)
            };

        // Dead stock is stock: an item sitting at zero has nothing on a shelf
        // to be money sitting on it, no matter how long it has gone unmoved,
        // so it is excluded rather than reported as "dead". An item that has
        // never moved at all counts as overdue too - it is treated as if its
        // last movement was infinitely long ago.
        var query = projected.Where(i => i.QuantityOnHand > 0 && (i.LastMovementAt == null || i.LastMovementAt < cutoff));

        // Oldest (and never-moved) first: that is the stock that has been
        // sitting the longest, which is the point of the report.
        query = query.OrderBy(i => i.LastMovementAt ?? DateTime.MinValue).ThenBy(i => i.Name);

        var totalCount = query.Count();

        var rows = query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Ok(new
        {
            Items = rows,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
            Days = days
        });
    }

    // What to buy, and how much of it - the report the other four could not
    // produce, because until Item carried a reorder point there was no level for
    // stock to be "below". Every rule about what counts as low and what to order
    // lives in LowStock, shared with the dashboards' tile, so the count on the
    // tile and the number of rows here are the same query.
    [HttpGet("reorder")]
    public IActionResult GetReorder(
        [FromQuery] string? category,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize)
    {
        (page, pageSize) = ClampPaging(page, pageSize);

        var query = LowStock.Ordered(_context);

        // Same exact, case-insensitive match the stock-on-hand report uses: the
        // value comes from a dropdown of the categories that exist, not from
        // someone searching within them.
        if (!string.IsNullOrWhiteSpace(category))
        {
            var trimmed = category.Trim();
            query = query.Where(line => line.Category.ToLower() == trimmed.ToLower());
        }

        var totalCount = query.Count();

        // Summed over everything the filters match rather than the page on
        // screen, because the question a buyer asks of this report - how much am
        // I about to order - is not a question about page one.
        var totalUnitsToOrder = query.Sum(line => (int?)line.SuggestedOrderQuantity) ?? 0;

        // Lines the report can put a quantity against. The two numbers differ
        // only for an item sitting exactly on its reorder point with no reorder
        // quantity recorded: it is genuinely low, and the data genuinely does not
        // say how much to buy, so it is listed and left out of the order rather
        // than given an invented figure.
        var orderableCount = query.Count(line => line.SuggestedOrderQuantity > 0);

        var rows = query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Ok(new
        {
            Items = rows,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
            TotalUnitsToOrder = totalUnitsToOrder,
            OrderableCount = orderableCount
        });
    }

    [HttpGet("velocity")]
    public IActionResult GetVelocity(
        [FromQuery] int days = 30,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize)
    {
        (page, pageSize) = ClampPaging(page, pageSize);
        days = ClampDays(days);
        var cutoff = DateTime.UtcNow.AddDays(-days);

        var movementsInWindow = _context.StockMovements.Where(m => m.Timestamp >= cutoff);

        // RECEIVE and PICK only, the same split GetEmployeeStats uses for its
        // "today" counters: a RELOCATE moves stock between bins without any
        // arriving or leaving the building, so counting its legs here would
        // read as demand that was never there.
        var unitsIn = movementsInWindow
            .Where(m => m.TransactionType == "RECEIVE")
            .GroupBy(m => m.ItemId)
            .Select(g => new { ItemId = g.Key, Units = g.Sum(m => m.QuantityChanged) });

        var unitsOut = movementsInWindow
            .Where(m => m.TransactionType == "PICK")
            .GroupBy(m => m.ItemId)
            .Select(g => new { ItemId = g.Key, Units = g.Sum(m => -m.QuantityChanged) });

        // Every item is listed, not just the ones that moved: an item with
        // zero units either way over the window is exactly the "stop buying
        // this" signal the report exists to surface, and it would be invisible
        // if only items with activity were included.
        var query = from item in _context.Items
                     select new
                     {
                         item.Id,
                         item.Sku,
                         item.Name,
                         item.Category,
                         UnitsIn = unitsIn.Where(x => x.ItemId == item.Id).Select(x => x.Units).FirstOrDefault(),
                         UnitsOut = unitsOut.Where(x => x.ItemId == item.Id).Select(x => x.Units).FirstOrDefault()
                     };

        // Fastest-moving first - that is the reorder list; the slow tail at
        // the end of the last page is the stop-buying list.
        query = query
            .OrderByDescending(i => i.UnitsIn + i.UnitsOut)
            .ThenBy(i => i.Name);

        var totalCount = query.Count();

        var rows = query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(i => new
            {
                i.Id,
                i.Sku,
                i.Name,
                i.Category,
                i.UnitsIn,
                i.UnitsOut,
                NetChange = i.UnitsIn - i.UnitsOut
            })
            .ToList();

        return Ok(new
        {
            Items = rows,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
            Days = days
        });
    }
}
