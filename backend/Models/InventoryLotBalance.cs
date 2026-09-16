using System.ComponentModel.DataAnnotations;

namespace Inventria.Models;

public class InventoryLotBalance
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public int WarehouseBinId { get; set; }
    public int LotId { get; set; }
    public int Quantity { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = null!;

    public Item? Item { get; set; }
    public WarehouseBin? WarehouseBin { get; set; }
    public Lot? Lot { get; set; }
}
