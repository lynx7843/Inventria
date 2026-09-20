namespace Inventria.Models;

/// <summary>
/// One row written automatically by InventriaDbContext.SaveChanges for every
/// insert, update or delete elsewhere in the database - see there for how.
/// This is the answer to "who deleted that user" or "who changed this item's
/// cost, and what was it before" that nothing but StockMovements had before.
/// </summary>
public class AuditLog
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // The signed-in username at the time, or "system" for a change made
    // outside a request - startup seeding, principally.
    public string Username { get; set; } = string.Empty;

    // The CLR type name of the entity that changed, e.g. "Item" - not the
    // table name, though today those are the same for everything audited.
    public string Entity { get; set; } = string.Empty;

    // The changed row's primary key, stringified - almost always an int, but
    // stored as text so a composite key (comma-joined) never needs a second
    // column type.
    public string EntityId { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty; // "Create", "Update", or "Delete"

    // Both are a JSON object of property name to value. OldValue is null for
    // a Create, NewValue is null for a Delete; for an Update, both hold only
    // the properties that actually changed - not a full row dump - so this
    // answers "what changed" rather than "what did every column say after".
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
}
