using Microsoft.AspNetCore.Mvc;
using Inventria.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventria.Controllers;

[Route("api/[controller]")]
[ApiController]
public class DashboardController : ControllerBase
{
    private readonly InventriaDbContext _context;

    public DashboardController(InventriaDbContext context)
    {
        _context = context;
    }

    [HttpGet("admin")]
    public async Task<IActionResult> GetAdminStats()
    {
        // 1. Total Users
        var totalUsers = await _context.Users.CountAsync();

        // 2. Total Physical Stock (Sum of all quantities in all bins)
        var totalStockQuantity = await _context.InventoryBalances.SumAsync(b => (int?)b.Quantity) ?? 0;

        // 3. Monthly Throughput (Total units moved in the last 30 days)
        var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);
        var monthlyThroughput = await _context.StockMovements
            .Where(m => m.Timestamp >= thirtyDaysAgo)
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

        // 5. Recent System Activity (Last 5 transactions)
        var recentActivity = await _context.StockMovements
            .OrderByDescending(m => m.Timestamp)
            .Take(5)
            .Select(m => new {
                m.TransactionType,
                m.QuantityChanged,
                m.Timestamp,
                m.PerformedBy,
                // Join to get the actual item name instead of just the ID
                ItemName = _context.Items.Where(i => i.Id == m.ItemId).Select(i => i.Name).FirstOrDefault(),
                m.WarehouseBinId
            })
            .ToListAsync();

        return Ok(new {
            TotalUsers = totalUsers,
            TotalStockQuantity = totalStockQuantity,
            MonthlyThroughput = monthlyThroughput,
            Distribution = distribution,
            TotalUniqueItems = totalItems,
            RecentActivity = recentActivity
        });
    }
}