namespace Inventria.Models;

/// <summary>
/// What happens to a returned unit next. Consts rather than an enum for the
/// same reason TransferStatus is - the string is what gets stored, returned
/// to the frontend, and compared against in ReturnsController.
/// </summary>
public static class ReturnDisposition
{
    /// <summary>Fit to sell again - added back to the ordinary sellable balance.</summary>
    public const string Restock = "Restock";

    /// <summary>Needs inspection before anyone decides whether it can be sold or has to be scrapped - not added to any sellable balance.</summary>
    public const string Quarantine = "Quarantine";

    /// <summary>Written off - never rejoins a sellable balance.</summary>
    public const string Scrap = "Scrap";
}

/// <summary>
/// The condition a returned unit was actually found in. A closed set, unlike
/// StockMovement.ReasonCode - the disposition a return gets often follows
/// mechanically from its grade (Damaged and Unsellable rarely get Restocked),
/// so this has to be a fixed vocabulary for that relationship to mean
/// anything, the same way ReturnDisposition itself has to be.
/// </summary>
public static class ReturnConditionGrade
{
    public const string New = "New";
    public const string Good = "Good";
    public const string Damaged = "Damaged";
    public const string Unsellable = "Unsellable";
}
