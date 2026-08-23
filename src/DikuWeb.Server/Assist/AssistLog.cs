namespace DikuWeb.Server.Assist;

/// <summary>
/// Log messages for the builder assist, source-generated per PLAN.md §2.4.
/// </summary>
/// <remarks>
/// Not on the game loop, so the allocation argument in <c>ServerLog</c> is weaker here - but a
/// second way of logging in the same server is its own cost, and the counts below are the only
/// record of what the model actually did with its window.
/// </remarks>
internal static partial class AssistLog
{
    [LoggerMessage(
        EventId = 1600,
        Level = LogLevel.Information,
        Message = "Assist enabled: {Model} at {BaseUrl}")]
    public static partial void Enabled(ILogger logger, string model, string baseUrl);

    [LoggerMessage(
        EventId = 1601,
        Level = LogLevel.Debug,
        Message = "Asking {Model} to draft {RoomKey}")]
    public static partial void Requesting(ILogger logger, string model, string roomKey);

    /// <summary>
    /// The two counts worth keeping.
    /// </summary>
    /// <remarks>
    /// <c>PromptTokens</c> is the truncation canary: it should sit a little above the canon's
    /// 10,183 every time, and a number near the window means the prefix is being squeezed by the
    /// context behind it. A number near 4096 means somebody pointed the server at the base model.
    /// </remarks>
    [LoggerMessage(
        EventId = 1602,
        Level = LogLevel.Information,
        Message = "Drafted {RoomKey}: {PromptTokens} prompt tokens, {GeneratedTokens} generated")]
    public static partial void Generated(
        ILogger logger, string roomKey, int promptTokens, int generatedTokens);

    [LoggerMessage(
        EventId = 1603,
        Level = LogLevel.Warning,
        Message = "Assist failed for {RoomKey}")]
    public static partial void Failed(ILogger logger, string roomKey, Exception exception);
}
