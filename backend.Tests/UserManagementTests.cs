using Inventria.Controllers;
using Inventria.Models;
using Microsoft.AspNetCore.Mvc;

namespace Inventria.Tests;

/// <summary>
/// Account administration. Passwords are the whole point of this controller, so
/// most of these are about what happens to them.
/// </summary>
public class UserManagementTests
{
    private static UsersController ControllerFor(TestDatabase db) => new(db.Context);

    private static User AddAccount(TestDatabase db, string username, string password = "original password")
    {
        var user = new User
        {
            Username = username,
            Password = BCrypt.Net.BCrypt.HashPassword(password),
            Role = UserRoles.Employee
        };

        db.Context.Users.Add(user);
        db.Context.SaveChanges();
        return user;
    }

    [Fact]
    public void A_created_account_stores_a_hash_and_never_the_password()
    {
        using var db = new TestDatabase();

        var result = ControllerFor(db).CreateUser(new UserRequest
        {
            Username = "emp_105",
            Password = "a real password",
            Role = UserRoles.Employee
        });

        Assert.IsType<OkObjectResult>(result);

        using var check = db.NewContext();
        var stored = check.Users.Single();

        Assert.NotEqual("a real password", stored.Password);
        Assert.True(BCrypt.Net.BCrypt.Verify("a real password", stored.Password));
    }

    [Fact]
    public void The_list_of_accounts_carries_no_password_field_at_all()
    {
        using var db = new TestDatabase();
        AddAccount(db, "alice");

        var result = ControllerFor(db).GetUsers();
        var row = ApiResult.Body(result).EnumerateArray().Single();

        var fields = row.EnumerateObject().Select(property => property.Name.ToLowerInvariant()).ToList();

        Assert.Equal(["id", "username", "role"], fields);
    }

    [Fact]
    public void A_new_account_cannot_be_created_without_a_password()
    {
        using var db = new TestDatabase();

        // The DTO cannot require this - the same shape is used for updates,
        // where an empty password means "leave it alone" - so the action has to.
        var result = ControllerFor(db).CreateUser(new UserRequest
        {
            Username = "emp_105",
            Password = "   ",
            Role = UserRoles.Employee
        });

        Assert.IsType<BadRequestObjectResult>(result);

        using var check = db.NewContext();
        Assert.Empty(check.Users);
    }

    [Fact]
    public void A_username_already_taken_is_refused()
    {
        using var db = new TestDatabase();
        AddAccount(db, "alice");

        var result = ControllerFor(db).CreateUser(new UserRequest
        {
            Username = "alice",
            Password = "a real password",
            Role = UserRoles.Employee
        });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("already exists", ApiResult.Message(result));
    }

    [Fact]
    public void A_username_is_trimmed_before_it_is_stored()
    {
        using var db = new TestDatabase();

        ControllerFor(db).CreateUser(new UserRequest
        {
            Username = "  emp_105  ",
            Password = "a real password",
            Role = UserRoles.Employee
        });

        using var check = db.NewContext();

        // " admin" would otherwise sit beside "admin" as a second, confusable
        // account that the unique index sees as different.
        Assert.Equal("emp_105", check.Users.Single().Username);
    }

    [Fact]
    public void Updating_without_a_password_leaves_the_current_one_alone()
    {
        using var db = new TestDatabase();
        var user = AddAccount(db, "alice", "original password");
        var originalHash = user.Password;

        var result = ControllerFor(db).UpdateUser(user.Id, new UserRequest
        {
            Username = "alice",
            Password = "",
            Role = UserRoles.Admin
        });

        Assert.IsType<OkObjectResult>(result);

        using var check = db.NewContext();
        var updated = check.Users.Single();

        Assert.Equal(UserRoles.Admin, updated.Role);
        Assert.Equal(originalHash, updated.Password);
        Assert.True(BCrypt.Net.BCrypt.Verify("original password", updated.Password));
    }

    [Fact]
    public void A_password_of_only_spaces_is_a_field_someone_tabbed_through()
    {
        using var db = new TestDatabase();
        var user = AddAccount(db, "alice", "original password");

        ControllerFor(db).UpdateUser(user.Id, new UserRequest
        {
            Username = "alice",
            Password = "     ",
            Role = UserRoles.Employee
        });

        using var check = db.NewContext();

        Assert.True(BCrypt.Net.BCrypt.Verify("original password", check.Users.Single().Password));
    }

    [Fact]
    public void Updating_with_a_new_password_replaces_the_hash()
    {
        using var db = new TestDatabase();
        var user = AddAccount(db, "alice", "original password");

        ControllerFor(db).UpdateUser(user.Id, new UserRequest
        {
            Username = "alice",
            Password = "a different password",
            Role = UserRoles.Employee
        });

        using var check = db.NewContext();

        Assert.True(BCrypt.Net.BCrypt.Verify("a different password", check.Users.Single().Password));
    }

    [Fact]
    public void An_account_cannot_be_renamed_onto_another_account()
    {
        using var db = new TestDatabase();
        AddAccount(db, "alice");
        var bob = AddAccount(db, "bob");

        var result = ControllerFor(db).UpdateUser(bob.Id, new UserRequest
        {
            Username = "alice",
            Password = "",
            Role = UserRoles.Employee
        });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void An_account_can_keep_its_own_name_when_something_else_changes()
    {
        using var db = new TestDatabase();
        var alice = AddAccount(db, "alice");

        // The duplicate check has to exclude the account being edited, or no
        // account could ever change its role.
        var result = ControllerFor(db).UpdateUser(alice.Id, new UserRequest
        {
            Username = "alice",
            Password = "",
            Role = UserRoles.Admin
        });

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public void Updating_or_deleting_an_account_that_does_not_exist_is_a_not_found()
    {
        using var db = new TestDatabase();
        var controller = ControllerFor(db);

        Assert.IsType<NotFoundObjectResult>(controller.UpdateUser(999, new UserRequest
        {
            Username = "ghost",
            Password = "",
            Role = UserRoles.Employee
        }));

        Assert.IsType<NotFoundObjectResult>(controller.DeleteUser(999));
    }

    [Fact]
    public void Deleting_an_account_removes_it()
    {
        using var db = new TestDatabase();
        var user = AddAccount(db, "alice");

        Assert.IsType<OkObjectResult>(ControllerFor(db).DeleteUser(user.Id));

        using var check = db.NewContext();
        Assert.Empty(check.Users);
    }
}
