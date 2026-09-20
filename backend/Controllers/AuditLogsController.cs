using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Inventria.Models;

namespace Inventria.Controllers;

// Compliance-facing, so Admin-only to read - same reasoning as UsersController:
// this is a record of what every account did, including to other accounts.
[Authorize(Roles = UserRoles.Admin)]
[Route("api/[controller]")]
[ApiController]
public class AuditLogsController : ControllerBase
{
    private readonly InventriaDbContext _context;

    public AuditLogsController(InventriaDbContext context)
    {
        _context = context;
    }

    // Same ceiling as ReportsController's paged endpoints, for the same
    // reason - a page size of 5000 against a table with no natural bound on
    // its growth is asking for the nearest sensible thing, not to be honored.
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 200;

    private static (int page, int pageSize) ClampPaging(int page, int pageSize)
        => (Math.Max(page, 1), Math.Clamp(pageSize, 1, MaxPageSize));

    [HttpGet]
    public IActionResult GetAuditLogs(
        [FromQuery] string? entity,
        [FromQuery] string? username,
        [FromQuery] string? action,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize)
    {
        (page, pageSize) = ClampPaging(page, pageSize);

        var query = _context.AuditLogs.AsQueryable();

        // Exact match on all three: each comes from a fixed set (the entity
        // names this context actually audits, the three actions, and
        // usernames that exist), not free text someone is searching within.
        if (!string.IsNullOrWhiteSpace(entity))
        {
            var trimmed = entity.Trim();
            query = query.Where(log => log.Entity == trimmed);
        }

        if (!string.IsNullOrWhiteSpace(username))
        {
            var trimmed = username.Trim();
            query = query.Where(log => log.Username == trimmed);
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            var trimmed = action.Trim();
            query = query.Where(log => log.Action == trimmed);
        }

        query = query.OrderByDescending(log => log.Timestamp);

        var totalCount = query.Count();

        var rows = query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Ok(new
        {
            Items = rows,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }
}
