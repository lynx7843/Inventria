using Inventria.Controllers;
using Inventria.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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

    private static UserProfileController ControllerFor(TestDatabase db, User user, IWebHostEnvironment? environment = null) =>
        new(db.Context, environment ?? new FakeWebHostEnvironment())
        {
            ControllerContext = ApiResult.SignedInAs(user.Username, id: user.Id)
        };

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
        var controller = new UserProfileController(db.Context, new FakeWebHostEnvironment())
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
        var controller = new UserProfileController(db.Context, new FakeWebHostEnvironment())
        {
            ControllerContext = ApiResult.SignedInAs("ghost", id: 999)
        };

        Assert.IsType<NotFoundObjectResult>(controller.GetMe());
    }

    // --- AVATAR ----------------------------------------------------------------

    // Real signature bytes, not just a plausible-looking Content-Type - the
    // whole point of AvatarStorage is that it sniffs these rather than trusting
    // what the upload claims to be.
    private static readonly byte[] PngBytes =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0, 1, 2, 3, 4];

    private static IFormFile FakeUpload(byte[] bytes, string contentType = "image/png", string filename = "photo.png") =>
        new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", filename) { Headers = new HeaderDictionary(), ContentType = contentType };

    [Fact]
    public async Task Uploading_a_real_image_stores_it_under_a_generated_name_and_points_the_row_at_it()
    {
        using var db = new TestDatabase();
        var environment = new FakeWebHostEnvironment();
        var alice = AddAccount(db, "alice");

        var result = await ControllerFor(db, alice, environment).UploadAvatar(FakeUpload(PngBytes));

        Assert.IsType<OkObjectResult>(result);
        var avatarUrl = ApiResult.Text(result, "AvatarUrl");
        Assert.StartsWith("/uploads/avatars/", avatarUrl);

        // Never the name or extension the caller uploaded it as - a GUID that
        // AvatarStorage minted, under the directory it controls.
        Assert.EndsWith(".png", avatarUrl);
        Assert.DoesNotContain("photo", avatarUrl);

        using var check = db.NewContext();
        var stored = check.Users.Single();
        Assert.Equal($"/{stored.AvatarPath}", avatarUrl);
        Assert.True(File.Exists(Path.Combine(environment.WebRootPath, stored.AvatarPath!)));
    }

    [Fact]
    public async Task A_file_that_is_not_actually_an_image_is_refused_regardless_of_its_claimed_type()
    {
        using var db = new TestDatabase();
        var environment = new FakeWebHostEnvironment();
        var alice = AddAccount(db, "alice");

        // Plain text, but labelled image/png with a .png name - the header
        // check is what has to catch this, since everything else about the
        // request looks legitimate.
        var spoofed = FakeUpload("<script>alert(1)</script>"u8.ToArray());

        var result = await ControllerFor(db, alice, environment).UploadAvatar(spoofed);

        Assert.IsType<BadRequestObjectResult>(result);
        using var check = db.NewContext();
        Assert.Null(check.Users.Single().AvatarPath);
        Assert.Empty(Directory.Exists(environment.WebRootPath) ? Directory.GetFiles(environment.WebRootPath, "*", SearchOption.AllDirectories) : []);
    }

    [Fact]
    public async Task An_oversized_upload_is_refused_before_anything_is_written()
    {
        using var db = new TestDatabase();
        var environment = new FakeWebHostEnvironment();
        var alice = AddAccount(db, "alice");

        var tooBig = new byte[AvatarStorage.MaxBytes + 1];
        PngBytes.CopyTo(tooBig, 0);

        var result = await ControllerFor(db, alice, environment).UploadAvatar(FakeUpload(tooBig));

        Assert.IsType<BadRequestObjectResult>(result);
        using var check = db.NewContext();
        Assert.Null(check.Users.Single().AvatarPath);
    }

    [Fact]
    public async Task Uploading_a_second_photo_replaces_the_first_on_disk()
    {
        using var db = new TestDatabase();
        var environment = new FakeWebHostEnvironment();
        var alice = AddAccount(db, "alice");
        var controller = ControllerFor(db, alice, environment);

        var first = await controller.UploadAvatar(FakeUpload(PngBytes));
        var firstPath = Path.Combine(environment.WebRootPath, ApiResult.Text(first, "AvatarUrl").TrimStart('/'));
        Assert.True(File.Exists(firstPath));

        var second = await controller.UploadAvatar(FakeUpload(PngBytes, filename: "other.png"));
        var secondPath = Path.Combine(environment.WebRootPath, ApiResult.Text(second, "AvatarUrl").TrimStart('/'));

        Assert.NotEqual(firstPath, secondPath);
        Assert.False(File.Exists(firstPath));
        Assert.True(File.Exists(secondPath));
    }

    [Fact]
    public async Task Removing_the_photo_clears_the_row_and_deletes_the_file()
    {
        using var db = new TestDatabase();
        var environment = new FakeWebHostEnvironment();
        var alice = AddAccount(db, "alice");
        var controller = ControllerFor(db, alice, environment);

        var uploaded = await controller.UploadAvatar(FakeUpload(PngBytes));
        var savedPath = Path.Combine(environment.WebRootPath, ApiResult.Text(uploaded, "AvatarUrl").TrimStart('/'));
        Assert.True(File.Exists(savedPath));

        var result = controller.DeleteAvatar();

        Assert.IsType<OkObjectResult>(result);
        using var check = db.NewContext();
        Assert.Null(check.Users.Single().AvatarPath);
        Assert.False(File.Exists(savedPath));
    }

    [Fact]
    public async Task Uploading_a_photo_for_an_account_that_no_longer_exists_is_a_not_found()
    {
        using var db = new TestDatabase();
        var controller = new UserProfileController(db.Context, new FakeWebHostEnvironment())
        {
            ControllerContext = ApiResult.SignedInAs("ghost", id: 999)
        };

        var result = await controller.UploadAvatar(FakeUpload(PngBytes));

        Assert.IsType<NotFoundObjectResult>(result);
    }
}
