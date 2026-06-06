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
| `OnEntityParing` | You pair a smart device. |
| `OnSmartSwitchParing` / `OnSmartAlarmParing` / `OnStorageMonitorParing` | A specific entity type is paired. |
| `OnAlarmTriggered` | A paired smart alarm fires. |
| `OnParing` | Any pairing notification (raw). |

Plus socket lifecycle events: `Connecting`, `Connected`, `SocketClosed`, `Disconnecting`,
`Disconnected`, `ErrorOccurred`.

```csharp
listener.OnServerPairing += (_, e) =>
    Console.WriteLine($"Pair: {e.Data?.Ip}:{e.Data?.Port} (player {e.PlayerId})");

listener.OnAlarmTriggered += (_, alarm) =>
    Console.WriteLine($"Alarm: {alarm?.Title}");
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
