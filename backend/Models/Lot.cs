namespace Inventria.Models;

public class Lot
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public string LotNumber { get; set; } = string.Empty;

    public DateTime? ExpirationDate { get; set; }

    public Item? Item { get; set; }
}
