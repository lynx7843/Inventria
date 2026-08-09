using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Inventria.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventria.Controllers;

[Authorize(Roles = UserRoles.Admin)]
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
        // A relocation writes two rows - out of the source bin, into the
        // destination - so adding up every row would count those units twice.
        // Throughput is units moved, and stock moved once, so only the outbound
        // leg counts here.
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

        // 5. Recent System Activity (Last 5 transactions)
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

        // Item deletion can no longer strand a movement, but rows written before
        // that was true still point at an item that is gone and the join returns
        // nothing for them. Naming the Id that went missing beats rendering the
        // sentence with a hole where the item should be.
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
            MonthlyThroughput = monthlyThroughput,
            Distribution = distribution,
            TotalUniqueItems = totalItems,
            RecentActivity = recentActivity
        });
    }
}