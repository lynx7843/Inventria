namespace Inventria.Models;

/// <summary>
/// A physical count of one zone, in progress or already posted. Opening one
/// snapshots what the books currently say is on the shelves in that zone into
/// its Lines, so the variance a counter reviews describes what they found
/// against what was true when they started walking the zone - not against
/// whatever the books say by the time they finish and post.
/// </summary>
public class CountSheet
{
    public int Id { get; set; }

    public int WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public string Zone { get; set; } = string.Empty;

    public string Status { get; set; } = CountSheetStatus.Open;

    public DateTime OpenedAt { get; set; } = DateTime.UtcNow;
    public string OpenedBy { get; set; } = string.Empty;

    // Set together once every counted variance has been written as an ADJUST
    // movement - see CountSheetsController.PostCountSheet.
    public DateTime? PostedAt { get; set; }
    public string? PostedBy { get; set; }

    public List<CountSheetLine> Lines { get; set; } = [];
}

/// <summary>
/// Where a count sheet sits, in the one direction it is allowed to move: Open
/// -&gt; Posted, or Open -&gt; Cancelled. Consts rather than an enum for the same
/// reason PurchaseOrderStatus is - the string is what gets stored, returned to
/// the frontend, and compared against in CountSheetsController.
/// </summary>
public static class CountSheetStatus
{
    /// <summary>Being counted. Lines can still be recorded against or added.</summary>
    public const string Open = "Open";

    /// <summary>Every line that differed from the books has been posted as an ADJUST movement. Terminal.</summary>
    public const string Posted = "Posted";

    /// <summary>Abandoned before posting - nothing on this sheet ever touched a balance. Terminal.</summary>
    public const string Cancelled = "Cancelled";
}
