using Microsoft.AspNetCore.Mvc;
using Inventria.Models;

namespace Inventria.Controllers;

[Route("api/[controller]")]
[ApiController]
public class InventoryController : ControllerBase
{
    private readonly InventriaDbContext _context;

    public InventoryController(InventriaDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public IActionResult GetAllItems()
    {
        // Fetches all items from the SQL Server database
        var items = _context.Items.ToList();
        return Ok(items);
    }
}