namespace Inventria.Models;

/// <summary>
/// A relocate that spans warehouses, split into two events instead of one -
/// see TransfersController. Between ShipTransfer and ReceiveTransfer the
/// stock is in neither InventoryBalance/InventoryLotBalance: gone from the
/// source, not yet arrived at the destination. That gap is what an
/// in-transit state is for - without it, a transfer would either double-count
/// the stock (still on the books at the source while also added at the
/// destination) or vanish from the books entirely for however long the truck
/// takes.
/// </summary>
public class Transfer
{
    public int Id { get; set; }

    public int SourceWarehouseId { get; set; }
    public Warehouse? SourceWarehouse { get; set; }

    public int DestinationWarehouseId { get; set; }
    public Warehouse? DestinationWarehouse { get; set; }

    public string Status { get; set; } = TransferStatus.Draft;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;

    public DateTime? ShippedAt { get; set; }
    public string? ShippedBy { get; set; }

    public DateTime? ReceivedAt { get; set; }
    public string? ReceivedBy { get; set; }

    public List<TransferLine> Lines { get; set; } = [];
}

/// <summary>
/// Where a transfer sits, in the one direction it is allowed to move: Draft
/// -&gt; InTransit -&gt; Received, with Cancelled reachable only from Draft - once
/// stock has actually left the source bin, cancelling would need to decide
/// where it goes back to, which is a decision this system leaves to a real
/// receive rather than inventing a silent reversal for. Consts for the same
/// reason PurchaseOrderStatus is.
/// </summary>
public static class TransferStatus
{
    /// <summary>Being assembled. Nothing has moved yet.</summary>
    public const string Draft = "Draft";

    /// <summary>Shipped: every line has left its source bin and none has arrived yet.</summary>
    public const string InTransit = "InTransit";

    /// <summary>Every line has arrived at its destination bin. Terminal.</summary>
    public const string Received = "Received";

    /// <summary>Called off before anything shipped. Terminal.</summary>
    public const string Cancelled = "Cancelled";
}
