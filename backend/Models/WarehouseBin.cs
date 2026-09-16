namespace Inventria.Models;

public class WarehouseBin
{
    public int Id { get; set; }

    // Which building this address is in. Required, not nullable - a bin with
    // no warehouse is an address with no building to walk it in, which is
    // meaningless once more than one exists. See InventriaDbContext for the
    // migration that backfills every bin that predates this column into one
    // default warehouse.
    public int WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public string Zone { get; set; } = string.Empty; // e.g., "Electronics", "Cold Storage"
    public string Aisle { get; set; } = string.Empty;
    public string Shelf { get; set; } = string.Empty;
}