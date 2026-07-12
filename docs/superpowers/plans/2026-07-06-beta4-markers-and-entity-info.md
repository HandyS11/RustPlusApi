# 2.0.0-beta.4 Implementation Plan — Full Marker Surface & Entity-Info Fixes

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship RustPlusApi `2.0.0-beta.4`: expose the full protobuf `AppMarker` surface across per-type marker records, and fix the entity-info defects (selector throws on successful replies, unreliable broadcast routing).

**Architecture:** Two independent feature branches off `develop`, one PR each — `feature/beta4-map-markers` (Data records + mapper extensions + dispatch) and `feature/beta4-entity-info` (`RustPlus.cs` request/broadcast pipeline + entity mappers). They touch disjoint files and may be built in any order. After both merge, a `v2.0.0-beta.4` tag on `develop` triggers CD.

**Tech Stack:** C# multi-targeting netstandard2.0 + net10.0, protobuf-net.BuildTools compile-time contracts, xUnit on net8.0 + net10.0 hosts, in-process `MockRustPlusServer` for integration tests.

**Spec:** `docs/superpowers/specs/2026-07-06-beta4-markers-and-entity-info-design.md`

## Global Constraints

- `dotnet build` is strict: `TreatWarningsAsErrors` + latest-all analyzers (Roslynator, Sonar, VSTHRD). Every public member needs XML docs.
- `dotnet test RustPlusApi.sln` runs every suite on BOTH TFM hosts (net8.0 exercises the netstandard2.0 build). All tests must pass on both.
- New code must reach 100/100 line/branch coverage (`tools/coverage/report.sh` gates at 95/90). No new `[ExcludeFromCodeCoverage]` without a justification in `docs/development/testing.md` — this plan needs none.
- Before every push: `dotnet tool restore && dotnet jb cleanupcode RustPlusApi.sln --profile="ReformatAndReorder"` — the committed pre-push hook rejects pushes that the formatter would change.
- Never bump versions in project files; CD injects the version from the tag.
- Git discipline: **only the orchestrating session runs branch commands** (`git checkout`/`switch`) — subagents implementing tasks must never switch branches. Commits on the feature branches are part of this approved plan. End commit messages with `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.
- Presence rule (spec §1.2): every optional proto field maps absent → `null` via the generated `ShouldSerialize*()` guards (verified to exist for all fields used here), never a zero default. This includes `string` fields: the generated getters coalesce unset to `""` (`get => __pbn__Name ?? "";`), so `Name` needs `ShouldSerializeName()` like the rest.
- The conditional-with-null pattern `cond ? value : null` assigned to a nullable property is the established idiom (see `PriceMultiplier` in `AppMarkerToModel.cs:67`) — reuse it verbatim.

---

## Phase 1 — PR `feature/beta4-map-markers`

### Task 1: Create the branch

**Files:** none (git only). **Orchestrator-only step — do not delegate to a subagent.**

- [ ] **Step 1: Branch off up-to-date develop**

```bash
git -C /home/handys11/Dev/RustPlusApi checkout develop
git -C /home/handys11/Dev/RustPlusApi pull
git -C /home/handys11/Dev/RustPlusApi checkout -b feature/beta4-map-markers
```

Expected: `Switched to a new branch 'feature/beta4-map-markers'`

### Task 2: `MarkerColor` record + `ToMarkerColor` mapper

**Files:**

- Create: `src/RustPlusApi/Data/MarkerColor.cs`
- Modify: `src/RustPlusApi/Extensions/AppMarkerToModel.cs`
- Test: `tests/RustPlusApi.UnitTests/MarkerMapperTests.cs`

**Interfaces:**

- Consumes: generated `RustPlusContracts.Vector4` (`X/Y/Z/W` floats + `ShouldSerializeX()`…`ShouldSerializeW()`).
- Produces: `RustPlusApi.Data.MarkerColor` record (`float? R/G/B/A`) and `public static MarkerColor ToMarkerColor(this Vector4 color)` — Tasks 4 and 5 use both.

- [ ] **Step 1: Write the failing tests** — append to `MarkerMapperTests`:

```csharp
    [Fact]
    public void ToMarkerColor_MapsComponents()
    {
        var color = new Vector4
        {
            X = 0.1f, Y = 0.2f, Z = 0.3f, W = 0.4f
        };

        var m = color.ToMarkerColor();

        Assert.Equal(0.1f, m.R);
        Assert.Equal(0.2f, m.G);
        Assert.Equal(0.3f, m.B);
        Assert.Equal(0.4f, m.A);
    }

    [Fact]
    public void ToMarkerColor_UnsetComponents_AreNull()
    {
        var m = new Vector4().ToMarkerColor();

        Assert.Null(m.R);
        Assert.Null(m.G);
        Assert.Null(m.B);
        Assert.Null(m.A);
    }
```

- [ ] **Step 2: Verify it fails to compile**

Run: `dotnet test RustPlusApi.sln -f net10.0 --filter "FullyQualifiedName~MarkerMapperTests.ToMarkerColor"`
Expected: build error `'Vector4' does not contain a definition for 'ToMarkerColor'`

- [ ] **Step 3: Create `src/RustPlusApi/Data/MarkerColor.cs`**

```csharp
namespace RustPlusApi.Data;

/// <summary>Color carried by a map marker, mapped from the server's <c>Vector4</c> (RGBA components, 0–1).</summary>
public sealed record MarkerColor
{
    /// <summary>Red component (0–1), or <see langword="null"/> when the server omitted it.</summary>
    public float? R { get; init; }

    /// <summary>Green component (0–1), or <see langword="null"/> when the server omitted it.</summary>
    public float? G { get; init; }

    /// <summary>Blue component (0–1), or <see langword="null"/> when the server omitted it.</summary>
    public float? B { get; init; }

    /// <summary>Alpha component (0–1), or <see langword="null"/> when the server omitted it.</summary>
    public float? A { get; init; }
}
```

- [ ] **Step 4: Add the mapper to `AppMarkerToModel`**

```csharp
    /// <summary>Maps a protobuf <see cref="Vector4"/> marker color to a <see cref="MarkerColor"/>.</summary>
    /// <param name="color">The protobuf color vector (RGBA components).</param>
    public static MarkerColor ToMarkerColor(this Vector4 color)
    {
        return new MarkerColor
        {
            R = color.ShouldSerializeX() ? color.X : null,
            G = color.ShouldSerializeY() ? color.Y : null,
            B = color.ShouldSerializeZ() ? color.Z : null,
            A = color.ShouldSerializeW() ? color.W : null
        };
    }
```

- [ ] **Step 5: Run tests on both TFMs**

Run: `dotnet test RustPlusApi.sln --filter "FullyQualifiedName~MarkerMapperTests"`
Expected: PASS (net8.0 and net10.0 hosts)

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusApi/Data/MarkerColor.cs src/RustPlusApi/Extensions/AppMarkerToModel.cs tests/RustPlusApi.UnitTests/MarkerMapperTests.cs
git commit -m "Add MarkerColor record and Vector4 mapper"
```

### Task 3: `Rotation` on moving markers + uniform presence guards

> **AMENDED 2026-07-06 (user decision):** `PlayerMarker` does NOT get `Rotation` — evidence from
> rustplus-desktop (player heading derived from movement history; marker rotation never read for
> players) and rustplusplus. Rotation ships on the four event movers only: CargoShip, CH47,
> PatrolHelicopter, TravellingVendor. The Player rotation code/tests below were removed in a
> follow-up commit; the four-mover code stands.

**Files:**

- Modify: `src/RustPlusApi/Data/Markers/PlayerMarker.cs`, `CargoShipMarker.cs`, `Ch47Marker.cs`, `PatrolHelicopterMarker.cs`, `TravellingVendorMarker.cs`, `VendingMachineMarker.cs` (doc only — property types unchanged)
- Modify: `src/RustPlusApi/Extensions/AppMarkerToModel.cs`
- Test: `tests/RustPlusApi.UnitTests/MarkerMapperTests.cs`

**Interfaces:**

- Produces: `float? Rotation { get; init; }` on the five moving marker records; `ToPlayerMarker`/`To*Marker` now null-guard `SteamId` and `IsOutOfStock`.

- [ ] **Step 1: Write the failing tests** — append to `MarkerMapperTests`:

