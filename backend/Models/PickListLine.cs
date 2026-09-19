namespace Inventria.Models;

/// <summary>
/// One item/bin to pick, in the route order the list was opened with.
/// </summary>
public class PickListLine
{
    public int Id { get; set; }

    public int PickListId { get; set; }
    public PickList? PickList { get; set; }

    public int ItemId { get; set; }
    public Item? Item { get; set; }

    public int WarehouseBinId { get; set; }
    public WarehouseBin? WarehouseBin { get; set; }

    // Where this line falls in the walking route through the warehouse -
    // assigned once, when the list is opened, by sorting every line's bin by
    // (WarehouseId, Zone, Aisle, Shelf). Fixed from then on: reordering
    // mid-pick would send someone already halfway down an aisle back the way
    // they came.
    public int Sequence { get; set; }

    public int QuantityRequested { get; set; }
    public int QuantityPicked { get; set; }

    // Required only when the item tracks lots - same as PickStockRequest.
    public string? LotNumber { get; set; }
}
