namespace Muwbta.Domain.Accounts;

/// <summary>
/// A login. Owns one or more <see cref="Characters.Character"/>.
/// </summary>
public sealed class Account
{
    /// <summary>
    /// UUIDv7: time-ordered, so inserts land append-mostly in the index instead of
    /// scattering across it the way UUIDv4 does (PLAN.md §6).
    /// </summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Stored as citext, so Kael@x.com and kael@x.com cannot both register.</summary>
    public required string Email { get; set; }

    /// <summary>Stored as citext. Distinct from character names.</summary>
    public required string Username { get; set; }

    /// <summary>PBKDF2 via ASP.NET Core PasswordHasher. Never logged (PLAN.md §2.4).</summary>
    public required string PasswordHash { get; set; }

    /// <summary>
    /// When the password last changed. Every auth cookie issued before this moment stops being a
    /// credential (PLAN.md §7.7).
    /// </summary>
    /// <remarks>
    /// A password change that leaves other sessions signed in is not much of a password change —
    /// the case it exists for is "somebody else has my password", and a fortnight-long cookie
    /// would outlive the fix. A timestamp rather than a random stamp because the cookie already
    /// carries its issue time, so the comparison needs nothing new in the ticket.
    ///
    /// Null for accounts whose password has never changed since registration, which is the same
    /// thing as "no cookie can be too old".
    /// </remarks>
    public DateTimeOffset? PasswordChangedAt { get; set; }

    public AccountRole Role { get; set; } = AccountRole.Player;

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? LastLoginAt { get; set; }

    public bool IsBanned { get; set; }

    public string? BanReason { get; set; }

    /// <summary>
    /// Silenced on the player-to-player channels until this moment (PLAN.md §8, Phase 6).
    /// </summary>
    /// <remarks>
    /// A time rather than a flag, so a mute expires on its own. The moderation action people
    /// actually want is "cool off for an hour", and an indefinite mute somebody has to remember
    /// to lift is one that does not get lifted.
    ///
    /// Null means not muted, and a time in the past means the same thing — the row is not cleaned
    /// up when it expires, because doing so would need a sweep whose only job is tidiness.
    /// </remarks>
    public DateTimeOffset? MutedUntil { get; set; }

    public ICollection<Characters.Character> Characters { get; init; } = [];
}