```csharp
    private static float? MapRotation(AppMarker marker, AppMarkerType type) => type switch
    {
        AppMarkerType.Player => marker.ToPlayerMarker().Rotation,
        AppMarkerType.Ch47 => marker.ToCh47Marker().Rotation,
        AppMarkerType.CargoShip => marker.ToCargoShipMarker().Rotation,
        AppMarkerType.PatrolHelicopter => marker.ToPatrolHelicopterMarker().Rotation,
        _ => marker.ToTravellingVendorMarker().Rotation,
    };

    [Theory]
    [InlineData(AppMarkerType.Player)]
    [InlineData(AppMarkerType.Ch47)]
    [InlineData(AppMarkerType.CargoShip)]
    [InlineData(AppMarkerType.PatrolHelicopter)]
    [InlineData(AppMarkerType.TravellingVendor)]
    public void MovingMarkers_RotationPresent_MapsValue(AppMarkerType type)
    {
        var marker = Marker(type);
        marker.Rotation = 123.5f;

        Assert.Equal(123.5f, MapRotation(marker, type));
    }

    [Theory]
    [InlineData(AppMarkerType.Player)]
    [InlineData(AppMarkerType.Ch47)]
    [InlineData(AppMarkerType.CargoShip)]
    [InlineData(AppMarkerType.PatrolHelicopter)]
    [InlineData(AppMarkerType.TravellingVendor)]
    public void MovingMarkers_RotationAbsent_MapsNull(AppMarkerType type)
        => Assert.Null(MapRotation(Marker(type), type));

    [Fact]
    public void ToPlayerMarker_UnsetOptionals_AreNull()
    {
        var m = new AppMarker
        {
            Id = 1, X = 0, Y = 0, Type = AppMarkerType.Player
        }.ToPlayerMarker();

        Assert.Null(m.SteamId);
        Assert.Null(m.Name);
        Assert.Null(m.Rotation);
    }

    [Fact]
    public void ToVendingMachineMarker_UnsetOutOfStock_IsNull()
    {
        var m = new AppMarker
        {
            Id = 1, X = 0, Y = 0, Type = AppMarkerType.VendingMachine
        }.ToVendingMachineMarker();

        Assert.Null(m.IsOutOfStock);
    }
```

- [ ] **Step 2: Verify it fails to compile**

Run: `dotnet test RustPlusApi.sln -f net10.0 --filter "FullyQualifiedName~MarkerMapperTests"`
Expected: build error `'PlayerMarker' does not contain a definition for 'Rotation'`

- [ ] **Step 3: Add `Rotation` to the five records**

The XML doc is identical on all five. `PlayerMarker` becomes:

```csharp
namespace RustPlusApi.Data.Markers;

/// <summary>Map marker for a player's current position.</summary>
public sealed record PlayerMarker : Marker
{
    /// <summary>In-game display name of the player.</summary>
    public string? Name { get; init; }

    /// <summary>Heading in degrees (0–360) as sent by the server, or <see langword="null"/> when omitted.
    /// Consumers own the render transform (the official app draws icons rotated by <c>-Rotation</c> on a Y-down canvas).</summary>
    public float? Rotation { get; init; }

    /// <summary>Steam64 ID of the player.</summary>
    public ulong? SteamId { get; init; }
}
```

