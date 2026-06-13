using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace RustPlusApi.Fcm.Registration.Steps;

/// <summary>
/// Step 5 — interactive Steam login. Launches Chrome/Chromium with the DevTools protocol enabled,
/// injects a <c>ReactNativeWebView.postMessage</c> shim into every page via
/// <c>Page.addScriptToEvaluateOnNewDocument</c>, and captures the Rust+ auth token the Facepunch
/// login page hands to that shim.
/// </summary>
/// <param name="port">The loopback port the OAuth callback listener binds to.</param>
/// <remarks>
/// The Facepunch login delivers the token to its host through <c>ReactNativeWebView.postMessage</c>.
/// Older techniques no longer work on modern Chrome: popup/opener injection is blocked by
/// cross-origin <c>WindowProxy</c> restrictions, and <c>--load-extension</c> was disabled for
/// stable Chrome in 137+. CDP injection runs inside the page's own main world before its scripts,
/// so it sidesteps all of that — the same mechanism Puppeteer/Playwright use. <b>Chrome/Chromium
/// is required</b> (native or Flatpak are auto-detected; <c>CHROME_PATH</c> overrides discovery).
/// This step is interactive and only validatable by a real run (e.g. the
/// <c>RustPlus.Register.ConsoleApp</c> sample); it is excluded from the coverage gate.
/// </remarks>
[ExcludeFromCodeCoverage]
public sealed class SteamLoginService(int port = 3000)
{
    private static readonly string[] FlatpakAppIds =
        ["com.google.Chrome", "org.chromium.Chromium", "com.github.Eloston.UngoogledChromium"];

    /// <summary>Launches Chrome, navigates to the Facepunch Steam login page, and returns the captured auth token.</summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled before a token is received.</exception>
    public Task<string> LoginAsync(CancellationToken cancellationToken = default) =>
        LoginAsync(RegistrationConstants.SteamLoginUrl, cancellationToken);

    /// <summary>Drives the login against an arbitrary start URL (the Facepunch login in production).</summary>
    /// <param name="startUrl">The URL to navigate Chrome to when the session starts.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled before a token is received.</exception>
    internal async Task<string> LoginAsync(string startUrl, CancellationToken cancellationToken = default)
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        // GetContextAsync takes no cancellation token: stopping the listener is the only way to
        // unblock the wait promptly, so cancellation must not have to wait for the next request.
#pragma warning disable RCS1261 // CancellationTokenRegistration.DisposeAsync is not available on netstandard2.0
        using var cancellationRegistration = cancellationToken.Register(listener.Stop);
#pragma warning restore RCS1261

        var workDir = Path.Combine(Path.GetTempPath(), "rustplusapi-" + Guid.NewGuid().ToString("N"));
        var profileDir = Path.Combine(workDir, "profile");
        var debugPort = GetFreePort();
        Process? chrome = null;
        ClientWebSocket? socket = null;

        try
        {
            chrome = LaunchChrome(workDir, profileDir, debugPort);
#pragma warning disable CA2000 // socket disposed via TryDisposeSocket in finally
            socket = await ConnectAndInjectAsync(debugPort, startUrl, cancellationToken).ConfigureAwait(false);
#pragma warning restore CA2000

            while (!cancellationToken.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
                {
                    // The listener was stopped under the wait — by the cancellation registration
                    // above (expected; rethrow as cancellation) or by an unrelated teardown.
                    cancellationToken.ThrowIfCancellationRequested();
                    throw;
                }

                var token = await ReadTokenAsync(context.Request).ConfigureAwait(false);
                await RespondAsync(context, "<h1>Done. You can close this window.</h1>").ConfigureAwait(false);
                if (!string.IsNullOrEmpty(token))
                {
                    return token!;
                }
            }
        }
        finally
        {
            listener.Stop();
            TryDisposeSocket(socket);
            TryKill(chrome);
            TryDeleteDirectory(workDir);
        }

