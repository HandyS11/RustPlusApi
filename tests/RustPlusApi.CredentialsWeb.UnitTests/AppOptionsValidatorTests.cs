using RustPlusApi.CredentialsWeb;
using Xunit;

namespace RustPlusApi.CredentialsWeb.UnitTests;

public sealed class AppOptionsValidatorTests
{
    private static AppOptions Valid() => new()
    {
        PublicBaseUrl = "https://creds.example.org"
    };

    [Fact]
    public void Validate_ReturnsNull_ForHttpsBaseUrl()
    {
        Assert.Null(AppOptionsValidator.Validate(Valid()));
    }

    [Fact]
    public void Validate_Rejects_BlankBaseUrl()
    {
        var options = Valid();
        options.PublicBaseUrl = "   ";

        var error = AppOptionsValidator.Validate(options);

        Assert.NotNull(error);
        Assert.Contains("PublicBaseUrl", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_Rejects_HttpBaseUrl()
    {
        var options = Valid();
        options.PublicBaseUrl = "http://creds.example.org";

        var error = AppOptionsValidator.Validate(options);

        Assert.NotNull(error);
        Assert.Contains("AllowInsecureBaseUrl", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_Allows_HttpBaseUrl_WhenEscapeHatchSet()
    {
        var options = Valid();
        options.PublicBaseUrl = "http://localhost:8080";
        options.AllowInsecureBaseUrl = true;

        Assert.Null(AppOptionsValidator.Validate(options));
    }

    [Fact]
    public void Validate_Rejects_BaseUrlWithTrailingSlash()
    {
        var options = Valid();
        options.PublicBaseUrl = "https://creds.example.org/";

        var error = AppOptionsValidator.Validate(options);

        Assert.NotNull(error);
        Assert.Contains("trailing", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_Rejects_NonAbsoluteBaseUrl()
    {
        var options = Valid();
        options.PublicBaseUrl = "creds.example.org";

        Assert.NotNull(AppOptionsValidator.Validate(options));
    }

    [Fact]
    public void Validate_Rejects_AKnownProxyThatIsNotAnIpAddress()
    {
        var options = Valid();
        options.KnownProxies.Add("proxy.example.org");

        var error = AppOptionsValidator.Validate(options);

        Assert.NotNull(error);
        Assert.Contains("KnownProxies", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_Accepts_AKnownProxyIpAddress()
    {
        var options = Valid();
        options.KnownProxies.Add("172.18.0.2");

        Assert.Null(AppOptionsValidator.Validate(options));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_Rejects_NonPositiveSessionLimit(int limit)
    {
        var options = Valid();
        options.MaxConcurrentSessions = limit;

        var error = AppOptionsValidator.Validate(options);

        Assert.NotNull(error);
        Assert.Contains("MaxConcurrentSessions", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_Rejects_NonPositiveTtl()
    {
        var options = Valid();
        options.SessionTtl = TimeSpan.Zero;

        var error = AppOptionsValidator.Validate(options);

        Assert.NotNull(error);
        Assert.Contains("SessionTtl", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_Rejects_NonPositivePairingLimit()
    {
        var options = Valid();
        options.MaxConcurrentPairings = 0;

        var error = AppOptionsValidator.Validate(options);

        Assert.NotNull(error);
        Assert.Contains("MaxConcurrentPairings", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_Rejects_NonPositiveCompletionsPerIpPerHour()
    {
        var options = Valid();
        options.MaxCompletionsPerIpPerHour = 0;

        var error = AppOptionsValidator.Validate(options);

        Assert.NotNull(error);
        Assert.Contains("MaxCompletionsPerIpPerHour", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_Rejects_NonPositiveCreatedTtl()
    {
        var options = Valid();
        options.CreatedTtl = TimeSpan.Zero;

        var error = AppOptionsValidator.Validate(options);

        Assert.NotNull(error);
        Assert.Contains("CreatedTtl", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_Rejects_NonPositivePairingTtl()
    {
        var options = Valid();
        options.PairingTtl = TimeSpan.Zero;

        var error = AppOptionsValidator.Validate(options);

        Assert.NotNull(error);
        Assert.Contains("PairingTtl", error, StringComparison.Ordinal);
    }
}
