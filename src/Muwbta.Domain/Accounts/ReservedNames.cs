namespace Muwbta.Domain.Accounts;

/// <summary>
/// Names nobody may register or play under, because they read as the people who run the place.
/// </summary>
/// <remarks>
/// <b>Why.</b> Three facts used to combine into the most likely way a player loses an account:
/// nothing stopped a character being called <c>Admin</c>, the <c>who</c> list showed no roles, and
/// a tell arrived as <c>{Name} tells you, '…'</c> with nothing else on the line. So
/// <c>Admin tells you, 'reply with your password to keep your characters'</c> was byte-for-byte
/// what a genuine staff message would look like, and there was no genuine one to compare it with.
/// Staff now wear their role on every line they send (<c>PlayerActor.TaggedName</c>); this is the
/// other half — nobody else gets to wear it as a name.
///
/// <b>Two lists.</b> The exact list is words that are only ever a claim of authority, and blocks
/// them whole. The anywhere list is the handful that cannot appear inside an honest name without
/// making the same claim — <c>admin</c> inside <c>Adminah</c> is still a tell that opens with
/// "Admin". It is short on purpose: <c>mod</c> inside <c>Modesty</c> and <c>gm</c> inside
/// <c>Sigmund</c> are exactly the false positives a substring rule produces, so those two are
/// exact-only.
///
/// Case-insensitive throughout, because the names it guards are compared that way in the database.
/// </remarks>
public static class ReservedNames
{
    private static readonly HashSet<string> Exact = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin", "administrator", "mod", "moderator", "gm", "staff", "system", "support",
        "server", "owner", "root", "sysop", "operator", "official", "help", "helpdesk",
        "dev", "developer", "muwbta", "reaches", "thereaches",
    };

    private static readonly string[] Anywhere =
        ["admin", "moderator", "muwbta", "sysop", "staff", "support"];

    /// <summary>Whether a username or character name reads as staff, and is therefore refused.</summary>
    public static bool IsReserved(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return Exact.Contains(name)
            || Anywhere.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase));
    }
}
