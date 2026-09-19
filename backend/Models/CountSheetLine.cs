namespace Inventria.Models;

/// <summary>
/// One item/bin(/lot) on a count sheet. ExpectedQuantity is fixed at the
/// moment the sheet was opened - not a live read of InventoryBalance - so
/// posting later applies the discrepancy actually found on the shelf rather
/// than whatever difference happens to exist by then.
///
/// Once CountSheetsController.PostCountSheet has written this line's
/// adjustment, ExpectedQuantity is advanced to match CountedQuantity, which
/// is what makes posting the same sheet twice a no-op rather than a double
/// adjustment - see the comment there.
/// </summary>
public class CountSheetLine
{
    public int Id { get; set; }

    public int CountSheetId { get; set; }
    public CountSheet? CountSheet { get; set; }

    public int ItemId { get; set; }
    public Item? Item { get; set; }

    public int WarehouseBinId { get; set; }
    public WarehouseBin? WarehouseBin { get; set; }

    // Null for the plain stock most lines carry; set only when the balance
    // being counted is one lot among possibly several in this bin - the same
    // split InventoryBalance/InventoryLotBalance draws everywhere else.
    public int? LotId { get; set; }
    public Lot? Lot { get; set; }

    public int ExpectedQuantity { get; set; }

    // Null until someone walks the shelf and records what is actually there.
    // A sheet can only be posted once every line has one.
    public int? CountedQuantity { get; set; }

    // Required once CountedQuantity differs from ExpectedQuantity - see
    // CountSheetsController.PostCountSheet. Free text rather than a closed
    // set: a warehouse's reasons for a miscount (damage, theft, a supplier
    // shorting a case, a mis-scanned pick) are not a list this system should
    // be the one to close.
    public string? ReasonCode { get; set; }
}
