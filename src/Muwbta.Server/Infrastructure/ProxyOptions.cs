using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

// KnownIPNetworks takes System.Net's IPNetwork; HttpOverrides declares an older one of the same
// name for the obsolete KnownNetworks list, and the two collide on the bare identifier.
using IPNetwork = System.Net.IPNetwork;

namespace Muwbta.Server.Infrastructure;

/// <summary>
/// Which proxies' <c>X-Forwarded-*</c> headers to believe, bound from the <c>Proxy</c> section.
/// </summary>
/// <remarks>
/// <b>Why this exists.</b> Behind the nginx front end every request arrives from the proxy's
/// address over plain HTTP, whatever the caller's address and scheme were. Three things read
/// those two values and were wrong for as long as the headers went unread: the auth rate limit
/// partitions by address, so it was one shared bucket for the whole site — ten sign-ins a minute
/// between everybody, and a lockout for everybody from anyone who sent ten; the session cookie's
/// <c>Secure</c> flag follows the request scheme, so it was never set; and moderation had no
/// address to record.
///
/// <b>Why it is a trust list and not a switch.</b> Believing the headers from <em>any</em> source
/// is worse than ignoring them: a caller who can reach the port directly sets a fresh
/// <c>X-Forwarded-For</c> on every request and is never rate limited at all. So the middleware is
/// told exactly which addresses may speak for a client, and a header from anywhere else is left
/// alone. Nothing configured means nothing trusted, which is the behaviour before this existed.
///
/// <b>Two hops, walked from the inside out.</b> With a TLS terminator in front of the compose
/// nginx the header reads <c>client, terminator</c>: the compose nginx appended the terminator's
/// address as it forwarded. The walk starts at the connection — the compose network — and steps
/// left through each trusted address until it reaches one it does not trust, which is the client.
/// The terminator therefore has to be listed too, or the walk stops at it and every player still
/// shares one address, one hop further out than before.
///
/// Strings rather than arrays, because <c>Proxy__KnownProxies__0</c> is a worse thing to type into
/// a compose file than <c>Proxy__KnownProxies: 10.0.0.5</c>, and because the compose-key test
/// reads a flat key per setting.
/// </remarks>
public sealed class ProxyOptions
{
    public const string Section = "Proxy";

    /// <summary>
    /// Networks whose members may set forwarded headers, as comma-separated CIDRs — normally the
    /// compose network the front-end nginx lives on.
    /// </summary>
    public string? KnownNetworks { get; set; }

    /// <summary>
    /// Individual addresses that may set forwarded headers, comma-separated — a TLS terminator
    /// outside the compose network, if there is one.
    /// </summary>
    public string? KnownProxies { get; set; }

    /// <summary>
    /// Configures the forwarded-headers middleware from this section, trusting nothing that is not
    /// listed here.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A value that is not an address or a network. Refusing to start is the right answer for a
    /// trust boundary: a typo that silently trusted nothing would leave the site-wide rate limit in
    /// place while the configuration read as fixed.
    /// </exception>
    public void Apply(ForwardedHeadersOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // The framework's rule, which is the opposite of what an empty trust list reads as: when
        // KnownProxies and KnownIPNetworks are BOTH empty the middleware skips the address check
        // and believes the headers from anyone. That is precisely the configuration that lets a
        // direct caller invent a new address per request. So nothing configured has to mean the
        // middleware does nothing at all - not "trusts nobody", which the framework cannot express
        // through the lists alone. Found by the test that asserts a header from an untrusted
        // source is ignored; without this it was honoured.
        if (!TrustsAnything)
        {
            options.ForwardedHeaders = ForwardedHeaders.None;
            return;
        }

        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

        // The defaults trust loopback, which is right for a proxy on the same host and wrong for a
        // list that claims to be exhaustive. Everything trusted is named below or not at all.
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();

        // No cap on the number of hops walked: the trust list bounds it. A limit of one — the
        // framework's default — would stop at the compose nginx and never reach the terminator's
        // entry behind it.
        options.ForwardLimit = null;

        foreach (var network in Networks())
        {
            options.KnownIPNetworks.Add(network);
        }

        foreach (var proxy in Proxies())
        {
            options.KnownProxies.Add(proxy);
        }
    }

    /// <summary>Whether anything at all is trusted — for the startup log.</summary>
    public bool TrustsAnything => Networks().Count > 0 || Proxies().Count > 0;

    /// <summary>What is trusted, in one line, so the log can say so at startup.</summary>
    public string Describe()
    {
        var parts = Networks().Select(n => n.ToString()).Concat(Proxies().Select(p => p.ToString())).ToList();
        return parts.Count == 0 ? "(none - forwarded headers are ignored)" : string.Join(", ", parts);
    }

    private IReadOnlyList<IPNetwork> Networks() =>
        [.. Split(KnownNetworks).Select(s =>
            IPNetwork.TryParse(s, out var network)
                ? network
                : throw new InvalidOperationException(
                    $"Proxy:KnownNetworks contains '{s}', which is not a network in CIDR form such as 172.25.0.0/16."))];

    private IReadOnlyList<IPAddress> Proxies() =>
        [.. Split(KnownProxies).Select(s =>
            IPAddress.TryParse(s, out var address)
                ? address
                : throw new InvalidOperationException(
                    $"Proxy:KnownProxies contains '{s}', which is not an IP address."))];

    private static IEnumerable<string> Split(string? raw) =>
        (raw ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
