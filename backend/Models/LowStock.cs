namespace Inventria.Models;

/// <summary>
/// An item that has fallen to or below the level it says it needs, with what
/// the shelves actually hold and what it would take to put that right.
/// </summary>
/// <remarks>
/// A projection, not an entity: nothing here is stored, and every field is read
/// off Items and InventoryBalances at the moment the question is asked. It has
/// no Id property for exactly that reason - ItemId names the row it came from
/// and nothing pretends this is a row of its own.
/// </remarks>
public class ReorderLine
{
    public int ItemId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;

    /// <summary>Units on the shelves for this item, summed across every bin.</summary>
    public int QuantityOnHand { get; set; }

    public int ReorderPoint { get; set; }
    public int ReorderQuantity { get; set; }

    /// <summary>
    /// How far under the line the item is. Zero for an item sitting exactly on
    /// its reorder point, which is still a reorder - the point is the level that
    /// triggers one, not the level below it.
    /// </summary>
    public int Shortfall { get; set; }

    /// <summary>
    /// How much to order. See <see cref="LowStock.Lines"/> for how it is worked
    /// out, and for the one case where it is zero.
    /// </summary>
    public int SuggestedOrderQuantity { get; set; }
}

/// <summary>
/// The one definition of "low on stock" this application has, so that the
/// dashboards' count, the reorder report, the suggested purchase order and the
/// warning a picker sees are all answering the same question.
/// </summary>
public static class LowStock
{
    /// <summary>
    /// Every item at or below its reorder point, with what to order for it.
    ///
    /// An item is low when it has a reorder point at all - zero means the item
    /// is not tracked and never raises an alert - and its total on-hand across
    /// every bin has fallen to that point or below it. At or below, not below:
    /// the reorder point is the level that triggers an order, so reaching it is
    /// the trigger, and waiting for stock to drop past it would place every
    /// order a unit late.
    ///
    /// The suggestion follows the textbook rule - when stock hits the point,
    /// order a lot - with the one extension a real shelf needs:
    ///
    ///   * ReorderQuantity set: order whole lots, as many as it takes to clear
    ///     the shortfall, and never fewer than one. An item 4 under its point
    ///     with a lot size of 50 gets 50; an item 120 under gets 150, because a
    ///     single lot would leave it under the line the moment it arrived.
    ///   * ReorderQuantity not set: top the item back up to its reorder point.
    ///     That is the only quantity the data supports, and for an item sitting
    ///     exactly on its point it comes out as zero - the item is flagged, and
    ///     the honest answer to "how much" is that nobody has recorded one.
    ///
    /// Everything is computed in the query rather than after it, so a caller can
    /// count, sum and page these without pulling the catalogue into memory to do
    /// it.
    /// </summary>
    public static IQueryable<ReorderLine> Lines(InventriaDbContext context)
    {
        // Two stages because the second one needs the first one's answer three
        // times over: QuantityOnHand is a subquery, and writing it inline in the
        // filter, the shortfall and the suggestion would repeat that subquery in
        // the generated SQL for each of them.
        var onHand = context.Items
            .Where(item => item.ReorderPoint > 0)
            .Select(item => new
            {
                item.Id,
                item.Sku,
                item.Name,
                item.Category,
                item.ReorderPoint,
                item.ReorderQuantity,
                QuantityOnHand = context.InventoryBalances
                    .Where(balance => balance.ItemId == item.Id)
                    .Sum(balance => (int?)balance.Quantity) ?? 0
            })
            .Where(item => item.QuantityOnHand <= item.ReorderPoint);

        return onHand.Select(item => new ReorderLine
        {
            ItemId = item.Id,
            Sku = item.Sku,
            Name = item.Name,
            Category = item.Category,
            QuantityOnHand = item.QuantityOnHand,
            ReorderPoint = item.ReorderPoint,
            ReorderQuantity = item.ReorderQuantity,
            Shortfall = item.ReorderPoint - item.QuantityOnHand,

            // Integer arithmetic on purpose: `(shortfall + lot - 1) / lot` is
            // how many lots cover the shortfall, rounded up, in a form both SQL
            // Server and SQLite evaluate the same way. A shortfall of zero
            // rounds to zero lots, which is why one lot is the floor.
            SuggestedOrderQuantity = item.ReorderQuantity > 0
                ? (item.ReorderPoint - item.QuantityOnHand > 0
                    ? (item.ReorderPoint - item.QuantityOnHand + item.ReorderQuantity - 1) / item.ReorderQuantity
                    : 1) * item.ReorderQuantity
                : item.ReorderPoint - item.QuantityOnHand
        });
    }

    /// <summary>
    /// The same lines, in the order a buyer works down them.
    /// </summary>
    public static IQueryable<ReorderLine> Ordered(InventriaDbContext context) =>
        Lines(context)
            // Most urgent first, and urgency is how much of the required level is
            // actually there rather than how many units are missing. An item at 0
            // of 5 is a stockout that stops orders going out; an item at 450 of
            // 500 has weeks of cover. Sorting by the shortfall alone would put
            // the second one first purely because its numbers are bigger.
            .OrderBy(line => (double)line.QuantityOnHand / line.ReorderPoint)
            .ThenByDescending(line => line.Shortfall)
            .ThenBy(line => line.Name);
}