`CargoShipMarker` becomes (apply the same shape to `Ch47Marker`, `PatrolHelicopterMarker`, `TravellingVendorMarker`, keeping each file's existing `<summary>` on the record):

```csharp
namespace RustPlusApi.Data.Markers;

/// <summary>Map marker for the Cargo Ship event.</summary>
public sealed record CargoShipMarker : Marker
{
    /// <summary>Heading in degrees (0–360) as sent by the server, or <see langword="null"/> when omitted.
    /// Consumers own the render transform (the official app draws icons rotated by <c>-Rotation</c> on a Y-down canvas).</summary>
    public float? Rotation { get; init; }
}
```

- [ ] **Step 4: Update the mappers in `AppMarkerToModel`**

`ToPlayerMarker` body becomes:

```csharp
        return new PlayerMarker
        {
            Id = marker.Id,
            X = marker.X,
            Y = marker.Y,
            Name = marker.ShouldSerializeName() ? marker.Name : null,
            Rotation = marker.ShouldSerializeRotation() ? marker.Rotation : null,
            SteamId = marker.ShouldSerializeSteamId() ? marker.SteamId : null
        };
```

`ToCargoShipMarker` body becomes (same change for `ToCh47Marker`, `ToPatrolHelicopterMarker`, `ToTravellingVendorMarker`, each constructing its own record type):

```csharp
        return new CargoShipMarker
        {
            Id = marker.Id,
            X = marker.X,
            Y = marker.Y,
            Rotation = marker.ShouldSerializeRotation() ? marker.Rotation : null
        };
```

In `ToVendingMachineMarker`, change the `Name` and `IsOutOfStock` lines to:

```csharp
            Name = marker.ShouldSerializeName() ? marker.Name : null,
            IsOutOfStock = marker.ShouldSerializeOutOfStock() ? marker.OutOfStock : null,
```

- [ ] **Step 5: Run tests on both TFMs**

Run: `dotnet test RustPlusApi.sln --filter "FullyQualifiedName~MarkerMapperTests"`
Expected: PASS. Note: `ToPlayerMarker_MapsNameAndSteamId` still passes — the shared `Marker()` helper sets `SteamId`, so the guard returns the value.

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusApi/Data/Markers/ src/RustPlusApi/Extensions/AppMarkerToModel.cs tests/RustPlusApi.UnitTests/MarkerMapperTests.cs
git commit -m "Map marker rotation and enforce absent-to-null on optional marker fields"
```

### Task 4: `ExplosionMarker`, `CrateMarker`, `GenericRadiusMarker` records + mappers

**Files:**

- Create: `src/RustPlusApi/Data/Markers/ExplosionMarker.cs`, `CrateMarker.cs`, `GenericRadiusMarker.cs`
- Modify: `src/RustPlusApi/Extensions/AppMarkerToModel.cs`
- Test: `tests/RustPlusApi.UnitTests/MarkerMapperTests.cs`

**Interfaces:**

- Consumes: `MarkerColor` + `ToMarkerColor` from Task 2.
- Produces: `ToExplosionMarker()`, `ToCrateMarker()`, `ToGenericRadiusMarker()` — Task 6 routes to them. `GenericRadiusMarker` has `float? Radius`, `MarkerColor? Color1`, `MarkerColor? Color2`, `float? Alpha`.

- [ ] **Step 1: Write the failing tests** — append to `MarkerMapperTests`:

```csharp
    [Fact]
    public void ToExplosionMarker_MapsIdAndCoords()
    {
        var m = Marker(AppMarkerType.Explosion).ToExplosionMarker();

        Assert.Equal(7u, m.Id);
        Assert.Equal(1.5f, m.X);
        Assert.Equal(2.5f, m.Y);
    }

    [Fact]
    public void ToCrateMarker_MapsIdAndCoords()
    {
        var m = Marker(AppMarkerType.Crate).ToCrateMarker();

        Assert.Equal(7u, m.Id);
        Assert.Equal(1.5f, m.X);
        Assert.Equal(2.5f, m.Y);
    }

    [Fact]
    public void ToGenericRadiusMarker_MapsStyling()
    {
        var marker = Marker(AppMarkerType.GenericRadius);
        marker.Radius = 25f;
        marker.Alpha = 0.75f;
        marker.Color1 = new Vector4
        {
            X = 1f, Y = 0.5f, Z = 0.25f, W = 1f
        };
        marker.Color2 = new Vector4
        {
            X = 0f, Y = 0f, Z = 0f, W = 0.5f
        };

        var m = marker.ToGenericRadiusMarker();

        Assert.Equal(25f, m.Radius);
        Assert.Equal(0.75f, m.Alpha);
        Assert.Equal(1f, m.Color1!.R);
        Assert.Equal(0.5f, m.Color2!.A);
    }

    [Fact]
    public void ToGenericRadiusMarker_UnsetStyling_IsNull()
    {
        var m = Marker(AppMarkerType.GenericRadius).ToGenericRadiusMarker();

        Assert.Null(m.Radius);
        Assert.Null(m.Alpha);
        Assert.Null(m.Color1);
        Assert.Null(m.Color2);
    }
```

- [ ] **Step 2: Verify it fails to compile**

Run: `dotnet test RustPlusApi.sln -f net10.0 --filter "FullyQualifiedName~MarkerMapperTests"`
Expected: build error `'AppMarker' does not contain a definition for 'ToExplosionMarker'`

- [ ] **Step 3: Create the three records**

`src/RustPlusApi/Data/Markers/ExplosionMarker.cs`:

```csharp
namespace RustPlusApi.Data.Markers;

/// <summary>Map marker for an explosion event (Rust+ marker type 2).</summary>
public sealed record ExplosionMarker : Marker;
```

`src/RustPlusApi/Data/Markers/CrateMarker.cs`:

```csharp
namespace RustPlusApi.Data.Markers;

/// <summary>Map marker for a locked crate (Rust+ marker type 6).</summary>
public sealed record CrateMarker : Marker;
```

`src/RustPlusApi/Data/Markers/GenericRadiusMarker.cs`:

```csharp
namespace RustPlusApi.Data.Markers;

/// <summary>Map marker for a generic radius overlay (Rust+ marker type 7), carrying its styling.</summary>
public sealed record GenericRadiusMarker : Marker
{
    /// <summary>Opacity of the overlay (0–1), or <see langword="null"/> when omitted.</summary>
    public float? Alpha { get; init; }

    /// <summary>Primary color of the overlay, or <see langword="null"/> when omitted.</summary>
    public MarkerColor? Color1 { get; init; }

    /// <summary>Secondary color of the overlay, or <see langword="null"/> when omitted.</summary>
    public MarkerColor? Color2 { get; init; }

    /// <summary>Radius of the overlay circle in world units, or <see langword="null"/> when omitted.</summary>
    public float? Radius { get; init; }
}
```

(`MarkerColor` lives in the parent namespace `RustPlusApi.Data` — no `using` needed, same as `VendingMachineItem` in `VendingMachineMarker.cs`.)

- [ ] **Step 4: Add the three mappers to `AppMarkerToModel`**

```csharp
    /// <summary>Maps a marker to an <see cref="ExplosionMarker"/>.</summary>
    /// <param name="marker">The protobuf map marker.</param>
    public static ExplosionMarker ToExplosionMarker(this AppMarker marker)
    {
        return new ExplosionMarker
        {
            Id = marker.Id, X = marker.X, Y = marker.Y
        };
    }

    /// <summary>Maps a marker to a <see cref="CrateMarker"/>.</summary>
    /// <param name="marker">The protobuf map marker.</param>
    public static CrateMarker ToCrateMarker(this AppMarker marker)
    {
        return new CrateMarker
        {
            Id = marker.Id, X = marker.X, Y = marker.Y
        };
    }

    /// <summary>Maps a marker to a <see cref="GenericRadiusMarker"/>, including its styling fields.</summary>
    /// <param name="marker">The protobuf map marker.</param>
    public static GenericRadiusMarker ToGenericRadiusMarker(this AppMarker marker)
    {
        return new GenericRadiusMarker
        {
            Id = marker.Id,
            X = marker.X,
            Y = marker.Y,
            Radius = marker.ShouldSerializeRadius() ? marker.Radius : null,
            Color1 = marker.Color1?.ToMarkerColor(),
            Color2 = marker.Color2?.ToMarkerColor(),
            Alpha = marker.ShouldSerializeAlpha() ? marker.Alpha : null
        };
    }
```

- [ ] **Step 5: Run tests on both TFMs**

Run: `dotnet test RustPlusApi.sln --filter "FullyQualifiedName~MarkerMapperTests"`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusApi/Data/Markers/ src/RustPlusApi/Extensions/AppMarkerToModel.cs tests/RustPlusApi.UnitTests/MarkerMapperTests.cs
git commit -m "Add explosion, crate and generic-radius marker types"
```

### Task 5: `UnknownMarker` full raw passthrough

**Files:**

- Modify: `src/RustPlusApi/Data/Markers/UnknownMarker.cs`
- Modify: `src/RustPlusApi/Extensions/AppMarkerToModel.cs` (`ToUnknownMarker`)
- Test: `tests/RustPlusApi.UnitTests/MarkerMapperTests.cs`

**Interfaces:**

- Produces: `UnknownMarker` with the full optional-field surface — Task 6's `default:` arm relies on nothing being dropped.

- [ ] **Step 1: Write the failing tests** — append to `MarkerMapperTests`:

```csharp
    [Fact]
    public void ToUnknownMarker_PassesThroughFullSurface()
    {
        var marker = Marker(AppMarkerType.Undefined);
        marker.Rotation = 90f;
        marker.Radius = 10f;
        marker.Alpha = 0.5f;
        marker.Color1 = new Vector4
        {
            X = 1f
        };
        marker.SellOrders.Add(new SellOrder
        {
            ItemId = 1, Quantity = 1, CostPerItem = 1, AmountInStock = 1
        });

        var m = marker.ToUnknownMarker();

        Assert.Equal("M", m.Name);
        Assert.Equal(76561198000000001ul, m.SteamId);
        Assert.True(m.IsOutOfStock);
        Assert.Equal(90f, m.Rotation);
        Assert.Equal(10f, m.Radius);
        Assert.Equal(0.5f, m.Alpha);
        Assert.Equal(1f, m.Color1!.R);
        Assert.Null(m.Color2);
        Assert.Single(m.VendingMachineItems!);
    }

    [Fact]
    public void ToUnknownMarker_UnsetOptionals_AreNull()
    {
        var m = new AppMarker
        {
            Id = 1, X = 0, Y = 0, Type = AppMarkerType.Undefined
        }.ToUnknownMarker();

        Assert.Null(m.Name);
        Assert.Null(m.SteamId);
        Assert.Null(m.IsOutOfStock);
        Assert.Null(m.Rotation);
        Assert.Null(m.Radius);
        Assert.Null(m.Alpha);
        Assert.Null(m.Color1);
        Assert.Null(m.Color2);
        Assert.Empty(m.VendingMachineItems!);
    }
```

- [ ] **Step 2: Verify it fails to compile**

Run: `dotnet test RustPlusApi.sln -f net10.0 --filter "FullyQualifiedName~MarkerMapperTests"`
Expected: build error `'UnknownMarker' does not contain a definition for 'Name'`

- [ ] **Step 3: Rewrite `UnknownMarker.cs`**

```csharp
namespace RustPlusApi.Data.Markers;

/// <summary>Marker with an unrecognised or unsupported type. Carries the full raw field surface of
/// the protobuf marker so nothing the server sends for a new marker type is dropped.</summary>
public sealed record UnknownMarker : Marker
{
    /// <summary>Opacity (0–1), or <see langword="null"/> when omitted.</summary>
    public float? Alpha { get; init; }

    /// <summary>Primary color, or <see langword="null"/> when omitted.</summary>
    public MarkerColor? Color1 { get; init; }

    /// <summary>Secondary color, or <see langword="null"/> when omitted.</summary>
    public MarkerColor? Color2 { get; init; }

    /// <summary><see langword="true"/> if the marker reports being out of stock, or <see langword="null"/> when omitted.</summary>
    public bool? IsOutOfStock { get; init; }

    /// <summary>Display name carried by the marker, or <see langword="null"/> when omitted.</summary>
    public string? Name { get; init; }

    /// <summary>Radius in world units, or <see langword="null"/> when omitted.</summary>
    public float? Radius { get; init; }

    /// <summary>Heading in degrees (0–360) as sent by the server, or <see langword="null"/> when omitted.</summary>
    public float? Rotation { get; init; }

    /// <summary>Steam64 ID carried by the marker, or <see langword="null"/> when omitted.</summary>
    public ulong? SteamId { get; init; }

    /// <summary>Sell orders carried by the marker; empty when none were sent.</summary>
    public IEnumerable<VendingMachineItem>? VendingMachineItems { get; init; }
}
```

- [ ] **Step 4: Rewrite `ToUnknownMarker` in `AppMarkerToModel`**

```csharp
    /// <summary>Maps a marker to an <see cref="UnknownMarker"/>, passing through the full raw field surface.</summary>
    /// <param name="marker">The protobuf map marker.</param>
    public static UnknownMarker ToUnknownMarker(this AppMarker marker)
    {
        return new UnknownMarker
        {
            Id = marker.Id,
            X = marker.X,
            Y = marker.Y,
            Name = marker.ShouldSerializeName() ? marker.Name : null,
            SteamId = marker.ShouldSerializeSteamId() ? marker.SteamId : null,
            Rotation = marker.ShouldSerializeRotation() ? marker.Rotation : null,
            Radius = marker.ShouldSerializeRadius() ? marker.Radius : null,
            Color1 = marker.Color1?.ToMarkerColor(),
            Color2 = marker.Color2?.ToMarkerColor(),
            Alpha = marker.ShouldSerializeAlpha() ? marker.Alpha : null,
            IsOutOfStock = marker.ShouldSerializeOutOfStock() ? marker.OutOfStock : null,
            VendingMachineItems = marker.SellOrders.ToVendingMachineItems()
        };
    }
```

- [ ] **Step 5: Run tests on both TFMs**

Run: `dotnet test RustPlusApi.sln --filter "FullyQualifiedName~MarkerMapperTests"`
Expected: PASS (including the pre-existing `ToUnknownMarker_MapsIdAndCoords`)

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusApi/Data/Markers/UnknownMarker.cs src/RustPlusApi/Extensions/AppMarkerToModel.cs tests/RustPlusApi.UnitTests/MarkerMapperTests.cs
git commit -m "Pass the full raw marker surface through UnknownMarker"
```

### Task 6: Dispatch — new dictionaries, no-throw fallback

**Files:**

- Modify: `src/RustPlusApi/Data/MapMarkers.cs`
- Modify: `src/RustPlusApi/Extensions/AppMapMarkerToModel.cs`
- Test: `tests/RustPlusApi.UnitTests/MapMarkerDispatchTests.cs`
- Test: `tests/RustPlusApi.IntegrationTests/EntityClientTests.cs`

**Interfaces:**

- Consumes: `ToExplosionMarker`/`ToCrateMarker`/`ToGenericRadiusMarker` (Task 4), full-surface `ToUnknownMarker` (Task 5).
- Produces: `MapMarkers.ExplosionMarkers`, `.CrateMarkers`, `.GenericRadiusMarkers` dictionaries.

- [ ] **Step 1: Update the unit tests** — in `MapMarkerDispatchTests`, extend `ToMapMarkers_RoutesEachKnownTypeToItsDictionary` to cover all ten types, and REPLACE `ToMapMarkers_IgnoresNoOpMarkerTypes` and `ToMapMarkers_UnknownType_Throws` (delete both) with the fallback test. Also update the class `<summary>` (the routing no longer throws):

```csharp
/// <summary>Locks the marker routing in <see cref="AppMapMarkerToModel.ToMapMarkers"/>:
/// each type lands in the right dictionary and unrecognized types fall back to
/// <c>UnknownMarkers</c> instead of throwing.</summary>
```

```csharp
    [Fact]
    public void ToMapMarkers_RoutesEachKnownTypeToItsDictionary()
    {
        var result = With(
            (1, AppMarkerType.Undefined),
            (2, AppMarkerType.Player),
            (3, AppMarkerType.VendingMachine),
            (4, AppMarkerType.Ch47),
            (5, AppMarkerType.CargoShip),
            (6, AppMarkerType.PatrolHelicopter),
            (7, AppMarkerType.TravellingVendor),
            (8, AppMarkerType.Explosion),
            (9, AppMarkerType.Crate),
            (10, AppMarkerType.GenericRadius)).ToMapMarkers();

        Assert.True(result.UnknownMarkers.ContainsKey(1));
        Assert.True(result.PlayerMarkers.ContainsKey(2));
        Assert.True(result.VendingMachineMarkers.ContainsKey(3));
        Assert.True(result.Ch47Markers.ContainsKey(4));
        Assert.True(result.CargoShipMarkers.ContainsKey(5));
        Assert.True(result.PatrolHelicopterMarkers.ContainsKey(6));
        Assert.True(result.TravellingVendorMarkers.ContainsKey(7));
        Assert.True(result.ExplosionMarkers.ContainsKey(8));
        Assert.True(result.CrateMarkers.ContainsKey(9));
        Assert.True(result.GenericRadiusMarkers.ContainsKey(10));
    }

    [Fact]
    public void ToMapMarkers_UnrecognizedType_FallsBackToUnknown()
    {
        var result = With((1, (AppMarkerType)999)).ToMapMarkers();

        var marker = Assert.Single(result.UnknownMarkers).Value;
        Assert.Equal(1u, marker.Id);
    }
```

- [ ] **Step 2: Verify it fails to compile**

Run: `dotnet test RustPlusApi.sln -f net10.0 --filter "FullyQualifiedName~MapMarkerDispatchTests"`
Expected: build error `'MapMarkers' does not contain a definition for 'ExplosionMarkers'`

- [ ] **Step 3: Update `MapMarkers.cs`** — replace the record (drop the stale `<remarks>`, add three dictionaries):

```csharp
using RustPlusApi.Data.Markers;

namespace RustPlusApi.Data;

/// <summary>Collects all map markers returned by the Rust+ server, keyed by marker ID.
/// Markers of a type this library does not recognize land in <see cref="UnknownMarkers"/>.</summary>
public sealed record MapMarkers
{
    /// <summary>Cargo ship markers, keyed by marker ID.</summary>
    public Dictionary<ulong, CargoShipMarker> CargoShipMarkers { get; init; } = [];

    /// <summary>CH-47 (Chinook helicopter) markers, keyed by marker ID.</summary>
    public Dictionary<ulong, Ch47Marker> Ch47Markers { get; init; } = [];

    /// <summary>Locked crate markers, keyed by marker ID.</summary>
    public Dictionary<ulong, CrateMarker> CrateMarkers { get; init; } = [];

    /// <summary>Explosion markers, keyed by marker ID.</summary>
    public Dictionary<ulong, ExplosionMarker> ExplosionMarkers { get; init; } = [];

    /// <summary>Generic radius overlay markers, keyed by marker ID.</summary>
    public Dictionary<ulong, GenericRadiusMarker> GenericRadiusMarkers { get; init; } = [];

    /// <summary>Patrol helicopter markers, keyed by marker ID.</summary>
    public Dictionary<ulong, PatrolHelicopterMarker> PatrolHelicopterMarkers { get; init; } = [];

    /// <summary>Player position markers, keyed by marker ID.</summary>
    public Dictionary<ulong, PlayerMarker> PlayerMarkers { get; init; } = [];

    /// <summary>Travelling vendor markers, keyed by marker ID.</summary>
    public Dictionary<ulong, TravellingVendorMarker> TravellingVendorMarkers { get; init; } = [];

    /// <summary>Markers of unknown or unrecognised type (full raw surface), keyed by marker ID.</summary>
    public Dictionary<ulong, UnknownMarker> UnknownMarkers { get; init; } = [];

    /// <summary>Vending machine markers, keyed by marker ID.</summary>
    public Dictionary<ulong, VendingMachineMarker> VendingMachineMarkers { get; init; } = [];
}
```

- [ ] **Step 4: Rewrite `AppMapMarkerToModel.cs`** (drop `System.Diagnostics`, the WTF branches, and the `ArgumentException`):

```csharp
using RustPlusApi.Data;
using RustPlusApi.Data.Markers;
using RustPlusContracts;

namespace RustPlusApi.Extensions;

/// <summary>Mapping extensions from protobuf map-marker messages to model types.</summary>
public static class AppMapMarkerToModel
{
    /// <summary>Maps an <see cref="AppMapMarkers"/> response to a <see cref="MapMarkers"/> model, routing each
    /// marker to its typed dictionary. Markers with an unrecognized type fall back to
    /// <see cref="MapMarkers.UnknownMarkers"/> so a game update cannot break the read.</summary>
    /// <param name="appMapMarker">The protobuf map markers response.</param>
    public static MapMarkers ToMapMarkers(this AppMapMarkers appMapMarker)
    {
        var result = new MapMarkers();

        foreach (var marker in appMapMarker.Markers)
        {
            switch (marker.Type)
            {
                case AppMarkerType.Player:
                    result.PlayerMarkers.Add(marker.Id, marker.ToPlayerMarker());
                    break;
                case AppMarkerType.Explosion:
                    result.ExplosionMarkers.Add(marker.Id, marker.ToExplosionMarker());
                    break;
                case AppMarkerType.VendingMachine:
                    result.VendingMachineMarkers.Add(marker.Id, marker.ToVendingMachineMarker());
                    break;
                case AppMarkerType.Ch47:
                    result.Ch47Markers.Add(marker.Id, marker.ToCh47Marker());
                    break;
                case AppMarkerType.CargoShip:
                    result.CargoShipMarkers.Add(marker.Id, marker.ToCargoShipMarker());
                    break;
                case AppMarkerType.Crate:
                    result.CrateMarkers.Add(marker.Id, marker.ToCrateMarker());
                    break;
                case AppMarkerType.GenericRadius:
                    result.GenericRadiusMarkers.Add(marker.Id, marker.ToGenericRadiusMarker());
                    break;
                case AppMarkerType.PatrolHelicopter:
                    result.PatrolHelicopterMarkers.Add(marker.Id, marker.ToPatrolHelicopterMarker());
                    break;
                case AppMarkerType.TravellingVendor:
                    result.TravellingVendorMarkers.Add(marker.Id, marker.ToTravellingVendorMarker());
                    break;
                case AppMarkerType.Undefined:
                default:
                    result.UnknownMarkers.Add(marker.Id, marker.ToUnknownMarker());
                    break;
            }
        }

        return result;
    }
}
```

- [ ] **Step 5: Add the integration test** — append to `EntityClientTests`:

```csharp
    [Fact]
    public async Task GetMapMarkersAsync_MixedAndUnrecognizedTypes_AllRoutedWithoutThrow()
    {
        await using var server = new MockRustPlusServer(req =>
        {
            var resp = new AppResponse
            {
                Seq = req.Seq
            };
            if (req.GetMapMarkers is not null)
            {
                var markers = new AppMapMarkers();
                markers.Markers.Add(new AppMarker
                {
                    Id = 1, X = 1, Y = 1, Type = AppMarkerType.Explosion
                });
                markers.Markers.Add(new AppMarker
                {
                    Id = 2, X = 1, Y = 1, Type = AppMarkerType.Crate
                });
                markers.Markers.Add(new AppMarker
                {
                    Id = 3,
                    X = 1,
                    Y = 1,
                    Type = AppMarkerType.GenericRadius,
                    Radius = 25f,
                    Alpha = 0.5f,
                    Color1 = new Vector4
                    {
                        X = 1f, Y = 0.5f, Z = 0f, W = 1f
                    }
                });
                markers.Markers.Add(new AppMarker
                {
                    Id = 4, X = 1, Y = 1, Type = (AppMarkerType)999
                });
                resp.MapMarkers = markers;
            }
            else
            {
                resp.Success = new AppSuccess();
            }

            return new AppMessage
            {
                Response = resp
            };
        });
        server.Start();
        await using var client =
            new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken));
        await client.ConnectAsync().WaitAsync(Timeout);

        var response = await client.GetMapMarkersAsync().WaitAsync(Timeout);

        Assert.True(response.IsSuccess);
        Assert.True(response.Data!.ExplosionMarkers.ContainsKey(1));
        Assert.True(response.Data.CrateMarkers.ContainsKey(2));
        Assert.Equal(25f, response.Data.GenericRadiusMarkers[3].Radius);
        Assert.Equal(1f, response.Data.GenericRadiusMarkers[3].Color1!.R);
        Assert.True(response.Data.UnknownMarkers.ContainsKey(4));
    }
