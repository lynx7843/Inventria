using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Inventria;

/// <summary>
/// Recognises the database refusing a write because it would leave a row
/// pointing at a row that does not exist.
///
/// Deleting an item is a check-then-act like every other guard in this app: the
/// controller counts the balances and movements that reference the item, and a
/// concurrent receive can create one between that count and the DELETE. The
/// foreign keys are what actually stop stock and audit history from being
/// stranded; this is how the controller tells that expected refusal apart from a
/// genuine database error, so the loser of the race gets the same 409 it would
/// have got a moment earlier.
/// </summary>
public static class ForeignKeyConstraint
{
    // SQL Server reports every foreign key conflict as 547, both "the row you
    // reference does not exist" and "another row still references the one you
    // are deleting".
    private const int ConstraintConflict = 547;

    public static bool WasViolated(DbUpdateException ex) =>
        ex.InnerException is SqlException sql && sql.Number == ConstraintConflict;
}
