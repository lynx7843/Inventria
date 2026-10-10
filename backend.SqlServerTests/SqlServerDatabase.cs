using Inventria.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Inventria.SqlServerTests;

/// <summary>
/// A throwaway database on a real SQL Server, built by running the actual
/// migrations and dropped afterwards. The connection string from
/// INVENTRIA_TEST_SQLSERVER only picks the server and login: the database name
/// is replaced, so this can never touch real data.
/// </summary>
public sealed class SqlServerDatabase : IDisposable
{
    private readonly string _connectionString;

    public SqlServerDatabase()
    {
        var builder = new SqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable(SqlServerFactAttribute.EnvironmentVariable))
        {
            InitialCatalog = $"InventriaTests_{Guid.NewGuid():N}"
        };
        _connectionString = builder.ConnectionString;

        using var context = NewContext();
        context.Database.Migrate();
    }

    public InventriaDbContext NewContext() =>
        new(new DbContextOptionsBuilder<InventriaDbContext>().UseSqlServer(_connectionString).Options);

    public (int ItemId, int BinId) AddItemAndBin()
    {
        using var context = NewContext();
        var warehouse = new Warehouse { Name = "Main Warehouse" };
        context.Warehouses.Add(warehouse);
        var item = new Item { Sku = "SKU-1", Name = "Steel Wrench", Category = "Tools" };
        context.Items.Add(item);
        context.SaveChanges();

        var bin = new WarehouseBin { WarehouseId = warehouse.Id, Zone = "Z", Aisle = "A1", Shelf = "S1" };
        context.WarehouseBins.Add(bin);
        context.SaveChanges();
        return (item.Id, bin.Id);
    }

    public void Dispose()
    {
        using var context = NewContext();
        context.Database.EnsureDeleted();
    }
}
