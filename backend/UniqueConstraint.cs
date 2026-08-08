using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Inventria;

/// <summary>
/// Recognises the database refusing a write because it would duplicate a value
/// that a unique index protects.
///
/// Every "is this taken?" test in this app is a check-then-act: two concurrent
/// requests can both run the SELECT, both see nothing, and both insert. The
/// unique index is what actually enforces uniqueness; the pre-checks only exist
/// to produce a friendly message in the ordinary case. This is how a controller
/// tells that one expected failure apart from a genuine database error, so the
/// loser of the race gets the same 400 it would have got a moment earlier.
/// </summary>
public static class UniqueConstraint
{
    // SQL Server reports a duplicate key as 2627 (unique constraint) or 2601
    // (unique index).
    private static readonly int[] DuplicateKeyErrors = [2601, 2627];

    public static bool WasViolated(DbUpdateException ex) =>
        ex.InnerException is SqlException sql && DuplicateKeyErrors.Contains(sql.Number);
}
