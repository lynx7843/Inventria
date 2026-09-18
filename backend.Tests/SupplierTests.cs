using Inventria.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Inventria.Tests;

/// <summary>
/// The list a purchase order's supplier picker is built from. Kept deliberately
/// small - no delete, since nothing here needs to remove a supplier that a
/// purchase order or an item might already reference.
/// </summary>
public class SupplierTests
{
    private static SuppliersController ControllerFor(TestDatabase db) =>
        new(db.Context) { ControllerContext = ApiResult.SignedInAs("alice") };

    [Fact]
    public void Creating_a_supplier_trims_the_name_and_normalizes_blank_optional_fields()
    {
        using var db = new TestDatabase();

        var result = ControllerFor(db).CreateSupplier(new SupplierRequest
        {
            Name = "  Acme Supply Co  ",
            ContactName = "   ",
            Email = "buyer@acme.test"
        });

        Assert.IsType<OkObjectResult>(result);

        using var check = db.NewContext();
        var supplier = check.Suppliers.Single();
        Assert.Equal("Acme Supply Co", supplier.Name);
        Assert.Null(supplier.ContactName);
        Assert.Equal("buyer@acme.test", supplier.Email);
    }

    [Fact]
    public void Creating_a_supplier_with_a_blank_name_is_refused_by_validation()
    {
        // [ApiController] runs model validation before the action executes in a
        // real request; called directly, ModelState is never populated, so this
        // exercises the attribute itself rather than the controller.
        var request = new SupplierRequest { Name = "   " };
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            request, new System.ComponentModel.DataAnnotations.ValidationContext(request), results, validateAllProperties: true);

        Assert.False(isValid);
    }

    [Fact]
    public void Listing_suppliers_orders_them_by_name()
    {
        using var db = new TestDatabase();
        var controller = ControllerFor(db);

        controller.CreateSupplier(new SupplierRequest { Name = "Zeta Corp" });
        controller.CreateSupplier(new SupplierRequest { Name = "Acme Supply Co" });

        var result = controller.GetSuppliers();
        var names = ApiResult.Body(result).EnumerateArray()
            .Select(s => ApiResult.Property(s, "Name").GetString())
            .ToList();

        Assert.Equal(["Acme Supply Co", "Zeta Corp"], names);
    }

    [Fact]
    public void Updating_a_supplier_that_does_not_exist_is_a_not_found()
    {
        using var db = new TestDatabase();

        var result = ControllerFor(db).UpdateSupplier(999, new SupplierRequest { Name = "Anyone" });

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void Updating_a_supplier_replaces_its_fields()
    {
        using var db = new TestDatabase();
        var controller = ControllerFor(db);
        var created = controller.CreateSupplier(new SupplierRequest { Name = "Acme Supply Co", LeadTimeDays = 5 });
        var id = ApiResult.Property(ApiResult.Property(ApiResult.Body(created), "Supplier"), "Id").GetInt32();

        var result = controller.UpdateSupplier(id, new SupplierRequest { Name = "Acme Supply Co (Renamed)", LeadTimeDays = 12 });

        Assert.IsType<OkObjectResult>(result);
        using var check = db.NewContext();
        var supplier = check.Suppliers.Single();
        Assert.Equal("Acme Supply Co (Renamed)", supplier.Name);
        Assert.Equal(12, supplier.LeadTimeDays);
    }
}
