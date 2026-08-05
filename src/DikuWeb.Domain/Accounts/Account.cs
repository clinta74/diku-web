namespace DikuWeb.Domain.Accounts;

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

    public AccountRole Role { get; set; } = AccountRole.Player;

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? LastLoginAt { get; set; }

    public bool IsBanned { get; set; }

    public string? BanReason { get; set; }

    public ICollection<Characters.Character> Characters { get; init; } = [];
}
