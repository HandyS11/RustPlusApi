using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RustPlusApi.Fcm.Registration.Steps.Cdp;

/// <summary>Finds a freshly launched browser's debugging port, from the file it reports it in, and then its page
/// target, via its remote-debugging HTTP endpoint.</summary>
internal static class DevToolsEndpoint
{
    /// <summary>The file a Chromium-family browser started with <c>--remote-debugging-port=0</c> writes into its
    /// <c>--user-data-dir</c>: the port it bound on the first line, its browser target path on the second.</summary>
    internal const string ActivePortFileName = "DevToolsActivePort";

    /// <summary>Polls the profile's <see cref="ActivePortFileName"/> file until it names the port the browser bound.
    /// </summary>
    /// <param name="profileDirectory">The browser's <c>--user-data-dir</c>.</param>
    /// <param name="timeout">How long to keep polling before giving up.</param>
    /// <param name="pollInterval">The pause between polls.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The debugging port, in 1–65535.</returns>
    /// <exception cref="TimeoutException">No usable port appeared within <paramref name="timeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public static Task<int> WaitForActivePortAsync(string profileDirectory,
        TimeSpan timeout,
        TimeSpan pollInterval,
        CancellationToken cancellationToken) =>
        WaitForActivePortAsync(profileDirectory, timeout, pollInterval, File.ReadAllText, cancellationToken);

    /// <summary>Polls with a custom file reader (a test seam).</summary>
    /// <param name="profileDirectory">The browser's <c>--user-data-dir</c>.</param>
    /// <param name="timeout">How long to keep polling before giving up.</param>
    /// <param name="pollInterval">The pause between polls.</param>
    /// <param name="readFile">Reads a whole file as text.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The debugging port, in 1–65535.</returns>
    /// <exception cref="TimeoutException">No usable port appeared within <paramref name="timeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    internal static async Task<int> WaitForActivePortAsync(string profileDirectory,
        TimeSpan timeout,
        TimeSpan pollInterval,
        Func<string, string> readFile,
        CancellationToken cancellationToken)
    {
        // A cancelled caller gets no port, even one the browser has already written.
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.Combine(profileDirectory, ActivePortFileName);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            while (true)
            {
                if (TryReadActivePort(path, readFile) is { } port)
                {
                    return port;
                }

                await Task.Delay(pollInterval, deadline.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Never the file's contents: only the parsed port ever leaves this class.
            throw new TimeoutException(string.Format(
                CultureInfo.InvariantCulture,
                "The browser did not report its debugging port within {0:0} seconds.",
                timeout.TotalSeconds));
        }
    }

    /// <summary>Polls <c>/json/list</c> until the browser reports a usable page target.</summary>
    /// <param name="http">The client used for the polling requests.</param>
    /// <param name="port">The remote-debugging port on the loopback address, as the browser reported it.</param>
    /// <param name="timeout">How long to keep polling before giving up.</param>
    /// <param name="pollInterval">The pause between polls.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The page's <c>webSocketDebuggerUrl</c>: a <c>ws://</c> address on the loopback host at <paramref name="port"/>.</returns>
    /// <exception cref="TimeoutException">No page appeared within <paramref name="timeout"/>.</exception>
    public static async Task<Uri> WaitForPageAsync(
        HttpClient http,
        int port,
        TimeSpan timeout,
        TimeSpan pollInterval,
        CancellationToken cancellationToken)
    {
        var listUrl = new Uri($"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}/json/list");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            while (true)
            {
                var page = await TryFindPageAsync(http, listUrl, port, deadline.Token).ConfigureAwait(false);
                if (page is not null)
                {
                    return page;
                }

                await Task.Delay(pollInterval, deadline.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(string.Format(
                CultureInfo.InvariantCulture,
                "The browser did not open its debugging port within {0:0} seconds.",
                timeout.TotalSeconds));
        }
    }

    private static async Task<Uri?> TryFindPageAsync(HttpClient http,
        Uri listUrl,
        int port,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(listUrl, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

#if NET10_0_OR_GREATER
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#else
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#endif
            if (JsonNode.Parse(body) is not JsonArray targets)
            {
                return null;
            }

            foreach (var target in targets)
            {
                if (ToPageAddress(target, port) is { } page)
                {
                    return page;
                }
            }

            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Reads the port from the browser's port file, or <see langword="null"/> while it is not there yet,
    /// is still being written or does not start with a port.</summary>
    /// <param name="path">The <see cref="ActivePortFileName"/> file in the profile.</param>
    /// <param name="readFile">Reads a whole file as text.</param>
    private static int? TryReadActivePort(string path, Func<string, string> readFile)
    {
        string contents;
        try
        {
            contents = readFile(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not created yet (a missing file or profile is an IOException), or still held open by the browser
            // writing it, which Windows reports either way.
            return null;
        }

        // The browser writes the port, a newline, then its target path, and not atomically: until the newline is
        // there the first line may still be growing ("92" of "9222").
#pragma warning disable CA1307 // string.IndexOf(char, StringComparison) is not available on netstandard2.0
        var newline = contents.IndexOf('\n');
#pragma warning restore CA1307
        if (newline < 0)
        {
            return null;
        }

        return int.TryParse(contents[..newline].TrimEnd('\r'), NumberStyles.None, CultureInfo.InvariantCulture,
                   out var port)
               && port is >= 1 and <= 65535
            ? port
            : null;
    }

    /// <summary>Reads a target's debugging address, but only from a well-formed page entry whose address is a
    /// <c>ws://</c> address on the loopback host at the port the browser reported. Anything else, such as an entry
    /// pointing the library elsewhere, is skipped as if the page were not there yet.</summary>
    /// <param name="target">One entry of the <c>/json/list</c> array.</param>
    /// <param name="port">The debugging port the browser reported.</param>
    private static Uri? ToPageAddress(JsonNode? target, int port)
    {
        if (target is not JsonObject entry
            || StringOf(entry["type"]) != "page"
            || !Uri.TryCreate(StringOf(entry["webSocketDebuggerUrl"]), UriKind.Absolute, out var address))
        {
            return null;
        }

        return address.Scheme == "ws" && address.IsLoopback && address.Port == port ? address : null;
    }

    /// <summary>The node's value when it is a JSON string; otherwise <see langword="null"/>.</summary>
    /// <param name="node">The node to read.</param>
    private static string? StringOf(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out string? text) ? text : null;
}
