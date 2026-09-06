using RustPlusApi.CredentialsWeb;
using Xunit;

namespace RustPlusApi.CredentialsWeb.UnitTests;

public sealed class AppOptionsValidatorTests
{
    private static AppOptions Valid() => new();

    [Fact]
    public void Validate_ReturnsNull_ForTheDefaults()
    {
        Assert.Null(AppOptionsValidator.Validate(Valid()));
    }

    [Fact]
    public void CreatedTtl_DefaultsToTenMinutes()
    {
        // The pre-login window now has to cover a real Steam login, two-factor included, plus a
        // copy and a paste.
        Assert.Equal(TimeSpan.FromMinutes(10), new AppOptions().CreatedTtl);
    }

    [Fact]
    public void AllowRemotePairing_DefaultsToOff()
    {
        Assert.False(new AppOptions().AllowRemotePairing);
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
