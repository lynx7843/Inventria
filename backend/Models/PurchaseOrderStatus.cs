namespace Inventria.Models;

/// <summary>
/// Where a purchase order sits in its life, in the one direction it is allowed
/// to move: Draft -&gt; Ordered -&gt; PartiallyReceived -&gt; Received, with Cancelled
/// reachable from any state that is not already Received. Consts rather than an
/// enum for the same reason UserRoles is - the string is what gets stored,
/// returned to the frontend, and compared against in PurchaseOrdersController,
/// and consts keep those three from drifting apart.
/// </summary>
public static class PurchaseOrderStatus
{
    /// <summary>Being assembled - lines can still be added, changed, or removed. Nothing has been sent to the supplier.</summary>
    public const string Draft = "Draft";

    /// <summary>Sent to the supplier. Lines are locked; the only thing that happens to an order now is stock arriving against it.</summary>
    public const string Ordered = "Ordered";

    /// <summary>At least one line has received some, but not all, of what it ordered.</summary>
    public const string PartiallyReceived = "PartiallyReceived";

    /// <summary>Every line has received everything it ordered. Terminal - nothing more can be received against it.</summary>
    public const string Received = "Received";

    /// <summary>Called off before it was fully received. Terminal - no further receiving is possible.</summary>
    public const string Cancelled = "Cancelled";
}
