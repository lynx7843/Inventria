namespace Inventria.Models;

public class PurchaseOrder
{
    public int Id { get; set; }

    public int SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    // One of the PurchaseOrderStatus constants. See there for the flow this is
    // only ever allowed to move through.
    public string Status { get; set; } = PurchaseOrderStatus.Draft;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;

    // Set when Place() moves this out of Draft - null for an order nobody has
    // sent to the supplier yet, which is also how a Draft is told apart from
    // one that has simply never received anything.
    public DateTime? OrderedAt { get; set; }

    // Set once every line has received everything it ordered - see
    // PurchaseOrdersController.RecomputeStatus. Alongside ExpectedDate, this is
    // what answers "which supplier is always late" without needing a table of
    // its own: the two dates are already on the row a lateness report would
    // group by supplier.
    public DateTime? ReceivedAt { get; set; }

    // What the supplier told us to expect, for comparing against ReceivedAt
    // once it lands. Optional - an order can be placed before a supplier has
    // confirmed a date.
    public DateTime? ExpectedDate { get; set; }

    public string? Notes { get; set; }

    public List<PurchaseOrderLine> Lines { get; set; } = [];
}
