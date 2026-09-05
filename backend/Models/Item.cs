namespace Inventria.Models;

public class Item
{
    public int Id { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;

    // The level at which this item needs reordering, and how much to order when
    // it does. Without these an inventory system can only say what is on the
    // shelves; with them it can say what to do about it, which is what the
    // low-stock tile, the reorder report and the suggested purchase order are
    // all reading.
    //
    // Zero means "not tracked", and it is the default for a reason: every item
    // that existed before this column did carries it, and none of them should
    // start raising alerts against a level nobody chose. An item only enters the
    // reorder machinery once someone sets a point for it.
    //
    // ReorderQuantity is the lot size - the amount a supplier ships in, or the
    // amount worth ordering at once. Left at zero, a suggestion just tops the
    // item back up to its reorder point; set, suggestions come in whole
    // multiples of it, because a supplier who ships in boxes of 50 will not ship
    // 37.
    public int ReorderPoint { get; set; }
    public int ReorderQuantity { get; set; }
}
