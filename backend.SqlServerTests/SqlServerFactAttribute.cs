namespace Inventria.SqlServerTests;

/// <summary>
/// A fact that only runs when INVENTRIA_TEST_SQLSERVER holds a connection
/// string, and reports itself skipped otherwise - so the default `dotnet test`
/// needs no database and stays fast.
/// </summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public const string EnvironmentVariable = "INVENTRIA_TEST_SQLSERVER";

    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvironmentVariable)))
            Skip = $"Set {EnvironmentVariable} to a SQL Server connection string to run this.";
    }
}
