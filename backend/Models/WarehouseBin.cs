namespace Inventria.Models;

public class WarehouseBin
{
    public int Id { get; set; }
    public string Zone { get; set; } = string.Empty; // e.g., "Electronics", "Cold Storage"
    public string Aisle { get; set; } = string.Empty;
    public string Shelf { get; set; } = string.Empty;
}