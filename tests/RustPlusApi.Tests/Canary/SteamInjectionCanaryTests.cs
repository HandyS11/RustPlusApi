using System.Net;
using System.Text;

using RustPlusApi.Fcm.Registration.Steps;

using Xunit;

namespace RustPlusApi.Tests.Canary;

/// <summary>
/// Validates the CDP <c>ReactNativeWebView</c> injection mechanism used by
/// <see cref="SteamLoginService"/> against a local test page (no Steam) — it launches the real
/// Chrome/Chromium on this machine, so it's a Skipped canary, not part of the CI gate.
/// </summary>
[Trait("Category", "Canary")]
public class SteamInjectionCanaryTests
{
    [Fact(Skip = "Canary: launches a real Chrome/Chromium window. Remove Skip to run manually.")]
    public async Task Injection_NavigatesCallbackWithToken()
    {
        var pagePort = GetFreePort();
        using var pageServer = new HttpListener();
        pageServer.Prefixes.Add($"http://localhost:{pagePort}/");
        pageServer.Start();
#pragma warning disable CA2025 // pageServer outlives the task; Stop() is called after LoginAsync returns.
        _ = Task.Run(() => ServeTestPageAsync(pageServer));
#pragma warning restore CA2025

        var steam = new SteamLoginService(GetFreePort());
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var token = await steam.LoginAsync($"http://localhost:{pagePort}/", cts.Token);

        Assert.Equal("CANARY123", token);
        pageServer.Stop();
    }

    private static async Task ServeTestPageAsync(HttpListener server)
    {
        try
        {
            var context = await server.GetContextAsync();
            const string html =
                "<html><body><script>setTimeout(function(){" +
                "window.ReactNativeWebView.postMessage(JSON.stringify({Token:'CANARY123'}));" +
                "},800);</script></body></html>";
            var buffer = Encoding.UTF8.GetBytes(html);
            context.Response.ContentType = "text/html";
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer);
            context.Response.Close();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SteamInjectionCanaryTests] Test server stopped: {ex.Message}");
        }
    }

    private static int GetFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