        throw new OperationCanceledException(cancellationToken);
    }

    // --- DevTools protocol ---

    private async Task<ClientWebSocket> ConnectAndInjectAsync(int debugPort,
        string startUrl,
        CancellationToken cancellationToken)
    {
        var pageWebSocketUrl = await GetPageWebSocketUrlAsync(debugPort, cancellationToken).ConfigureAwait(false);

        var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(pageWebSocketUrl), cancellationToken).ConfigureAwait(false);

        // Drain incoming CDP events so the socket never blocks on backpressure.
        _ = Task.Run(() => DrainAsync(socket, cancellationToken), cancellationToken);

        // Deliver the token via a POST body so it never lands in the URL (browser history, request
        // logs). text/plain keeps it a CORS "simple request" (no preflight) and mode:'no-cors' is
        // fine because the response is never read. If fetch fails for any reason, fall back to the
        // old query-string navigation so the login can't break — the listener accepts both.
        var callback = $"http://localhost:{port}/callback";
        var shim =
            "window.ReactNativeWebView = { postMessage: function (m) { try { var a = JSON.parse(m); " +
            "if (a && a.Token) { " +
            $"fetch('{callback}', {{ method: 'POST', mode: 'no-cors', headers: {{ 'Content-Type': 'text/plain' }}, body: a.Token }})" +
            $".catch(function () {{ window.location.href = '{callback}?token=' + encodeURIComponent(a.Token); }});" +
            " } } catch (e) {} } };";

        await SendAsync(socket, 1, "Page.enable", new
        {
        }, cancellationToken).ConfigureAwait(false);
        await SendAsync(socket, 2, "Page.addScriptToEvaluateOnNewDocument", new
        {
            source = shim
        }, cancellationToken).ConfigureAwait(false);
        await SendAsync(socket, 3, "Page.navigate", new
        {
            url = startUrl
        }, cancellationToken).ConfigureAwait(false);

        return socket;
    }

    private static async Task<string> GetPageWebSocketUrlAsync(int debugPort, CancellationToken cancellationToken)
    {
        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        for (var attempt = 0; attempt < 50; attempt++)
        {
            try
            {
#if NET10_0_OR_GREATER
                var json = await http.GetStringAsync(new Uri($"http://localhost:{debugPort}/json"), cancellationToken)
                    .ConfigureAwait(false);
#else
                var json =
                    await http.GetStringAsync(new Uri($"http://localhost:{debugPort}/json")).ConfigureAwait(false);
#endif
                using var document = JsonDocument.Parse(json);
                foreach (var target in document.RootElement.EnumerateArray())
                {
                    if (target.TryGetProperty("type", out var type) && type.GetString() == "page"
                                                                    && target.TryGetProperty("webSocketDebuggerUrl",
                                                                        out var url))
                    {
                        return url.GetString()!;
                    }
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // DevTools endpoint not ready yet.
            }

            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException("Chrome DevTools endpoint did not become available.");
    }

    private static async Task SendAsync(ClientWebSocket socket,
        int id,
        string method,
        object parameters,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            id, method, @params = parameters
        });
        var bytes = Encoding.UTF8.GetBytes(payload);
        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task DrainAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken)
                    .ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[{nameof(SteamLoginService)}] DevTools socket closed during shutdown: {ex.Message}");
        }
    }

    // --- Chrome process ---

    private static Process LaunchChrome(string workDir, string profileDir, int debugPort)
    {
        var (executable, prefixArguments) = ResolveChromeLaunch(workDir)
                                            ?? throw new InvalidOperationException(
                                                "Could not find Chrome/Chromium, which is required for the Steam login step. " +
                                                "Install Google Chrome or Chromium (native or Flatpak), or set the CHROME_PATH " +
                                                "environment variable to the executable.");

        Directory.CreateDirectory(profileDir);

        var chromeArguments = new[]
        {
            $"--user-data-dir={profileDir}",
            $"--remote-debugging-port={debugPort.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            "--no-first-run", "--no-default-browser-check", "about:blank"
        };
        var arguments = prefixArguments.Concat(chromeArguments).ToArray();

        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false
        };
#if NET10_0_OR_GREATER
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
#else
        startInfo.Arguments = string.Join(" ", arguments.Select(QuoteIfNeeded));
