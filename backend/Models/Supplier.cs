namespace Inventria.Models;

public class Supplier
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }

    // How many days out an order placed with this supplier is expected to
    // land, for turning a reorder point into "order by" date once purchase
    // orders exist. Nullable, like everything else here but Name: a supplier
    // can be on file before anyone has measured how long they take.
    public int? LeadTimeDays { get; set; }
}