```

- [ ] **Step 6: Run both suites on both TFMs**

Run: `dotnet test RustPlusApi.sln --filter "FullyQualifiedName~MapMarkerDispatchTests|FullyQualifiedName~EntityClientTests"`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusApi/Data/MapMarkers.cs src/RustPlusApi/Extensions/AppMapMarkerToModel.cs tests/RustPlusApi.UnitTests/MapMarkerDispatchTests.cs tests/RustPlusApi.IntegrationTests/EntityClientTests.cs
git commit -m "Route explosion, crate and generic-radius markers; fall back to UnknownMarkers instead of throwing"
```

### Task 7: `ServerMap` documentation fix (docs-only)

**Files:**

- Modify: `src/RustPlusApi/Data/ServerMap.cs`

- [ ] **Step 1: Replace the class and property docs** — the record becomes:

```csharp
using System.Drawing;

namespace RustPlusApi.Data;

/// <summary>Server map image and monument list returned by <c>GetMapAsync</c>.</summary>
/// <remarks>
/// <para><see cref="Width"/>, <see cref="Height"/> and <see cref="OceanMargin"/> are pixel
/// measurements of <see cref="JpgImage"/> — only marker/monument coordinates and
/// <c>ServerInfo.MapSize</c> are world units. The canonical world→pixel transform (world origin
/// bottom-left, image origin top-left — hence the Y flip):</para>
/// <code>
/// px = worldX * ((Width  - 2 * OceanMargin) / ServerInfo.MapSize) + OceanMargin
/// py = Height - (worldY * ((Height - 2 * OceanMargin) / ServerInfo.MapSize) + OceanMargin)
/// </code>
/// </remarks>
public sealed record ServerMap
{
    /// <summary>Height of <see cref="JpgImage"/> in pixels.</summary>
    public uint? Height { get; init; }

    /// <summary>Width of <see cref="JpgImage"/> in pixels.</summary>
    public uint? Width { get; init; }

    /// <summary>Width of the ocean border baked into <see cref="JpgImage"/>, in pixels.</summary>
    public int? OceanMargin { get; init; }

    /// <summary>Background colour of the map (ocean colour).</summary>
    public Color Background { get; init; }

    /// <summary>List of monuments present on the map.</summary>
    public List<ServerMapMonument>? Monuments { get; init; }

    /// <summary>Raw JPEG image bytes of the map tile, if available.</summary>
    public byte[]? JpgImage { get; init; }
}
```

