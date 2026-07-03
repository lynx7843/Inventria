namespace Inventria.Models;

public class InventoryBalance
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public int WarehouseBinId { get; set; }
    public int Quantity { get; set; }
    
    // Navigation properties for Entity Framework to understand the relationships
    public Item? Item { get; set; }
    public WarehouseBin? WarehouseBin { get; set; }
}