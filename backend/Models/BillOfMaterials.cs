namespace Inventria.Models;

/// <summary>
/// What one unit of a finished item is built from - one hammer needs one
/// handle and one head. An item has at most one bill of materials, kept as
/// its own table rather than a column on Item so the great majority of items,
/// which are never assembled, carry nothing extra at all.
/// </summary>
public class BillOfMaterials
{
    public int Id { get; set; }

    public int ItemId { get; set; }
    public Item? Item { get; set; }

    public List<BillOfMaterialLine> Components { get; set; } = [];
}

/// <summary>One component and how many of it one unit of the finished item consumes.</summary>
public class BillOfMaterialLine
{
    public int Id { get; set; }

    public int BillOfMaterialsId { get; set; }
    public BillOfMaterials? BillOfMaterials { get; set; }

    public int ComponentItemId { get; set; }
    public Item? ComponentItem { get; set; }

    public int QuantityRequired { get; set; }
}
