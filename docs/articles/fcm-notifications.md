# FCM Notifications

`RustPlusFcm` (in `RustPlusApi.Fcm`) connects to Firebase Cloud Messaging and raises events when
Rust+ sends push notifications — pairing requests and alarm triggers.

## Connect

```csharp
using RustPlusApi.Fcm;
using RustPlusApi.Fcm.Registration;

var credentials = CredentialsStore.Load("rustplus.config.json");
var listener = new RustPlusFcm(credentials, persistentIds: null);
await listener.ConnectAsync();
// …
listener.Disconnect();
```

`persistentIds` is an optional collection of already-seen notification ids to skip on reconnect.

## Events

| Event | Fires when |
| --- | --- |
| `OnServerPairing` | You choose *Pair with Server* in game — carries ip/port/playerId/playerToken. |
| `OnEntityPairing` | You pair a smart device. |
| `OnSmartSwitchPairing` / `OnSmartAlarmPairing` / `OnStorageMonitorPairing` | A specific entity type is paired. |
| `OnAlarmTriggered` | A paired smart alarm fires. |
| `OnPairing` | Any pairing notification (raw). |

Plus socket lifecycle events: `Connecting`, `Connected`, `SocketClosed`, `Disconnecting`,
`Disconnected`, `ErrorOccurred`.

```csharp
listener.OnServerPairing += (_, e) =>
    Console.WriteLine($"Pair: {e.Data?.Ip}:{e.Data?.Port} (player {e.PlayerId})");

listener.OnAlarmTriggered += (_, alarm) =>
    Console.WriteLine($"Alarm: {alarm?.Title}");
```

## Heartbeat & dead-connection detection

The listener sends its own MCS heartbeat ping every 5 minutes (NATs and firewalls silently drop
idle TCP mappings) and watches for inactivity: if no frame arrives for 12 minutes, the connection
is presumed dead — `ErrorOccurred` fires with a `TimeoutException` and the socket disconnects so
you can create a fresh listener. Both intervals are tunable:

```csharp
var listener = new RustPlusFcm(credentials, options: new RustPlusFcmSocketOptions
{
    HeartbeatInterval = TimeSpan.FromMinutes(2),
    InactivityTimeout = TimeSpan.FromMinutes(6)
});
```

## One-await pairing

For the common "wait for the next server pairing" case, `RustPlusApi.Fcm.Registration` provides
`PairingListener`, which wraps `RustPlusFcm` and returns a strongly-typed `ServerPairing`:

```csharp
using var pairing = new PairingListener(credentials);
ServerPairing server = await pairing.WaitForServerPairingAsync();
using var rustPlus = new RustPlus(server.Ip, server.Port, server.PlayerId, server.PlayerToken);
```

See [Credentials](credentials.md) for how to obtain the FCM credentials.
