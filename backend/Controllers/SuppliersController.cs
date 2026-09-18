using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Inventria.Models;
using System.ComponentModel.DataAnnotations;

namespace Inventria.Controllers;

// The Supplier table has existed since Item gained a nullable SupplierId, but
// nothing has ever been able to list, add, or edit one - there was no way to
// populate the picker a purchase order needs. Same audience as Items and
// WarehouseBins: anyone signed in maintains who the warehouse buys from, only
// user administration is Admin-only.
[Authorize]
[Route("api/[controller]")]
[ApiController]
public class SuppliersController : ControllerBase
{
    private readonly InventriaDbContext _context;

    public SuppliersController(InventriaDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public IActionResult GetSuppliers()
    {
        var suppliers = _context.Suppliers.OrderBy(s => s.Name).ToList();
        return Ok(suppliers);
    }

    [HttpPost]
    public IActionResult CreateSupplier([FromBody] SupplierRequest request)
    {
        var supplier = new Supplier
        {
            Name = request.Name.Trim(),
            ContactName = NormalizeOptional(request.ContactName),
            Email = NormalizeOptional(request.Email),
            Phone = NormalizeOptional(request.Phone),
            Address = NormalizeOptional(request.Address),
            LeadTimeDays = request.LeadTimeDays
        };

        _context.Suppliers.Add(supplier);
        _context.SaveChanges();

        return Ok(new { Message = "Supplier created successfully.", Supplier = supplier });
    }

    [HttpPut("{id}")]
    public IActionResult UpdateSupplier(int id, [FromBody] SupplierRequest request)
    {
        var supplier = _context.Suppliers.Find(id);
        if (supplier == null) return NotFound(new { Message = "Supplier not found." });

        supplier.Name = request.Name.Trim();
        supplier.ContactName = NormalizeOptional(request.ContactName);
        supplier.Email = NormalizeOptional(request.Email);
        supplier.Phone = NormalizeOptional(request.Phone);
        supplier.Address = NormalizeOptional(request.Address);
        supplier.LeadTimeDays = request.LeadTimeDays;

        _context.SaveChanges();

        return Ok(new { Message = "Supplier updated successfully.", Supplier = supplier });
    }

    // Blank isn't a value for any of these free-text fields, it's the absence
    // of one - same reasoning as InventoryController.NormalizeBarcode.
    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public class SupplierRequest
{
    [NotBlank(ErrorMessage = "Supplier name is required.")]
    [StringLength(200, ErrorMessage = "Supplier name cannot be longer than 200 characters.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(200, ErrorMessage = "Contact name cannot be longer than 200 characters.")]
    public string? ContactName { get; set; }

    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(255, ErrorMessage = "Email cannot be longer than 255 characters.")]
    public string? Email { get; set; }

    [StringLength(32, ErrorMessage = "Phone cannot be longer than 32 characters.")]
    public string? Phone { get; set; }

    [StringLength(500, ErrorMessage = "Address cannot be longer than 500 characters.")]
    public string? Address { get; set; }

    // How many days out an order placed with this supplier is expected to
    // land - see Supplier.LeadTimeDays. Null means nobody has measured it yet.
    [Range(0, 3650, ErrorMessage = "Lead time must be between 0 and 3650 days.")]
    public int? LeadTimeDays { get; set; }
}
