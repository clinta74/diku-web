namespace DikuWeb.Server.Auth;

/// <summary>
/// What counts as an acceptable password, in one place.
/// </summary>
/// <remarks>
/// Three surfaces set a password — registration, a player changing their own, and an admin
/// resetting somebody else's — and a rule enforced at only two of them is not a rule. The admin
/// path is the one that would have been forgotten, and it is the one whose output someone else
/// has to live with.
///
/// Deliberately a length floor and nothing else. Composition rules (a digit, a symbol, mixed
/// case) push people towards <c>Password1!</c> and away from the long passphrases that actually
/// resist guessing, and the hash (PBKDF2, 100k iterations) plus the per-address attempt limit
/// are what carry the weight here.
/// </remarks>
public static class PasswordPolicy
{
    public const int MinimumLength = 8;

    public const int MaximumLength = 256;

    public const string Requirement =
        "Password must be at least 8 characters.";

    /// <summary>
    /// Validates a candidate, returning the message to show when it fails.
    /// </summary>
    /// <remarks>
    /// The upper bound is not cosmetic: PBKDF2 hashes whatever it is given, so an unbounded field
    /// lets one request spend arbitrary CPU on the thread pool — a cheap way to hurt a server whose
    /// world runs on a single loop.
    /// </remarks>
    public static bool IsAcceptable(string? password, out string error)
    {
        if (password is null || password.Length < MinimumLength)
        {
            error = Requirement;
            return false;
        }

        if (password.Length > MaximumLength)
        {
            error = $"Password must be at most {MaximumLength} characters.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
