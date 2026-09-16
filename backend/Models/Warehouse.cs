namespace Inventria.Models;

// The building a bin sits in. WarehouseBin used to be flat - Zone/Aisle/Shelf
// with no notion of which site those even belonged to - which modelled
// exactly one building. A second site needs this table above it, because
// "A1" in one warehouse and "A1" in another are not the same shelf just
// because they share an address within their own building; see the
// (WarehouseId, Zone, Aisle, Shelf) index on WarehouseBin.
public class Warehouse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
