using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RustPlusApi.Fcm.Registration.Steps.Cdp;

/// <summary>Finds the page target of a freshly launched browser via its remote-debugging HTTP endpoint.</summary>
internal static class DevToolsEndpoint
{
    /// <summary>Polls <c>/json/list</c> until the browser reports a usable page target.</summary>
    /// <param name="http">The client used for the polling requests.</param>
    /// <param name="port">The remote-debugging port on the loopback address.</param>
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

    /// <summary>Reads a target's debugging address, but only from a well-formed page entry whose address is a
    /// <c>ws://</c> address on the loopback host at the probed port. Anything else, such as a process squatting the
    /// port pointing the library elsewhere, is skipped as if the page were not there yet.</summary>
    /// <param name="target">One entry of the <c>/json/list</c> array.</param>
    /// <param name="port">The probed debugging port.</param>
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
