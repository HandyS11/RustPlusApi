using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Registration;

namespace RustPlusApi.CredentialsWeb.Upstream;

/// <summary>The app's seam over the four live-network classes in RustPlusApi.Fcm.Registration, so the
/// flow can be tested without reaching Google, Expo or Facepunch.</summary>
internal interface IRegistrationSteps
{
    /// <summary>Steps 1-3: GCM check-in, Firebase install, FCM register, Expo token.</summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<Credentials> AcquireDeviceCredentialsAsync(CancellationToken cancellationToken);

    /// <summary>Step 5: register the device's Expo token with Rust Companion.</summary>
    /// <param name="steamToken">The Steam auth token from the Facepunch callback.</param>
    /// <param name="expoPushToken">The Expo token from <see cref="AcquireDeviceCredentialsAsync"/>.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task RegisterWithCompanionAsync(string steamToken, string expoPushToken, CancellationToken cancellationToken);

    /// <summary>Step 6: hold an MCS socket until an in-game pairing push arrives.</summary>
    /// <param name="credentials">Credentials from <see cref="AcquireDeviceCredentialsAsync"/>.</param>
    /// <param name="cancellationToken">Token to cancel the wait.</param>
    Task<ServerPairing> WaitForPairingAsync(Credentials credentials, CancellationToken cancellationToken);
}