- [ ] **Step 2: Build to validate the XML docs**

Run: `dotnet build src/RustPlusApi/RustPlusApi.csproj`
Expected: 0 errors, 0 warnings

- [ ] **Step 3: Commit**

```bash
git add src/RustPlusApi/Data/ServerMap.cs
git commit -m "Document ServerMap dimensions as image pixels and record the world-to-pixel transform"
```

### Task 8: Phase-1 gate — full verification, format, push, PR

**Files:** none new. **Orchestrator-involved step (push + PR).**

- [ ] **Step 1: Full build and test matrix**

Run: `dotnet build && dotnet test RustPlusApi.sln`
Expected: 0 warnings, all tests pass on net8.0 and net10.0 hosts

- [ ] **Step 2: Coverage gate**

Run: `tools/coverage/report.sh`
Expected: gate passes; `AppMarkerToModel`, `AppMapMarkerToModel`, all marker records at 100/100

- [ ] **Step 3: Format + reorder**

Run: `dotnet tool restore && dotnet jb cleanupcode RustPlusApi.sln --profile="ReformatAndReorder"`
Then: `git status --porcelain` — if the formatter changed files, review and amend/commit them:

```bash
git add -u
git commit -m "Apply ReSharper formatting"
```

- [ ] **Step 4: Push and open the PR**

```bash
git push -u origin feature/beta4-map-markers
gh pr create --base develop --title "beta.4: full AppMarker surface, new marker types, no-throw dispatch" --body "$(cat <<'EOF'
## Summary
- Map `rotation` on CargoShip/CH47/PatrolHelicopter/TravellingVendor markers (absent → null; not Player — the server does not populate it for players, reference apps derive player heading from movement)
- New `ExplosionMarker`, `CrateMarker`, `GenericRadiusMarker` (+ `MarkerColor` RGBA record) and their `MapMarkers` dictionaries
- `UnknownMarker` now passes through the full raw `AppMarker` surface
- Unrecognized marker types fall back to `UnknownMarkers` instead of throwing from `GetMapMarkersAsync`
- Uniform presence rule: absent optional proto fields map to `null` (incl. `PlayerMarker.SteamId`, `IsOutOfStock`)
- `ServerMap` docs: dimensions are pixels of `JpgImage`; canonical world→pixel transform documented

Spec: `docs/superpowers/specs/2026-07-06-beta4-markers-and-entity-info-design.md`

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

Expected: PR URL printed.

---

## Phase 2 — PR `feature/beta4-entity-info`

### Task 9: Create the branch (from develop, NOT from Phase 1)

**Files:** none (git only). **Orchestrator-only step — do not delegate to a subagent.**

- [ ] **Step 1: Branch off develop**

```bash
git -C /home/handys11/Dev/RustPlusApi checkout develop
git -C /home/handys11/Dev/RustPlusApi checkout -b feature/beta4-entity-info
```

Expected: `Switched to a new branch 'feature/beta4-entity-info'`

### Task 10: Type-tolerant `ToSmartDeviceInfo` mapper

**Files:**

- Modify: `src/RustPlusApi/Extensions/AppEntityInfoToModel.cs`
- Test: `tests/RustPlusApi.UnitTests/EntityInfoMapperTests.cs`

**Interfaces:**

- Produces: `public static SmartDeviceInfo ToSmartDeviceInfo(this AppEntityInfo entity)` — Task 11 uses it.

- [ ] **Step 1: Write the failing tests** — append to `EntityInfoMapperTests`:

```csharp
    [Theory]
    [InlineData(AppEntityType.Switch)]
    [InlineData(AppEntityType.Alarm)]
    public void ToSmartDeviceInfo_AcceptsBinaryStateDevices(AppEntityType type)
    {
        var info = Entity(type, new AppEntityPayload
        {
            Value = true
        }).ToSmartDeviceInfo();

        Assert.True(info.IsActive);
    }

    [Fact]
    public void ToSmartDeviceInfo_StorageMonitor_Throws() =>
        Assert.Throws<InvalidOperationException>(() =>
            Entity(AppEntityType.StorageMonitor, new AppEntityPayload()).ToSmartDeviceInfo());
```

- [ ] **Step 2: Verify it fails to compile**

Run: `dotnet test RustPlusApi.sln -f net10.0 --filter "FullyQualifiedName~EntityInfoMapperTests"`
Expected: build error `'AppEntityInfo' does not contain a definition for 'ToSmartDeviceInfo'`

- [ ] **Step 3: Add the mapper to `AppEntityInfoToModel`**

```csharp
    /// <summary>Maps an <see cref="AppEntityInfo"/> of a binary-state smart device (a smart switch or a
    /// smart alarm) to a <see cref="SmartDeviceInfo"/>. The server replies with the entity's actual type
    /// and switch/alarm payloads are physically identical, so both types are accepted.</summary>
    /// <param name="entity">The protobuf entity info.</param>
    /// <exception cref="InvalidOperationException">Thrown when the entity type is neither <c>Switch</c> nor <c>Alarm</c>.</exception>
    public static SmartDeviceInfo ToSmartDeviceInfo(this AppEntityInfo entity)
    {
        if (entity.Type is not (AppEntityType.Switch or AppEntityType.Alarm))
        {
            throw new InvalidOperationException("Entity type is not a binary-state smart device.");
        }

        return new SmartDeviceInfo
        {
            IsActive = entity.Payload.Value
        };
    }
```

- [ ] **Step 4: Run tests on both TFMs**

Run: `dotnet test RustPlusApi.sln --filter "FullyQualifiedName~EntityInfoMapperTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusApi/Extensions/AppEntityInfoToModel.cs tests/RustPlusApi.UnitTests/EntityInfoMapperTests.cs
git commit -m "Add type-tolerant ToSmartDeviceInfo mapper for switch and alarm entities"
```

### Task 11: Public `GetSmartDeviceInfoAsync`

**Files:**

- Modify: `src/RustPlusApi/RustPlus.cs` (place before `GetSmartSwitchInfoAsync`, ~line 293)
- Modify: `src/RustPlusApi/Interfaces/IRustPlus.cs` (before the `GetSmartSwitchInfoAsync` declaration)
- Test: `tests/RustPlusApi.IntegrationTests/EntityClientTests.cs`

**Interfaces:**

- Consumes: `ToSmartDeviceInfo` (Task 10), existing `GetEntityInfoAsync<T>` (`RustPlus.cs:633`).
- Produces: `Task<Response<SmartDeviceInfo?>> GetSmartDeviceInfoAsync(ulong entityId, CancellationToken cancellationToken = default)` on `RustPlus` and `IRustPlus`.

- [ ] **Step 1: Write the failing integration tests** — append to `EntityClientTests`:

```csharp
    [Fact]
    public async Task GetSmartDeviceInfoAsync_SwitchTypedReply_Succeeds()
    {
        var (server, client) = await ConnectEntityAsync();
        await using var _ = server;
        await using var __ = client;

        var response = await client.GetSmartDeviceInfoAsync(1).WaitAsync(Timeout);

        Assert.True(response.IsSuccess);
        Assert.True(response.Data!.IsActive);
    }

    [Fact]
    public async Task GetSmartDeviceInfoAsync_AlarmTypedReply_Succeeds()
    {
        await using var server = new MockRustPlusServer(req =>
        {
            var resp = new AppResponse
            {
                Seq = req.Seq
            };
            if (req.GetEntityInfo is not null)
            {
                resp.EntityInfo = MockResponses.SampleAlarm(value: true);
            }
            else
            {
                resp.Success = new AppSuccess();
            }

            return new AppMessage
            {
                Response = resp
            };
        });
        server.Start();
        await using var client =
            new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken));
        await client.ConnectAsync().WaitAsync(Timeout);

        var response = await client.GetSmartDeviceInfoAsync(1).WaitAsync(Timeout);

        Assert.True(response.IsSuccess);
        Assert.True(response.Data!.IsActive);
    }
