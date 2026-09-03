using RustPlusApi.CredentialsWeb.Upstream;
using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Registration;

namespace RustPlusApi.CredentialsWeb.UnitTests;

/// <summary>Records what the flow asked for, and lets each step be made to fail or hang.</summary>
internal sealed class FakeRegistrationSteps : IRegistrationSteps
{
    internal List<string> Calls { get; } = [];

    internal Credentials CredentialsToReturn { get; set; } = new()
    {
        Gcm = new Gcm
        {
            AndroidId = 1, SecurityToken = 2
        },
        Fcm = new FcmToken
        {
            Token = "fcm-token"
        },
        ExpoPushToken = "ExponentPushToken[fake]"
    };

    internal Exception? AcquireFailure { get; set; }

    internal Exception? CompanionFailure { get; set; }

    internal Exception? PairingFailure { get; set; }

    internal ServerPairing PairingToReturn { get; set; } = new()
    {
        Ip = "10.0.0.1",
        Port = 28082,
        PlayerId = 76561198249527954,
        PlayerToken = 987654321,
        Name = "Test Server"
    };

    /// <summary>When set, the pairing wait blocks until this is signalled or cancelled.</summary>
    internal TaskCompletionSource PairingGate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal bool PairingWaitsForGate { get; set; }

    internal string? SteamTokenSeen { get; private set; }

    public Task<Credentials> AcquireDeviceCredentialsAsync(CancellationToken cancellationToken)
    {
        Calls.Add(nameof(AcquireDeviceCredentialsAsync));
        return AcquireFailure is not null
            ? Task.FromException<Credentials>(AcquireFailure)
            : Task.FromResult(CredentialsToReturn);
    }

    public Task RegisterWithCompanionAsync(string steamToken, string expoPushToken, CancellationToken cancellationToken)
    {
        Calls.Add(nameof(RegisterWithCompanionAsync));
        SteamTokenSeen = steamToken;
        return CompanionFailure is not null ? Task.FromException(CompanionFailure) : Task.CompletedTask;
    }

    public async Task<ServerPairing> WaitForPairingAsync(Credentials credentials, CancellationToken cancellationToken)
    {
        Calls.Add(nameof(WaitForPairingAsync));

        if (PairingFailure is not null)
        {
            throw PairingFailure;
        }

        if (PairingWaitsForGate)
        {
            await PairingGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        return PairingToReturn;
    }
}
