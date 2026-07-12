# 2.0.0-beta.4 — Full map-marker surface & entity-info fixes (design)

Date: 2026-07-06. Target: two PRs to `develop`, then tag `2.0.0-beta.4` (versions are
CD-injected; no version bumps in project files).

Findings originate from running RustPlusBot (a `2.0.0-beta.3` consumer) against a live Rust
server on 2026-07-05/06. This spec is self-contained; the scratch notes it was distilled from
are deleted and were never committed.

## Goals

1. The public marker API exposes **every field of the protobuf `AppMarker`** message, placed on
   the marker types where the server actually populates them (per-type placement — that is why
   several typed records exist).
2. Marker types currently discarded (`Explosion`, `Crate`, `GenericRadius`) get typed records
   and dictionaries; unrecognized future types degrade gracefully instead of throwing.
3. A successful server reply never surfaces as a thrown exception: entity-info reads on a
   mismatched type, and any other success-selector failure, return a failed `Response`.
4. Consumers can route `EntityChanged` broadcasts reliably via a new raw `OnEntityChanged`
   event; the convenience events use a hardened heuristic.
5. Documentation corrected where it caused real consumer defects (`ServerMap` units).

Non-goals: no changes to FCM, Camera, or DI packages; no changes to the proto contract itself.

## Protobuf reference (authoritative, from `src/RustPlusApi/Protobuf/RustPlusContracts.proto`)

```proto
message AppMarker {
    required uint64 id = 1;
    required AppMarkerType type = 2;
    required float x = 3;
    required float y = 4;
    optional uint64 steam_id = 5;
    optional float rotation = 6;
    optional float radius = 7;
    optional Vector4 color1 = 8;
    optional Vector4 color2 = 9;
    optional float alpha = 10;
    optional string name = 11;
    optional bool out_of_stock = 12;
    repeated AppMarker.SellOrder sell_orders = 13;   // SellOrder already fully mapped
}

enum AppMarkerType {
    Undefined = 0; Player = 1; Explosion = 2; VendingMachine = 3; CH47 = 4;
    CargoShip = 5; Crate = 6; GenericRadius = 7; PatrolHelicopter = 8; TravellingVendor = 9;
}

message AppEntityPayload {
    optional bool value = 1;
    repeated AppEntityPayload.Item items = 2;
    optional int32 capacity = 3;
    optional bool has_protection = 4;
    optional uint32 protection_expiry = 5;
}

message AppEntityChanged {
    required uint64 entity_id = 1;
    required AppEntityPayload payload = 2;   // note: NO entity type field
}
```

`Vector4` is Unity's vector serialized as optional floats `x, y, z, w`; for `color1`/`color2`
it carries a color (x→R, y→G, z→B, w→A, 0–1 floats).

## Part 1 — Map markers (PR `feature/beta4-map-markers`)

### 1.1 Field placement matrix

| Record | Fields (base `Marker`: `Id`, `X`, `Y`) |
| --- | --- |
| `PlayerMarker` | + `Name`, `SteamId` (no `Rotation` — the server does not populate it for players; both reference apps derive player heading from consecutive position polls, never from the marker) |
| `CargoShipMarker` | + **`Rotation`** (new) |
| `Ch47Marker` | + **`Rotation`** (new) |
| `PatrolHelicopterMarker` | + **`Rotation`** (new) |
| `TravellingVendorMarker` | + **`Rotation`** (new) |
| `VendingMachineMarker` | + `Name`, `IsOutOfStock`, `VendingMachineItems` (unchanged) |
| `ExplosionMarker` (new) | base only |
| `CrateMarker` (new) | base only |
| `GenericRadiusMarker` (new) | + **`Radius`**, **`Color1`**, **`Color2`**, **`Alpha`** |
| `UnknownMarker` | + **all** optional fields: `SteamId`, `Rotation`, `Radius`, `Color1`, `Color2`, `Alpha`, `Name`, `IsOutOfStock`, `VendingMachineItems` (raw passthrough — nothing the server sends for an unrecognized type is dropped) |

New shared DTO in `Data/`:

```csharp
/// <summary>Color carried by a map marker, mapped from the server's Vector4 (RGBA, 0–1).</summary>
public sealed record MarkerColor
{
    public float? R { get; init; }
    public float? G { get; init; }
    public float? B { get; init; }
    public float? A { get; init; }
}
```

### 1.2 Semantics & XML docs

- `Rotation`: *raw server heading in degrees (0–360), as sent by the server; `null` when the
  server omitted the field.* Consumers own the render transform (the reference desktop app
  draws cargo/patrol/CH47 icons rotated by `-rotation` on a Y-down canvas). `null` vs `0`
  distinguishes "no heading" from "heading north".
