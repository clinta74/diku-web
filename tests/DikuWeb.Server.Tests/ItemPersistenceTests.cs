using DikuWeb.Domain.Items;
using DikuWeb.Persistence;
using DikuWeb.Server.Infrastructure;
using DikuWeb.Server.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DikuWeb.Server.Tests;

/// <summary>
/// Items are the one entity the loop creates, mutates, <em>and</em> destroys at runtime, so the
/// save queue has to handle all three. These drive the real queue and worker against a real
/// Postgres: an in-memory provider would not reproduce the insert-vs-update distinction that
/// silently lost every newly bought item.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ItemPersistenceTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_newly_created_item_is_inserted_rather_than_lost()
    {
        // The regression: buying, quest rewards, and `spawn` all hand the queue an item that
        // has never been in the database. Issuing an UPDATE for it affects zero rows and throws
        // DbUpdateConcurrencyException, which took the whole batch down with it.
        var item = NewItem();

        await RunWorkerAsync(queue => queue.Enqueue(item));

        await using var read = postgres.CreateDbContext();
        var loaded = await read.ItemInstances.AsNoTracking().SingleOrDefaultAsync(i => i.Id == item.Id);

        Assert.NotNull(loaded);
        Assert.Equal(item.TemplateKey, loaded.TemplateKey);
        Assert.Equal(item.OwnerCharacterId, loaded.OwnerCharacterId);
    }

    [Fact]
    public async Task An_existing_item_is_updated_in_place()
    {
        var item = NewItem();
        await InsertAsync(item);

        var newOwner = Guid.CreateVersion7();
        item.OwnerCharacterId = newOwner;

        await RunWorkerAsync(queue => queue.Enqueue(item));

        await using var read = postgres.CreateDbContext();
        var rows = await read.ItemInstances.AsNoTracking().Where(i => i.Id == item.Id).ToListAsync();

        Assert.Single(rows);
        Assert.Equal(newOwner, rows[0].OwnerCharacterId);
    }

    [Fact]
    public async Task A_sold_item_leaves_no_row_behind()
    {
        // Selling removed the item from the world but left the row, still pointing at its
        // former owner - so it reappeared in the player's inventory after a restart.
        var item = NewItem();
        await InsertAsync(item);

        await RunWorkerAsync(queue => queue.EnqueueDelete(item.Id));

        await using var read = postgres.CreateDbContext();
        Assert.False(await read.ItemInstances.AsNoTracking().AnyAsync(i => i.Id == item.Id));
    }

    [Fact]
    public async Task Deleting_an_item_that_was_never_persisted_is_harmless()
    {
        // Buy then sell inside one flush window: the delete arrives for a row that does not
        // exist yet. That must not fault the worker, or every later save is lost with it.
        var survivor = NewItem();

        await RunWorkerAsync(queue =>
        {
            queue.EnqueueDelete(Guid.CreateVersion7());
            queue.Enqueue(survivor);
        });

        await using var read = postgres.CreateDbContext();
        Assert.True(await read.ItemInstances.AsNoTracking().AnyAsync(i => i.Id == survivor.Id));
    }

    [Fact]
    public async Task A_delete_wins_over_a_save_for_the_same_item_in_one_batch()
    {
        // Buying and selling between two flushes coalesces to a single job for that id. If the
        // save won, the sale would be undone by the next restart.
        var item = NewItem();

        await RunWorkerAsync(queue =>
        {
            queue.Enqueue(item);
            queue.EnqueueDelete(item.Id);
        });

        await using var read = postgres.CreateDbContext();
        Assert.False(await read.ItemInstances.AsNoTracking().AnyAsync(i => i.Id == item.Id));
    }

    [Fact]
    public async Task Flush_does_not_return_until_pending_saves_are_durable()
    {
        // What logout depends on. The marker used to complete while snapshots were still
        // queued behind it, so a player could quit before their items had actually landed.
        var item = NewItem();

        var queue = new ItemSaveQueue();
        var worker = new ItemSaveQueueWorker(
            new FixtureDbContextFactory(postgres),
            queue,
            NullLogger<ItemSaveQueueWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);

        try
        {
            queue.Enqueue(item);
            await queue.FlushAsync(CancellationToken.None);

            // No polling: if FlushAsync is honest, the row is already there.
            await using var read = postgres.CreateDbContext();
            Assert.True(await read.ItemInstances.AsNoTracking().AnyAsync(i => i.Id == item.Id));
        }
        finally
        {
            queue.Complete();
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Flush_on_a_closed_queue_returns_instead_of_hanging()
    {
        // A logout racing shutdown must not block for its whole timeout, nor throw.
        var queue = new ItemSaveQueue();
        queue.Complete();

        await queue.FlushAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static ItemInstance NewItem() => new()
    {
        TemplateKey = "rusty-dagger",
        TemplateName = "a rusty dagger",
        Icon = "/",
        OwnerCharacterId = Guid.CreateVersion7(),
        Value = 12,
    };

    private async Task InsertAsync(ItemInstance item)
    {
        await using var db = postgres.CreateDbContext();
        db.ItemInstances.Add(item);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Runs the real worker over whatever the callback enqueues, then flushes so the assertions
    /// never have to sleep or poll.
    /// </summary>
    private async Task RunWorkerAsync(Action<ItemSaveQueue> enqueue)
    {
        var queue = new ItemSaveQueue();
        var worker = new ItemSaveQueueWorker(
            new FixtureDbContextFactory(postgres),
            queue,
            NullLogger<ItemSaveQueueWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);

        try
        {
            enqueue(queue);
            await queue.FlushAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            queue.Complete();
            await worker.StopAsync(CancellationToken.None);
        }
    }

    private sealed class FixtureDbContextFactory(PostgresFixture fixture)
        : IDbContextFactory<DikuWebDbContext>
    {
        public DikuWebDbContext CreateDbContext() => fixture.CreateDbContext();
    }
}
