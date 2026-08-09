namespace Inventria.Models;

/// <summary>
/// The roles this system knows how to be. The set is closed on purpose: the
/// frontend routes on the role it gets back at login and has a screen for each
/// of these and nothing else, so an account created with any other string is one
/// that can authenticate and then has nowhere to go - a dead account that only
/// an Admin editing it can rescue.
///
/// These are consts rather than an enum so they can be used both as the
/// whitelist behind <c>UserRequest.Role</c> and in the <c>[Authorize(Roles =
/// ...)]</c> attributes that enforce them, which is what keeps the two from
/// drifting apart.
/// </summary>
public static class UserRoles
{
    public const string Admin = "Admin";
    public const string Employee = "Employee";
}
