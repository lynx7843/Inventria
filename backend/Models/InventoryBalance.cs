using System.ComponentModel.DataAnnotations;

namespace Inventria.Models;

public class InventoryBalance
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public int WarehouseBinId { get; set; }
    public int Quantity { get; set; }

    // SQL Server bumps this on every UPDATE of the row. EF puts the value it read
    // into the WHERE clause, so a save built on a stale Quantity matches no rows
    // and throws DbUpdateConcurrencyException instead of silently overwriting the
    // other writer. That is what stops two concurrent picks from both passing the
    // "enough stock?" check and driving the quantity negative.
    [Timestamp]
    public byte[] RowVersion { get; set; } = null!;


    // Navigation properties for Entity Framework to understand the relationships
    public Item? Item { get; set; }
    public WarehouseBin? WarehouseBin { get; set; }
}