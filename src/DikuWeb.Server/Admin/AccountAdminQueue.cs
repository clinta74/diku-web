using System.Threading.Channels;
using DikuWeb.Engine;
using DikuWeb.Engine.Protocol;

namespace DikuWeb.Server.Admin;

/// <summary>
/// The write side of the in-game admin command hand-off (PLAN.md §7.7). The loop calls
/// <see cref="Enqueue"/> and moves on.
/// </summary>
public sealed class AccountAdminQueue : IAccountAdminQueue
{
    private readonly Channel<AccountAdminRequest> _channel =
        Channel.CreateUnbounded<AccountAdminRequest>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });

    public ChannelReader<AccountAdminRequest> Reader => _channel.Reader;

    public void Enqueue(AccountAdminRequest request) => _channel.Writer.TryWrite(request);

    public void Complete() => _channel.Writer.TryComplete();
}

/// <summary>
/// Performs account administration requested from inside the game, off the loop thread, and
/// reports the result back to the session that asked (PLAN.md §7.7).
/// </summary>
/// <remarks>
/// The round trip is the whole point: the loop cannot read the account store, so the command
/// only ever enqueues. Without the reply, an admin typing <c>promote</c> would get silence
/// whether it worked, failed, or named somebody who does not exist.
/// </remarks>
public sealed class AccountAdminWorker(
    AccountAdminQueue queue,
    GameGateway gateway,
    IServiceScopeFactory scopeFactory,
    ILogger<AccountAdminWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Normal shutdown cancels the read; letting that escape would fault the host.
        try
        {
            await foreach (var request in queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await HandleAsync(request, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    ServerLog.AdminRequestFailed(logger, request.GetType().Name, ex);
                    Reply(request, "That could not be completed.", SysKinds.Warning);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private async Task HandleAsync(AccountAdminRequest request, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountAdminService>();

        switch (request)
        {
            case SetAccountRoleRequest change:
            {
                var result = await service.SetRoleAsync(
                    change.ActorAccountId, change.TargetUsername, change.Role, cancellationToken);

                Reply(
                    request,
                    result.Message,
                    result.Ok ? SysKinds.Info : SysKinds.Warning);

                if (result.Outcome == RoleChangeOutcome.Changed && result.TargetAccountId is { } id)
                {
                    ServerLog.RoleChanged(
                        logger,
                        change.TargetUsername,
                        result.Previous?.ToString() ?? "?",
                        result.Current?.ToString() ?? "?");

                    // Reaches a character already playing, so the verbs change immediately -
                    // which matters most when the change is a revocation.
                    gateway.TrySubmit(new SetActorRole { AccountId = id, Role = change.Role });
                }

                break;
            }

            case LookupAccountRequest lookup:
            {
                var account = await service.FindAsync(lookup.TargetUsername, cancellationToken);

                Reply(
                    request,
                    account is null
                        ? $"There is no account named '{lookup.TargetUsername}'."
                        : Describe(account),
                    account is null ? SysKinds.Warning : SysKinds.Info);

                break;
            }
        }
    }

    private static string Describe(AccountSummary account)
    {
        var characters = account.Characters.Count == 0
            ? "no characters"
            : string.Join(", ", account.Characters);

        var banned = account.IsBanned ? ", BANNED" : string.Empty;
        var seen = account.LastLoginAt is { } at ? at.ToString("u") : "never";

        return $"{account.Username} — {account.Role}{banned}. Last login {seen}. Characters: {characters}.";
    }

    /// <summary>
    /// Sends the answer back through the loop, because the loop owns the session's output
    /// channel and writing to it from here would be exactly the race §2.1 forbids.
    /// </summary>
    private void Reply(AccountAdminRequest request, string message, string kind) =>
        gateway.TrySubmit(new Notify
        {
            SessionId = request.ReplyToSessionId,
            Message = message,
            Kind = kind,
        });
}
