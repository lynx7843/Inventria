namespace Inventria.Models;

/// <summary>
/// A batch of picks to fulfil together, walked in one pass through the
/// warehouse rather than one bin trip per line. Lines/Sequence carries the
/// route - see PickListsController.OpenPickList.
/// </summary>
public class PickList
{
    public int Id { get; set; }

    public string Status { get; set; } = PickListStatus.Open;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;

    // Set once every line has picked everything it asked for.
    public DateTime? CompletedAt { get; set; }

    public List<PickListLine> Lines { get; set; } = [];
}

/// <summary>
/// Where a pick list sits, in the one direction it is allowed to move: Open
/// -&gt; Completed, or Open -&gt; Cancelled. Consts for the same reason
/// PurchaseOrderStatus and CountSheetStatus are.
/// </summary>
public static class PickListStatus
{
    /// <summary>Being walked. Lines can still be picked against.</summary>
    public const string Open = "Open";

    /// <summary>Every line has picked everything it asked for. Terminal.</summary>
    public const string Completed = "Completed";

    /// <summary>Abandoned before completion - nothing left unpicked was ever picked. Terminal.</summary>
    public const string Cancelled = "Cancelled";
}
