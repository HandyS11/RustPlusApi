using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Threading;
using RustPlusApi.Fcm.Registration;
using RustPlusApi.Fcm.Registration.Steps;
using Xunit;

namespace RustPlusApi.Fcm.Registration.UnitTests;

/// <summary>Offline coverage of the Steam login redirect flow: URL construction, callback
/// parsing, and the loopback listener loop driven without a browser.</summary>
public class SteamLoginServiceTests
{
    [Fact]
    public void BuildLoginUrl_EncodesReturnUrlAsQueryParameter()
    {
        var url = SteamLoginService.BuildLoginUrl("http://localhost:3000/callback/abc123");

        Assert.Equal(
            RegistrationConstants.SteamLoginUrl
            + "?returnUrl=http%3A%2F%2Flocalhost%3A3000%2Fcallback%2Fabc123",
            url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [SuppressMessage("Design", "CA1054:URI parameters should not be strings",
        Justification = "Test parameters use string for testability")]
    public void BuildLoginUrl_BlankReturnUrl_Throws(string? returnUrl) =>
        Assert.Throws<ArgumentException>(() => SteamLoginService.BuildLoginUrl(returnUrl!));

    [Fact]
    public void ParseCallback_ReturnsSteamIdAndToken()
    {
        var uri = new Uri("http://localhost:3000/callback/abc123?steamId=76561198249527954&token=eyJhbGciOi");

        var result = SteamLoginService.ParseCallback(uri);

        Assert.Equal(76561198249527954UL, result.SteamId);
        Assert.Equal("eyJhbGciOi", result.Token);
    }

    [Fact]
    public void ParseCallback_UrlDecodesValuesAndIgnoresExtraParameters()
    {
        var uri = new Uri("http://localhost:3000/callback?extra=1&token=a%2Bb%3Dc&steamId=7&other=x");

        var result = SteamLoginService.ParseCallback(uri);

        Assert.Equal(7UL, result.SteamId);
        Assert.Equal("a+b=c", result.Token);
    }

    [Theory]
    [InlineData("http://localhost:3000/callback?steamId=7")] // no token
    [InlineData("http://localhost:3000/callback?steamId=7&token=")] // empty token
    [InlineData("http://localhost:3000/callback?steamId=7&token=%20%20")] // whitespace token
    [InlineData("http://localhost:3000/callback?token=abc")] // no steamId
    [InlineData("http://localhost:3000/callback?steamId=nope&token=abc")] // non-numeric steamId
    [InlineData("http://localhost:3000/callback?steamId=-1&token=abc")] // negative steamId
    [InlineData("http://localhost:3000/callback")] // no query at all
    [SuppressMessage("Design", "CA1054:URI parameters should not be strings",
        Justification = "Test parameters use string for testability")]
    public void ParseCallback_InvalidCallback_Throws(string uri) =>
        Assert.Throws<InvalidOperationException>(() => SteamLoginService.ParseCallback(new Uri(uri)));

    [Fact]
    public void ParseCallback_DuplicateParameter_LastValueWins()
    {
        var uri = new Uri("http://localhost:3000/callback?steamId=100&token=first&token=second&steamId=200");

        var result = SteamLoginService.ParseCallback(uri);

        Assert.Equal(200UL, result.SteamId);
        Assert.Equal("second", result.Token);
    }

    [Fact]
    public void ParseCallback_BareValuelessSegment_IgnoredParsesSuccessfully()
    {
        var uri = new Uri("http://localhost:3000/callback?steamId=42&token=valid&barekey&extra=1");

        var result = SteamLoginService.ParseCallback(uri);

        Assert.Equal(42UL, result.SteamId);
        Assert.Equal("valid", result.Token);
    }

    /// <summary>Starts the interactive flow with the browser suppressed and returns the login URL
    /// it reported, plus the running task.</summary>
    /// <param name="service">The service under test.</param>
    /// <param name="cancellationToken">Token to cancel the login while it awaits a callback.</param>
    private static async Task<(Task<SteamLoginResult> Login, Uri ReturnUrl)> StartLoginAsync(
        SteamLoginService service,
        CancellationToken cancellationToken)
    {
        var reported = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var login = service.LoginAsync("https://example.invalid/login",
            url => reported.TrySetResult(url),
            openBrowser: false,
            cancellationToken);

        var loginUrl = await reported.Task;
        var query = new Uri(loginUrl).Query;
        var returnUrl = Uri.UnescapeDataString(query[(query.IndexOf("returnUrl=", StringComparison.Ordinal)
                                                      + "returnUrl=".Length)..]);
        return (login, new Uri(returnUrl));
    }

    [Fact]
    public async Task LoginAsync_ReturnsResultFromCallbackRedirect()
    {
        var service = new SteamLoginService(port: 0);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var (login, returnUrl) = await StartLoginAsync(service, cts.Token);

        using var http = new HttpClient();
        using var response = await http.GetAsync(new Uri($"{returnUrl}?steamId=7&token=abc"), cts.Token);

        var result = await login;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(7UL, result.SteamId);
        Assert.Equal("abc", result.Token);
    }

    [Fact]
    public async Task LoginAsync_IgnoresCallbackWithWrongNonce_ThenAcceptsTheRealOne()
    {
        var service = new SteamLoginService(port: 0);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var (login, returnUrl) = await StartLoginAsync(service, cts.Token);

        using var http = new HttpClient();
        var forged = new Uri($"{returnUrl.GetLeftPart(UriPartial.Authority)}/callback/forged?steamId=1&token=evil");
        using var forgedResponse = await http.GetAsync(forged, cts.Token);
        Assert.Equal(HttpStatusCode.NotFound, forgedResponse.StatusCode);
        Assert.False(login.IsCompleted);

        using var real = await http.GetAsync(new Uri($"{returnUrl}?steamId=7&token=abc"), cts.Token);

        var result = await login;
        Assert.Equal("abc", result.Token);
    }

    [Fact]
    public async Task LoginAsync_KeepsListeningAfterCallbackWithoutToken()
    {
        var service = new SteamLoginService(port: 0);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var (login, returnUrl) = await StartLoginAsync(service, cts.Token);

        using var http = new HttpClient();
        using var bad = await http.GetAsync(new Uri($"{returnUrl}?steamId=7"), cts.Token);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.False(login.IsCompleted);

        using var good = await http.GetAsync(new Uri($"{returnUrl}?steamId=7&token=abc"), cts.Token);

        var result = await login;
        Assert.Equal("abc", result.Token);
    }

    [Fact]
    public async Task LoginAsync_ReportsUrlPointingAtTheLoopbackCallback()
    {
        var service = new SteamLoginService(port: 0);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var (login, returnUrl) = await StartLoginAsync(service, cts.Token);

        Assert.Equal("localhost", returnUrl.Host);
        Assert.NotEqual(0, returnUrl.Port);
        Assert.StartsWith("/callback/", returnUrl.AbsolutePath, StringComparison.Ordinal);
        Assert.True(returnUrl.AbsolutePath.Length > "/callback/".Length);

        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => login);
    }

    [Fact]
    public async Task LoginAsync_Cancelled_Throws()
    {
        var service = new SteamLoginService(port: 0);
        using var cts = new CancellationTokenSource();
        var (login, _) = await StartLoginAsync(service, cts.Token);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => login);
    }

    [Fact]
    public async Task LoginAsync_PortAlreadyBound_ThrowsWithGuidance()
    {
        var occupied = new SteamLoginService(port: 0);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var (holder, returnUrl) = await StartLoginAsync(occupied, cts.Token);

        var conflicting = new SteamLoginService(returnUrl.Port);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            conflicting.LoginAsync("https://example.invalid/login", null, openBrowser: false, cts.Token));
        Assert.Contains("steamLoginPort: 0", ex.Message, StringComparison.Ordinal);

        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => holder);
    }
}
