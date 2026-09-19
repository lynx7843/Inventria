namespace Inventria.Models;

/// <summary>One item moving from a bin in the source warehouse to a bin in the destination warehouse.</summary>
public class TransferLine
{
    public int Id { get; set; }

    public int TransferId { get; set; }
    public Transfer? Transfer { get; set; }

    public int ItemId { get; set; }
    public Item? Item { get; set; }

    public int SourceBinId { get; set; }
    public WarehouseBin? SourceBin { get; set; }

    public int DestinationBinId { get; set; }
    public WarehouseBin? DestinationBin { get; set; }

    public int Quantity { get; set; }

    // Required only when the item tracks lots - same as RelocateStockRequest.
    public string? LotNumber { get; set; }

    // Set once this line's deduction from SourceBin has actually been
    // written. This is what makes a retried ShipTransfer - the same "stock
    // changed underneath us, try again" story every other stock write in this
    // app tells - skip a line it already shipped instead of deducting it
    // twice. Received is the same idea for ReceiveTransfer.
    public bool Shipped { get; set; }
    public bool Received { get; set; }
}