#endif

        return Process.Start(startInfo)
               ?? throw new InvalidOperationException("Failed to launch Chrome/Chromium.");
    }

    /// <summary>Resolves a native Chrome/Chromium binary, or a Flatpak launcher, with any prefix args.</summary>
    /// <param name="workDir">Temporary working directory passed to Flatpak's <c>--filesystem</c> flag when needed.</param>
    private static (string Executable, string[] PrefixArguments)? ResolveChromeLaunch(string workDir)
    {
        var native = FindChrome();
        if (native != null)
        {
            return (native, []);
        }

        var flatpak = ResolveOnPath("flatpak");
        if (flatpak != null)
        {
            var appId = FlatpakAppIds.FirstOrDefault(IsFlatpakAppInstalled);
            if (appId is not null)
            {
                return (flatpak, ["run", $"--filesystem={workDir}", appId]);
            }
        }

        return null;
    }

    private static bool IsFlatpakAppInstalled(string appId)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var locations = new[]
        {
            $"/var/lib/flatpak/app/{appId}", Path.Combine(home, ".local", "share", "flatpak", "app", appId),
        };
        return Array.Exists(locations, Directory.Exists);
    }

    private static string? FindChrome()
    {
        var fromEnv = Environment.GetEnvironmentVariable("CHROME_PATH");
        if (!string.IsNullOrEmpty(fromEnv) && File.Exists(fromEnv))
        {
            return fromEnv;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var candidates = new[]
            {
                @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
                @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            };
            return Array.Find(candidates, File.Exists);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var candidates = new[]
            {
                "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
                "/Applications/Chromium.app/Contents/MacOS/Chromium",
            };
            return Array.Find(candidates, File.Exists);
        }

        foreach (var name in new[]
                 {
                     "google-chrome", "google-chrome-stable", "chromium", "chromium-browser", "microsoft-edge"
                 })
        {
            var resolved = ResolveOnPath(name);
            if (resolved != null)
            {
                return resolved;
            }
        }

        return null;
    }

    private static string? ResolveOnPath(string executable)
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVariable))
        {
            return null;
        }

        foreach (var directory in pathVariable!.Split(Path.PathSeparator))
        {
            var candidate = Path.Combine(directory, executable);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static int GetFreePort()
    {
#pragma warning disable CA2000 // TcpListener is not IDisposable; Stop() + Server.Dispose() is the correct cleanup pattern.
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
#pragma warning restore CA2000
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
            listener.Server.Dispose();
        }
    }

#if !NET10_0_OR_GREATER
    private static string QuoteIfNeeded(string argument) =>
        argument.IndexOf(' ') >= 0 ? "\"" + argument + "\"" : argument;
#endif

    /// <summary>Extracts the auth token from a callback request: POST body (primary path, keeps the
    /// token out of URLs) or the <c>token</c> query parameter (navigation fallback).</summary>
    /// <param name="request">The callback request from the injected shim.</param>
    private static async Task<string?> ReadTokenAsync(HttpListenerRequest request)
    {
        if (request.HttpMethod == "POST")
        {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            var body = await reader.ReadToEndAsync().ConfigureAwait(false);
            return body.Trim();
        }

        return request.QueryString["token"];
    }

    private static async Task RespondAsync(HttpListenerContext context, string html)
    {
        var buffer = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html";
        context.Response.ContentLength64 = buffer.Length;
#if NET10_0_OR_GREATER
        await context.Response.OutputStream.WriteAsync(buffer.AsMemory()).ConfigureAwait(false);
#else
        await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
#endif
        context.Response.Close();
    }

    private static void TryDisposeSocket(ClientWebSocket? socket)
    {
        try
        {
            socket?.Dispose();
        }
        catch (Exception ex) { Debug.WriteLine($"[{nameof(SteamLoginService)}] Socket dispose failed: {ex.Message}"); }
    }

    private static void TryKill(Process? process)
    {
        try
        {
            if (process is { HasExited: false })
            {
#if NET10_0_OR_GREATER
                process.Kill(entireProcessTree: true);
#else
                process.Kill();
#endif
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[{nameof(SteamLoginService)}] Chrome kill failed: {ex.Message}");
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[{nameof(SteamLoginService)}] Profile directory cleanup failed: {ex.Message}");
        }
    }
}
