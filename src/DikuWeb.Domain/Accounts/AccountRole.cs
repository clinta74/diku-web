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