- **Uniform presence rule** for every optional proto field, including the already-mapped
  ones: absent on the wire → `null` in the model, guarded the way `PriceMultiplier` already
  does it (`marker.ShouldSerializeRotation() ? marker.Rotation : null`; `Color1`/`Color2` via
  null message check). This changes existing behavior for `PlayerMarker.SteamId` /
  `IsOutOfStock` (absent previously surfaced as `0` / `false`); acceptable in beta and pinned
  by tests.

### 1.3 Mappers (`Extensions/AppMarkerToModel.cs`)

- New: `ToExplosionMarker()`, `ToCrateMarker()`, `ToGenericRadiusMarker()`,
  `ToMarkerColor(this Vector4)`.
- Updated: `ToPlayerMarker`, `ToCargoShipMarker`, `ToCh47Marker`, `ToPatrolHelicopterMarker`,
  `ToTravellingVendorMarker` (add `Rotation`), `ToUnknownMarker` (full passthrough).

### 1.4 Dispatch (`Extensions/AppMapMarkerToModel.cs` + `Data/MapMarkers.cs`)

- `MapMarkers` gains `ExplosionMarkers`, `CrateMarkers`, `GenericRadiusMarkers` dictionaries
  (10 total). Remove the `<remarks>` claiming types 2/6/7 are never emitted.
- The switch routes `Explosion`, `Crate`, `GenericRadius` to their mappers; the
  `Debug.WriteLine` branches and `using System.Diagnostics` go away.
- `default:` (unrecognized future enum value) routes to `UnknownMarkers` via
  `ToUnknownMarker()` instead of throwing `ArgumentException` — a Rust game update can no
  longer crash `GetMapMarkersAsync`. (`Undefined` also continues to land there.)

### 1.5 `ServerMap` documentation fix (`Data/ServerMap.cs`, docs-only)

`Width`, `Height`, `OceanMargin` are **pixels of `JpgImage`** (JPEG dimensions and the baked
ocean border), not game units — only marker/monument `X`/`Y` and `ServerInfo.MapSize` are world
units. Correct the three `<summary>` lines and record the canonical world→pixel transform on
the class doc:

```text
px = worldX * ((Width  - 2 * OceanMargin) / ServerInfo.MapSize) + OceanMargin
py = Height - (worldY * ((Height - 2 * OceanMargin) / ServerInfo.MapSize) + OceanMargin)
```

(world origin bottom-left, image origin top-left — hence the Y flip).

## Part 2 — Entity info (PR `feature/beta4-entity-info`)

Background defect: the server answers `getEntityInfo` with the entity's *actual*
`AppEntityType`, so `GetSmartSwitchInfoAsync(alarmId)` receives a **successful** response with
`Type = Alarm` and the strict mapper throws inside the success selector of
`ProcessRequestAsync` — the caller gets a raw `InvalidOperationException`, indistinguishable
from a transport failure. Switch and alarm payloads are physically identical.

### 2.1 Type-tolerant read (additive)

`Extensions/AppEntityInfoToModel.cs`:

```csharp
public static SmartDeviceInfo ToSmartDeviceInfo(this AppEntityInfo entity)
{
    if (entity.Type is not (AppEntityType.Switch or AppEntityType.Alarm))
    {
        throw new InvalidOperationException("Entity type is not a binary-state smart device.");
    }
    return new SmartDeviceInfo { IsActive = entity.Payload.Value };
}
```

`RustPlus.cs`: public `GetSmartDeviceInfoAsync(ulong entityId, CancellationToken)` →
`Response<SmartDeviceInfo?>` using that mapper. The strict `GetSmartSwitchInfoAsync` /
`GetAlarmInfoAsync` remain for callers that want type validation.

### 2.2 Selector exceptions become failed responses (behavioral)

In `ProcessRequestAsync<T>` (`RustPlus.cs:594`), wrap the `successSelector(response)` call:
any exception except `OperationCanceledException` is caught and returned as a failed
`Response<T>` whose error message is the exception message. A successful server reply can
never escape as a thrown exception again. This also covers any future selector defect
anywhere in the typed API surface.

### 2.3 Raw `OnEntityChanged` event (additive)

```csharp
/// <summary>Raised for every EntityChanged broadcast, before any type heuristics.</summary>
public event EventHandler<EntityChangedEventArg>? OnEntityChanged;
```

New `EntityChangedEventArg` (in `Data/Events/`) carrying the full payload surface: `Id`,
`Value`, `Capacity`, `HasProtection`, `ProtectionExpiry` (UTC `DateTime`), `Items`. New mapper
`ToEntityChangedEvent(this AppEntityChanged)` in `Extensions/EntityChangedToModel.cs`.
`ParseNotification` raises it first, unconditionally, for every `EntityChanged` broadcast.

### 2.4 Hardened convenience-event heuristic (behavioral)

Current routing (`Capacity is 0` → smart device, else storage) misroutes: the broadcast
carries no entity type, capacity is not always present on tool-cupboard broadcasts, and
`value == true` storage broadcasts carry no item list (evidence: rustplusplus routes by known
entity id, skips `value === true` storage broadcasts, and special-cases TCs whose broadcasts
lack `capacity`).

