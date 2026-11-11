using System.Text;
using System.Text.Json.Nodes;
using RustPlusApi.Fcm.Registration.Steps.Browser;
using RustPlusApi.Fcm.Registration.Steps.Cdp;
using RustPlusApi.Fcm.Registration.UnitTests.TestHelpers;
using Xunit;

namespace RustPlusApi.Fcm.Registration.UnitTests;

public class RustPlusAppLoginTests
{
    private const string LoginUrl = "https://companion-rust.facepunch.com/login?x=1";

    private const string AppUrl =
        "https://companion-rust.facepunch.com/app?returnUrl=rust-plus%3A%2F%2Frust-auth-session";

    private const string FinalLocation = "rust-plus://rust-auth-session?steamId=7&token=a%2Bb";

    private const string WindowsUa =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/150.0.0.0 Safari/537.36";

    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(20);

    private static string RequestPaused(string id, string url, JsonObject? headers = null) =>
        new JsonObject
        {
            ["method"] = "Fetch.requestPaused",
            ["params"] = new JsonObject
            {
                ["requestId"] = id,
                ["request"] = new JsonObject
                {
                    ["url"] = url, ["headers"] = headers ?? []
                },
            },
        }.ToJsonString();

    private static string ResponsePaused(string id,
        int status,
        string? headerName,
        string? location,
        string url = AppUrl)
    {
        var headers = new JsonArray(new JsonObject
        {
            ["name"] = "Content-Type", ["value"] = "text/html"
        });
        if (headerName is not null)
        {
            headers.Add(new JsonObject
            {
                ["name"] = headerName, ["value"] = location
            });
        }

        return new JsonObject
        {
            ["method"] = "Fetch.requestPaused",
            ["params"] = new JsonObject
            {
                ["requestId"] = id,
                ["request"] = new JsonObject
                {
                    ["url"] = url, ["headers"] = new JsonObject()
                },
                ["responseStatusCode"] = status,
                ["responseHeaders"] = headers,
            },
        }.ToJsonString();
    }

    private static string FinalRedirect(string id = "final", string location = FinalLocation) =>
        ResponsePaused(id, 302, "Location", location);

    private static FakeCdpTransport Script(params string[] events)
    {
        // Pushes events[0] after Page.navigate and each later entry after the previous event's follow-up command.
        var next = 0;
        var transport = new FakeCdpTransport();
        transport.OnCommand = command =>
        {
            var method = (string)command["method"]!;
            if (method == "Fetch.enable")
            {
                return;
            }

            if (next < events.Length)
            {
                transport.Push(events[next++]);
            }
        };
        return transport;
    }

    private static List<string> Methods(FakeCdpTransport transport) =>
        [.. transport.Sent.Select(c => (string)c["method"]!)];

    private static async Task<SteamLoginResult> RunAsync(FakeCdpTransport transport)
    {
        await using var session = new CdpSession(transport);
        return await RustPlusAppLogin.RunAsync(session, LoginUrl, CancellationToken.None).WaitAsync(TestTimeout);
    }

