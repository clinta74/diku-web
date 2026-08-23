namespace DikuWeb.Server.Assist;

/// <summary>
/// Where the builder assist's model lives and what it is allowed to cost.
/// </summary>
/// <remarks>
/// <b><see cref="Enabled"/> defaults to false, and that is the important line in this file.</b>
/// PLAN.md §13's rule is that the builder degrades to a plain textarea: Save is never gated on the
/// assistant. A server with no inference behind it has to start, run, and let people build exactly
/// as it did before, so the feature is off unless somebody turns it on. Development turns it on in
/// <c>appsettings.Development.json</c>, pointing at a local container.
/// </remarks>
public sealed class AssistOptions
{
    public const string Section = "Assist";

    /// <summary>Whether the assist endpoints exist at all.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The Ollama server. <c>http://ollama:11434</c> in compose; localhost when a developer is
    /// running the server outside the network and the container publishes its port.
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>
    /// The derived model, never the base.
    /// </summary>
    /// <remarks>
    /// <c>gemma3:12b</c> would answer to this and would answer at 4096 tokens, silently truncating
    /// the canon (tools/ollama/README.md). The name of the model is load-bearing.
    /// </remarks>
    public string Model { get; set; } = "dikuweb-builder";

    /// <summary>
    /// How long one generation may take before it is abandoned.
    /// </summary>
    /// <remarks>
    /// Measured: a room description is ~230 tokens at 1.3-1.8 tok/s, so about three minutes on a
    /// 20-thread desktop and expected to be worse on the NAS's four cores. Ten minutes is a
    /// ceiling for a stuck call rather than a budget - the queue is what makes a slow answer
    /// tolerable, not this.
    /// </remarks>
    public int TimeoutSeconds { get; set; } = 600;

    /// <summary>
    /// How many jobs may be waiting.
    /// </summary>
    /// <remarks>
    /// Small on purpose. <c>OLLAMA_NUM_PARALLEL</c> is 1 and one job takes minutes, so a deep queue
    /// only promises people an answer they will not wait for. Refusing the ninth request says
    /// something true; accepting it would not.
    /// </remarks>
    public int MaxQueued { get; set; } = 8;

    /// <summary>How long a finished job's result stays readable before it is swept.</summary>
    public int JobRetentionMinutes { get; set; } = 30;
}
