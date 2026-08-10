using Inventria.Controllers;
using Inventria.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace Inventria.Tests;

/// <summary>
/// Signing in: what a wrong password gets you, what ten of them get you, and
/// what an unknown username must not reveal.
/// </summary>
public class AuthenticationTests
{
    // Long enough for HMAC-SHA256, which the app refuses to start without.
    private const string TestSigningKey = "0123456789abcdef0123456789abcdef0123456789abcdef";

    private const string KnownPassword = "correct horse battery staple";

    private static AuthController ControllerFor(TestDatabase db, LoginThrottle? throttle = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = TestSigningKey,
                // Development serves the API over plain HTTP; the cookie policy
                // is not what these tests are about.
                ["Auth:CookieSecure"] = "false"
            })
            .Build();

        throttle ??= new LoginThrottle(new MemoryCache(new MemoryCacheOptions()));

        return new AuthController(db.Context, configuration, throttle)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private static User AddAccount(TestDatabase db, string username = "alice", string role = UserRoles.Employee)
    {
        var user = new User
        {
            Username = username,
            Password = BCrypt.Net.BCrypt.HashPassword(KnownPassword),
            Role = role
        };

        db.Context.Users.Add(user);
        db.Context.SaveChanges();
        return user;
    }

    [Fact]
    public void The_right_password_signs_in_and_returns_the_role_the_UI_routes_on()
    {
        using var db = new TestDatabase();
        AddAccount(db, "alice", UserRoles.Admin);
        var controller = ControllerFor(db);

        var result = controller.Login(new LoginRequest { Username = "alice", Password = KnownPassword });

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("alice", ApiResult.Text(result, "username"));
        Assert.Equal(UserRoles.Admin, ApiResult.Text(result, "role"));
    }

    [Fact]
    public void The_token_goes_back_as_a_cookie_and_never_in_the_body()
    {
        using var db = new TestDatabase();
        AddAccount(db);
        var controller = ControllerFor(db);

        controller.Login(new LoginRequest { Username = "alice", Password = KnownPassword });

        // Script on the page - including anything injected - must not be able to
        // read the token, so it travels as an HttpOnly cookie and the body says
        // only who signed in.
        var setCookie = controller.Response.Headers.SetCookie.ToString();

        Assert.Contains(AuthCookie.Name, setCookie);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_wrong_password_is_rejected()
    {
        using var db = new TestDatabase();
        AddAccount(db);

        var result = ControllerFor(db).Login(new LoginRequest { Username = "alice", Password = "not it" });

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public void An_unknown_username_is_answered_exactly_like_a_wrong_password()
    {
        using var db = new TestDatabase();
        AddAccount(db);
        var controller = ControllerFor(db);

        var wrongPassword = controller.Login(new LoginRequest { Username = "alice", Password = "not it" });
        var noSuchUser = controller.Login(new LoginRequest { Username = "nobody", Password = "not it" });

        // Same status, same sentence. Anything that differs between these two is
        // a way to find out which usernames are real.
        Assert.IsType<UnauthorizedObjectResult>(noSuchUser);
        Assert.Equal(ApiResult.StatusOf(wrongPassword), ApiResult.StatusOf(noSuchUser));
        Assert.Equal(ApiResult.Message(wrongPassword), ApiResult.Message(noSuchUser));
    }

    [Fact]
    public void An_unknown_username_still_costs_a_password_verification()
    {
        using var db = new TestDatabase();
        AddAccount(db);
        var controller = ControllerFor(db);

        // Skipping the hash for an account that does not exist answered in about
        // a millisecond instead of the ~100 BCrypt costs, which sorts real
        // usernames from invented ones with nothing but a stopwatch. Timing is a
        // blunt thing to assert on, so this only insists the fast path is gone:
        // a run that skipped hashing entirely would come back far under this.
        var start = System.Diagnostics.Stopwatch.StartNew();
        controller.Login(new LoginRequest { Username = "nobody at all", Password = "not it" });
        var elapsed = start.Elapsed;

        Assert.True(
            elapsed > TimeSpan.FromMilliseconds(20),
            $"Login for an unknown username answered in {elapsed.TotalMilliseconds:F1}ms, which is too fast to have hashed anything.");
    }

    [Fact]
    public void Ten_wrong_passwords_lock_the_account_out_with_a_429()
    {
        using var db = new TestDatabase();
        AddAccount(db);
        var controller = ControllerFor(db);

        for (var i = 0; i < 10; i++)
        {
            controller.Login(new LoginRequest { Username = "alice", Password = "not it" });
        }

        var result = controller.Login(new LoginRequest { Username = "alice", Password = "not it" });

        Assert.Equal(StatusCodes.Status429TooManyRequests, ApiResult.StatusOf(result));
        Assert.Contains("Too many failed sign-in attempts", ApiResult.Message(result));
        Assert.False(string.IsNullOrEmpty(controller.Response.Headers.RetryAfter.ToString()));
    }

    [Fact]
    public void A_locked_out_account_is_refused_even_with_the_right_password()
    {
        using var db = new TestDatabase();
        AddAccount(db);
        var controller = ControllerFor(db);

        for (var i = 0; i < 10; i++)
        {
            controller.Login(new LoginRequest { Username = "alice", Password = "not it" });
        }

        var result = controller.Login(new LoginRequest { Username = "alice", Password = KnownPassword });

        Assert.Equal(StatusCodes.Status429TooManyRequests, ApiResult.StatusOf(result));
    }

    [Fact]
    public void Signing_in_before_the_limit_clears_the_failures()
    {
        using var db = new TestDatabase();
        AddAccount(db);
        var controller = ControllerFor(db);

        for (var i = 0; i < 9; i++)
        {
            controller.Login(new LoginRequest { Username = "alice", Password = "not it" });
        }

        Assert.IsType<OkObjectResult>(controller.Login(new LoginRequest { Username = "alice", Password = KnownPassword }));

        // The count is back to zero, so the next mistake is the first one again
        // rather than the tenth.
        for (var i = 0; i < 9; i++)
        {
            controller.Login(new LoginRequest { Username = "alice", Password = "not it" });
        }

        Assert.IsType<UnauthorizedObjectResult>(controller.Login(new LoginRequest { Username = "alice", Password = "not it" }));
    }

    [Fact]
    public void Guessing_at_one_account_does_not_lock_out_another()
    {
        using var db = new TestDatabase();
        AddAccount(db, "alice");
        AddAccount(db, "bob");
        var controller = ControllerFor(db);

        for (var i = 0; i < 10; i++)
        {
            controller.Login(new LoginRequest { Username = "alice", Password = "not it" });
        }

        Assert.IsType<OkObjectResult>(controller.Login(new LoginRequest { Username = "bob", Password = KnownPassword }));
    }

    [Fact]
    public void Logging_out_clears_the_session_cookie()
    {
        using var db = new TestDatabase();
        var controller = ControllerFor(db);

        controller.Logout();

        // The cookie is HttpOnly, so only the server can delete it - navigating
        // away would leave a valid session behind for the rest of its life.
        var setCookie = controller.Response.Headers.SetCookie.ToString();

        Assert.Contains(AuthCookie.Name, setCookie);
        Assert.Contains("expires=Thu, 01 Jan 1970", setCookie, StringComparison.OrdinalIgnoreCase);
    }
}
