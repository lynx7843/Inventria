using Inventria;
using Inventria.Controllers;
using Inventria.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using OtpNet;
using System.Text.Json;

namespace Inventria.Tests;

/// <summary>
/// The second step of signing in: an authenticator app, and the recovery codes
/// for when the phone holding it is not.
///
/// Two things here are not about the happy path and matter more than it. A TOTP
/// secret has to be stored in a form the server can read back, unlike a
/// password - so these check it is genuinely encrypted, and that the audit trail
/// the rest of the app writes on every update does not copy it out in the clear.
/// And a recovery code has to work exactly once.
/// </summary>
public class TwoFactorTests
{
    private const string KnownPassword = "correct horse battery staple";

    private static IConfiguration Configuration(bool withTotpKey = true) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "0123456789abcdef0123456789abcdef0123456789abcdef",
                ["Auth:CookieSecure"] = "false",
                [TotpSecretProtector.ConfigurationKey] = withTotpKey ? "a test key for encrypting totp secrets" : null
            })
            .Build();

    private static User AddAccount(TestDatabase db, string username = "alice")
    {
        var user = new User
        {
            Username = username,
            Password = BCrypt.Net.BCrypt.HashPassword(KnownPassword),
            Role = UserRoles.Employee
        };

        db.Context.Users.Add(user);
        db.Context.SaveChanges();
        return user;
    }

    private static TwoFactorController ControllerFor(TestDatabase db, User user, TotpSecretProtector? protector = null) =>
        new(db.Context, protector ?? TotpSecretProtector.FromConfiguration(Configuration()))
        {
            ControllerContext = ApiResult.SignedInAs(user.Username, id: user.Id)
        };

    /// <summary>Runs enrolment through to "switched on", returning the recovery codes.</summary>
    private static (byte[] Secret, List<string> RecoveryCodes) Enrol(TestDatabase db, User user)
    {
        var controller = ControllerFor(db, user);

        var enrolled = controller.Enroll(new ConfirmPasswordRequest { CurrentPassword = KnownPassword });
        var base32 = ApiResult.Text(enrolled, "Secret");
        var secret = Base32Encoding.ToBytes(base32);

        var confirmed = controller.Confirm(new TwoFactorCodeRequest { Code = new Totp(secret).ComputeTotp() });
        Assert.IsType<OkObjectResult>(confirmed);

        var codes = ApiResult.Property(ApiResult.Body(confirmed), "RecoveryCodes")
            .EnumerateArray().Select(c => c.GetString()!).ToList();

        // Confirming spends the code it was given, on purpose - see
        // Confirming_spends_the_code_that_switched_it_on. Every test below is
        // about the sign-ins that come afterwards, minutes or days later, so
        // the step is cleared here rather than each of them having to wait out
        // a real thirty seconds to get past a guard they are not testing.
        // On db.Context, not a fresh one: the controllers under test share that
        // context, and its change tracker would keep handing back the instance
        // it already has - a write through a second context would be invisible
        // to them.
        db.Context.Users.Single(u => u.Id == user.Id).TotpLastUsedStep = null;
        db.Context.SaveChanges();

        return (secret, codes);
    }

    private static AuthController AuthFor(TestDatabase db, PendingTwoFactorLogins pending) =>
        AuthenticationTests.ControllerFor(db, pending: pending, configuration: Configuration());

    /// <summary>
    /// Whether a session cookie was actually set. Via Count rather than
    /// Assert.Empty on the header: StringValues casts implicitly to a string,
    /// which is null when the header is absent, so the obvious spelling asserts
    /// on the wrong thing and throws instead of passing.
    /// </summary>
    private static void AssertSignedIn(AuthController auth) =>
        Assert.Contains(auth.Response.Headers.SetCookie.ToArray(), c => c!.Contains(AuthCookie.Name));

    private static void AssertNotSignedIn(AuthController auth) =>
        Assert.DoesNotContain(auth.Response.Headers.SetCookie.ToArray(), c => c!.Contains(AuthCookie.Name));

    // --- ENROLMENT -----------------------------------------------------------

    [Fact]
    public void Enrolling_needs_the_accounts_own_password()
    {
        using var db = new TestDatabase();
        var user = AddAccount(db);

        var result = ControllerFor(db, user).Enroll(new ConfirmPasswordRequest { CurrentPassword = "not it" });

        Assert.IsType<BadRequestObjectResult>(result);

        // Whoever is holding the session cookie is not necessarily the account
        // holder, and enrolling their own phone is how a borrowed session
        // becomes a permanent one.
        using var check = db.NewContext();
        Assert.Null(check.Users.Single().TotpPendingSecret);
    }

    [Fact]
    public void A_server_with_no_encryption_key_refuses_to_enrol_rather_than_storing_a_bare_secret()
    {
        using var db = new TestDatabase();
        var user = AddAccount(db);
        var unconfigured = TotpSecretProtector.FromConfiguration(Configuration(withTotpKey: false));

        var result = ControllerFor(db, user, unconfigured)
            .Enroll(new ConfirmPasswordRequest { CurrentPassword = KnownPassword });

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, ApiResult.StatusOf(result));

        using var check = db.NewContext();
        Assert.Null(check.Users.Single().TotpPendingSecret);
    }

    [Fact]
    public void Enrolling_does_not_switch_anything_on_until_a_code_confirms_it()
    {
        using var db = new TestDatabase();
        var user = AddAccount(db);
        var controller = ControllerFor(db, user);

        controller.Enroll(new ConfirmPasswordRequest { CurrentPassword = KnownPassword });

        // The whole reason enrolment is two steps: an account that required a
        // code from an app that never actually scanned the QR would be locked
        // out by its own security setting.
        using (var check = db.NewContext())
        {
            var stored = check.Users.Single();
            Assert.NotNull(stored.TotpPendingSecret);
            Assert.Null(stored.TotpSecret);
            Assert.Null(stored.TotpEnabledAt);
        }

        var wrongCode = controller.Confirm(new TwoFactorCodeRequest { Code = "000000" });
        Assert.IsType<BadRequestObjectResult>(wrongCode);

        using (var check = db.NewContext())
        {
            Assert.Null(check.Users.Single().TotpEnabledAt);
        }
    }

    [Fact]
    public void Confirming_switches_it_on_and_issues_a_full_set_of_recovery_codes()
    {
        using var db = new TestDatabase();
        var user = AddAccount(db);

        var (_, codes) = Enrol(db, user);

        Assert.Equal(TwoFactor.RecoveryCodeCount, codes.Count);
        Assert.Equal(codes.Count, codes.Distinct().Count());

        using var check = db.NewContext();
        var stored = check.Users.Single();
        Assert.NotNull(stored.TotpSecret);
        Assert.NotNull(stored.TotpEnabledAt);

        // Promoted, not copied - a leftover pending secret is a second working
        // key to the account that nobody is tracking.
        Assert.Null(stored.TotpPendingSecret);

        Assert.Equal(TwoFactor.RecoveryCodeCount, check.RecoveryCodes.Count());
    }

    [Fact]
    public void Confirming_spends_the_code_that_switched_it_on()
    {
        using var db = new TestDatabase();
        var user = AddAccount(db);
        var controller = ControllerFor(db, user);

        var enrolled = controller.Enroll(new ConfirmPasswordRequest { CurrentPassword = KnownPassword });
        var secret = Base32Encoding.ToBytes(ApiResult.Text(enrolled, "Secret"));
        var code = new Totp(secret).ComputeTotp();

        controller.Confirm(new TwoFactorCodeRequest { Code = code });

        // The six digits that proved the phone had scanned the QR must not also
        // be the six digits that sign someone in a second later - anyone who
        // read them over a shoulder during setup would otherwise have most of a
        // minute to use them.
        var auth = AuthFor(db, new PendingTwoFactorLogins(new MemoryCache(new MemoryCacheOptions())));
        var result = auth.LoginTwoFactor(new TwoFactorLoginRequest
        {
            Ticket = LoginAndExpectPrompt(auth, user.Username),
            Code = code
        });

        Assert.IsType<UnauthorizedObjectResult>(result);
        AssertNotSignedIn(auth);
    }

    // --- STORAGE -------------------------------------------------------------

    [Fact]
    public void The_secret_is_encrypted_in_the_database()
    {
        using var db = new TestDatabase();
        var user = AddAccount(db);

        var (secret, _) = Enrol(db, user);
        var base32 = Base32Encoding.ToString(secret);

        using var check = db.NewContext();
        var stored = check.Users.Single().TotpSecret!;

        // Anyone who reads this column in the clear can mint valid codes for
        // every account, forever, without leaving a trace - worse than reading
        // the password hashes, which still have to be cracked.
        Assert.DoesNotContain(base32, stored, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Convert.ToHexString(secret), stored, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Convert.ToBase64String(secret), stored, StringComparison.Ordinal);
    }

    [Fact]
    public void A_secret_cannot_be_moved_to_another_account()
    {
        var protector = TotpSecretProtector.FromConfiguration(Configuration());
        var secret = TwoFactor.NewSecret();

        var forAlice = protector.Protect(secret, userId: 1);

        Assert.Equal(secret, protector.Unprotect(forAlice, userId: 1));

        // Pasting one account's row onto another - which is a database edit,
        // the same access this whole scheme assumes an attacker might get - must
        // not give that account a working second factor on the first one's phone.
        Assert.Null(protector.Unprotect(forAlice, userId: 2));
    }

    [Fact]
    public void A_different_encryption_key_cannot_read_the_secret()
    {
        var protector = TotpSecretProtector.FromConfiguration(Configuration());
        var secret = TwoFactor.NewSecret();
        var stored = protector.Protect(secret, userId: 1);

        var other = TotpSecretProtector.FromConfiguration(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [TotpSecretProtector.ConfigurationKey] = "a completely different key"
            })
            .Build());

        // Null rather than a throw, and null rather than twenty wrong bytes:
        // the account falls back to its recovery codes instead of being told
        // its correct code is wrong.
        Assert.Null(other.Unprotect(stored, userId: 1));
    }

    [Fact]
    public void The_audit_trail_records_that_the_secret_changed_but_never_what_it_is()
    {
        using var db = new TestDatabase();
        var user = AddAccount(db);

        var (secret, codes) = Enrol(db, user);
        var base32 = Base32Encoding.ToString(secret);

        using var check = db.NewContext();
        var audited = check.AuditLogs.ToList();

        // Every update anywhere in this app writes an audit row carrying the
        // before and after of each changed column. Left alone, switching on
        // two-factor would copy the encrypted secret and every recovery code
        // hash into a second table - a table whose whole purpose is being read
        // later, by more people than can read Users.
        Assert.NotEmpty(audited);

        foreach (var log in audited)
        {
            var text = $"{log.OldValue} {log.NewValue}";
            Assert.DoesNotContain(base32, text, StringComparison.OrdinalIgnoreCase);

            foreach (var code in codes)
            {
                Assert.DoesNotContain(TwoFactor.HashRecoveryCode(code), text, StringComparison.OrdinalIgnoreCase);
            }
        }

        // And it still says something happened, which is the part worth keeping.
        Assert.Contains(audited, log => log.Entity == nameof(User) && log.Action == "Update");
    }

    // --- SIGNING IN ----------------------------------------------------------

    private static string LoginAndExpectPrompt(AuthController auth, string username)
    {
        var result = auth.Login(new LoginRequest { Username = username, Password = KnownPassword });
        var body = ApiResult.Body(result);

        Assert.True(ApiResult.Property(body, "twoFactorRequired").GetBoolean());

        // Nothing resembling a session comes out of step one.
        AssertNotSignedIn(auth);

        return ApiResult.Property(body, "ticket").GetString()!;
    }

    [Fact]
    public void A_correct_password_alone_no_longer_signs_in_an_enrolled_account()
    {
        using var db = new TestDatabase();
        var user = AddAccount(db);
        var (secret, _) = Enrol(db, user);

        var pending = new PendingTwoFactorLogins(new MemoryCache(new MemoryCacheOptions()));
        var auth = AuthFor(db, pending);

        var ticket = LoginAndExpectPrompt(auth, user.Username);

        var finished = auth.LoginTwoFactor(new TwoFactorLoginRequest
        {
            Ticket = ticket,
            Code = new Totp(secret).ComputeTotp()
        });

        Assert.IsType<OkObjectResult>(finished);
        AssertSignedIn(auth);
    }

    [Fact]
    public void A_wrong_code_does_not_sign_in_and_burns_the_ticket()
    {
        using var db = new TestDatabase();
        var user = AddAccount(db);
        var (secret, _) = Enrol(db, user);

        var pending = new PendingTwoFactorLogins(new MemoryCache(new MemoryCacheOptions()));
        var auth = AuthFor(db, pending);
        var ticket = LoginAndExpectPrompt(auth, user.Username);

        Assert.IsType<UnauthorizedObjectResult>(
            auth.LoginTwoFactor(new TwoFactorLoginRequest { Ticket = ticket, Code = "000000" }));

        // One ticket, one attempt. Otherwise a single password check buys an
        // unlimited run at six digits.
        var retry = auth.LoginTwoFactor(new TwoFactorLoginRequest
        {
            Ticket = ticket,
            Code = new Totp(secret).ComputeTotp()
        });

        Assert.IsType<UnauthorizedObjectResult>(retry);
        AssertNotSignedIn(auth);
    }

    [Fact]
    public void The_same_code_cannot_be_used_twice()
    {
        using var db = new TestDatabase();
        var user = AddAccount(db);
        var (secret, _) = Enrol(db, user);

        var pending = new PendingTwoFactorLogins(new MemoryCache(new MemoryCacheOptions()));
        var code = new Totp(secret).ComputeTotp();

        var first = AuthFor(db, pending);
        Assert.IsType<OkObjectResult>(first.LoginTwoFactor(new TwoFactorLoginRequest
        {
            Ticket = LoginAndExpectPrompt(first, user.Username),
            Code = code
        }));

        // A code is live for its own 30-second step plus the drift window
        // either side, so without this it keeps working for about a minute and
        // a half after it was read over someone's shoulder.
        var second = AuthFor(db, pending);
        var replay = second.LoginTwoFactor(new TwoFactorLoginRequest
        {
            Ticket = LoginAndExpectPrompt(second, user.Username),
            Code = code
        });

        Assert.IsType<UnauthorizedObjectResult>(replay);
        AssertNotSignedIn(second);
    }

    [Fact]
    public void An_unknown_ticket_cannot_finish_a_sign_in()
    {
        using var db = new TestDatabase();
        var user = AddAccount(db);
        var (secret, _) = Enrol(db, user);

        var auth = AuthFor(db, new PendingTwoFactorLogins(new MemoryCache(new MemoryCacheOptions())));

        var result = auth.LoginTwoFactor(new TwoFactorLoginRequest
        {
            Ticket = "0000000000000000000000000000000000000000000000000000000000000000",
            Code = new Totp(secret).ComputeTotp()
        });

        Assert.IsType<UnauthorizedObjectResult>(result);
        AssertNotSignedIn(auth);
    }

    // --- RECOVERY CODES ------------------------------------------------------

    [Fact]
    public void A_recovery_code_signs_in_a_phone_that_is_gone_exactly_once()
    {
        using var db = new TestDatabase();
        var user = AddAccount(db);
        var (_, codes) = Enrol(db, user);

        var pending = new PendingTwoFactorLogins(new MemoryCache(new MemoryCacheOptions()));

        var first = AuthFor(db, pending);
        var result = first.LoginTwoFactor(new TwoFactorLoginRequest
        {
            Ticket = LoginAndExpectPrompt(first, user.Username),
            Code = codes[0]
        });

        Assert.IsType<OkObjectResult>(result);
        AssertSignedIn(first);

        // Said out loud, because somebody signing in this way has lost their
        // phone and is working through a pile that does not refill itself.
        var body = ApiResult.Body(result);
        Assert.True(ApiResult.Property(body, "usedRecoveryCode").GetBoolean());
        Assert.Equal(TwoFactor.RecoveryCodeCount - 1, ApiResult.Property(body, "recoveryCodesRemaining").GetInt32());

        var second = AuthFor(db, pending);
        var reuse = second.LoginTwoFactor(new TwoFactorLoginRequest
        {
            Ticket = LoginAndExpectPrompt(second, user.Username),
            Code = codes[0]
        });

        Assert.IsType<UnauthorizedObjectResult>(reuse);
        AssertNotSignedIn(second);
    }

    [Fact]
    public void A_recovery_code_is_accepted_however_it_was_written_down()
    {
        using var db = new TestDatabase();
        var user = AddAccount(db);
        var (_, codes) = Enrol(db, user);

        // Lowercase, spaces for the dashes - how it comes back from somebody
        // reading it off a card. The grouping and the case are presentation.
        var retyped = codes[0].Replace("-", " ").ToLowerInvariant();

        var auth = AuthFor(db, new PendingTwoFactorLogins(new MemoryCache(new MemoryCacheOptions())));
        var result = auth.LoginTwoFactor(new TwoFactorLoginRequest
        {
            Ticket = LoginAndExpectPrompt(auth, user.Username),
            Code = retyped
        });

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public void Recovery_codes_are_stored_hashed()
    {
        using var db = new TestDatabase();
        var user = AddAccount(db);
        var (_, codes) = Enrol(db, user);

        using var check = db.NewContext();
        var stored = check.RecoveryCodes.Select(c => c.CodeHash).ToList();

        foreach (var code in codes)
        {
            Assert.DoesNotContain(TwoFactor.Normalize(code), stored);
            Assert.Contains(TwoFactor.HashRecoveryCode(code), stored);
        }
    }

    [Fact]
    public void Reissuing_recovery_codes_invalidates_the_old_ones()
    {
        using var db = new TestDatabase();
        var user = AddAccount(db);
        var (_, original) = Enrol(db, user);

        var reissued = ControllerFor(db, user)
            .RegenerateRecoveryCodes(new ConfirmPasswordRequest { CurrentPassword = KnownPassword });

        var fresh = ApiResult.Property(ApiResult.Body(reissued), "RecoveryCodes")
            .EnumerateArray().Select(c => c.GetString()!).ToList();

        Assert.Equal(TwoFactor.RecoveryCodeCount, fresh.Count);
        Assert.Empty(fresh.Intersect(original));

        // The point of reissuing is usually that the old card is somewhere it
        // should not be, so leaving it working would achieve nothing.
        var auth = AuthFor(db, new PendingTwoFactorLogins(new MemoryCache(new MemoryCacheOptions())));
        var result = auth.LoginTwoFactor(new TwoFactorLoginRequest
        {
            Ticket = LoginAndExpectPrompt(auth, user.Username),
            Code = original[0]
        });

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public void Reissuing_recovery_codes_needs_the_accounts_own_password()
    {
        using var db = new TestDatabase();
        var user = AddAccount(db);
        var (_, original) = Enrol(db, user);

        var result = ControllerFor(db, user)
            .RegenerateRecoveryCodes(new ConfirmPasswordRequest { CurrentPassword = "not it" });

        Assert.IsType<BadRequestObjectResult>(result);

        using var check = db.NewContext();
        Assert.Contains(TwoFactor.HashRecoveryCode(original[0]), check.RecoveryCodes.Select(c => c.CodeHash));
    }

    // --- TURNING IT OFF ------------------------------------------------------

    [Fact]
    public void Disabling_needs_the_accounts_own_password()
    {
        using var db = new TestDatabase();
        var user = AddAccount(db);
        Enrol(db, user);

        var result = ControllerFor(db, user).Disable(new ConfirmPasswordRequest { CurrentPassword = "not it" });

        Assert.IsType<BadRequestObjectResult>(result);

        using var check = db.NewContext();
        Assert.NotNull(check.Users.Single().TotpEnabledAt);
    }

    [Fact]
    public void Disabling_destroys_the_secret_and_every_recovery_code()
    {
        using var db = new TestDatabase();
        var user = AddAccount(db);
        Enrol(db, user);

        var result = ControllerFor(db, user).Disable(new ConfirmPasswordRequest { CurrentPassword = KnownPassword });

        Assert.IsType<OkObjectResult>(result);

        using var check = db.NewContext();
        var stored = check.Users.Single();
        Assert.Null(stored.TotpSecret);
        Assert.Null(stored.TotpPendingSecret);
        Assert.Null(stored.TotpEnabledAt);
        Assert.Null(stored.TotpLastUsedStep);

        // Codes left behind would still open an account that no longer expects
        // a second factor at all.
        Assert.Empty(check.RecoveryCodes);

        // And the password on its own signs in again.
        var auth = AuthFor(db, new PendingTwoFactorLogins(new MemoryCache(new MemoryCacheOptions())));
        var login = auth.Login(new LoginRequest { Username = user.Username, Password = KnownPassword });
        AssertSignedIn(auth);
        Assert.IsType<OkObjectResult>(login);
    }

    [Fact]
    public void Deleting_an_account_takes_its_recovery_codes_with_it()
    {
        using var db = new TestDatabase();
        var user = AddAccount(db);
        Enrol(db, user);

        using (var deleting = db.NewContext())
        {
            deleting.Users.Remove(deleting.Users.Single(u => u.Id == user.Id));
            deleting.SaveChanges();
        }

        using var check = db.NewContext();
        Assert.Empty(check.RecoveryCodes);
    }

    // --- STATUS --------------------------------------------------------------

    [Fact]
    public void The_settings_page_can_see_whether_it_is_on_and_how_many_codes_are_left()
    {
        using var db = new TestDatabase();
        var user = AddAccount(db);

        var before = ApiResult.Body(ControllerFor(db, user).GetStatus());
        Assert.False(ApiResult.Property(before, "Enabled").GetBoolean());
        Assert.Equal(0, ApiResult.Property(before, "RecoveryCodesRemaining").GetInt32());

        var (_, codes) = Enrol(db, user);

        var auth = AuthFor(db, new PendingTwoFactorLogins(new MemoryCache(new MemoryCacheOptions())));
        auth.LoginTwoFactor(new TwoFactorLoginRequest
        {
            Ticket = LoginAndExpectPrompt(auth, user.Username),
            Code = codes[0]
        });

        var after = ApiResult.Body(ControllerFor(db, user).GetStatus());
        Assert.True(ApiResult.Property(after, "Enabled").GetBoolean());
        Assert.Equal(TwoFactor.RecoveryCodeCount - 1, ApiResult.Property(after, "RecoveryCodesRemaining").GetInt32());
    }
}
