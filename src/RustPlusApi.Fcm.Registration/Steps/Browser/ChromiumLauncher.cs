using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using RustPlusApi.Fcm.Registration.Steps.Cdp;

namespace RustPlusApi.Fcm.Registration.Steps.Browser;

/// <summary>Starts a real Chromium-family browser process and connects to its page over the DevTools Protocol.</summary>
/// <remarks>Excluded from coverage: every member starts a real browser process, polls its real debugging port or opens
/// a real WebSocket. The command line it uses (<see cref="BrowserLaunchArguments"/>), the page discovery
/// (<see cref="DevToolsEndpoint"/>) and everything driven over the connection are unit-tested on their own.</remarks>
[ExcludeFromCodeCoverage]
internal sealed class ChromiumLauncher : IBrowserLauncher
{
    private static readonly TimeSpan PageTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(10);

    /// <inheritdoc/>
    public async Task<IBrowserSession> LaunchAsync(
        BrowserInfo browser,
        int port,
        string profileDirectory,
        CancellationToken cancellationToken)
    {
        // BuildStartInfo names the browser by its absolute path with UseShellExecute off, never by a bare program name.
        var process = Process.Start(BrowserLaunchArguments.BuildStartInfo(browser, port, profileDirectory))
                      ?? throw new InvalidOperationException("the process did not start");
        try
        {
            // Both streams are redirected; reading them keeps a chatty browser from blocking on a full pipe.
            // Nothing is logged: the output can carry addresses of the pages being visited.
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // CA2000: the handler is disposed by the HttpClient, which owns it.
            // CA5399: plain HTTP to the browser's own loopback debugging port has no certificate to check.
#pragma warning disable CA2000, CA5399
            using var http = new HttpClient(new HttpClientHandler
            {
                UseProxy = false
            })
            {
                Timeout = PageTimeout
            };
#pragma warning restore CA2000, CA5399
            var page = await DevToolsEndpoint.WaitForPageAsync(http, port, PageTimeout, PollInterval, cancellationToken)
                .ConfigureAwait(false);
#pragma warning disable CA2000 // Ownership passes to the returned session, which disposes it.
            var transport = await WebSocketCdpTransport.ConnectAsync(page, cancellationToken).ConfigureAwait(false);
#pragma warning restore CA2000
            return new Session(process, transport);
        }
        catch
        {
            Kill(process);
            process.Dispose();
            throw;
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            // Only reached for a browser that is stuck (still running 10 s after Browser.close) or that never opened
            // its debugging port, so it changes no timing. Killing the whole tree also ends what a plain Kill leaves
            // behind, such as the sandboxed browser a flatpak launcher started. Process.Kill(bool) only exists from
            // .NET Core 3.0, so a netstandard2.0 host kills the started process alone.
#if NET10_0_OR_GREATER
            process.Kill(entireProcessTree: true);
#else
            process.Kill();
#endif
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException
                                       or AggregateException)
        {
            // Already exited, or not ours to kill any more: nothing left to stop.
        }
    }

    /// <summary>The running browser; disposing it closes the connection and ends the process.</summary>
    /// <param name="process">The browser process (the <c>flatpak</c> process for a Flatpak app).</param>
    /// <param name="transport">The DevTools connection to its page.</param>
    private sealed class Session(Process process, ICdpTransport transport) : IBrowserSession
    {
        /// <inheritdoc/>
        public ICdpTransport Transport => transport;

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            try
            {
                try
                {
                    // A CdpSession over the transport has usually disposed it already; a WebSocket's Dispose is
                    // idempotent.
                    await transport.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    // Runs even when the transport's dispose throws: the process must still be waited for and ended.
                    if (!await WaitForExitAsync().ConfigureAwait(false))
                    {
                        Kill(process);
                    }
                }
            }
            finally
            {
                process.Dispose();
            }
        }

        /// <summary>Waits for the process itself to exit, the same way on every target framework.</summary>
        /// <remarks>Not <c>Process.WaitForExitAsync</c> (.NET 5+): it also waits for end-of-file on the redirected
        /// output streams, which a helper process that inherited them (crashpad, the Flatpak proxy) can hold open
        /// after the browser has exited. That would add the full timeout and a needless Kill to every login on
        /// .NET 10 only. <c>WaitForExit(int)</c> with a finite timeout waits for the process alone.</remarks>
        private Task<bool> WaitForExitAsync() =>
            Task.Run(() => process.WaitForExit((int)ExitTimeout.TotalMilliseconds));
    }
}