New routing in `ParseNotification`, after raising `OnEntityChanged`:

- **Storage-shaped** when `Items` is non-empty **or** `Capacity > 0` **or** `HasProtection`.
- Storage-shaped with `Value == true` and empty `Items`: **suppressed** from
  `OnStorageMonitorTriggered` (it carries no contents snapshot and would wipe tracked
  contents) — still observable via `OnEntityChanged`.
- Everything else → `OnSmartDeviceTriggered`.

Known residual limit (by design): a storage broadcast whose payload is *only*
`value == true` (no items, no capacity, no protection) is indistinguishable from a switch and
routes to `OnSmartDeviceTriggered`; `OnEntityChanged` is the reliable channel for consumers
that know their paired ids. Document this on both convenience events.

### 2.5 Documentation additions

- `getEntityInfo` **subscribes** the connection to that entity's broadcasts server-side, even
  when the client-side mapping fails — document on `GetEntityInfoAsync` and the public typed
  getters (explains "alarm triggers arrive while alarm reads fail").
- Document known `EntityChanged` payload shapes per device type: switch/alarm → `value` only;
  storage → `items`/`capacity`/`hasProtection`/`protectionExpiry`, sometimes partial for tool
  cupboards; `value == true` storage broadcasts carry no items.

## Part 3 — Tests

Constraints: suites run on both TFM hosts (net8.0 exercises the netstandard2.0 build); new
code needs 100/100 line/branch (`tools/coverage/report.sh` gate at 95/90); Stryker cannot
mutate the core project — behavior is pinned by exact-assertion unit tests; read
`docs/development/testing.md` before touching tests.

Unit (`tests/RustPlusApi.UnitTests`):

- `MarkerMapperTests`: per moving type, rotation present → value and absent → `null` (not
  `0`); `GenericRadiusMarker` styling fields present/absent; `ToMarkerColor` component
  mapping; `UnknownMarker` full passthrough (every optional field).
- `MapMarkerDispatchTests`: Explosion/Crate/GenericRadius land in their dictionaries;
  unrecognized enum value lands in `UnknownMarkers` and does not throw; `Undefined` unchanged.
- `EntityInfoMapperTests`: `ToSmartDeviceInfo` accepts Switch and Alarm, rejects
  StorageMonitor with `InvalidOperationException`.

Integration (`tests/RustPlusApi.IntegrationTests` + MockServer):

- `GetSmartDeviceInfoAsync` returns `IsSuccess = true` for a Switch-typed and an Alarm-typed
  reply.
- `GetSmartSwitchInfoAsync` against an Alarm-typed reply returns a **failed** `Response`
  (correct error message, no thrown exception) — pins §2.2 via §2.1's strict path.
- `OnEntityChanged` fires for every `EntityChanged` broadcast shape.
- Routing matrix for §2.4: items-only, capacity-only, protection-only → storage; `value`-only
  → smart device; storage-shaped + `value == true` + no items → suppressed from
  `OnStorageMonitorTriggered` but present on `OnEntityChanged`.
- `GetMapMarkersAsync` with a mixed marker payload including type 2/6/7 and an out-of-range
  type value → all routed, no throw.

## Part 4 — Release

1. PR `feature/beta4-map-markers` → `develop` (Part 1 + its tests).
2. PR `feature/beta4-entity-info` → `develop` (Part 2 + its tests).
3. Both PRs: `dotnet build` (warnings are errors), full test matrix, coverage gate, ReSharper
   `cleanupcode` before push (pre-push hook rejects otherwise).
4. After merge: tag `2.0.0-beta.4`; CD injects the version and publishes the packages.

### Manual live-server verification gate (cannot be CI'd)

- [ ] `GetSmartDeviceInfoAsync(alarmId)` and `(switchId)` both return `IsSuccess = true`.
- [ ] `GetSmartSwitchInfoAsync(alarmId)` returns a failed `Response` (not a thrown exception).
- [ ] Tool-cupboard item change → broadcast observable via `OnEntityChanged` with the TC id.
- [ ] Storage broadcast with `value == true` does not surface an empty `Items` list through
      `OnStorageMonitorTriggered`.
- [ ] Cargo/patrol/CH47/travelling-vendor markers expose a plausible `Rotation` that tracks
      their movement; markers without the field report `null`.

### Behavior changes shipped in beta.4 (acceptable within the beta channel)

- `GetSmartSwitchInfoAsync(alarmId)` (and the inverse) now returns a failed `Response`
  instead of throwing.
- Capacity-less storage broadcasts may now route to `OnStorageMonitorTriggered` (when items
  or protection present) instead of `OnSmartDeviceTriggered`; `value == true` item-less
  storage broadcasts no longer raise `OnStorageMonitorTriggered`.
- `GetMapMarkersAsync` no longer throws on unrecognized marker types (they surface in
  `UnknownMarkers`).
