using Muwbta.Domain.Accounts;
using Muwbta.Domain.Characters;
using Muwbta.Domain.Worlds;
using Muwbta.Server.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Muwbta.Server.Tests;

/// <summary>
/// Exercises the Postgres-specific parts of the schema that an in-memory provider would
/// silently let through: citext case-insensitivity, jsonb stat blocks, and uuidv7 keys.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PersistenceRoundTripTests(PostgresFixture postgres)
{
    private static readonly RoomKey StartRoom = RoomKey.Parse("aldenmoor.millbrook.north-gate");

    [Fact]
    public async Task Character_stat_blocks_round_trip_through_jsonb()
    {
        await using var db = postgres.CreateDbContext();

        var account = NewAccount();
        var character = new Character
        {
            AccountId = account.Id,
            Name = $"Kael{Guid.NewGuid():N}"[..20],
            Path = CharacterPath.Warden,
            Attributes = new AttributeSet
            {
                Might = 16,
                Agility = 12,
                Vitality = 14,
                Insight = 8,
                Resolve = 11,
            },
            Vitals = Vitals.StartingFor(CharacterPath.Warden),
            RoomKey = StartRoom,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Accounts.Add(account);
        db.Characters.Add(character);
        await db.SaveChangesAsync();

        await using var readContext = postgres.CreateDbContext();
        var loaded = await readContext.Characters
            .AsNoTracking()
            .SingleAsync(c => c.Id == character.Id);

        Assert.Equal(16, loaded.Attributes.Might);
        Assert.Equal(8, loaded.Attributes.Insight);
        Assert.Equal(-1, loaded.Attributes.InsightModifier);
        Assert.Equal(60, loaded.Vitals.HealthMax);
        Assert.Equal(StartRoom, loaded.RoomKey);
        Assert.Equal("aldenmoor", loaded.RoomKey.World);
        Assert.Equal("aldenmoor.millbrook", loaded.RoomKey.ZoneKey);
    }

    [Fact]
    public async Task Ids_are_uuid_version_7()
    {
        // PLAN.md §6: time-ordered keys so inserts land append-mostly in the index
        // instead of scattering the way UUIDv4 does.
        await using var db = postgres.CreateDbContext();

        var account = NewAccount();
        db.Accounts.Add(account);
        await db.SaveChangesAsync();

        var version = (account.Id.ToByteArray(bigEndian: true)[6] & 0xF0) >> 4;
        Assert.Equal(7, version);
    }

    [Fact]
    public async Task Account_usernames_are_case_insensitive()
    {
        // citext is why "Kael" and "kael" cannot both register. A varchar column would
        // let this through and the duplicate would only surface as player confusion.
        await using var db = postgres.CreateDbContext();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        db.Accounts.Add(NewAccount(username: $"Kael{suffix}"));
        await db.SaveChangesAsync();

        await using var second = postgres.CreateDbContext();
        second.Accounts.Add(NewAccount(username: $"kael{suffix}"));

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
        var postgresError = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresError.SqlState);
    }

    [Fact]
    public async Task Deleting_an_account_cascades_to_its_characters()
    {
        await using var db = postgres.CreateDbContext();

        var account = NewAccount();
        var character = new Character
        {
            AccountId = account.Id,
            Name = $"Doomed{Guid.NewGuid():N}"[..20],
            Path = CharacterPath.Temper,
            Attributes = AttributeSet.Baseline,
            Vitals = Vitals.StartingFor(CharacterPath.Temper),
            RoomKey = StartRoom,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Accounts.Add(account);
        db.Characters.Add(character);
        await db.SaveChangesAsync();

        db.Accounts.Remove(account);
        await db.SaveChangesAsync();

        await using var readContext = postgres.CreateDbContext();
        Assert.False(await readContext.Characters.AnyAsync(c => c.Id == character.Id));
    }

    private static Account NewAccount(string? username = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        return new Account
        {
            Email = $"player-{suffix}@example.test",
            Username = username ?? $"player{suffix}",
            PasswordHash = "not-a-real-hash",
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
