using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Inventria.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventria.Controllers;

// Signed in is the floor; the admin figures below ask for more than that on the
// action itself. Both attributes have to pass, so moving the role check down
// opened the employee counters to Employees without loosening anything else.
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

    // The counters on the warehouse floor's own dashboard, which until now were
    // four numbers typed into the markup. Each of these is something the database
    // can actually answer; the two that it could not - a low-stock alert count,
    // which needs a reorder level no item carries, and an "efficiency rate",
    // which was never defined as anything - are gone rather than approximated.
    [HttpGet("employee")]
    public async Task<IActionResult> GetEmployeeStats()
    {
        var unitsOnHand = await _context.InventoryBalances.SumAsync(b => (int?)b.Quantity) ?? 0;
        var skusTracked = await _context.Items.CountAsync();

        // "Today" is the UTC day, because that is the clock every movement is
        // stamped with. A warehouse that wants its own local day boundary needs
        // to say which timezone it is in, and nothing here records that yet.
        var startOfDay = DateTime.UtcNow.Date;

        var movementsToday = _context.StockMovements.Where(m => m.Timestamp >= startOfDay);

        // RECEIVE and PICK only: a relocation moves units between bins without
        // any arriving or leaving, and counting its legs here would make a shuffle
        // look like a day's work.
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
            ReceivedToday = receivedToday,
            PickedToday = pickedToday
        });
    }
}