# Troubleshooting

Symptoms and fixes for the most common issues.

## Connection refused or times out

**Symptom:** `ConnectAsync` throws or `ErrorOccurred` fires immediately with a connection-refused
or timeout error.

**Cause:** The `port` you are passing is almost certainly the game's UDP join port (usually 28015),
not the Rust+ companion port. They are different ports — the companion port is only delivered in
the server-pairing push notification (`ServerPairing.Port`).

**Fix:**

1. Re-pair the server in game (*Rust+* → *Pair with Server*) and capture the `ServerPairing.Port`
   value from the notification — not the game join port.
2. If you are on a network that blocks direct WebSocket connections (corporate firewall, some VPNs),
   pass `useFacepunchProxy: true` to route traffic through Facepunch's relay:

```csharp
using var rustPlus = new RustPlus(new RustPlusConnection(server, port, playerId, playerToken, UseFacepunchProxy: true));
```

See [Getting Started](getting-started.md) for the full connection example and [Credentials](credentials.md)
for how to obtain the correct port from the pairing notification.

## The pairing notification never arrives

**Symptom:** You paired in game but `PairingListener.WaitForServerPairingAsync` (or
`RustPlusFcm.OnServerPairing`) never fires.

**Checklist:**

1. **Complete the registration chain first.** The FCM listener receives nothing until your device is
   registered with Rust Companion (Step 6 in [Credentials](credentials.md)). If `AcquireCredentialsAsync`
   or `RegisterWithRustPlusAsync` returned an error, re-run the full registration.
2. **Connect the listener before pairing in game.** The FCM socket must be connected and waiting
   before you choose *Pair with Server* — notifications are not queued for offline listeners.
3. **Check the raw `OnPairing` event.** If the notification is arriving but not surfacing as
   `OnServerPairing`, subscribe to the lower-level `OnPairing` event (payload: `FcmMessage`) to
   inspect the raw message. A pairing with `body.Type != "server"` will not fire `OnServerPairing`.
4. **PersistentIds may be filtering it out.** If you are passing a `persistentIds` collection that
   already contains the notification's ID, the socket will silently skip it. Clear the collection
   and reconnect.
5. **Re-pair in game.** If the listener was not connected when you first chose *Pair with Server*,
   go back to the in-game Rust+ menu and pair again.

See [FCM Notifications](fcm-notifications.md) for the complete event table and reconnect strategy.

## The browser doesn't open during registration

**Symptom:** registration prints the Steam login URL and then waits, but no browser window appears.
Common on containers, SSH sessions, WSL and minimal desktop installs.

**Fix:** open the printed URL yourself, in any browser. It is not a degraded path — the callback is
served from your own machine's loopback address, so it works even if you open the link on a
different device that can reach `localhost:<port>` of the machine running registration.

`SteamLoginService` reports the URL through the `onLoginUrl` callback *before* attempting to open a
browser, and never fails just because no browser could be launched.

## Port already in use during registration

**Symptom:** `FcmRegistration.RegisterWithRustPlusAsync` throws `InvalidOperationException` saying
the callback listener could not bind to `http://localhost:3000/`.

**Fix:** pass a different port, or `0` to pick a free one automatically:

```csharp
var registration = new FcmRegistration(steamLoginPort: 0);
```

## Registration fails partway through the chain

**Symptom:** `AcquireCredentialsAsync` or `RegisterWithRustPlusAsync` throws `HttpRequestException`
or returns an unexpected response partway through the 6-step chain.

**Cause:** The registration flow hits live Google (GCM/Firebase/FCM), Expo, and Facepunch
(Rust Companion) endpoints. These constants and endpoints drift when Google or Facepunch update
their apps — `RegistrationConstants` in `src/RustPlusApi.Fcm.Registration/RegistrationConstants.cs`
may be out of date.

**Fix:**

