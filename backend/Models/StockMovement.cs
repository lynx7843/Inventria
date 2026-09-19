using System;

namespace Inventria.Models;

public class StockMovement
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public int? WarehouseBinId { get; set; }
    public string TransactionType { get; set; } = string.Empty; // "RECEIVE", "PICK", "RELOCATE", "ADJUST", "ASSEMBLE", or "RETURN"
    public int QuantityChanged { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string PerformedBy { get; set; } = string.Empty; // The Employee ID who made the change

    // Set on an ADJUST movement (CountSheetsController) or a RETURN
    // (ReturnsController), both of which require one - a correction or a
    // return with no reason recorded against it is indistinguishable from one
    // nobody thought to explain. Null for every other transaction type, which
    // never asks for one.
    public string? ReasonCode { get; set; }

    // Set only on a RETURN movement - one of ReturnConditionGrade's values,
    // describing what state the goods actually came back in.
    public string? ConditionGrade { get; set; }

    // Set only on a RETURN movement - one of ReturnDisposition's values. This
    // is what stops a return from silently becoming a plain receive: Restock
    // is the only decision that lets QuantityChanged actually add to a
    // sellable balance, so a damaged unit marked Quarantine or Scrap cannot
    // rejoin stock just because someone logged that it came back.
    public string? Disposition { get; set; }

    // Which batch this movement touched, for a lot-tracked item - null for
    // everything else. This is what "which customers got the recalled
    // batch" is answered from: the movement log already says what left and
    // when, so naming the lot here is the only piece a recall needs that
    // was not already being recorded.
    public int? LotId { get; set; }
    public Lot? Lot { get; set; }
}