```

- [ ] **Step 2: Verify it fails to compile**

Run: `dotnet test RustPlusApi.sln -f net10.0 --filter "FullyQualifiedName~EntityClientTests.GetSmartDeviceInfoAsync"`
Expected: build error `'RustPlus' does not contain a definition for 'GetSmartDeviceInfoAsync'`

- [ ] **Step 3: Add the method to `RustPlus.cs`**

```csharp
    /// <summary>
    /// Retrieves the state of a binary-state smart device (a smart switch or a smart alarm)
    /// asynchronously, whichever of the two types the entity actually is. The server replies with the
    /// entity's actual type and switch/alarm payloads are identical, so this method reads mixed device
    /// sets without tracking each entity's type.
    /// </summary>
    /// <param name="entityId">The ID of the smart device entity.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the smart device information.</returns>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    /// <remarks>The underlying <c>getEntityInfo</c> request also subscribes this connection to the
    /// entity's <c>EntityChanged</c> broadcasts server-side.</remarks>
    public async Task<Response<SmartDeviceInfo?>> GetSmartDeviceInfoAsync(ulong entityId,
        CancellationToken cancellationToken = default)
    {
        return await GetEntityInfoAsync<SmartDeviceInfo?>(
            entityId,
            r => r.Response.EntityInfo.ToSmartDeviceInfo(),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
```

- [ ] **Step 4: Add the declaration to `IRustPlus.cs`**

```csharp
    /// <summary>Returns the state of a binary-state smart device (a smart switch or a smart alarm),
    /// whichever of the two types the entity actually is.</summary>
    /// <param name="entityId">Entity ID of the smart device.</param>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    Task<Response<SmartDeviceInfo?>> GetSmartDeviceInfoAsync(ulong entityId,
        CancellationToken cancellationToken = default);
```

- [ ] **Step 5: Run tests on both TFMs**

Run: `dotnet test RustPlusApi.sln --filter "FullyQualifiedName~EntityClientTests"`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusApi/RustPlus.cs src/RustPlusApi/Interfaces/IRustPlus.cs tests/RustPlusApi.IntegrationTests/EntityClientTests.cs
git commit -m "Add GetSmartDeviceInfoAsync for type-agnostic binary-state device reads"
```

### Task 12: Selector exceptions become failed responses

**Files:**

- Modify: `src/RustPlusApi/RustPlus.cs` (`ProcessRequestAsync<T>`, line 594)
- Test: `tests/RustPlusApi.IntegrationTests/EntityClientTests.cs`

**Interfaces:**

- Produces: `ProcessRequestAsync<T>` never lets a success-selector exception escape (except `OperationCanceledException`); the error message is the exception message.

- [ ] **Step 1: Write the failing tests** — append to `EntityClientTests`. The second test needs a probe subclass (protected member access); add it at the bottom of the class:

```csharp
    [Fact]
    public async Task GetSmartSwitchInfoAsync_AlarmTypedReply_ReturnsFailedResponse()
    {
        await using var server = new MockRustPlusServer(req =>
        {
            var resp = new AppResponse
            {
                Seq = req.Seq
            };
            if (req.GetEntityInfo is not null)
            {
                resp.EntityInfo = MockResponses.SampleAlarm(value: false);
            }
            else
            {
                resp.Success = new AppSuccess();
            }

            return new AppMessage
            {
                Response = resp
            };
        });
        server.Start();
        await using var client =
            new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken));
        await client.ConnectAsync().WaitAsync(Timeout);

        // The server answered successfully with the entity's actual type (Alarm); the strict
        // mapper's type check must surface as a failed Response, not a thrown exception.
        var response = await client.GetSmartSwitchInfoAsync(1).WaitAsync(Timeout);

        Assert.False(response.IsSuccess);
        Assert.Null(response.Data);
        Assert.Equal("Entity type is not a SmartSwitch.", response.Error!.Message);
    }

    [Fact]
    public async Task ProcessRequestAsync_SelectorThrowsOperationCanceled_Propagates()
    {
        await using var server = new MockRustPlusServer();
        server.Start();
        await using var client =
            new SelectorProbeRustPlus(new RustPlusConnection(MockRustPlusServer.Host, server.Port, PlayerId,
                PlayerToken));
        await client.ConnectAsync().WaitAsync(Timeout);

        await Assert.ThrowsAsync<OperationCanceledException>(() => client.ProbeAsync<object>(
            new AppRequest
            {
                GetTime = new AppEmpty()
            },
            _ => throw new OperationCanceledException()).WaitAsync(Timeout));
    }

    /// <summary>Exposes the protected <c>ProcessRequestAsync</c> to pin its selector exception handling.</summary>
    /// <param name="connection">The server endpoint and player credentials to connect as.</param>
    private sealed class SelectorProbeRustPlus(RustPlusConnection connection) : RustPlus(connection)
    {
        public Task<Response<T?>> ProbeAsync<T>(AppRequest request, Func<AppMessage, T> selector) =>
            ProcessRequestAsync(request, selector);
    }
```

- [ ] **Step 2: Verify the behavioral test fails**

Run: `dotnet test RustPlusApi.sln -f net10.0 --filter "FullyQualifiedName~EntityClientTests.GetSmartSwitchInfoAsync_AlarmTypedReply"`
Expected: FAIL — the raw `InvalidOperationException` escapes instead of a failed `Response`

- [ ] **Step 3: Update `ProcessRequestAsync<T>`** — replace the method body (keep the signature and XML docs, and append the `<remarks>` below):

```csharp
    protected async Task<Response<T?>> ProcessRequestAsync<T>(AppRequest request,
        Func<AppMessage, T> successSelector,
        Func<AppBroadcast, bool>? broadcastReplyMatcher = null,
        CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(request, broadcastReplyMatcher, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (IsError(response))
        {
            return ResponseHelper.BuildGenericOutput<T>(false, default!, GetErrorMessage(response));
        }

        try
        {
            return ResponseHelper.BuildGenericOutput(true, successSelector(response));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A successful server reply must never escape as a thrown exception: a selector failure
            // (e.g. reading an alarm through GetSmartSwitchInfoAsync — the server answers with the
            // entity's actual type) becomes a failed Response the consumer can tell apart from a
            // transport error.
            return ResponseHelper.BuildGenericOutput<T>(false, default!, ex.Message);
        }
    }
```

Add to the method's XML docs:

```csharp
    /// <remarks>A success-selector exception (other than <see cref="OperationCanceledException"/>) is
    /// returned as a failed <see cref="Response{T}"/> carrying the exception message — a successful
    /// server reply never surfaces as a thrown exception.</remarks>
```

- [ ] **Step 4: Run the full integration + unit suites on both TFMs**

Run: `dotnet test RustPlusApi.sln`
Expected: PASS (nothing else relied on selector exceptions escaping)

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusApi/RustPlus.cs tests/RustPlusApi.IntegrationTests/EntityClientTests.cs
git commit -m "Surface success-selector exceptions as failed responses"
```

### Task 13: `EntityChangedEventArg` + raw `OnEntityChanged` event

**Files:**

- Create: `src/RustPlusApi/Data/Events/EntityChangedEventArg.cs`
- Modify: `src/RustPlusApi/Extensions/EntityChangedToModel.cs`
- Modify: `src/RustPlusApi/RustPlus.cs` (event + raise in `ParseNotification`)
- Modify: `src/RustPlusApi/Interfaces/IRustPlus.cs`
- Create: `tests/RustPlusApi.UnitTests/EntityChangedMapperTests.cs`
- Test: `tests/RustPlusApi.UnitTests/RustPlusParseNotificationTests.cs`

**Interfaces:**

- Produces: `EntityChangedEventArg` (`ulong Id`, `bool? Value`, `int? Capacity`, `bool? HasProtection`, `DateTime? ProtectionExpiry`, `IEnumerable<StorageMonitorItemInfo> Items`), `ToEntityChangedEvent(this AppEntityChanged)`, `event EventHandler<EntityChangedEventArg>? OnEntityChanged` — Task 14's routing raises it first.

- [ ] **Step 1: Write the failing mapper tests** — create `tests/RustPlusApi.UnitTests/EntityChangedMapperTests.cs`:

```csharp
using RustPlusApi.Extensions;
using RustPlusContracts;
using Xunit;

namespace RustPlusApi.UnitTests;

/// <summary>Locks <see cref="EntityChangedToModel.ToEntityChangedEvent"/>: full payload passthrough
/// with absent optional fields mapping to <see langword="null"/>.</summary>
public class EntityChangedMapperTests
{
    [Fact]
    public void ToEntityChangedEvent_MapsFullPayload()
    {
        var changed = new AppEntityChanged
        {
            EntityId = 42,
            Payload = new AppEntityPayload
            {
                Value = true,
                Capacity = 24,
                HasProtection = true,
                ProtectionExpiry = 1_700_000_000,
                Items =
                {
                    new AppEntityPayload.Item
                    {
                        ItemId = 1, Quantity = 5, ItemIsBlueprint = false
                    }
                }
            }
        };

        var arg = changed.ToEntityChangedEvent();

        Assert.Equal(42u, arg.Id);
        Assert.True(arg.Value);
        Assert.Equal(24, arg.Capacity);
        Assert.True(arg.HasProtection);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000).UtcDateTime, arg.ProtectionExpiry);
        var item = Assert.Single(arg.Items);
        Assert.Equal(1, item.Id);
    }

    [Fact]
    public void ToEntityChangedEvent_UnsetOptionals_AreNull()
    {
        var arg = new AppEntityChanged
        {
            EntityId = 7, Payload = new AppEntityPayload()
        }.ToEntityChangedEvent();

        Assert.Equal(7u, arg.Id);
        Assert.Null(arg.Value);
        Assert.Null(arg.Capacity);
        Assert.Null(arg.HasProtection);
        Assert.Null(arg.ProtectionExpiry);
        Assert.Empty(arg.Items);
    }
}
```

- [ ] **Step 2: Verify it fails to compile**

Run: `dotnet test RustPlusApi.sln -f net10.0 --filter "FullyQualifiedName~EntityChangedMapperTests"`
Expected: build error `'AppEntityChanged' does not contain a definition for 'ToEntityChangedEvent'`

- [ ] **Step 3: Create `src/RustPlusApi/Data/Events/EntityChangedEventArg.cs`**

```csharp
using RustPlusApi.Data.Entities;

