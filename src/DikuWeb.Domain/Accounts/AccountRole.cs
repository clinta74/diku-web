namespace DikuWeb.Domain.Accounts;

/// <summary>
/// PLAN.md Phase 2 moves role checks up from Operations because the world builder needs them.
/// Declared now so the column exists in the initial migration rather than arriving as an alter.
/// </summary>
public enum AccountRole
{
    Player = 0,
    Builder = 1,
    Moderator = 2,
    Admin = 3,
}

public static class AccountRoleExtensions
{
    /// <summary>
    /// Whether an account holding <paramref name="actor"/> may do something that requires
    /// <paramref name="required"/>.
    /// </summary>
    /// <remarks>
    /// Deliberately not <c>actor &gt;= required</c>. The numbers ascend, but the roles are not a
    /// single ladder: a Moderator is not a builder, and would wrongly satisfy a Builder
    /// requirement under a comparison. Admin is the only role that satisfies everything.
    ///
    /// This is the one definition of that rule. The HTTP authorization policies and the in-game
    /// command table both derive from it rather than each restating it, because two copies of an
    /// access rule is how they end up disagreeing.
    /// </remarks>
    public static bool Satisfies(this AccountRole actor, AccountRole required) =>
        required switch
        {
            AccountRole.Player => true,
            AccountRole.Builder => actor is AccountRole.Builder or AccountRole.Admin,
            AccountRole.Moderator => actor is AccountRole.Moderator or AccountRole.Admin,
            AccountRole.Admin => actor is AccountRole.Admin,
            _ => false,
        };

    /// <summary>Every role that satisfies the requirement, for building an authorization policy.</summary>
    public static string[] RolesSatisfying(AccountRole required) =>
        [.. Enum.GetValues<AccountRole>().Where(r => r.Satisfies(required)).Select(r => r.ToString())];
}
