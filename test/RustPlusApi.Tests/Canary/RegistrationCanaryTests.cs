using RustPlusApi.Fcm.Registration;

using Xunit;

namespace RustPlusApi.Tests.Canary;

/// <summary>
/// Opt-in "canary" tests (v2 §7) that hit the real Google/Facepunch endpoints, so registration
/// breakage is caught early rather than by users. They are <b>Skip</b>ped by default and are NOT
/// part of the CI gate — remove the Skip (or run this trait explicitly) to exercise them.
/// </summary>
[Trait("Category", "Canary")]
public class RegistrationCanaryTests
{
    [Fact(Skip = "Canary: hits the live Google GCM check-in endpoint. Remove Skip to run manually.")]
    public async Task CheckIn_AgainstRealEndpoint_ReturnsIdentity()
    {
        var register = new AndroidFcmRegister();

        var gcm = await register.CheckInAsync();

        Assert.NotEqual(0UL, gcm.AndroidId);
        Assert.NotEqual(0UL, gcm.SecurityToken);
    }

    [Fact(Skip = "Canary: full registration hits live Google + Expo endpoints. Remove Skip to run manually.")]
    public async Task AcquireCredentials_AgainstRealEndpoints_ProducesTokens()
    {
        var registration = new FcmRegistration();

        var credentials = await registration.AcquireCredentialsAsync();

        Assert.NotEqual(0UL, credentials.Gcm.AndroidId);
        Assert.False(string.IsNullOrEmpty(credentials.Fcm?.Token));
        Assert.False(string.IsNullOrEmpty(credentials.ExpoPushToken));
    }
}