namespace RustPlusApi.Data.Events;

/// <summary>Event argument raised for every <c>EntityChanged</c> broadcast, exposing the full raw
/// payload before any device-type heuristic. The broadcast carries no entity type; consumers that
/// know their paired entity ids should route on <see cref="Id"/>.</summary>
public sealed record EntityChangedEventArg
{
    /// <summary>Container capacity, or <see langword="null"/> when omitted (storage payloads only).</summary>
    public int? Capacity { get; init; }

    /// <summary>Tool-cupboard protection flag, or <see langword="null"/> when omitted.</summary>
    public bool? HasProtection { get; init; }

    /// <summary>Entity ID of the entity that changed.</summary>
    public ulong Id { get; init; }

    /// <summary>Items in the container; empty for binary-state payloads and for storage broadcasts
    /// that carry no contents snapshot.</summary>
    public IEnumerable<StorageMonitorItemInfo> Items { get; init; } = [];

    /// <summary>UTC time when the tool-cupboard protection expires, or <see langword="null"/> when omitted.</summary>
    public DateTime? ProtectionExpiry { get; init; }

    /// <summary>Binary state (on / triggered), or <see langword="null"/> when omitted.</summary>
    public bool? Value { get; init; }
}
```

- [ ] **Step 4: Add the mapper to `EntityChangedToModel`**

```csharp
    /// <summary>Maps an <see cref="AppEntityChanged"/> broadcast to the raw
    /// <see cref="EntityChangedEventArg"/>, preserving field presence (absent optional fields map to
    /// <see langword="null"/>).</summary>
    /// <param name="entityChanged">The protobuf entity-changed broadcast.</param>
    public static EntityChangedEventArg ToEntityChangedEvent(this AppEntityChanged entityChanged)
    {
        var payload = entityChanged.Payload;
        return new EntityChangedEventArg
        {
            Id = entityChanged.EntityId,
            Value = payload.ShouldSerializeValue() ? payload.Value : null,
            Capacity = payload.ShouldSerializeCapacity() ? payload.Capacity : null,
            HasProtection = payload.ShouldSerializeHasProtection() ? payload.HasProtection : null,
            ProtectionExpiry = payload.ShouldSerializeProtectionExpiry()
                ? DateTimeOffset.FromUnixTimeSeconds(payload.ProtectionExpiry).UtcDateTime
                : null,
            Items = payload.Items.ToStorageMonitorItemsInfo()
        };
    }
```

- [ ] **Step 5: Add the event and raise it** — in `RustPlus.cs`, after the `OnCameraRaysReceived` declaration:

```csharp
    /// <summary>
    /// Occurs for every <c>EntityChanged</c> broadcast, before any device-type heuristic, with the
    /// full raw payload. The broadcast carries no entity type; consumers that know their paired
    /// entity ids should route on <see cref="EntityChangedEventArg.Id"/> — this is the reliable
    /// channel when the <see cref="OnSmartDeviceTriggered"/>/<see cref="OnStorageMonitorTriggered"/>
    /// heuristics cannot classify a payload.
    /// </summary>
    public event EventHandler<EntityChangedEventArg>? OnEntityChanged;
```

In `ParseNotification`, insert as the FIRST statement inside `if (broadcast.EntityChanged is not null)`:

```csharp
            OnEntityChanged?.Invoke(this, broadcast.EntityChanged.ToEntityChangedEvent());
```

In `IRustPlus.cs`, after `OnStorageMonitorTriggered`:

```csharp
    /// <summary>Raised for every <c>EntityChanged</c> broadcast, before any device-type heuristic,
    /// with the full raw payload. The broadcast carries no entity type; consumers that know their
    /// paired entity ids should route on <see cref="EntityChangedEventArg.Id"/>.</summary>
    event EventHandler<EntityChangedEventArg>? OnEntityChanged;
```

- [ ] **Step 6: Add `ParseNotification` coverage** — append to `RustPlusParseNotificationTests`:

```csharp
    [Fact]
    public void EntityChanged_WithRawSubscriber_InvokesOnEntityChanged()
    {
        using var sut = new TestRustPlus();
        RustPlusApi.Data.Events.EntityChangedEventArg? captured = null;
        sut.OnEntityChanged += (_, e) => captured = e;

        sut.Feed(new AppBroadcast
        {
            EntityChanged = new AppEntityChanged
            {
                EntityId = 42,
                Payload = new AppEntityPayload
                {
                    Value = true
                }
            }
        });

        Assert.NotNull(captured);
        Assert.Equal(42u, captured!.Id);
        Assert.True(captured.Value);
    }
```

- [ ] **Step 7: Run the suites on both TFMs**

Run: `dotnet test RustPlusApi.sln --filter "FullyQualifiedName~EntityChangedMapperTests|FullyQualifiedName~RustPlusParseNotificationTests"`
Expected: PASS

- [ ] **Step 8: Commit**

```bash
git add src/RustPlusApi/Data/Events/EntityChangedEventArg.cs src/RustPlusApi/Extensions/EntityChangedToModel.cs src/RustPlusApi/RustPlus.cs src/RustPlusApi/Interfaces/IRustPlus.cs tests/RustPlusApi.UnitTests/EntityChangedMapperTests.cs tests/RustPlusApi.UnitTests/RustPlusParseNotificationTests.cs
git commit -m "Add raw OnEntityChanged event exposing the full EntityChanged payload"
```

### Task 14: Hardened broadcast routing + suppression + doc updates

**Files:**

- Modify: `src/RustPlusApi/RustPlus.cs` (`ParseNotification`, event docs, entity-getter remarks)
- Modify: `src/RustPlusApi/Interfaces/IRustPlus.cs` (event docs, getter remarks)
- Test: `tests/RustPlusApi.UnitTests/RustPlusParseNotificationTests.cs`

**Interfaces:**

- Consumes: `OnEntityChanged` raise from Task 13 (stays first).
- Produces: final `EntityChanged` routing — storage-shaped when `Items.Count > 0 || Capacity > 0 || HasProtection`; suppression of item-less `Value == true` storage broadcasts.

- [ ] **Step 1: Write the failing routing-matrix tests** — append to `RustPlusParseNotificationTests`:

```csharp
    private sealed record RoutingCapture
    {
        public RustPlusApi.Data.Events.EntityChangedEventArg? Raw { get; set; }
        public RustPlusApi.Data.Events.SmartDeviceEventArg? Smart { get; set; }
        public RustPlusApi.Data.Events.StorageMonitorEventArg? Storage { get; set; }
    }

    private static RoutingCapture Route(AppEntityPayload payload)
    {
        using var sut = new TestRustPlus();
        var capture = new RoutingCapture();
        sut.OnEntityChanged += (_, e) => capture.Raw = e;
        sut.OnSmartDeviceTriggered += (_, e) => capture.Smart = e;
        sut.OnStorageMonitorTriggered += (_, e) => capture.Storage = e;

        sut.Feed(new AppBroadcast
        {
            EntityChanged = new AppEntityChanged
            {
                EntityId = 42, Payload = payload
            }
        });

        return capture;
    }

    [Fact]
    public void EntityChanged_ItemsOnly_RoutesToStorageMonitor()
    {
        var capture = Route(new AppEntityPayload
        {
            Items =
            {
                new AppEntityPayload.Item
                {
                    ItemId = 1, Quantity = 1, ItemIsBlueprint = false
                }
            }
        });

        Assert.NotNull(capture.Storage);
        Assert.Null(capture.Smart);
        Assert.NotNull(capture.Raw);
    }

    [Fact]
    public void EntityChanged_CapacityOnly_RoutesToStorageMonitor()
    {
        var capture = Route(new AppEntityPayload
        {
            Capacity = 48
        });

        Assert.NotNull(capture.Storage);
        Assert.Null(capture.Smart);
    }

    [Fact]
    public void EntityChanged_ProtectionOnly_RoutesToStorageMonitor()
    {
        var capture = Route(new AppEntityPayload
        {
            HasProtection = true
        });

        Assert.NotNull(capture.Storage);
        Assert.Null(capture.Smart);
    }

    [Fact]
    public void EntityChanged_ValueOnly_RoutesToSmartDevice()
    {
        var capture = Route(new AppEntityPayload
        {
            Value = true
        });

        Assert.NotNull(capture.Smart);
        Assert.Null(capture.Storage);
        Assert.NotNull(capture.Raw);
    }

    [Fact]
    public void EntityChanged_StorageShapedValueTrueWithoutItems_SuppressedFromConvenienceEvents()
    {
        // A storage broadcast with value == true carries no contents snapshot; surfacing it through
        // OnStorageMonitorTriggered would wipe consumer-tracked contents (rustplusplus skips these).
        var capture = Route(new AppEntityPayload
        {
            Value = true, Capacity = 48
        });

        Assert.Null(capture.Storage);
        Assert.Null(capture.Smart);
        Assert.NotNull(capture.Raw);
    }

    [Fact]
    public void EntityChanged_StorageShapedValueTrueWithItems_RoutesToStorageMonitor()
    {
        var capture = Route(new AppEntityPayload
        {
            Value = true,
            Capacity = 48,
            Items =
            {
                new AppEntityPayload.Item
                {
                    ItemId = 1, Quantity = 1, ItemIsBlueprint = false
                }
            }
        });

        Assert.NotNull(capture.Storage);
        Assert.Null(capture.Smart);
    }
