using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Inventria.Tests;

/// <summary>
/// Reading what an action actually answered with.
///
/// The controllers return anonymous types, which are internal to the assembly
/// that declares them - `dynamic` cannot see through that from a test project.
/// Serialising to JSON and reading the result sidesteps the whole question, and
/// has the side benefit of asserting on the shape the frontend really receives
/// rather than on a C# object it never sees.
/// </summary>
public static class ApiResult
{
    public static JsonElement Body(IActionResult result)
    {
        var value = Assert.IsAssignableFrom<ObjectResult>(result).Value;
        return JsonSerializer.SerializeToElement(value);
    }

    /// <summary>The `message` every error in this API carries.</summary>
    public static string Message(IActionResult result) => Text(result, "Message");

    public static string Text(IActionResult result, string property) =>
        Property(Body(result), property).GetString() ?? string.Empty;

    public static int Number(IActionResult result, string property) =>
        Property(Body(result), property).GetInt32();

    public static JsonElement Property(JsonElement body, string name)
    {
        // Case-insensitively, because whether these come back PascalCase or
        // camelCase is a serializer setting and not what any of these tests are
        // about.
        foreach (var property in body.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        throw new Xunit.Sdk.XunitException($"No '{name}' in response: {body}");
    }

    public static int StatusOf(IActionResult result) =>
        Assert.IsAssignableFrom<ObjectResult>(result).StatusCode
            ?? throw new Xunit.Sdk.XunitException("Result carried no status code.");

    /// <summary>
    /// The controller context a signed-in caller arrives with. Movements are
    /// stamped with the name on the token, so an action that writes one needs a
    /// principal to read it from.
    /// </summary>
    public static ControllerContext SignedInAs(string username, string role = "Employee")
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, username), new Claim(ClaimTypes.Role, role)],
            authenticationType: "Test");

        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
    }
}
