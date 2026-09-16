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

    // Which batch this movement touched, for a lot-tracked item - null for
    // everything else. This is what "which customers got the recalled
    // batch" is answered from: the movement log already says what left and
    // when, so naming the lot here is the only piece a recall needs that
    // was not already being recorded.
    public int? LotId { get; set; }
    public Lot? Lot { get; set; }
}