1. Re-check `RegistrationConstants` against the upstream sources it is ported from:
   [rustplus.js](https://github.com/liamcottle/rustplus.js) and
   [@liamcottle/push-receiver](https://github.com/liamcottle/push-receiver).
2. As a fallback, run the upstream Node CLI directly:

```bash
npx @liamcottle/rustplus.js fcm-register
```

   Its `rustplus.config.json` uses a different layout than `CredentialsStore.Load` expects
   (`fcm_credentials.*`), but the `RustPlus.Fcm.ConsoleApp` sample ships a loader that accepts
   both formats — see [Samples](samples.md).

See [Credentials — upstream fragility](credentials.md#upstream-fragility) for more context.

## Entity events never fire

**Symptom:** `OnSmartDeviceTriggered` or `OnStorageMonitorTriggered` never fires even when the
device changes state in game.

**Cause:** The server only sends broadcasts for entities that your client has explicitly queried.
You must make at least one request on the entity (any read request) before broadcasts for it start
arriving.

**Fix:** Call the corresponding info method once after connecting:

```csharp
// Register the entity with the server — broadcasts start after this call.
// GetSmartDeviceInfoAsync works for both smart switches and smart alarms.
await rustPlus.GetSmartDeviceInfoAsync(entityId);

rustPlus.OnSmartDeviceTriggered += (_, e) =>
    Console.WriteLine($"Device {e.Id}: {(e.IsActive ? "on" : "off")}");
```

The registration happens server-side even when the read itself fails (e.g. a strict
`GetSmartSwitchInfoAsync` on an alarm) — so an entity can broadcast while its reads fail. The
broadcast carries no entity type, so the convenience events route by payload shape; when that
heuristic can't work for you, subscribe to `OnEntityChanged` and route on the entity ID yourself.

> [!NOTE]
> Camera frames (`OnCameraRaysReceived`) work differently — they start automatically after
> `SubscribeToCameraAsync`, no extra call needed.

See [RustPlus Client — Events](rustplus-client.md#events) for the full broadcast list.

## Reading a device fails with `ClientMappingFailed`

**Symptom:** `GetSmartSwitchInfoAsync` (or `GetAlarmInfoAsync`) returns `IsSuccess = false` with
`RustPlusErrorCode.ClientMappingFailed` for an entity that definitely exists — while its
broadcasts keep arriving.

**Cause:** The server answers `getEntityInfo` with the entity's *actual* type. Reading an alarm
through the strict switch method (or vice versa) is a client-side type mismatch: the server reply
was successful, the strict mapper refused it. Before 2.0.0-beta.4 this surfaced as a thrown
`InvalidOperationException`, easily mistaken for "device unreachable".

**Fix:** For mixed or unknown device sets, use the type-agnostic read — switch and alarm payloads
are physically identical:

```csharp
var device = await rustPlus.GetSmartDeviceInfoAsync(entityId);
if (device.IsSuccess)
    Console.WriteLine($"Device {entityId} is {(device.Data!.IsActive ? "on" : "off")}");
```

Keep the strict methods when you *want* the type check (e.g. to detect a mis-paired entity).

## Camera subscribe fails with `no_player`

**Symptom:** `SubscribeToCameraAsync` (or `CameraController.SubscribeAsync`) returns
`IsSuccess = false` with `RustPlusErrorCode.NoPlayer` (raw identifier `no_player`) for an
identifier that worked before.

**Cause:** the camera entity no longer exists — it was destroyed in game. Despite the name,
the error says nothing about the paired player's own state. (Verified live: a destroyed
camera produced exactly this error; rebuilding it restored access.)

**Fix:** rebuild the camera/turret/drone in game and set the identifier again on the computer
station. Also remember that cameras are watched while the paired player is *disconnected*
from the server.

## `ErrorOccurred` fires with `TimeoutException` after ~12 minutes

**Symptom:** The `RustPlusFcm` listener raises `ErrorOccurred` with a `TimeoutException` after
roughly 12 minutes of inactivity, even though the network is up.

**Cause:** This is the inactivity watchdog firing by design. The socket sends an MCS heartbeat
ping every **5 minutes** to keep NAT/firewall mappings alive, and considers the connection dead if
no frame arrives for **12 minutes**. When the watchdog triggers, `ErrorOccurred` fires and the
socket disconnects so you can create a fresh listener.

**Fix:** Implement the reconnect loop described in [FCM Notifications — Reconnect strategy](fcm-notifications.md#reconnect-strategy),
which handles `ErrorOccurred` (including `TimeoutException`) with exponential back-off. If you want
longer or shorter intervals, tune them via `RustPlusFcmSocketOptions`:

```csharp
var listener = new RustPlusFcm(credentials, options: new RustPlusFcmSocketOptions
{
    HeartbeatInterval  = TimeSpan.FromMinutes(2),
    InactivityTimeout  = TimeSpan.FromMinutes(20),
});
```

## My `playerToken` stopped working

**Symptom:** `ConnectAsync` succeeds but every request returns an auth error, or the connection is
dropped immediately after the handshake.

**Cause:** Player tokens are per-server and rotate each time you re-pair that server. The old token
is invalidated as soon as a new pairing is issued.

**Fix:** Re-pair the server in game (*Rust+* → *Pair with Server*) to get a fresh token, then
update your stored values:

```csharp
var creds = CredentialsStore.Load("rustplus.config.json");
using var pairingListener = new PairingListener(creds);
var pairing = await pairingListener.WaitForServerPairingAsync();
// pairing.PlayerToken is the fresh token — use it going forward.
```

See [Credentials](credentials.md) for the full pairing flow and [Getting Started](getting-started.md)
for how the four constructor values fit together.
