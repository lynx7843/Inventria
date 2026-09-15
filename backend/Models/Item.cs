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

    // What one unit of Quantity actually is - "12" means nothing on its own,
    // and "12 boxes" is a different amount of stock than "12 screws". Defaults
    // to "unit" so every item that existed before this column did carries an
    // answer, just an uninformative one, rather than a blank someone has to
    // notice and fill in. Display-only for now: it does not yet convert
    // anything, it just says what the number on screen is counting.
    public string UnitOfMeasure { get; set; } = "unit";

    // How many units make up one pack/case, for items received or shipped by
    // the case rather than the each - e.g. UnitOfMeasure "case", UnitsPerPack
    // 24. Null means the item has no such grouping, which is most of them.
    public int? UnitsPerPack { get; set; }

    // The current cost of one unit, for turning "how many" into "how much".
    // Nullable because an item can be catalogued before anyone has priced it,
    // and a missing cost should leave it out of the total rather than count it
    // as free. A single current cost, not a cost per movement - see
    // InventriaDbContext for why, and what moving-average or FIFO costing
    // would need on top of this.
    public decimal? UnitCost { get; set; }

    // What the item sells for. Not used in valuation - that is cost, not
    // price - but priced alongside cost since they are set together.
    public decimal? SalePrice { get; set; }

    // The code printed on the item's label. Null until someone assigns one -
    // an item can be catalogued before it is barcoded - so the uniqueness
    // constraint on this column has to be filtered; see InventriaDbContext for
    // why an unfiltered one would make every unbarcoded item collide with
    // every other. A USB/Bluetooth scanner or a camera scan both just need
    // somewhere to type this value into and something to look it up by.
    public string? Barcode { get; set; }

    // Who to buy more of this from. Null is the default and the safe state -
    // every item that existed before this column did has no supplier on file
    // rather than a guessed one, and nothing currently requires one to be set.
    // See InventriaDbContext for why deleting a supplier doesn't cascade here.
    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
}