    private static async Task<InvalidOperationException> RunFailsAsync(FakeCdpTransport transport)
    {
        await using var session = new CdpSession(transport);
        return await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RustPlusAppLogin.RunAsync(session, LoginUrl, CancellationToken.None).WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task RunAsync_EnablesFetchForAppAndLoginFailedOnly_ThenNavigates()
    {
        var transport = Script(FinalRedirect());

        await RunAsync(transport);

        Assert.Equal(
            """{"id":1,"method":"Fetch.enable","params":{"patterns":[{"urlPattern":"https://companion-rust.facepunch.com/app*","requestStage":"Request"},{"urlPattern":"https://companion-rust.facepunch.com/app*","requestStage":"Response"},{"urlPattern":"https://companion-rust.facepunch.com/login-failed*","requestStage":"Request"}]}}""",
            transport.Sent[0].ToJsonString());
        Assert.Equal(
            $$$"""{"id":2,"method":"Page.navigate","params":{"url":"{{{LoginUrl}}}"}}""",
            transport.Sent[1].ToJsonString());
    }

    [Fact]
    public async Task RunAsync_WindowsRequest_RewritesUserAgentAndPlatformOnly()
    {
        var headers = new JsonObject
        {
            ["User-Agent"] = WindowsUa, ["sec-ch-ua-platform"] = "\"Windows\"", ["Cookie"] = "c=1",
        };
        var transport = Script(RequestPaused("r1", AppUrl, headers), FinalRedirect());

        await RunAsync(transport);

        var command = transport.Sent[2];
        Assert.Equal("Fetch.continueRequest", (string)command["method"]!);
        var sent = (JsonObject)command["params"]!;
        Assert.Equal("r1", (string)sent["requestId"]!);
        var list = ((JsonArray)sent["headers"]!).Select(h => ((string)h!["name"]!, (string)h["value"]!)).ToList();
        Assert.Equal(
            [
                ("User-Agent", UserAgentRewriter.ToNonWindows(WindowsUa)), ("sec-ch-ua-platform", "\"Linux\""),
                ("Cookie", "c=1")
            ],
            list);
    }

    [Fact]
    public async Task RunAsync_WindowsRequestWithNullHeaderValue_KeepsItAsEmpty()
    {
        var headers = new JsonObject
        {
            ["User-Agent"] = WindowsUa, ["X-Empty"] = null
        };
        var transport = Script(RequestPaused("r1", AppUrl, headers), FinalRedirect());

        await RunAsync(transport);

        var list = ((JsonArray)transport.Sent[2]["params"]!["headers"]!)
            .Select(h => ((string)h!["name"]!, (string)h["value"]!)).ToList();
        Assert.Equal([("User-Agent", UserAgentRewriter.ToNonWindows(WindowsUa)), ("X-Empty", "")], list);
    }

    [Fact]
    public async Task RunAsync_NonWindowsRequest_ContinuesWithoutHeaders()
    {
        var headers = new JsonObject
        {
            ["User-Agent"] = "Mozilla/5.0 (X11; Linux x86_64) Chrome/150"
        };
        var transport = Script(RequestPaused("r1", AppUrl, headers), FinalRedirect());

        await RunAsync(transport);

        Assert.Equal("""{"requestId":"r1"}""", transport.Sent[2]["params"]!.ToJsonString());
        Assert.Equal("Fetch.continueRequest", (string)transport.Sent[2]["method"]!);
    }

    [Fact]
    public async Task RunAsync_RequestWithoutHeaders_ContinuesWithoutHeaders()
    {
        var transport = Script(
            """{"method":"Fetch.requestPaused","params":{"requestId":"r1","request":{"url":"https://companion-rust.facepunch.com/app"}}}""",
            FinalRedirect());

        await RunAsync(transport);

        Assert.Equal("""{"requestId":"r1"}""", transport.Sent[2]["params"]!.ToJsonString());
    }

    [Fact]
    public async Task RunAsync_MiddleHopRedirect_Continues()
    {
        var transport = Script(
            ResponsePaused("hop", 302, "Location", "/app?returnUrl=rust-plus://rust-auth-session"),
            FinalRedirect());

        await RunAsync(transport);

        Assert.Equal("Fetch.continueRequest", (string)transport.Sent[2]["method"]!);
        Assert.Equal("""{"requestId":"hop"}""", transport.Sent[2]["params"]!.ToJsonString());
    }

    [Fact]
    public async Task RunAsync_RedirectWithoutLocation_Continues()
    {
        var transport = Script(ResponsePaused("hop", 302, null, null), FinalRedirect());

        await RunAsync(transport);

        Assert.Equal("""{"requestId":"hop"}""", transport.Sent[2]["params"]!.ToJsonString());
    }

    [Fact]
    public async Task RunAsync_ResponseWithoutHeaders_ContinuesAndKeepsWaiting()
    {
        var transport = Script(
            """{"method":"Fetch.requestPaused","params":{"requestId":"hop","request":{"url":"https://companion-rust.facepunch.com/app"},"responseStatusCode":302}}""",
            FinalRedirect());

        await RunAsync(transport);

        Assert.Equal("""{"requestId":"hop"}""", transport.Sent[2]["params"]!.ToJsonString());
    }

    [Fact]
    public async Task RunAsync_ContinueRequestRejectedAtResponseStage_FallsBackToContinueResponse()
    {
        var transport = Script(
            ResponsePaused("hop", 302, "Location", "/app?x=1"),
            FinalRedirect());
        transport.FailCommand("Fetch.continueRequest", "x");

        await RunAsync(transport);

        Assert.Equal("Fetch.continueRequest", (string)transport.Sent[2]["method"]!);
        Assert.Equal("Fetch.continueResponse", (string)transport.Sent[3]["method"]!);
        Assert.Equal("""{"requestId":"hop"}""", transport.Sent[3]["params"]!.ToJsonString());
    }

    [Fact]
    public async Task RunAsync_FinalRedirect_ReturnsTokenAndServesDonePage()
    {
        var transport = Script(FinalRedirect("final"));

        var result = await RunAsync(transport);

        Assert.Equal(7UL, result.SteamId);
        Assert.Equal("a+b", result.Token);
        var last = transport.Sent[^1];
        Assert.Equal("Fetch.fulfillRequest", (string)last["method"]!);
        var sent = (JsonObject)last["params"]!;
        Assert.Equal("final", (string)sent["requestId"]!);
        Assert.Equal(200, (int)sent["responseCode"]!);
        Assert.Equal(
            """[{"name":"Content-Type","value":"text/html; charset=utf-8"},{"name":"Cache-Control","value":"no-store"}]""",
            sent["responseHeaders"]!.ToJsonString());
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes(RustPlusAppLogin.DoneHtml)), (string)sent["body"]!);
        Assert.Equal(["Fetch.enable", "Page.navigate", "Fetch.fulfillRequest"], Methods(transport));
        Assert.Contains("Done. You can close this window and go back to your terminal.", RustPlusAppLogin.DoneHtml,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Location")]
    [InlineData("location")]
    [InlineData("LOCATION")]
    public async Task RunAsync_FindsLocationHeaderCaseInsensitively(string headerName)
    {
        var transport = Script(ResponsePaused("final", 302, headerName, FinalLocation));

        var result = await RunAsync(transport);

        Assert.Equal(new SteamLoginResult(7, "a+b"), result);
    }

    [Fact]
    public async Task RunAsync_AppAnswersPage_ThrowsRefusal()
    {
        var transport = Script(ResponsePaused("page", 200, null, null));

        var ex = await RunFailsAsync(transport);

        Assert.StartsWith("Facepunch refused the Steam login at its last step", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("reported as non-Windows", ex.Message, StringComparison.Ordinal);
        Assert.Equal("Fetch.continueRequest", (string)transport.Sent[^1]["method"]!);
        Assert.Equal(["Fetch.enable", "Page.navigate", "Fetch.continueRequest"], Methods(transport));
    }

    [Fact]
    public async Task RunAsync_AppAnswersPageAfterWindowsRewrite_MentionsNonWindows()
    {
        var headers = new JsonObject
        {
            ["User-Agent"] = WindowsUa
        };
        var transport = Script(RequestPaused("r1", AppUrl, headers), ResponsePaused("page", 200, null, null));

        var ex = await RunFailsAsync(transport);

        Assert.StartsWith("Facepunch refused the Steam login at its last step", ex.Message, StringComparison.Ordinal);
        Assert.Contains("reported as non-Windows", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_AppAnswersServerError_ThrowsStatus()
    {
        var transport = Script(ResponsePaused("err", 500, null, null));

        var ex = await RunFailsAsync(transport);

        Assert.Equal("Facepunch answered HTTP 500 at its last step.", ex.Message);
        Assert.Equal("Fetch.continueRequest", (string)transport.Sent[^1]["method"]!);
    }

    [Theory]
    [InlineData("https://companion-rust.facepunch.com/apple-touch-icon.png", 200)]
    [InlineData("https://companion-rust.facepunch.com/app.js", 200)]
    [InlineData("https://companion-rust.facepunch.com/application/x?y=1", 404)]
    [InlineData("https://companion-rust.facepunch.com/app/", 500)]
    [InlineData("not a url", 200)]
    public async Task RunAsync_ResponseOutsideTheAppPath_IsContinuedAndNeverARefusal(string address, int status)
    {
        // The /app* pattern also matches other resources; only /app itself is Facepunch's last step.
        var transport = Script(ResponsePaused("res", status, null, null, address), FinalRedirect());

        var result = await RunAsync(transport);

        Assert.Equal(new SteamLoginResult(7, "a+b"), result);
        Assert.Equal("""{"requestId":"res"}""", transport.Sent[2]["params"]!.ToJsonString());
        Assert.Equal(["Fetch.enable", "Page.navigate", "Fetch.continueRequest", "Fetch.fulfillRequest"],
            Methods(transport));
    }

    [Theory]
    [InlineData("https://companion-rust.facepunch.com/app.js")]
    [InlineData("https://companion-rust.facepunch.com/application/x")]
    [InlineData("not a url")]
    public async Task RunAsync_WindowsRequestOutsideTheAppPath_IsContinuedUntouched(string address)
    {
        var headers = new JsonObject
        {
            ["User-Agent"] = WindowsUa, ["sec-ch-ua-platform"] = "\"Windows\""
        };
        var transport = Script(RequestPaused("r1", address, headers), ResponsePaused("page", 200, null, null));

        var ex = await RunFailsAsync(transport);

        Assert.Equal("""{"requestId":"r1"}""", transport.Sent[2]["params"]!.ToJsonString());
        // No rewrite happened, so the refusal does not claim the browser was reported as non-Windows.
        Assert.Equal(
            "Facepunch refused the Steam login at its last step (it answered with a page instead of the sign-in redirect). Facepunch may have changed its login.",
            ex.Message);
    }

    [Fact]
    public async Task RunAsync_RustPlusRedirectOutsideTheAppPath_IsStillCapturedAndNeverContinued()
    {
        // Continuing any rust-plus: redirect would launch the official Rust+ desktop app, whatever path issued it.
        var transport = Script(ResponsePaused("final", 302, "Location", FinalLocation,
            "https://companion-rust.facepunch.com/app/"));

        var result = await RunAsync(transport);

        Assert.Equal(new SteamLoginResult(7, "a+b"), result);
        Assert.Equal(["Fetch.enable", "Page.navigate", "Fetch.fulfillRequest"], Methods(transport));
    }

    [Fact]
    public async Task RunAsync_LoginFailedPage_ThrowsWithReason()
    {
        var transport = Script(RequestPaused("lf",
            "https://companion-rust.facepunch.com/login-failed?returnUrl=&reason=cancelled"));

        var ex = await RunFailsAsync(transport);

        Assert.Contains("reason: cancelled", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Start the login again", ex.Message, StringComparison.Ordinal);
        Assert.Equal("""{"requestId":"lf"}""", transport.Sent[^1]["params"]!.ToJsonString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("?returnUrl=&other=1")]
    [InlineData("?reason=")]
    [InlineData("?reason=a%20very%2Cstrange%3Cvalue")]
    [InlineData("?reason=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task RunAsync_LoginFailedPageWithoutPlainReason_SaysUnknown(string suffix)
    {
        var transport = Script(RequestPaused("lf", "https://companion-rust.facepunch.com/login-failed" + suffix));

        var ex = await RunFailsAsync(transport);

        Assert.Contains("reason: unknown", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ConnectionClosed_ThrowsWindowClosed()
    {
        var transport = Script();
        transport.OnCommand = command =>
        {
            if ((string)command["method"]! == "Page.navigate")
            {
                transport.Close();
            }
        };

        var ex = await RunFailsAsync(transport);

        Assert.Equal("The browser window was closed before the Steam login finished.", ex.Message);
    }

    [Fact]
    public async Task RunAsync_ConnectionClosedWhileSendingContinueRequest_ThrowsWindowClosed()
    {
        var transport = Script(RequestPaused("r1", AppUrl));
        var inner = transport.OnCommand!;
        transport.OnCommand = command =>
        {
            var method = (string)command["method"]!;
            if (method == "Page.navigate")
            {
                transport.AutoReply = false;
            }

            inner(command);
            if (method == "Fetch.continueRequest")
            {
                transport.Close();
            }
        };

        var ex = await RunFailsAsync(transport);

        Assert.Equal("The browser window was closed before the Steam login finished.", ex.Message);
        Assert.IsType<CdpException>(ex.InnerException);
    }

    [Fact]
    public async Task RunAsync_BrowserRejectsNavigate_PropagatesCdpException()
    {
        var transport = Script();
        transport.FailCommand("Page.navigate", "bad url");
        await using var session = new CdpSession(transport);

        var ex = await Assert.ThrowsAsync<CdpException>(() =>
            RustPlusAppLogin.RunAsync(session, LoginUrl, CancellationToken.None).WaitAsync(TestTimeout));

        Assert.Equal("bad url", ex.Message);
    }

    [Fact]
    public async Task RunAsync_Cancelled_ThrowsOperationCanceled()
    {
        var transport = new FakeCdpTransport();
        using var cts = new CancellationTokenSource();
        var navigated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.OnCommand = command =>
        {
            if ((string)command["method"]! == "Page.navigate")
            {
                navigated.TrySetResult(true);
            }
        };
        await using var session = new CdpSession(transport);

        var run = RustPlusAppLogin.RunAsync(session, LoginUrl, cts.Token);
        await navigated.Task.WaitAsync(TestTimeout);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task RunAsync_IgnoresUnrelatedAndMalformedEvents()
    {
        var transport = new FakeCdpTransport();
        transport.OnCommand = command =>
        {
            if ((string)command["method"]! == "Page.navigate")
            {
                foreach (var json in new[]
                         {
                             """{"method":"Page.frameNavigated","params":{}}""", """{"method":"Fetch.requestPaused"}""",
                             """{"method":"Fetch.requestPaused","params":{"request":{"url":"https://companion-rust.facepunch.com/app"}}}""",
                             """{"method":"Fetch.requestPaused","params":{"requestId":"x"}}""", FinalRedirect(),
                         })
                {
                    transport.Push(json);
                }
            }
        };

        var result = await RunAsync(transport);

        Assert.Equal(new SteamLoginResult(7, "a+b"), result);
    }

    [Fact]
    public async Task RunAsync_NeverPutsTokenInExceptionMessages()
    {
        var transport =
            Script(FinalRedirect("final", "rust-plus://rust-auth-session?steamId=notanumber&token=SECRETTOKEN"));

        var ex = await RunFailsAsync(transport);

        Assert.DoesNotContain("SECRETTOKEN", ex.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("notanumber", ex.ToString(), StringComparison.Ordinal);
        Assert.Equal(["Fetch.enable", "Page.navigate"], Methods(transport));
    }

    [Fact]
    public async Task RunAsync_UnparseableRedirect_ThrowsWithoutTheLocation()
    {
        var transport = Script(FinalRedirect("final", "rust-plus://rust-auth-session:SECRETPORT?token=SECRETTOKEN"));

        var ex = await RunFailsAsync(transport);

        Assert.Equal("The Facepunch login redirect was not a valid address.", ex.Message);
        Assert.Null(ex.InnerException);
        Assert.Equal(["Fetch.enable", "Page.navigate"], Methods(transport));
    }

    [Theory]
    [InlineData("RUST-PLUS://rust-auth-session?steamId=7&token=abc")]
    [InlineData("rust-plus://other-host?steamId=7&token=abc")]
    public async Task RunAsync_AnyRustPlusRedirect_IsCapturedAndNeverContinued(string location)
    {
        var transport = Script(FinalRedirect("final", location));

        var result = await RunAsync(transport);

        Assert.Equal(new SteamLoginResult(7, "abc"), result);
        Assert.Equal(["Fetch.enable", "Page.navigate", "Fetch.fulfillRequest"], Methods(transport));
    }

    [Fact]
    public async Task RunAsync_RustPlusRedirectWithoutCredentials_ThrowsAndIsNeverContinued()
    {
        var transport = Script(FinalRedirect("final", "rust-plus://rust-auth-session?nope=1"));

        var ex = await RunFailsAsync(transport);

        Assert.Contains("no 'token'", ex.Message, StringComparison.Ordinal);
        Assert.Equal(["Fetch.enable", "Page.navigate"], Methods(transport));
    }
}