```

- [ ] **Step 2: Verify the new expectations fail**

Run: `dotnet test RustPlusApi.sln -f net10.0 --filter "FullyQualifiedName~RustPlusParseNotificationTests"`
Expected: FAIL — `EntityChanged_ProtectionOnly_RoutesToStorageMonitor` and `EntityChanged_StorageShapedValueTrueWithoutItems_SuppressedFromConvenienceEvents` fail under the old `Capacity is 0` heuristic

- [ ] **Step 3: Replace the `EntityChanged` block in `ParseNotification`**

```csharp
        if (broadcast.EntityChanged is not null)
        {
            var entityChanged = broadcast.EntityChanged;
            OnEntityChanged?.Invoke(this, entityChanged.ToEntityChangedEvent());

            // The broadcast carries no entity type. A payload is storage-shaped when it exposes any
            // container state; a bare `value` is indistinguishable from a switch/alarm and routes to
            // OnSmartDeviceTriggered. Consumers that know their paired ids use OnEntityChanged.
            var payload = entityChanged.Payload;
            if (payload.Items.Count > 0 || payload.Capacity > 0 || payload.HasProtection)
            {
                // A storage broadcast with value == true carries no contents snapshot; surfacing it
                // would wipe consumer-tracked contents.
                if (!payload.Value || payload.Items.Count > 0)
                {
                    OnStorageMonitorTriggered?.Invoke(this, entityChanged.ToStorageMonitorEvent());
                }
            }
            else
            {
                OnSmartDeviceTriggered?.Invoke(this, entityChanged.ToSmartDeviceEvent());
            }

            return;
        }
```

- [ ] **Step 4: Update the convenience-event and getter docs**

In `RustPlus.cs`, replace the `OnSmartDeviceTriggered` doc:

```csharp
    /// <summary>
    /// Occurs when an <c>EntityChanged</c> broadcast is classified as a binary-state smart device
    /// (a smart switch or a smart alarm): the payload carries no container state (no items, no
    /// capacity, no protection). The broadcast omits the entity type, so a storage broadcast whose
    /// payload is only <c>value</c> is indistinguishable from a switch and lands here too — route on
    /// <see cref="OnEntityChanged"/> with your paired entity ids when that matters.
    /// </summary>
```

Replace the `OnStorageMonitorTriggered` doc:

```csharp
    /// <summary>
    /// Occurs when an <c>EntityChanged</c> broadcast is classified as a storage monitor: the payload
    /// carries items, a capacity, or tool-cupboard protection. Storage broadcasts with
    /// <c>value == true</c> and no items carry no contents snapshot and are NOT raised here (they
    /// remain observable via <see cref="OnEntityChanged"/>).
    /// </summary>
```

Mirror both texts on the `IRustPlus` event declarations. Add the subscription `<remarks>` to `GetSmartSwitchInfoAsync`, `GetAlarmInfoAsync`, and `GetStorageMonitorInfoAsync` in `RustPlus.cs` (the Task 11 method already has it):

```csharp
    /// <remarks>The underlying <c>getEntityInfo</c> request also subscribes this connection to the
    /// entity's <c>EntityChanged</c> broadcasts server-side — even when the read itself fails on a
    /// type mismatch.</remarks>
```

- [ ] **Step 5: Run the full test matrix**

Run: `dotnet test RustPlusApi.sln`
Expected: PASS — pre-existing routing tests (`SmartSwitch_WithSubscriber_InvokesHandler`: value-only → smart; `StorageMonitor_NoSubscriber_DoesNotThrow`: value=false + capacity → storage) still hold

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusApi/RustPlus.cs src/RustPlusApi/Interfaces/IRustPlus.cs tests/RustPlusApi.UnitTests/RustPlusParseNotificationTests.cs
git commit -m "Harden EntityChanged routing and suppress item-less value-true storage broadcasts"
```

### Task 15: Phase-2 gate — full verification, format, push, PR

**Files:** none new. **Orchestrator-involved step (push + PR).**

- [ ] **Step 1: Full build and test matrix**

Run: `dotnet build && dotnet test RustPlusApi.sln`
Expected: 0 warnings, all tests pass on net8.0 and net10.0 hosts

- [ ] **Step 2: Coverage gate**

Run: `tools/coverage/report.sh`
Expected: gate passes; `ProcessRequestAsync`, `ParseNotification`, `EntityChangedToModel`, `AppEntityInfoToModel`, `EntityChangedEventArg` at 100/100

- [ ] **Step 3: Format + reorder**

Run: `dotnet tool restore && dotnet jb cleanupcode RustPlusApi.sln --profile="ReformatAndReorder"`
Then: `git status --porcelain` — if the formatter changed files:

```bash
git add -u
git commit -m "Apply ReSharper formatting"
```

- [ ] **Step 4: Push and open the PR**

```bash
git push -u origin feature/beta4-entity-info
gh pr create --base develop --title "beta.4: entity-info reads never throw on success; raw OnEntityChanged + hardened routing" --body "$(cat <<'EOF'
## Summary
- `GetSmartDeviceInfoAsync`: type-tolerant read for switch/alarm (server replies with the actual type; payloads are identical)
- `ProcessRequestAsync` returns success-selector exceptions as failed `Response`s — `GetSmartSwitchInfoAsync(alarmId)` now fails cleanly instead of throwing
- New `OnEntityChanged` event: every `EntityChanged` broadcast, raw payload, before any heuristic
- Hardened routing: storage-shaped = items || capacity > 0 || protection; item-less `value == true` storage broadcasts suppressed from `OnStorageMonitorTriggered` (rustplusplus behavior)
- Docs: `getEntityInfo` subscribes the connection server-side; per-device payload shapes

Spec: `docs/superpowers/specs/2026-07-06-beta4-markers-and-entity-info-design.md`

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

Expected: PR URL printed.

---

## Phase 3 — Release `v2.0.0-beta.4`

### Task 16: Manual live-server verification gate (user-run, after both PRs merge)

Before tagging, verify against a live Rust server (cannot be CI'd):

- [ ] `GetSmartDeviceInfoAsync(alarmId)` and `(switchId)` both return `IsSuccess = true`.
- [ ] `GetSmartSwitchInfoAsync(alarmId)` returns a failed `Response` (not a thrown exception).
- [ ] Tool-cupboard item change → broadcast observable via `OnEntityChanged` with the TC id.
- [ ] Storage broadcast with `value == true` does not surface an empty `Items` list through `OnStorageMonitorTriggered`.
- [ ] Cargo/patrol/CH47/travelling-vendor markers expose a plausible `Rotation` that tracks movement; markers without the field report `null`.
- [ ] Partial tool-cupboard broadcast (capacity absent) routes to `OnStorageMonitorTriggered` via the protection flag. If TCs turn out to send an explicit `has_protection = false` with no items/capacity, switch the discriminator to field presence (`ShouldSerializeHasProtection()`) instead of truthiness.

### Task 17: Tag and publish

**Orchestrator/user step.** CD triggers on tag push; the tag must point at a commit on `develop` or `main` (`CD.yml` verifies), version is parsed from the tag name.

- [ ] **Step 1: Tag the merge commit on develop**

```bash
git checkout develop && git pull
git tag v2.0.0-beta.4
git push origin v2.0.0-beta.4
```

- [ ] **Step 2: Watch CD**

Run: `gh run watch --workflow=CD.yml` (or check the Actions tab)
Expected: all six packages packed with `-p:Version=2.0.0-beta.4` and published; GitHub prerelease created.
