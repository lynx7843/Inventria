namespace Inventria.Models;

public class PurchaseOrderLine
{
    public int Id { get; set; }

    public int PurchaseOrderId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }

    public int ItemId { get; set; }
    public Item? Item { get; set; }

    public int QuantityOrdered { get; set; }

    // Running total received against this line, across however many separate
    // deliveries it took - a short shipment followed by a top-up is still one
    // line, not two. QuantityOrdered - QuantityReceived is what "is this
    // delivery short" is answered from.
    public int QuantityReceived { get; set; }

    // The price this line was placed at, distinct from Item.UnitCost - the
    // item's current cost can move between when an order was placed and when
    // it is looked back on, and a purchase order is a record of what was
    // agreed, not a live reference to today's price. Nullable for the same
    // reason Item.UnitCost is: a line can be entered before anyone has priced
    // it.
    public decimal? UnitCost { get; set; }
}
