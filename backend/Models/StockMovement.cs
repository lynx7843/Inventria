using System;

namespace Inventria.Models;

public class StockMovement
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public int? WarehouseBinId { get; set; }
    public string TransactionType { get; set; } = string.Empty; // "RECEIVE", "PICK", or "RELOCATE"
    public int QuantityChanged { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string PerformedBy { get; set; } = string.Empty; // The Employee ID who made the change
}