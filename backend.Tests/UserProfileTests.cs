using Inventria.Controllers;
using Inventria.Models;
using Microsoft.AspNetCore.Mvc;

namespace Inventria.Tests;

/// <summary>
/// Self-service account changes - a caller acting on their own record, resolved
/// from the token rather than an id in the URL. Changing a password is the one
/// action here that has to prove something first: everything else UsersController
/// lets an Admin do without asking, but nobody else gets to set this account's
/// password without showing they already knew it.
/// </summary>
public class UserProfileTests
{
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

    private static UserProfileController ControllerFor(TestDatabase db, User user) =>
        new(db.Context) { ControllerContext = ApiResult.SignedInAs(user.Username, id: user.Id) };

    [Fact]
    public void The_right_current_password_lets_a_new_one_replace_it()
    {
        using var db = new TestDatabase();
        var alice = AddAccount(db, "alice", "original password");

        var result = ControllerFor(db, alice).ChangePassword(new ChangePasswordRequest
        {
            CurrentPassword = "original password",
            NewPassword = "a stronger password"
        });

        Assert.IsType<OkObjectResult>(result);

        using var check = db.NewContext();
        var stored = check.Users.Single();

        Assert.True(BCrypt.Net.BCrypt.Verify("a stronger password", stored.Password));
        Assert.False(BCrypt.Net.BCrypt.Verify("original password", stored.Password));
    }

    [Fact]
    public void A_wrong_current_password_is_refused_and_changes_nothing()
    {
        using var db = new TestDatabase();
        var alice = AddAccount(db, "alice", "original password");

        // This is the check the whole endpoint exists for: without it, whoever
        // holds the session cookie - not necessarily the account's owner - could
        // set a new password with no proof they knew the old one.
        var result = ControllerFor(db, alice).ChangePassword(new ChangePasswordRequest
        {
            CurrentPassword = "a guess",
            NewPassword = "a stronger password"
        });

        Assert.IsType<BadRequestObjectResult>(result);

        using var check = db.NewContext();
        Assert.True(BCrypt.Net.BCrypt.Verify("original password", check.Users.Single().Password));
    }

    [Fact]
    public void Changing_a_password_for_an_account_that_no_longer_exists_is_a_not_found()
    {
        using var db = new TestDatabase();
        var controller = new UserProfileController(db.Context)
        {
            ControllerContext = ApiResult.SignedInAs("ghost", id: 999)
        };

        var result = controller.ChangePassword(new ChangePasswordRequest
        {
            CurrentPassword = "whatever",
            NewPassword = "a stronger password"
        });

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // --- NOTIFICATION PREFERENCES --------------------------------------------

    [Fact]
    public void A_freshly_created_account_defaults_to_low_stock_on_and_daily_summary_off()
    {
        using var db = new TestDatabase();
        var alice = AddAccount(db, "alice");

        var result = ControllerFor(db, alice).GetMe();
        var body = ApiResult.Body(result);

        // Matches what the Settings page already showed everyone before these
        // columns existed - a migration that flipped an existing account's
        // subscriptions would be a surprise, not a preference.
        Assert.True(ApiResult.Property(body, "NotifyLowStock").GetBoolean());
        Assert.False(ApiResult.Property(body, "NotifyDailySummary").GetBoolean());
    }

    [Fact]
    public void Updating_the_profile_persists_both_notification_toggles()
    {
        using var db = new TestDatabase();
        var alice = AddAccount(db, "alice");

        var result = ControllerFor(db, alice).UpdateMe(new UpdateMeRequest
        {
            Username = "alice",
            Email = "alice@example.com",
            NotifyLowStock = false,
            NotifyDailySummary = true
        });

        Assert.IsType<OkObjectResult>(result);
        Assert.False(ApiResult.Property(ApiResult.Body(result), "NotifyLowStock").GetBoolean());
        Assert.True(ApiResult.Property(ApiResult.Body(result), "NotifyDailySummary").GetBoolean());

        using var check = db.NewContext();
        var stored = check.Users.Single();

        // Not just the response - the point is that a reload can read this back,
        // which means it has to actually be in the row.
        Assert.False(stored.NotifyLowStock);
        Assert.True(stored.NotifyDailySummary);
    }

    [Fact]
    public void Getting_the_profile_for_an_account_that_no_longer_exists_is_a_not_found()
    {
        using var db = new TestDatabase();
        var controller = new UserProfileController(db.Context)
        {
            ControllerContext = ApiResult.SignedInAs("ghost", id: 999)
        };

        Assert.IsType<NotFoundObjectResult>(controller.GetMe());
    }
}
