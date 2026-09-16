using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Inventria.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventria.Controllers;

[Authorize]
[Route("api/[controller]")]
[ApiController]
public class DashboardController : ControllerBase
{
    private readonly InventriaDbContext _context;

    public DashboardController(InventriaDbContext context)
    {
        _context = context;
    }

    [Authorize(Roles = UserRoles.Admin)]
    [HttpGet("admin")]
    public async Task<IActionResult> GetAdminStats()
    {
        // 1. Total Users
        var totalUsers = await _context.Users.CountAsync();

        var totalStockQuantity = (await _context.InventoryBalances.SumAsync(b => (int?)b.Quantity) ?? 0)
            + (await _context.InventoryLotBalances.SumAsync(b => (int?)b.Quantity) ?? 0);


        var totalInventoryValue = await _context.InventoryBalances
            .Join(_context.Items, b => b.ItemId, i => i.Id, (b, i) => b.Quantity * (i.UnitCost ?? 0))
            .SumAsync()
            + await _context.InventoryLotBalances
                .Join(_context.Items, b => b.ItemId, i => i.Id, (b, i) => b.Quantity * (i.UnitCost ?? 0))
                .SumAsync();

        // 3. Monthly Throughput (Total units moved in the last 30 days)
        var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);

        var monthlyThroughput = await _context.StockMovements
            .Where(m => m.Timestamp >= thirtyDaysAgo)
            .Where(m => m.TransactionType != "RELOCATE" || m.QuantityChanged < 0)
            .SumAsync(m => (int?)Math.Abs(m.QuantityChanged)) ?? 0;

        // 4. Category Distribution (Count of unique items per category)
        var distribution = await _context.Items
            .GroupBy(i => i.Category)
            .Select(g => new {
                Category = g.Key,
                Count = g.Count()
            })
            .ToListAsync();

        // Calculate total items to determine percentages for the frontend
        var totalItems = distribution.Sum(d => d.Count);

        var lowStockCount = await LowStock.Lines(_context).CountAsync();

        // 6. Recent System Activity (Last 5 transactions)
        var movements = await _context.StockMovements
            .OrderByDescending(m => m.Timestamp)
            .Take(5)
            .Select(m => new {
                m.TransactionType,
                m.QuantityChanged,
                m.Timestamp,
                m.PerformedBy,
                // Join to get the actual item name instead of just the ID
                ItemName = _context.Items.Where(i => i.Id == m.ItemId).Select(i => i.Name).FirstOrDefault(),
                m.ItemId,
                m.WarehouseBinId
            })
            .ToListAsync();

        var recentActivity = movements
            .Select(m => new {
                m.TransactionType,
                m.QuantityChanged,
                m.Timestamp,
                m.PerformedBy,
                ItemName = m.ItemName ?? $"deleted item #{m.ItemId}",
                m.WarehouseBinId
            })
            .ToList();

        return Ok(new {
            TotalUsers = totalUsers,
            TotalStockQuantity = totalStockQuantity,
            TotalInventoryValue = totalInventoryValue,
            MonthlyThroughput = monthlyThroughput,
            LowStockCount = lowStockCount,
            Distribution = distribution,
            TotalUniqueItems = totalItems,
            RecentActivity = recentActivity
        });
    }

    [HttpGet("employee")]
    public async Task<IActionResult> GetEmployeeStats()
    {
        var unitsOnHand = (await _context.InventoryBalances.SumAsync(b => (int?)b.Quantity) ?? 0)
            + (await _context.InventoryLotBalances.SumAsync(b => (int?)b.Quantity) ?? 0);
        var skusTracked = await _context.Items.CountAsync();

        var lowStockCount = await LowStock.Lines(_context).CountAsync();

        var startOfDay = DateTime.UtcNow.Date;

        var movementsToday = _context.StockMovements.Where(m => m.Timestamp >= startOfDay);

        var receivedToday = await movementsToday
            .Where(m => m.TransactionType == "RECEIVE")
            .SumAsync(m => (int?)m.QuantityChanged) ?? 0;

        var pickedToday = await movementsToday
            .Where(m => m.TransactionType == "PICK")
            .SumAsync(m => (int?)-m.QuantityChanged) ?? 0;

        return Ok(new
        {
            UnitsOnHand = unitsOnHand,
            SkusTracked = skusTracked,
            LowStockCount = lowStockCount,
            ReceivedToday = receivedToday,
            PickedToday = pickedToday
        });
    }
}