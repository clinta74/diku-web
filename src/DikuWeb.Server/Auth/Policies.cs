using DikuWeb.Domain.Accounts;
using Microsoft.AspNetCore.Authorization;

namespace DikuWeb.Server.Auth;

/// <summary>
/// PLAN.md Phase 2 pulls roles up from Operations, because the world builder is the first
/// thing that needs them.
/// </summary>
public static class Policies
{
    public const string Builder = "builder";
    public const string Moderator = "moderator";
    public const string Admin = "admin";

    public static void AddDikuWebPolicies(this AuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Derived from AccountRoleExtensions.Satisfies rather than restated here, so the HTTP
        // policies and the in-game command table cannot drift apart. The roles are not a single
        // ladder - a Moderator is not a builder - which is exactly the kind of detail that goes
        // wrong when it is written down twice.
        options.AddPolicy(Builder, policy => policy.RequireRole(
            AccountRoleExtensions.RolesSatisfying(AccountRole.Builder)));

        options.AddPolicy(Moderator, policy => policy.RequireRole(
            AccountRoleExtensions.RolesSatisfying(AccountRole.Moderator)));

        options.AddPolicy(Admin, policy => policy.RequireRole(
            AccountRoleExtensions.RolesSatisfying(AccountRole.Admin)));
    }
}
