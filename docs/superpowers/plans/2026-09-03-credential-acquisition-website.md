# Credential-acquisition website Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A single-page web app that walks a visitor from nothing to working Rust+ credentials in a browser, deployable by anyone with one `docker run` and hostable as a public instance.

**Architecture:** An ASP.NET Core minimal-API app in `apps/RustPlusApi.CredentialsWeb`, referencing `RustPlusApi.Fcm.Registration` and using only its existing public API. Per-visitor state lives in an in-memory `SessionStore` with hard TTLs; the browser gets progress over Server-Sent Events. The credential flow is reordered to `4 → 1,2,3 → 5` so a real Steam login gates all upstream work, with step 6 (the pairing wait, which holds a live MCS socket) offered as an opt-in continuation.

**Tech Stack:** .NET 10, ASP.NET Core minimal API, Server-Sent Events, hand-written HTML/CSS/JS with no build step, xUnit, `WebApplicationFactory`, `FakeTimeProvider`, Docker.

**Spec:** `docs/superpowers/specs/2026-09-03-credential-acquisition-website-design.md`

## Global Constraints

- **Target framework:** `net10.0` only. This project is deliberately outside the `netstandard2.0` multi-TFM parity story that governs `src/`. The test project also targets `net10.0` only.
- **No library changes.** `src/**` must not be modified. The app uses only the existing public API: `SteamLoginService.BuildLoginUrl`, `SteamLoginService.ParseCallback`, `AndroidFcmRegister`, `ExpoPushClient`, `RustCompanionClient`, `PairingListener`, `CredentialsStore`.
- **Build is strict.** `TreatWarningsAsErrors`, `AnalysisLevel=latest-all`, Roslynator + Sonar + VSTHRD analyzers, `GenerateDocumentationFile=true`. Because XML-doc warnings (CS1591) apply only to public members, **every type in this app is `internal`**, and the test project gets access via `InternalsVisibleTo`. Add XML doc comments to internal members anyway where intent is non-obvious — it matches the repo's style.
- **Central package management.** Versions go in `Directory.Packages.props`; `PackageReference` elements carry no `Version` attribute.
- **Formatting.** `dotnet jb cleanupcode RustPlusApi.sln --profile="ReformatAndReorder"` must produce no diff. A pre-push hook enforces this.
- **Ids are 128-bit.** Both `sessionId` and `returnToken` come from `RandomNumberGenerator.GetBytes(16)` rendered as 32 lowercase hex characters.
- **`ulong` values are serialized as JSON strings.** `steamId` and `playerId` exceed JavaScript's `Number.MAX_SAFE_INTEGER` (2^53). Emitting them as JSON numbers silently corrupts them in the browser.
- **Secrets are never logged.** No log statement, exception message, or metric may contain a Steam token, an FCM/Expo credential, or a `playerToken`. Task 13 adds the test that enforces this.
- **Time comes from `TimeProvider`.** Never `DateTimeOffset.UtcNow` directly — every TTL must be testable with `FakeTimeProvider`.
- **Commit after every task.** Conventional Commits, matching repo history (`feat:`, `fix:`, `chore:`, `docs:`, `test:`).

## File Structure

```
apps/RustPlusApi.CredentialsWeb/
    RustPlusApi.CredentialsWeb.csproj
    Program.cs                      Host wiring only. [ExcludeFromCodeCoverage].
    AppOptions.cs                   Config record + pure validator.
    Sessions/
        SessionIds.cs               128-bit hex id generation.
        SessionState.cs             The state enum.
        SessionEvent.cs             One SSE event: type + payload.
        SessionEventPayloads.cs     Typed payloads for step/credentials/paired/error.
        SessionEventStream.cs       Replay history + multi-subscriber fan-out.
        Session.cs                  One visitor's state and secrets.
        SessionStore.cs             Create / lookup / consume / caps / TTL sweep.
        SessionSweeper.cs           BackgroundService driving SessionStore.SweepExpired.
    Upstream/
        IRegistrationSteps.cs       Seam over the four live-network classes.
        LiveRegistrationSteps.cs    Real implementation. [ExcludeFromCodeCoverage].
    Flow/
        CredentialFlow.cs           Orchestrates 4→1,2,3→5, and optionally 6.
    Endpoints/
        SessionEndpoints.cs         POST /api/sessions, POST /api/sessions/{id}/pairing
        CallbackEndpoints.cs        GET /callback/{returnToken}
        EventEndpoints.cs           GET /api/sessions/{id}/events (SSE)
    Security/
        SecurityHeaders.cs          CSP, Referrer-Policy, no-store, nosniff.
    wwwroot/
        index.html                  The page.
        app.js                      Flow driver + SSE client.
        app.css                     Styles.
    Dockerfile
    docker-compose.yml              Copyable documentation, not a second install path.
    Caddyfile.example               Ditto — shows the safe log format.
    README.md                       Self-host guide.
tests/RustPlusApi.CredentialsWeb.UnitTests/
    RustPlusApi.CredentialsWeb.UnitTests.csproj
    AssemblyInfo.cs                 Serializes the assembly (the factory uses global env vars).
    AppOptionsValidatorTests.cs
    SessionEventStreamTests.cs
    SessionTests.cs
    SessionStoreTests.cs
    SessionStoreCapsTests.cs
    SessionSweeperTests.cs
    CredentialFlowTests.cs
    CredentialFlowPairingTests.cs
    FakeRegistrationSteps.cs        Shared test double for IRegistrationSteps.
    CredentialsWebFactory.cs        WebApplicationFactory harness + log capture.
    SessionEndpointTests.cs
    CallbackEndpointTests.cs
    EventEndpointTests.cs
    PairingEndpointTests.cs
    SecretsAreNeverLoggedTests.cs
```

---

### Task 1: Project scaffolding and startup validation

Creates both projects, wires them into the solution, and lands the one piece of startup logic that has a testable contract: the app must refuse to run with a non-`https` public base URL.

**Files:**
- Create: `apps/RustPlusApi.CredentialsWeb/RustPlusApi.CredentialsWeb.csproj`
- Create: `apps/RustPlusApi.CredentialsWeb/AppOptions.cs`
- Create: `apps/RustPlusApi.CredentialsWeb/Program.cs`
- Create: `tests/RustPlusApi.CredentialsWeb.UnitTests/RustPlusApi.CredentialsWeb.UnitTests.csproj`
- Create: `tests/RustPlusApi.CredentialsWeb.UnitTests/AppOptionsValidatorTests.cs`
- Modify: `Directory.Packages.props`
- Modify: `RustPlusApi.sln`

**Interfaces:**
- Consumes: nothing.
- Produces: `internal sealed class AppOptions` with `SectionName`, `PublicBaseUrl`, `AllowInsecureBaseUrl`, `KnownProxies`, `MaxConcurrentSessions`, `MaxConcurrentPairings`, `MaxCompletionsPerIpPerHour`, `CreatedTtl`, `SessionTtl`, `PairingTtl`; and `internal static class AppOptionsValidator` with `internal static string? Validate(AppOptions options)` returning `null` when valid or a human-readable error otherwise.

- [ ] **Step 1: Add the new package versions**

In `Directory.Packages.props`, add to the `<!-- Test stack -->` `ItemGroup`:

```xml
    <PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.11" />
    <PackageVersion Include="Microsoft.Extensions.TimeProvider.Testing" Version="10.9.0" />
```

- [ ] **Step 2: Create the app project file**

`apps/RustPlusApi.CredentialsWeb/RustPlusApi.CredentialsWeb.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

    <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
        <IsPackable>false</IsPackable>
        <!-- The app is net10.0-only and deliberately outside the netstandard2.0 parity story
             that governs src/. See docs/superpowers/specs/2026-09-03-credential-acquisition-website-design.md -->
        <UserSecretsId>rustplusapi-credentialsweb</UserSecretsId>
    </PropertyGroup>

    <ItemGroup>
        <ProjectReference Include="..\..\src\RustPlusApi.Fcm.Registration\RustPlusApi.Fcm.Registration.csproj" />
    </ItemGroup>

    <ItemGroup>
        <InternalsVisibleTo Include="RustPlusApi.CredentialsWeb.UnitTests" />
    </ItemGroup>

</Project>
```

- [ ] **Step 3: Create the test project file**

`tests/RustPlusApi.CredentialsWeb.UnitTests/RustPlusApi.CredentialsWeb.UnitTests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <!-- net10.0 only: the app under test is net10.0 only, so there is no
             netstandard2.0 build to validate for parity. -->
        <TargetFramework>net10.0</TargetFramework>
        <IsPackable>false</IsPackable>
        <IsTestProject>true</IsTestProject>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="Microsoft.NET.Test.Sdk" />
        <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
        <PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" />
        <PackageReference Include="xunit" />
        <PackageReference Include="xunit.runner.visualstudio">
            <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
            <PrivateAssets>all</PrivateAssets>
        </PackageReference>
        <PackageReference Include="coverlet.collector">
            <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
            <PrivateAssets>all</PrivateAssets>
        </PackageReference>
    </ItemGroup>

    <ItemGroup>
        <ProjectReference Include="..\..\apps\RustPlusApi.CredentialsWeb\RustPlusApi.CredentialsWeb.csproj" />
    </ItemGroup>

</Project>
```

- [ ] **Step 4: Add both projects to the solution**

```bash
dotnet sln RustPlusApi.sln add apps/RustPlusApi.CredentialsWeb/RustPlusApi.CredentialsWeb.csproj
dotnet sln RustPlusApi.sln add tests/RustPlusApi.CredentialsWeb.UnitTests/RustPlusApi.CredentialsWeb.UnitTests.csproj
```

- [ ] **Step 5: Write the failing tests**

`tests/RustPlusApi.CredentialsWeb.UnitTests/AppOptionsValidatorTests.cs`:

```csharp
using RustPlusApi.CredentialsWeb;

namespace RustPlusApi.CredentialsWeb.UnitTests;

public sealed class AppOptionsValidatorTests
{
    private static AppOptions Valid() => new() { PublicBaseUrl = "https://creds.example.org" };

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
}
```

- [ ] **Step 6: Run the tests to verify they fail**

Run: `dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests --filter "FullyQualifiedName~AppOptionsValidatorTests"`
Expected: FAIL — `AppOptions` and `AppOptionsValidator` do not exist (CS0246).

- [ ] **Step 7: Write AppOptions and the validator**

`apps/RustPlusApi.CredentialsWeb/AppOptions.cs`:

```csharp
using System.Net;

namespace RustPlusApi.CredentialsWeb;

/// <summary>Every knob the app exposes. Bound from the "CredentialsWeb" configuration section.</summary>
internal sealed class AppOptions
{
    /// <summary>Configuration section these options bind from.</summary>
    internal const string SectionName = "CredentialsWeb";

    /// <summary>The externally reachable origin, with no trailing slash. Required: behind a reverse
    /// proxy this is not what Kestrel sees, and the Facepunch returnUrl must be the external one.</summary>
    internal string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>Development escape hatch permitting a non-https <see cref="PublicBaseUrl"/>.</summary>
    internal bool AllowInsecureBaseUrl { get; set; }

    /// <summary>Addresses of reverse proxies whose <c>X-Forwarded-For</c> is trusted. Empty means
    /// forwarded headers are ignored, which is the safe default: trusting them from anyone lets a
    /// caller spoof their address past every per-IP cap.</summary>
    internal IList<string> KnownProxies { get; } = [];

    /// <summary>Global cap on live sessions in any state.</summary>
    internal int MaxConcurrentSessions { get; set; } = 200;

    /// <summary>Global cap on concurrent MCS sockets — the genuinely scarce resource.</summary>
    internal int MaxConcurrentPairings { get; set; } = 50;

    /// <summary>Per-IP cap on completed flows in a rolling hour. Bounds Google device registrations.</summary>
    internal int MaxCompletionsPerIpPerHour { get; set; } = 5;

    /// <summary>Lifetime of a session that has not yet completed the Steam login. Shortest leash:
    /// this is the cheapest state to create and therefore the cheapest to spam.</summary>
    internal TimeSpan CreatedTtl { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Lifetime of a session once the Steam login has completed.</summary>
    internal TimeSpan SessionTtl { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Maximum time an MCS socket is held waiting for a pairing push.</summary>
    internal TimeSpan PairingTtl { get; set; } = TimeSpan.FromMinutes(10);
}

/// <summary>Pure validation for <see cref="AppOptions"/>, kept separate from host wiring so it is
/// unit-testable without building a web host.</summary>
internal static class AppOptionsValidator
{
    /// <summary>Returns <see langword="null"/> when the options are usable, or a message naming the
    /// offending setting.</summary>
    internal static string? Validate(AppOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.PublicBaseUrl))
        {
            return $"{AppOptions.SectionName}:PublicBaseUrl is required. Set it to the externally "
                + "reachable origin of this instance, for example https://creds.example.org.";
        }

        if (!Uri.TryCreate(options.PublicBaseUrl, UriKind.Absolute, out var baseUri))
        {
            return $"{AppOptions.SectionName}:PublicBaseUrl must be an absolute URL, "
                + $"but was '{options.PublicBaseUrl}'.";
        }

        if (options.PublicBaseUrl.EndsWith('/'))
        {
            return $"{AppOptions.SectionName}:PublicBaseUrl must not have a trailing slash.";
        }

        if (!baseUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.Ordinal)
            && !options.AllowInsecureBaseUrl)
        {
            return $"{AppOptions.SectionName}:PublicBaseUrl must use https, because it carries the "
                + "Steam auth token back from Facepunch. Set "
                + $"{AppOptions.SectionName}:AllowInsecureBaseUrl=true only for local development.";
        }

        foreach (var proxy in options.KnownProxies)
        {
            if (!IPAddress.TryParse(proxy, out _))
            {
                return $"{AppOptions.SectionName}:KnownProxies contains '{proxy}', which is not an "
                    + "IP address.";
            }
        }

        if (options.MaxConcurrentSessions <= 0)
        {
            return $"{AppOptions.SectionName}:MaxConcurrentSessions must be greater than zero.";
        }

        if (options.MaxConcurrentPairings <= 0)
        {
            return $"{AppOptions.SectionName}:MaxConcurrentPairings must be greater than zero.";
        }

        if (options.MaxCompletionsPerIpPerHour <= 0)
        {
            return $"{AppOptions.SectionName}:MaxCompletionsPerIpPerHour must be greater than zero.";
        }

        if (options.CreatedTtl <= TimeSpan.Zero)
        {
            return $"{AppOptions.SectionName}:CreatedTtl must be greater than zero.";
        }

        if (options.SessionTtl <= TimeSpan.Zero)
        {
            return $"{AppOptions.SectionName}:SessionTtl must be greater than zero.";
        }

        return options.PairingTtl <= TimeSpan.Zero
            ? $"{AppOptions.SectionName}:PairingTtl must be greater than zero."
            : null;
    }
}
```

- [ ] **Step 8: Write the minimal Program.cs**

Endpoints and services arrive in later tasks. `Program` is `[ExcludeFromCodeCoverage]` wiring; the `public partial class Program` declaration at the bottom is what `WebApplicationFactory<Program>` needs in Task 13.

`apps/RustPlusApi.CredentialsWeb/Program.cs`:

```csharp
using RustPlusApi.CredentialsWeb;
using System.Diagnostics.CodeAnalysis;

var builder = WebApplication.CreateBuilder(args);

// ASP.NET Core's hosting diagnostics log the full request line — path AND query — at Information.
// The Facepunch callback carries the Steam auth token in its query string, so that logger is
// silenced outright rather than filtered per-path: the "Request starting" entry is written before
// any middleware of ours could redact it. Enforced by SecretsAreNeverLoggedTests.
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);

var options = new AppOptions();
builder.Configuration.GetSection(AppOptions.SectionName).Bind(options);

var validationError = AppOptionsValidator.Validate(options);
if (validationError is not null)
{
    Console.Error.WriteLine($"Configuration error: {validationError}");
    return 1;
}

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

await app.RunAsync();
return 0;

/// <summary>Entry point marker so <c>WebApplicationFactory&lt;Program&gt;</c> can boot the app in tests.</summary>
[ExcludeFromCodeCoverage(Justification = "Host wiring: composition only, exercised end to end by the endpoint tests.")]
public partial class Program;
```

- [ ] **Step 9: Run the tests to verify they pass**

Run: `dtk dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests --filter "FullyQualifiedName~AppOptionsValidatorTests"`
Expected: PASS, 8 tests.

- [ ] **Step 10: Verify the strict build is clean**

Run: `dtk dotnet build RustPlusApi.sln`
Expected: no warnings, no errors. If `Bind` is unresolved, add `using Microsoft.Extensions.Configuration;`.

- [ ] **Step 11: Commit**

```bash
git add Directory.Packages.props RustPlusApi.sln apps/ tests/RustPlusApi.CredentialsWeb.UnitTests/
git commit -m "feat(web): scaffold credentials web app with validated startup options"
```

---

### Task 2: Session event stream

The SSE plumbing, built first because everything downstream publishes into it. Two requirements make it more than a `Channel`: a reconnecting client must receive events it missed, and the Steam login may complete in a different browser from the one holding the stream, so there can be more than one subscriber.

**Files:**
- Create: `apps/RustPlusApi.CredentialsWeb/Sessions/SessionEvent.cs`
- Create: `apps/RustPlusApi.CredentialsWeb/Sessions/SessionEventPayloads.cs`
- Create: `apps/RustPlusApi.CredentialsWeb/Sessions/SessionEventStream.cs`
- Test: `tests/RustPlusApi.CredentialsWeb.UnitTests/SessionEventStreamTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `internal sealed record SessionEvent(string Type, object? Data)`; the payload records `StepPayload(string State)`, `CredentialsPayload(string SteamId, string ConfigJson)`, `PairedPayload(string Ip, int Port, string PlayerId, int PlayerToken, string? Name)` and `ErrorPayload(string Message)`; `internal sealed class SessionEventStream` with `internal void Publish(SessionEvent sessionEvent)`, `internal void Complete()`, and `internal IAsyncEnumerable<SessionEvent> SubscribeAsync(CancellationToken cancellationToken)`. A new subscriber receives every event published so far, in order, before any live one. `Publish` after `Complete` is a no-op.

- [ ] **Step 1: Write the failing tests**

`tests/RustPlusApi.CredentialsWeb.UnitTests/SessionEventStreamTests.cs`:

```csharp
using RustPlusApi.CredentialsWeb.Sessions;

namespace RustPlusApi.CredentialsWeb.UnitTests;

public sealed class SessionEventStreamTests
{
    private static async Task<List<SessionEvent>> DrainAsync(SessionEventStream stream, int expected)
    {
        var received = new List<SessionEvent>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await foreach (var item in stream.SubscribeAsync(timeout.Token))
        {
            received.Add(item);
            if (received.Count == expected)
            {
                break;
            }
        }

        return received;
    }

    [Fact]
    public async Task SubscribeAsync_ReplaysEventsPublishedBeforeSubscription()
    {
        var stream = new SessionEventStream();
        stream.Publish(new SessionEvent("step", new StepPayload("Registering")));
        stream.Publish(new SessionEvent("step", new StepPayload("Ready")));

        var received = await DrainAsync(stream, 2);

        Assert.Equal(2, received.Count);
        Assert.All(received, e => Assert.Equal("step", e.Type));
    }

    [Fact]
    public async Task SubscribeAsync_DeliversEventsPublishedAfterSubscription()
    {
        var stream = new SessionEventStream();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var enumerator = stream.SubscribeAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);

        stream.Publish(new SessionEvent("paired", null));

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("paired", enumerator.Current.Type);
        await enumerator.DisposeAsync();
    }

    [Fact]
    public async Task SubscribeAsync_ReplaysThenStreams_InOrder()
    {
        var stream = new SessionEventStream();
        stream.Publish(new SessionEvent("first", null));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var enumerator = stream.SubscribeAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("first", enumerator.Current.Type);

        stream.Publish(new SessionEvent("second", null));

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("second", enumerator.Current.Type);
        await enumerator.DisposeAsync();
    }

    [Fact]
    public async Task SubscribeAsync_SupportsTwoConcurrentSubscribers()
    {
        var stream = new SessionEventStream();
        var first = DrainAsync(stream, 1);
        var second = DrainAsync(stream, 1);

        // Give both subscribers a chance to register before publishing.
        await Task.Delay(50);
        stream.Publish(new SessionEvent("step", null));

        Assert.Single(await first);
        Assert.Single(await second);
    }

    [Fact]
    public async Task SubscribeAsync_CompletesWhenStreamCompleted()
    {
        var stream = new SessionEventStream();
        stream.Publish(new SessionEvent("expired", null));
        stream.Complete();

        var received = new List<SessionEvent>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var item in stream.SubscribeAsync(timeout.Token))
        {
            received.Add(item);
        }

        Assert.Single(received);
    }

    [Fact]
    public async Task Publish_AfterComplete_IsIgnored()
    {
        var stream = new SessionEventStream();
        stream.Complete();
        stream.Publish(new SessionEvent("step", null));

        var received = new List<SessionEvent>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var item in stream.SubscribeAsync(timeout.Token))
        {
            received.Add(item);
        }

        Assert.Empty(received);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dtk dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests --filter "FullyQualifiedName~SessionEventStreamTests"`
Expected: FAIL — `SessionEvent` and `SessionEventStream` do not exist.

- [ ] **Step 3: Write SessionEvent**

`apps/RustPlusApi.CredentialsWeb/Sessions/SessionEvent.cs`:

```csharp
namespace RustPlusApi.CredentialsWeb.Sessions;

/// <summary>One server-sent event. <paramref name="Type"/> becomes the SSE <c>event:</c> name and
/// <paramref name="Data"/> is serialized to JSON for the <c>data:</c> line.</summary>
/// <param name="Type">One of <c>step</c>, <c>credentials</c>, <c>paired</c>, <c>error</c>, <c>expired</c>.</param>
/// <param name="Data">Payload, or <see langword="null"/> for an event that carries none.</param>
internal sealed record SessionEvent(string Type, object? Data);
```

- [ ] **Step 3b: Write the payload records**

Concrete records rather than anonymous objects, so tests can assert on payloads and so the
`ulong`-as-string rule is enforced by the type rather than by remembering it at each call site.

`apps/RustPlusApi.CredentialsWeb/Sessions/SessionEventPayloads.cs`:

```csharp
namespace RustPlusApi.CredentialsWeb.Sessions;

/// <summary>Payload of a <c>step</c> event.</summary>
/// <param name="State">The new <see cref="SessionState"/>, as its enum name.</param>
internal sealed record StepPayload(string State);

/// <summary>Payload of a <c>credentials</c> event.</summary>
/// <param name="SteamId">Steam64 as a string: it exceeds JavaScript's safe integer range.</param>
/// <param name="ConfigJson">The exact contents of rustplus.config.json, from <c>CredentialsStore.Serialize</c>.</param>
internal sealed record CredentialsPayload(string SteamId, string ConfigJson);

/// <summary>Payload of a <c>paired</c> event.</summary>
/// <param name="Ip">Server address.</param>
/// <param name="Port">Server app port.</param>
/// <param name="PlayerId">Steam64 as a string, for the same reason as <see cref="CredentialsPayload.SteamId"/>.</param>
/// <param name="PlayerToken">The pairing token. Full Rust+ account access.</param>
/// <param name="Name">Server name, when the push carried one.</param>
internal sealed record PairedPayload(string Ip, int Port, string PlayerId, int PlayerToken, string? Name);

/// <summary>Payload of an <c>error</c> event. Always a fixed, non-reflective message: never an
/// exception message, which could carry upstream response content.</summary>
/// <param name="Message">What to show the visitor.</param>
internal sealed record ErrorPayload(string Message);
```

- [ ] **Step 4: Write SessionEventStream**

The lock covers both the history snapshot and the subscriber registration, so an event published between the two cannot be lost. The snapshot is taken in a non-iterator helper because `lock` cannot span a `yield`.

`apps/RustPlusApi.CredentialsWeb/Sessions/SessionEventStream.cs`:

```csharp
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace RustPlusApi.CredentialsWeb.Sessions;

/// <summary>Fans one session's events out to every subscriber, replaying the events published
/// before each subscription. Replay is what makes an SSE reconnect resume rather than restart —
/// which matters because the drop happens exactly when the visitor alt-tabs into fullscreen Rust.</summary>
internal sealed class SessionEventStream
{
    private readonly List<SessionEvent> _history = [];
    private readonly Lock _gate = new();
    private readonly List<Channel<SessionEvent>> _subscribers = [];
    private bool _completed;

    /// <summary>Appends an event to the history and pushes it to every live subscriber.
    /// Ignored once <see cref="Complete"/> has been called.</summary>
    internal void Publish(SessionEvent sessionEvent)
    {
        lock (_gate)
        {
            if (_completed)
            {
                return;
            }

            _history.Add(sessionEvent);
            foreach (var subscriber in _subscribers)
            {
                // Unbounded channel: writes always succeed, so a slow reader can never block the flow.
                subscriber.Writer.TryWrite(sessionEvent);
            }
        }
    }

    /// <summary>Ends every subscriber's enumeration. Idempotent.</summary>
    internal void Complete()
    {
        lock (_gate)
        {
            if (_completed)
            {
                return;
            }

            _completed = true;
            foreach (var subscriber in _subscribers)
            {
                subscriber.Writer.TryComplete();
            }

            _subscribers.Clear();
        }
    }

    /// <summary>Yields every event published so far, then every subsequent one until the stream is
    /// completed or <paramref name="cancellationToken"/> fires.</summary>
    internal async IAsyncEnumerable<SessionEvent> SubscribeAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var (history, reader) = Subscribe();

        foreach (var item in history)
        {
            yield return item;
        }

        if (reader is null)
        {
            yield break;
        }

        await foreach (var item in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    /// <summary>Snapshots the history and registers a subscriber under one lock, so no event can slip
    /// between the two. Returns a null reader when the stream is already complete.</summary>
    private (IReadOnlyList<SessionEvent> History, ChannelReader<SessionEvent>? Reader) Subscribe()
    {
        lock (_gate)
        {
            var history = _history.ToArray();
            if (_completed)
            {
                return (history, null);
            }

            var channel = Channel.CreateUnbounded<SessionEvent>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

            _subscribers.Add(channel);
            return (history, channel.Reader);
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dtk dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests --filter "FullyQualifiedName~SessionEventStreamTests"`
Expected: PASS, 6 tests.

Note: `System.Threading.Lock` is .NET 9+. If the analyzer objects, `private readonly object _gate = new();` is equivalent here.

- [ ] **Step 6: Commit**

```bash
git add apps/RustPlusApi.CredentialsWeb/Sessions/ tests/RustPlusApi.CredentialsWeb.UnitTests/SessionEventStreamTests.cs
git commit -m "feat(web): add replaying multi-subscriber session event stream"
```

---

### Task 3: Session identity and state

One visitor's state, its secrets, and their lifetimes. The two ids are deliberately separate: the `returnToken` is the value that necessarily travels through Facepunch and can land in logs, so it must not be the value that grants access to the session's events.

**Files:**
- Create: `apps/RustPlusApi.CredentialsWeb/Sessions/SessionIds.cs`
- Create: `apps/RustPlusApi.CredentialsWeb/Sessions/SessionState.cs`
- Create: `apps/RustPlusApi.CredentialsWeb/Sessions/Session.cs`
- Test: `tests/RustPlusApi.CredentialsWeb.UnitTests/SessionTests.cs`

**Interfaces:**
- Consumes: `SessionEvent`, `SessionEventStream` from Task 2.
- Produces:
  - `internal static class SessionIds` with `internal static string New()` → 32 lowercase hex characters.
  - `internal enum SessionState { Created, Authenticated, Registering, Ready, AwaitingPairing, Paired, Failed }`
  - `internal sealed class Session : IDisposable` — constructor `Session(string sessionId, string returnToken, string clientIp, DateTimeOffset expiresAt)`; properties `SessionId`, `ReturnToken`, `ClientIp`, `Events`, `State`, `ExpiresAt`, `SteamId`, `SteamToken`, `Credentials`, `Pairing`, `Lifetime`, `BackgroundWork`; methods `Advance(SessionState, DateTimeOffset)`, `SetSteamLogin(SteamLoginResult)`, `ClearSteamToken()`, `SetCredentials(Credentials)`, `SetPairing(ServerPairing)`, `IsExpired(DateTimeOffset)`, `Dispose()`.

- [ ] **Step 1: Write the failing tests**

`tests/RustPlusApi.CredentialsWeb.UnitTests/SessionTests.cs`:

```csharp
using RustPlusApi.CredentialsWeb.Sessions;
using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Registration;

namespace RustPlusApi.CredentialsWeb.UnitTests;

public sealed class SessionTests
{
    private static readonly DateTimeOffset Origin = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    private static Session NewSession() =>
        new("session-id", "return-token", "203.0.113.7", Origin.AddMinutes(5));

    private static async Task<List<SessionEvent>> ReadAsync(Session session, int expected)
    {
        var received = new List<SessionEvent>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await foreach (var item in session.Events.SubscribeAsync(timeout.Token))
        {
            received.Add(item);
            if (received.Count == expected)
            {
                break;
            }
        }

        return received;
    }

    [Fact]
    public void New_StartsInCreatedState()
    {
        using var session = NewSession();

        Assert.Equal(SessionState.Created, session.State);
        Assert.Equal(Origin.AddMinutes(5), session.ExpiresAt);
        Assert.Equal("203.0.113.7", session.ClientIp);
    }

    [Fact]
    public async Task Advance_UpdatesStateAndExpiry_AndPublishesStepEvent()
    {
        using var session = NewSession();

        session.Advance(SessionState.Registering, Origin.AddMinutes(15));

        Assert.Equal(SessionState.Registering, session.State);
        Assert.Equal(Origin.AddMinutes(15), session.ExpiresAt);

        var events = await ReadAsync(session, 1);
        Assert.Equal("step", events[0].Type);
    }

    [Fact]
    public void SetSteamLogin_StoresIdAndToken()
    {
        using var session = NewSession();

        session.SetSteamLogin(new SteamLoginResult(76561198249527954, "steam-token"));

        Assert.Equal(76561198249527954UL, session.SteamId);
        Assert.Equal("steam-token", session.SteamToken);
    }

    [Fact]
    public void ClearSteamToken_DropsTokenButKeepsSteamId()
    {
        using var session = NewSession();
        session.SetSteamLogin(new SteamLoginResult(76561198249527954, "steam-token"));

        session.ClearSteamToken();

        Assert.Null(session.SteamToken);
        Assert.Equal(76561198249527954UL, session.SteamId);
    }

    [Fact]
    public void SetCredentialsAndPairing_AreExposed()
    {
        using var session = NewSession();
        var credentials = new Credentials { ExpoPushToken = "expo-token" };
        var pairing = new ServerPairing { Ip = "10.0.0.1", Port = 28082, PlayerId = 1, PlayerToken = 2 };

        session.SetCredentials(credentials);
        session.SetPairing(pairing);

        Assert.Same(credentials, session.Credentials);
        Assert.Same(pairing, session.Pairing);
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    public void IsExpired_ComparesAgainstExpiry(int minutes, bool expected)
    {
        using var session = NewSession();

        Assert.Equal(expected, session.IsExpired(Origin.AddMinutes(minutes)));
    }

    [Fact]
    public void Dispose_ClearsSecretsAndCancelsLifetime()
    {
        var session = NewSession();
        session.SetSteamLogin(new SteamLoginResult(1, "steam-token"));
        session.SetCredentials(new Credentials { ExpoPushToken = "expo-token" });
        session.SetPairing(new ServerPairing { Ip = "10.0.0.1", Port = 1, PlayerId = 1, PlayerToken = 2 });

        session.Dispose();

        Assert.Null(session.SteamToken);
        Assert.Null(session.Credentials);
        Assert.Null(session.Pairing);
        Assert.True(session.Lifetime.IsCancellationRequested);
    }

    [Fact]
    public async Task Dispose_CompletesTheEventStream()
    {
        var session = NewSession();
        session.Dispose();

        var received = new List<SessionEvent>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var item in session.Events.SubscribeAsync(timeout.Token))
        {
            received.Add(item);
        }

        Assert.Empty(received);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var session = NewSession();

        session.Dispose();
        session.Dispose();
    }

    [Fact]
    public void SessionIds_AreThirtyTwoLowercaseHexCharsAndUnique()
    {
        var first = SessionIds.New();
        var second = SessionIds.New();

        Assert.Equal(32, first.Length);
        Assert.Matches("^[0-9a-f]{32}$", first);
        Assert.NotEqual(first, second);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dtk dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests --filter "FullyQualifiedName~SessionTests"`
Expected: FAIL — `Session`, `SessionState` and `SessionIds` do not exist.

- [ ] **Step 3: Write SessionIds and SessionState**

`apps/RustPlusApi.CredentialsWeb/Sessions/SessionIds.cs`:

```csharp
using System.Security.Cryptography;

namespace RustPlusApi.CredentialsWeb.Sessions;

/// <summary>Generates the opaque identifiers used for both the session handle and the return token.</summary>
internal static class SessionIds
{
    /// <summary>A fresh 128-bit identifier as 32 lowercase hex characters.</summary>
    internal static string New() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
}
```

`apps/RustPlusApi.CredentialsWeb/Sessions/SessionState.cs`:

```csharp
namespace RustPlusApi.CredentialsWeb.Sessions;

/// <summary>Where a visitor is in the credential flow.</summary>
internal enum SessionState
{
    /// <summary>Created; the visitor has not completed the Steam login yet. Nothing upstream touched.</summary>
    Created,

    /// <summary>The Facepunch callback arrived with a usable Steam token.</summary>
    Authenticated,

    /// <summary>Steps 1-3 and 5 are running.</summary>
    Registering,

    /// <summary>Credentials acquired and registered with Rust Companion.</summary>
    Ready,

    /// <summary>Holding an MCS socket, waiting for the in-game pairing push.</summary>
    AwaitingPairing,

    /// <summary>A pairing push arrived; the flow is complete.</summary>
    Paired,

    /// <summary>A step failed. Terminal.</summary>
    Failed
}

// There is deliberately no Expired state. A pairing wait that times out returns the session to
// Ready and emits an `expired` event: the credentials are still valid, so the visitor can retry
// the pairing without repeating the Steam login. A session that outlives its TTL is removed by
// the sweeper rather than parked in a state nobody can observe.
```

- [ ] **Step 4: Write Session**

`apps/RustPlusApi.CredentialsWeb/Sessions/Session.cs`:

```csharp
using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Registration;

namespace RustPlusApi.CredentialsWeb.Sessions;

/// <summary>One visitor's flow: its state, its secrets and their lifetimes. Everything here is
/// in-memory only and dies with <see cref="Dispose"/> or the process — nothing is ever persisted.</summary>
/// <param name="sessionId">The handle the browser uses for the event stream and follow-up calls.</param>
/// <param name="returnToken">Single-use token embedded in the Facepunch <c>returnUrl</c> path.</param>
/// <param name="clientIp">The caller's address, for per-IP accounting.</param>
/// <param name="expiresAt">When this session becomes sweepable.</param>
internal sealed class Session(string sessionId, string returnToken, string clientIp, DateTimeOffset expiresAt)
    : IDisposable
{
    private readonly Lock _gate = new();
    private bool _disposed;

    /// <summary>Background work started for this session, kept so it is observed rather than fire-and-forget.</summary>
    internal Task BackgroundWork { get; set; } = Task.CompletedTask;

    /// <summary>The caller's address.</summary>
    internal string ClientIp { get; } = clientIp;

    /// <summary>Credentials from steps 1-3, once acquired.</summary>
    internal Credentials? Credentials { get; private set; }

    /// <summary>This session's event stream.</summary>
    internal SessionEventStream Events { get; } = new();

    /// <summary>When this session becomes sweepable.</summary>
    internal DateTimeOffset ExpiresAt { get; private set; } = expiresAt;

    /// <summary>Cancelled on disposal, so any in-flight upstream work stops with the session.</summary>
    internal CancellationTokenSource Lifetime { get; } = new();

    /// <summary>The pairing, once a push arrives.</summary>
    internal ServerPairing? Pairing { get; private set; }

    /// <summary>Single-use token embedded in the Facepunch <c>returnUrl</c> path.</summary>
    internal string ReturnToken { get; } = returnToken;

    /// <summary>The handle the browser uses. Never appears in a <c>returnUrl</c>.</summary>
    internal string SessionId { get; } = sessionId;

    /// <summary>The Steam64 from the callback. Not a secret; kept for display.</summary>
    internal ulong SteamId { get; private set; }

    /// <summary>The Steam auth token. Dropped the moment step 5 succeeds.</summary>
    internal string? SteamToken { get; private set; }

    /// <summary>Where the visitor is in the flow.</summary>
    internal SessionState State { get; private set; } = SessionState.Created;

    /// <summary>Cancels in-flight work, ends the event stream and drops every secret. Idempotent.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            SteamToken = null;
            Credentials = null;
            Pairing = null;
        }

        // Outside the lock: cancellation callbacks may re-enter session code.
        Lifetime.Cancel();
        Events.Complete();
        Lifetime.Dispose();
    }

    /// <summary>Moves to <paramref name="state"/>, resets the expiry and publishes a <c>step</c> event.</summary>
    /// <param name="state">The new state.</param>
    /// <param name="newExpiry">The new expiry instant.</param>
    internal void Advance(SessionState state, DateTimeOffset newExpiry)
    {
        lock (_gate)
        {
            State = state;
            ExpiresAt = newExpiry;
        }

        Events.Publish(new SessionEvent("step", new StepPayload(state.ToString())));
    }

    /// <summary>Drops the Steam auth token once it has no further use.</summary>
    internal void ClearSteamToken()
    {
        lock (_gate)
        {
            SteamToken = null;
        }
    }

    /// <summary>True once <paramref name="now"/> has reached the expiry.</summary>
    /// <param name="now">The current instant, from the ambient <see cref="TimeProvider"/>.</param>
    internal bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    /// <summary>Stores the credentials from steps 1-3.</summary>
    /// <param name="credentials">The acquired credentials.</param>
    internal void SetCredentials(Credentials credentials)
    {
        lock (_gate)
        {
            Credentials = credentials;
        }
    }

    /// <summary>Stores the pairing from step 6.</summary>
    /// <param name="pairing">The pairing that arrived.</param>
    internal void SetPairing(ServerPairing pairing)
    {
        lock (_gate)
        {
            Pairing = pairing;
        }
    }

    /// <summary>Stores the Steam identity captured from the Facepunch callback.</summary>
    /// <param name="login">The parsed callback result.</param>
    internal void SetSteamLogin(SteamLoginResult login)
    {
        lock (_gate)
        {
            SteamId = login.SteamId;
            SteamToken = login.Token;
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dtk dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests --filter "FullyQualifiedName~SessionTests"`
Expected: PASS, 11 tests.

- [ ] **Step 6: Commit**

```bash
git add apps/RustPlusApi.CredentialsWeb/Sessions/ tests/RustPlusApi.CredentialsWeb.UnitTests/SessionTests.cs
git commit -m "feat(web): add session state, identity and secret lifetimes"
```

---

### Task 4: Session store — create, lookup, consume, sweep

The registry. This task implements everything except the IP-based caps, which arrive in Task 5.

**Files:**
- Create: `apps/RustPlusApi.CredentialsWeb/Sessions/SessionStore.cs`
- Test: `tests/RustPlusApi.CredentialsWeb.UnitTests/SessionStoreTests.cs`

**Interfaces:**
- Consumes: `AppOptions` (Task 1); `Session`, `SessionIds`, `SessionState` (Task 3).
- Produces:
  - `internal enum SessionCreateFailure { None, GlobalLimit, ActiveSessionForIp, HourlyLimit }`
  - `internal sealed class SessionStore(AppOptions options, TimeProvider timeProvider) : IDisposable` with:
    - `internal bool TryCreate(string clientIp, [NotNullWhen(true)] out Session? session, out SessionCreateFailure failure)`
    - `internal bool TryGet(string sessionId, [NotNullWhen(true)] out Session? session)`
    - `internal bool TryConsumeReturnToken(string returnToken, [NotNullWhen(true)] out Session? session)`
    - `internal void Remove(string sessionId)`
    - `internal int SweepExpired()`
    - `internal int Count { get; }`

- [ ] **Step 1: Write the failing tests**

`tests/RustPlusApi.CredentialsWeb.UnitTests/SessionStoreTests.cs`:

```csharp
using Microsoft.Extensions.Time.Testing;
using RustPlusApi.CredentialsWeb;
using RustPlusApi.CredentialsWeb.Sessions;

namespace RustPlusApi.CredentialsWeb.UnitTests;

public sealed class SessionStoreTests
{
    private static readonly DateTimeOffset Origin = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    private static (SessionStore Store, FakeTimeProvider Time) NewStore()
    {
        var time = new FakeTimeProvider(Origin);
        var options = new AppOptions { PublicBaseUrl = "https://creds.example.org" };
        return (new SessionStore(options, time), time);
    }

    [Fact]
    public void TryCreate_ReturnsCreatedSessionWithDistinctIds()
    {
        var (store, _) = NewStore();
        using var _s = store;

        Assert.True(store.TryCreate("203.0.113.7", out var session, out var failure));

        Assert.Equal(SessionCreateFailure.None, failure);
        Assert.Equal(SessionState.Created, session.State);
        Assert.Equal("203.0.113.7", session.ClientIp);
        Assert.NotEqual(session.SessionId, session.ReturnToken);
        Assert.Equal(Origin.AddMinutes(5), session.ExpiresAt);
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void TryGet_FindsSessionBySessionId()
    {
        var (store, _) = NewStore();
        using var _s = store;
        store.TryCreate("203.0.113.7", out var created, out _);

        Assert.True(store.TryGet(created!.SessionId, out var found));
        Assert.Same(created, found);
    }

    [Fact]
    public void TryGet_ReturnsFalse_ForUnknownId()
    {
        var (store, _) = NewStore();
        using var _s = store;

        Assert.False(store.TryGet("nope", out var found));
        Assert.Null(found);
    }

    [Fact]
    public void TryGet_DoesNotAcceptTheReturnToken()
    {
        var (store, _) = NewStore();
        using var _s = store;
        store.TryCreate("203.0.113.7", out var created, out _);

        Assert.False(store.TryGet(created!.ReturnToken, out _));
    }

    [Fact]
    public void TryConsumeReturnToken_ReturnsSessionOnce()
    {
        var (store, _) = NewStore();
        using var _s = store;
        store.TryCreate("203.0.113.7", out var created, out _);

        Assert.True(store.TryConsumeReturnToken(created!.ReturnToken, out var first));
        Assert.Same(created, first);

        Assert.False(store.TryConsumeReturnToken(created.ReturnToken, out var second));
        Assert.Null(second);
    }

    [Fact]
    public void TryConsumeReturnToken_ReturnsFalse_ForUnknownToken()
    {
        var (store, _) = NewStore();
        using var _s = store;

        Assert.False(store.TryConsumeReturnToken("unknown", out _));
    }

    [Fact]
    public void TryConsumeReturnToken_LeavesSessionRetrievable()
    {
        var (store, _) = NewStore();
        using var _s = store;
        store.TryCreate("203.0.113.7", out var created, out _);

        store.TryConsumeReturnToken(created!.ReturnToken, out _);

        Assert.True(store.TryGet(created.SessionId, out _));
    }

    [Fact]
    public void Remove_DisposesAndForgetsTheSession()
    {
        var (store, _) = NewStore();
        using var _s = store;
        store.TryCreate("203.0.113.7", out var created, out _);

        store.Remove(created!.SessionId);

        Assert.False(store.TryGet(created.SessionId, out _));
        Assert.True(created.Lifetime.IsCancellationRequested);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void Remove_IsSafeForUnknownId()
    {
        var (store, _) = NewStore();
        using var _s = store;

        store.Remove("unknown");
    }

    [Fact]
    public void SweepExpired_RemovesOnlyExpiredSessions()
    {
        var (store, time) = NewStore();
        using var _s = store;
        store.TryCreate("203.0.113.7", out var stale, out _);
        time.Advance(TimeSpan.FromMinutes(6));
        store.TryCreate("203.0.113.8", out var fresh, out _);

        var swept = store.SweepExpired();

        Assert.Equal(1, swept);
        Assert.False(store.TryGet(stale!.SessionId, out _));
        Assert.True(store.TryGet(fresh!.SessionId, out _));
    }

    [Fact]
    public void SweepExpired_AlsoInvalidatesTheReturnToken()
    {
        var (store, time) = NewStore();
        using var _s = store;
        store.TryCreate("203.0.113.7", out var session, out _);
        time.Advance(TimeSpan.FromMinutes(6));

        store.SweepExpired();

        Assert.False(store.TryConsumeReturnToken(session!.ReturnToken, out _));
    }

    [Fact]
    public void SweepExpired_UsesTheStateSpecificTtl()
    {
        var (store, time) = NewStore();
        using var _s = store;
        store.TryCreate("203.0.113.7", out var session, out _);

        // Authenticated sessions get SessionTtl (15 min), not CreatedTtl (5 min).
        session!.Advance(SessionState.Authenticated, time.GetUtcNow().Add(TimeSpan.FromMinutes(15)));
        time.Advance(TimeSpan.FromMinutes(6));

        Assert.Equal(0, store.SweepExpired());
    }

    [Fact]
    public void Dispose_DisposesEverySession()
    {
        var (store, _) = NewStore();
        store.TryCreate("203.0.113.7", out var session, out _);

        store.Dispose();

        Assert.True(session!.Lifetime.IsCancellationRequested);
        Assert.Equal(0, store.Count);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dtk dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests --filter "FullyQualifiedName~SessionStoreTests"`
Expected: FAIL — `SessionStore` and `SessionCreateFailure` do not exist.

- [ ] **Step 3: Write SessionStore**

The `ActiveSessionForIp` and `HourlyLimit` branches of `SessionCreateFailure` are declared here but only produced in Task 5; keeping the enum whole avoids a signature change one task later.

`apps/RustPlusApi.CredentialsWeb/Sessions/SessionStore.cs`:

```csharp
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace RustPlusApi.CredentialsWeb.Sessions;

/// <summary>Why <see cref="SessionStore.TryCreate"/> refused.</summary>
internal enum SessionCreateFailure
{
    /// <summary>Creation succeeded.</summary>
    None,

    /// <summary>The instance-wide session cap is full.</summary>
    GlobalLimit,

    /// <summary>This address already holds a session past <see cref="SessionState.Created"/>.</summary>
    ActiveSessionForIp,

    /// <summary>This address has completed too many flows in the last hour.</summary>
    HourlyLimit
}

/// <summary>The in-memory session registry. There is no persistence anywhere in this app: a process
/// restart wipes every session by construction.</summary>
/// <param name="options">Caps and TTLs.</param>
/// <param name="timeProvider">Clock, injected so TTLs are testable.</param>
internal sealed class SessionStore(AppOptions options, TimeProvider timeProvider) : IDisposable
{
    private readonly ConcurrentDictionary<string, string> _byReturnToken = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Session> _bySessionId = new(StringComparer.Ordinal);

    /// <summary>Live sessions in any state.</summary>
    internal int Count => _bySessionId.Count;

    /// <summary>Disposes every live session.</summary>
    public void Dispose()
    {
        foreach (var sessionId in _bySessionId.Keys)
        {
            Remove(sessionId);
        }
    }

    /// <summary>Forgets a session and disposes it, invalidating its return token.</summary>
    /// <param name="sessionId">The session handle.</param>
    internal void Remove(string sessionId)
    {
        if (!_bySessionId.TryRemove(sessionId, out var session))
        {
            return;
        }

        _byReturnToken.TryRemove(session.ReturnToken, out _);
        session.Dispose();
    }

    /// <summary>Disposes every session whose expiry has passed. Returns how many were removed.</summary>
    internal int SweepExpired()
    {
        var now = timeProvider.GetUtcNow();
        var swept = 0;

        foreach (var (sessionId, session) in _bySessionId)
        {
            if (!session.IsExpired(now))
            {
                continue;
            }

            Remove(sessionId);
            swept++;
        }

        return swept;
    }

    /// <summary>Looks a session up by its return token and invalidates that token, so a callback URL
    /// replayed from browser history finds nothing.</summary>
    /// <param name="returnToken">The token from the callback path.</param>
    /// <param name="session">The owning session when the token was live.</param>
    internal bool TryConsumeReturnToken(string returnToken, [NotNullWhen(true)] out Session? session)
    {
        session = null;
        return _byReturnToken.TryRemove(returnToken, out var sessionId)
            && _bySessionId.TryGetValue(sessionId, out session);
    }

    /// <summary>Creates a session for <paramref name="clientIp"/>, or explains why it could not.</summary>
    /// <param name="clientIp">The caller's address.</param>
    /// <param name="session">The new session on success.</param>
    /// <param name="failure">Why creation was refused.</param>
    internal bool TryCreate(
        string clientIp,
        [NotNullWhen(true)] out Session? session,
        out SessionCreateFailure failure)
    {
        session = null;

        if (_bySessionId.Count >= options.MaxConcurrentSessions)
        {
            failure = SessionCreateFailure.GlobalLimit;
            return false;
        }

        var created = new Session(
            SessionIds.New(),
            SessionIds.New(),
            clientIp,
            timeProvider.GetUtcNow().Add(options.CreatedTtl));

        _bySessionId[created.SessionId] = created;
        _byReturnToken[created.ReturnToken] = created.SessionId;

        session = created;
        failure = SessionCreateFailure.None;
        return true;
    }

    /// <summary>Looks a session up by its handle. The return token is deliberately not accepted here.</summary>
    /// <param name="sessionId">The session handle.</param>
    /// <param name="session">The session when found.</param>
    internal bool TryGet(string sessionId, [NotNullWhen(true)] out Session? session) =>
        _bySessionId.TryGetValue(sessionId, out session);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests --filter "FullyQualifiedName~SessionStoreTests"`
Expected: PASS, 13 tests.

- [ ] **Step 5: Commit**

```bash
git add apps/RustPlusApi.CredentialsWeb/Sessions/SessionStore.cs tests/RustPlusApi.CredentialsWeb.UnitTests/SessionStoreTests.cs
git commit -m "feat(web): add in-memory session store with single-use return tokens"
```

---

### Task 5: Caps, per-IP accounting and the eviction rule

The bounding model. The subtle requirement is the eviction rule: a naive one-session-per-IP cap locks a visitor out of their own retry for five minutes when they close the tab and start over — which is the single most likely thing a confused first-time visitor does.

**Files:**
- Modify: `apps/RustPlusApi.CredentialsWeb/Sessions/SessionStore.cs`
- Test: `tests/RustPlusApi.CredentialsWeb.UnitTests/SessionStoreCapsTests.cs`

**Interfaces:**
- Consumes: everything from Task 4.
- Produces, added to `SessionStore`:
  - `internal void RecordCompletion(string clientIp)`
  - `internal bool TryAcquirePairingSlot()`
  - `internal void ReleasePairingSlot()`
  - `internal int ActivePairings { get; }`
  - `TryCreate` now also returns `SessionCreateFailure.ActiveSessionForIp` and `SessionCreateFailure.HourlyLimit`, and evicts a same-IP session still in `Created`.

- [ ] **Step 1: Write the failing tests**

`tests/RustPlusApi.CredentialsWeb.UnitTests/SessionStoreCapsTests.cs`:

```csharp
using Microsoft.Extensions.Time.Testing;
using RustPlusApi.CredentialsWeb;
using RustPlusApi.CredentialsWeb.Sessions;

namespace RustPlusApi.CredentialsWeb.UnitTests;

public sealed class SessionStoreCapsTests
{
    private static readonly DateTimeOffset Origin = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    private static (SessionStore Store, FakeTimeProvider Time) NewStore(Action<AppOptions>? configure = null)
    {
        var time = new FakeTimeProvider(Origin);
        var options = new AppOptions { PublicBaseUrl = "https://creds.example.org" };
        configure?.Invoke(options);
        return (new SessionStore(options, time), time);
    }

    [Fact]
    public void TryCreate_Refuses_WhenGlobalLimitReached()
    {
        var (store, _) = NewStore(o => o.MaxConcurrentSessions = 1);
        using var _s = store;
        store.TryCreate("203.0.113.7", out _, out _);

        Assert.False(store.TryCreate("203.0.113.8", out var session, out var failure));

        Assert.Null(session);
        Assert.Equal(SessionCreateFailure.GlobalLimit, failure);
    }

    [Fact]
    public void TryCreate_EvictsAbandonedCreatedSession_FromTheSameIp()
    {
        var (store, _) = NewStore();
        using var _s = store;
        store.TryCreate("203.0.113.7", out var abandoned, out _);

        Assert.True(store.TryCreate("203.0.113.7", out var replacement, out var failure));

        Assert.Equal(SessionCreateFailure.None, failure);
        Assert.NotSame(abandoned, replacement);
        Assert.False(store.TryGet(abandoned!.SessionId, out _));
        Assert.True(store.TryGet(replacement!.SessionId, out _));
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void TryCreate_Refuses_WhenTheSameIpHasAnAuthenticatedSession()
    {
        var (store, time) = NewStore();
        using var _s = store;
        store.TryCreate("203.0.113.7", out var active, out _);
        active!.Advance(SessionState.Authenticated, time.GetUtcNow().AddMinutes(15));

        Assert.False(store.TryCreate("203.0.113.7", out var session, out var failure));

        Assert.Null(session);
        Assert.Equal(SessionCreateFailure.ActiveSessionForIp, failure);
    }

    [Fact]
    public void TryCreate_IsUnaffectedByOtherAddresses()
    {
        var (store, time) = NewStore();
        using var _s = store;
        store.TryCreate("203.0.113.7", out var other, out _);
        other!.Advance(SessionState.Authenticated, time.GetUtcNow().AddMinutes(15));

        Assert.True(store.TryCreate("203.0.113.8", out _, out var failure));
        Assert.Equal(SessionCreateFailure.None, failure);
    }

    [Fact]
    public void TryCreate_Refuses_AfterTooManyCompletionsInAnHour()
    {
        var (store, _) = NewStore(o => o.MaxCompletionsPerIpPerHour = 2);
        using var _s = store;
        store.RecordCompletion("203.0.113.7");
        store.RecordCompletion("203.0.113.7");

        Assert.False(store.TryCreate("203.0.113.7", out _, out var failure));
        Assert.Equal(SessionCreateFailure.HourlyLimit, failure);
    }

    [Fact]
    public void TryCreate_Recovers_AfterTheHourlyWindowSlidesPast()
    {
        var (store, time) = NewStore(o => o.MaxCompletionsPerIpPerHour = 1);
        using var _s = store;
        store.RecordCompletion("203.0.113.7");
        Assert.False(store.TryCreate("203.0.113.7", out _, out _));

        time.Advance(TimeSpan.FromMinutes(61));

        Assert.True(store.TryCreate("203.0.113.7", out _, out var failure));
        Assert.Equal(SessionCreateFailure.None, failure);
    }

    [Fact]
    public void RecordCompletion_IsScopedToOneAddress()
    {
        var (store, _) = NewStore(o => o.MaxCompletionsPerIpPerHour = 1);
        using var _s = store;
        store.RecordCompletion("203.0.113.7");

        Assert.True(store.TryCreate("203.0.113.8", out _, out _));
    }

    [Fact]
    public void TryAcquirePairingSlot_HonoursTheGlobalPairingCap()
    {
        var (store, _) = NewStore(o => o.MaxConcurrentPairings = 2);
        using var _s = store;

        Assert.True(store.TryAcquirePairingSlot());
        Assert.True(store.TryAcquirePairingSlot());
        Assert.False(store.TryAcquirePairingSlot());
        Assert.Equal(2, store.ActivePairings);
    }

    [Fact]
    public void ReleasePairingSlot_FreesCapacity()
    {
        var (store, _) = NewStore(o => o.MaxConcurrentPairings = 1);
        using var _s = store;
        store.TryAcquirePairingSlot();

        store.ReleasePairingSlot();

        Assert.Equal(0, store.ActivePairings);
        Assert.True(store.TryAcquirePairingSlot());
    }

    [Fact]
    public void ReleasePairingSlot_NeverGoesNegative()
    {
        var (store, _) = NewStore();
        using var _s = store;

        store.ReleasePairingSlot();

        Assert.Equal(0, store.ActivePairings);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dtk dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests --filter "FullyQualifiedName~SessionStoreCapsTests"`
Expected: FAIL — `RecordCompletion`, `TryAcquirePairingSlot`, `ReleasePairingSlot` and `ActivePairings` do not exist, and the eviction/limit tests fail.

- [ ] **Step 3: Add the fields**

In `SessionStore`, add alongside the existing dictionaries:

```csharp
    private readonly ConcurrentDictionary<string, List<DateTimeOffset>> _completionsByIp =
        new(StringComparer.Ordinal);

    private readonly Lock _createGate = new();
    private int _activePairings;
```

- [ ] **Step 4: Replace TryCreate with the capped version**

The whole method runs under `_createGate` so the eviction, the counts and the insert cannot interleave. Throughput is bounded by `MaxConcurrentSessions` anyway, so a single lock costs nothing real.

```csharp
    /// <summary>Creates a session for <paramref name="clientIp"/>, or explains why it could not.
    /// A session from the same address that is still in <see cref="SessionState.Created"/> is
    /// evicted rather than blocking: a visitor who closed the tab and started over must not be
    /// locked out by their own abandoned attempt. An address holding a session past
    /// <see cref="SessionState.Created"/> is refused instead — real upstream work exists there,
    /// and that session is resumable via its handle.</summary>
    /// <param name="clientIp">The caller's address.</param>
    /// <param name="session">The new session on success.</param>
    /// <param name="failure">Why creation was refused.</param>
    internal bool TryCreate(
        string clientIp,
        [NotNullWhen(true)] out Session? session,
        out SessionCreateFailure failure)
    {
        session = null;

        lock (_createGate)
        {
            if (CountCompletions(clientIp) >= options.MaxCompletionsPerIpPerHour)
            {
                failure = SessionCreateFailure.HourlyLimit;
                return false;
            }

            foreach (var (sessionId, existing) in _bySessionId)
            {
                if (!string.Equals(existing.ClientIp, clientIp, StringComparison.Ordinal))
                {
                    continue;
                }

                if (existing.State != SessionState.Created)
                {
                    failure = SessionCreateFailure.ActiveSessionForIp;
                    return false;
                }

                Remove(sessionId);
            }

            if (_bySessionId.Count >= options.MaxConcurrentSessions)
            {
                failure = SessionCreateFailure.GlobalLimit;
                return false;
            }

            var created = new Session(
                SessionIds.New(),
                SessionIds.New(),
                clientIp,
                timeProvider.GetUtcNow().Add(options.CreatedTtl));

            _bySessionId[created.SessionId] = created;
            _byReturnToken[created.ReturnToken] = created.SessionId;

            session = created;
            failure = SessionCreateFailure.None;
            return true;
        }
    }
```

- [ ] **Step 5: Add the completion counter and pairing slots**

```csharp
    /// <summary>Live MCS sockets.</summary>
    internal int ActivePairings => Volatile.Read(ref _activePairings);

    /// <summary>Records that <paramref name="clientIp"/> finished a flow, for the rolling hourly cap.</summary>
    /// <param name="clientIp">The caller's address.</param>
    internal void RecordCompletion(string clientIp)
    {
        var stamps = _completionsByIp.GetOrAdd(clientIp, static _ => []);
        lock (stamps)
        {
            stamps.Add(timeProvider.GetUtcNow());
        }
    }

    /// <summary>Releases a pairing slot. Safe to call more often than it was acquired.</summary>
    internal void ReleasePairingSlot()
    {
        while (true)
        {
            var current = Volatile.Read(ref _activePairings);
            if (current == 0)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _activePairings, current - 1, current) == current)
            {
                return;
            }
        }
    }

    /// <summary>Takes one of the globally capped MCS socket slots.</summary>
    internal bool TryAcquirePairingSlot()
    {
        while (true)
        {
            var current = Volatile.Read(ref _activePairings);
            if (current >= options.MaxConcurrentPairings)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _activePairings, current + 1, current) == current)
            {
                return true;
            }
        }
    }

    /// <summary>Completions by this address inside the trailing hour, pruning older entries as it goes
    /// so the map cannot grow without bound.</summary>
    /// <param name="clientIp">The caller's address.</param>
    private int CountCompletions(string clientIp)
    {
        if (!_completionsByIp.TryGetValue(clientIp, out var stamps))
        {
            return 0;
        }

        var cutoff = timeProvider.GetUtcNow().AddHours(-1);
        lock (stamps)
        {
            stamps.RemoveAll(stamp => stamp < cutoff);
            if (stamps.Count == 0)
            {
                _completionsByIp.TryRemove(clientIp, out _);
            }

            return stamps.Count;
        }
    }
```

- [ ] **Step 6: Run the full store suite**

Run: `dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests --filter "FullyQualifiedName~SessionStore"`
Expected: PASS — 13 from Task 4 plus 10 here. Task 4's `TryCreate_ReturnsCreatedSessionWithDistinctIds` must still pass unchanged.

- [ ] **Step 7: Commit**

```bash
git add apps/RustPlusApi.CredentialsWeb/Sessions/SessionStore.cs tests/RustPlusApi.CredentialsWeb.UnitTests/SessionStoreCapsTests.cs
git commit -m "feat(web): bound sessions by global, per-IP and pairing-slot caps"
```

---

### Task 6: Expiry sweeper

**Files:**
- Create: `apps/RustPlusApi.CredentialsWeb/Sessions/SessionSweeper.cs`
- Test: `tests/RustPlusApi.CredentialsWeb.UnitTests/SessionSweeperTests.cs`

**Interfaces:**
- Consumes: `SessionStore` (Tasks 4-5).
- Produces: `internal sealed class SessionSweeper(SessionStore store, TimeProvider timeProvider) : BackgroundService`, ticking every 30 seconds and calling `SessionStore.SweepExpired`.

- [ ] **Step 1: Write the failing test**

`tests/RustPlusApi.CredentialsWeb.UnitTests/SessionSweeperTests.cs`:

```csharp
using Microsoft.Extensions.Time.Testing;
using RustPlusApi.CredentialsWeb;
using RustPlusApi.CredentialsWeb.Sessions;

namespace RustPlusApi.CredentialsWeb.UnitTests;

public sealed class SessionSweeperTests
{
    [Fact]
    public async Task ExecuteAsync_RemovesExpiredSessionsOnTick()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero));
        var options = new AppOptions { PublicBaseUrl = "https://creds.example.org" };
        using var store = new SessionStore(options, time);
        store.TryCreate("203.0.113.7", out var session, out _);

        using var sweeper = new SessionSweeper(store, time);
        await sweeper.StartAsync(CancellationToken.None);

        time.Advance(TimeSpan.FromMinutes(6));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (store.TryGet(session!.SessionId, out _))
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Delay(20, timeout.Token);
        }

        await sweeper.StopAsync(CancellationToken.None);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task ExecuteAsync_LeavesLiveSessionsAlone()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero));
        var options = new AppOptions { PublicBaseUrl = "https://creds.example.org" };
        using var store = new SessionStore(options, time);
        store.TryCreate("203.0.113.7", out var session, out _);

        using var sweeper = new SessionSweeper(store, time);
        await sweeper.StartAsync(CancellationToken.None);

        time.Advance(TimeSpan.FromMinutes(1));
        await Task.Delay(100);

        await sweeper.StopAsync(CancellationToken.None);
        Assert.True(store.TryGet(session!.SessionId, out _));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dtk dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests --filter "FullyQualifiedName~SessionSweeperTests"`
Expected: FAIL — `SessionSweeper` does not exist.

- [ ] **Step 3: Write SessionSweeper**

`PeriodicTimer` takes a `TimeProvider`, which is what makes this testable with `FakeTimeProvider.Advance`.

`apps/RustPlusApi.CredentialsWeb/Sessions/SessionSweeper.cs`:

```csharp
using Microsoft.Extensions.Hosting;

namespace RustPlusApi.CredentialsWeb.Sessions;

/// <summary>Disposes sessions past their TTL, which is what actually bounds how long an MCS socket
/// and a set of credentials can live in memory.</summary>
/// <param name="store">The registry to sweep.</param>
/// <param name="timeProvider">Clock, injected so the interval is testable.</param>
internal sealed class SessionSweeper(SessionStore store, TimeProvider timeProvider) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                store.SweepExpired();
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dtk dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests --filter "FullyQualifiedName~SessionSweeperTests"`
Expected: PASS, 2 tests.

- [ ] **Step 5: Commit**

```bash
git add apps/RustPlusApi.CredentialsWeb/Sessions/SessionSweeper.cs tests/RustPlusApi.CredentialsWeb.UnitTests/SessionSweeperTests.cs
git commit -m "feat(web): sweep expired sessions on a background timer"
```

---

### Task 7: Upstream seam and the registration flow

The reordered `4 → 1,2,3 → 5` sequence, behind an interface so no test ever touches Google or Facepunch.

**Files:**
- Create: `apps/RustPlusApi.CredentialsWeb/Upstream/IRegistrationSteps.cs`
- Create: `apps/RustPlusApi.CredentialsWeb/Upstream/LiveRegistrationSteps.cs`
- Create: `apps/RustPlusApi.CredentialsWeb/Flow/CredentialFlow.cs`
- Create: `tests/RustPlusApi.CredentialsWeb.UnitTests/FakeRegistrationSteps.cs`
- Test: `tests/RustPlusApi.CredentialsWeb.UnitTests/CredentialFlowTests.cs`

**Interfaces:**
- Consumes: `AppOptions` (Task 1); `SessionEvent` and payload records (Task 2); `Session`, `SessionState` (Task 3); `SessionStore` (Tasks 4-5).
- Produces:
  - `internal interface IRegistrationSteps` with `Task<Credentials> AcquireDeviceCredentialsAsync(CancellationToken)`, `Task RegisterWithCompanionAsync(string steamToken, string expoPushToken, CancellationToken)`, `Task<ServerPairing> WaitForPairingAsync(Credentials credentials, CancellationToken)`.
  - `internal sealed class LiveRegistrationSteps(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory) : IRegistrationSteps`, `[ExcludeFromCodeCoverage]`, exposing `internal const string HttpClientName = "upstream"`.
  - `internal sealed class CredentialFlow(IRegistrationSteps steps, SessionStore store, AppOptions options, TimeProvider timeProvider, ILogger<CredentialFlow> logger)` with `internal Task CompleteRegistrationAsync(Session session, SteamLoginResult login, CancellationToken cancellationToken)`.

- [ ] **Step 1: Write the test double**

`tests/RustPlusApi.CredentialsWeb.UnitTests/FakeRegistrationSteps.cs`:

```csharp
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
        Gcm = new Gcm { AndroidId = 1, SecurityToken = 2 },
        Fcm = new FcmToken { Token = "fcm-token" },
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
```

- [ ] **Step 2: Write the failing tests**

`tests/RustPlusApi.CredentialsWeb.UnitTests/CredentialFlowTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using RustPlusApi.CredentialsWeb;
using RustPlusApi.CredentialsWeb.Flow;
using RustPlusApi.CredentialsWeb.Sessions;
using RustPlusApi.Fcm.Registration;

namespace RustPlusApi.CredentialsWeb.UnitTests;

public sealed class CredentialFlowTests
{
    private static readonly DateTimeOffset Origin = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    private static SteamLoginResult Login() =>
        new() { SteamId = 76561198249527954, Token = "steam-token" };

    private sealed record Harness(
        CredentialFlow Flow,
        SessionStore Store,
        FakeRegistrationSteps Steps,
        FakeTimeProvider Time,
        AppOptions Options);

    private static Harness NewHarness()
    {
        var time = new FakeTimeProvider(Origin);
        var options = new AppOptions { PublicBaseUrl = "https://creds.example.org" };
        var store = new SessionStore(options, time);
        var steps = new FakeRegistrationSteps();
        var flow = new CredentialFlow(steps, store, options, time, NullLogger<CredentialFlow>.Instance);
        return new Harness(flow, store, steps, time, options);
    }

    /// <summary>Drains the buffered events. The stream is open-ended while the session lives, so a
    /// short window is what stops the enumeration.</summary>
    private static async Task<List<SessionEvent>> EventsOfAsync(Session session)
    {
        var received = new List<SessionEvent>();
        using var window = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        try
        {
            await foreach (var item in session.Events.SubscribeAsync(window.Token))
            {
                received.Add(item);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected: the window closed.
        }

        return received;
    }

    [Fact]
    public async Task CompleteRegistrationAsync_RunsStepsInTheReorderedSequence()
    {
        var h = NewHarness();
        using var _s = h.Store;
        h.Store.TryCreate("203.0.113.7", out var session, out _);

        await h.Flow.CompleteRegistrationAsync(session!, Login(), CancellationToken.None);

        Assert.Equal(
            [nameof(FakeRegistrationSteps.AcquireDeviceCredentialsAsync), nameof(FakeRegistrationSteps.RegisterWithCompanionAsync)],
            h.Steps.Calls);
        Assert.Equal("steam-token", h.Steps.SteamTokenSeen);
    }

    [Fact]
    public async Task CompleteRegistrationAsync_ReachesReadyAndStoresCredentials()
    {
        var h = NewHarness();
        using var _s = h.Store;
        h.Store.TryCreate("203.0.113.7", out var session, out _);

        await h.Flow.CompleteRegistrationAsync(session!, Login(), CancellationToken.None);

        Assert.Equal(SessionState.Ready, session!.State);
        Assert.NotNull(session.Credentials);
        Assert.Equal(Origin.Add(h.Options.SessionTtl), session.ExpiresAt);
    }

    [Fact]
    public async Task CompleteRegistrationAsync_DropsTheSteamTokenOnSuccess()
    {
        var h = NewHarness();
        using var _s = h.Store;
        h.Store.TryCreate("203.0.113.7", out var session, out _);

        await h.Flow.CompleteRegistrationAsync(session!, Login(), CancellationToken.None);

        Assert.Null(session!.SteamToken);
        Assert.Equal(76561198249527954UL, session.SteamId);
    }

    [Fact]
    public async Task CompleteRegistrationAsync_PublishesCredentialsWithSteamIdAsString()
    {
        var h = NewHarness();
        using var _s = h.Store;
        h.Store.TryCreate("203.0.113.7", out var session, out _);

        await h.Flow.CompleteRegistrationAsync(session!, Login(), CancellationToken.None);

        var events = await EventsOfAsync(session!);
        var payload = Assert.IsType<CredentialsPayload>(
            Assert.Single(events, e => e.Type == "credentials").Data);

        Assert.Equal("76561198249527954", payload.SteamId);
        Assert.Contains("ExponentPushToken", payload.ConfigJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteRegistrationAsync_RecordsACompletionForTheAddress()
    {
        var h = NewHarness();
        using var _s = h.Store;
        h.Options.MaxCompletionsPerIpPerHour = 1;
        h.Store.TryCreate("203.0.113.7", out var session, out _);

        await h.Flow.CompleteRegistrationAsync(session!, Login(), CancellationToken.None);
        h.Store.Remove(session!.SessionId);

        Assert.False(h.Store.TryCreate("203.0.113.7", out _, out var failure));
        Assert.Equal(SessionCreateFailure.HourlyLimit, failure);
    }

    [Fact]
    public async Task CompleteRegistrationAsync_FailsWhenDeviceRegistrationThrows()
    {
        var h = NewHarness();
        using var _s = h.Store;
        h.Steps.AcquireFailure = new HttpRequestException("upstream down");
        h.Store.TryCreate("203.0.113.7", out var session, out _);

        await h.Flow.CompleteRegistrationAsync(session!, Login(), CancellationToken.None);

        Assert.Equal(SessionState.Failed, session!.State);
        Assert.Null(session.SteamToken);

        var events = await EventsOfAsync(session);
        var error = Assert.IsType<ErrorPayload>(Assert.Single(events, e => e.Type == "error").Data);
        Assert.DoesNotContain("upstream down", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteRegistrationAsync_FailsWhenCompanionRegistrationThrows()
    {
        var h = NewHarness();
        using var _s = h.Store;
        h.Steps.CompanionFailure = new HttpRequestException("rejected");
        h.Store.TryCreate("203.0.113.7", out var session, out _);

        await h.Flow.CompleteRegistrationAsync(session!, Login(), CancellationToken.None);

        Assert.Equal(SessionState.Failed, session!.State);
        Assert.Null(session.SteamToken);
    }

    [Fact]
    public async Task CompleteRegistrationAsync_FailsWhenExpoTokenIsMissing()
    {
        var h = NewHarness();
        using var _s = h.Store;
        h.Steps.CredentialsToReturn = new RustPlusApi.Fcm.Data.Credentials { ExpoPushToken = null };
        h.Store.TryCreate("203.0.113.7", out var session, out _);

        await h.Flow.CompleteRegistrationAsync(session!, Login(), CancellationToken.None);

        Assert.Equal(SessionState.Failed, session!.State);
        Assert.DoesNotContain(nameof(FakeRegistrationSteps.RegisterWithCompanionAsync), h.Steps.Calls);
    }

    [Fact]
    public async Task CompleteRegistrationAsync_StaysQuietWhenCancelled()
    {
        var h = NewHarness();
        using var _s = h.Store;
        h.Steps.AcquireFailure = new OperationCanceledException();
        h.Store.TryCreate("203.0.113.7", out var session, out _);

        await h.Flow.CompleteRegistrationAsync(session!, Login(), CancellationToken.None);

        var events = await EventsOfAsync(session!);
        Assert.DoesNotContain(events, e => e.Type == "error");
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dtk dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests --filter "FullyQualifiedName~CredentialFlowTests"`
Expected: FAIL — `CredentialFlow` and `IRegistrationSteps` do not exist.

- [ ] **Step 4: Write the upstream seam**

`apps/RustPlusApi.CredentialsWeb/Upstream/IRegistrationSteps.cs`:

```csharp
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
```

`apps/RustPlusApi.CredentialsWeb/Upstream/LiveRegistrationSteps.cs`:

```csharp
using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Registration;
using RustPlusApi.Fcm.Registration.Steps;
using System.Diagnostics.CodeAnalysis;

namespace RustPlusApi.CredentialsWeb.Upstream;

/// <summary>The real implementation, delegating to RustPlusApi.Fcm.Registration.</summary>
/// <param name="httpClientFactory">Source of clients. A factory rather than a captured
/// <see cref="HttpClient"/> because this type is a singleton, and a singleton-held client never
/// picks up DNS changes.</param>
/// <param name="loggerFactory">Passed to <see cref="PairingListener"/> so its skip paths are visible.</param>
[ExcludeFromCodeCoverage(Justification =
    "Live-network seam: every member drives Google, Expo, Facepunch or the MCS socket and cannot be "
    + "validated offline. All logic above it is tested against IRegistrationSteps.")]
internal sealed class LiveRegistrationSteps(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : IRegistrationSteps
{
    /// <summary>Named client used for every upstream call.</summary>
    internal const string HttpClientName = "upstream";

    /// <inheritdoc/>
    public async Task<Credentials> AcquireDeviceCredentialsAsync(CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        var (gcm, fcmToken) = await new AndroidFcmRegister(client)
            .RegisterAsync(cancellationToken).ConfigureAwait(false);
        var expoToken = await new ExpoPushClient(client)
            .GetTokenAsync(fcmToken, cancellationToken).ConfigureAwait(false);

        return new Credentials
        {
            Gcm = gcm,
            Fcm = new FcmToken { Token = fcmToken },
            ExpoPushToken = expoToken
        };
    }

    /// <inheritdoc/>
    public Task RegisterWithCompanionAsync(
        string steamToken,
        string expoPushToken,
        CancellationToken cancellationToken) =>
        new RustCompanionClient(httpClientFactory.CreateClient(HttpClientName))
            .RegisterAsync(steamToken, expoPushToken, cancellationToken: cancellationToken);

    /// <inheritdoc/>
    public async Task<ServerPairing> WaitForPairingAsync(
        Credentials credentials,
        CancellationToken cancellationToken)
    {
        using var listener = new PairingListener(
            credentials,
            loggerFactory: loggerFactory,
            httpClient: httpClientFactory.CreateClient(HttpClientName));

        return await listener.WaitForServerPairingAsync(cancellationToken).ConfigureAwait(false);
    }
}
```

- [ ] **Step 5: Write CredentialFlow**

Two rules encoded here are load-bearing. The Steam token is dropped the instant step 5 returns, whether it succeeded or threw. And the `error` event carries a fixed message per step, never `ex.Message` — an upstream exception can embed response content, and that content reaches the browser.

`apps/RustPlusApi.CredentialsWeb/Flow/CredentialFlow.cs`:

```csharp
using RustPlusApi.CredentialsWeb.Sessions;
using RustPlusApi.CredentialsWeb.Upstream;
using RustPlusApi.Fcm.Registration;
using System.Globalization;

namespace RustPlusApi.CredentialsWeb.Flow;

/// <summary>Drives the credential flow in the order 4 → 1,2,3 → 5, so that a real Steam login gates
/// every upstream call. An unauthenticated visitor costs one dictionary entry and nothing else.</summary>
/// <param name="steps">The upstream seam.</param>
/// <param name="store">The session registry, for completion accounting and pairing slots.</param>
/// <param name="options">TTLs and caps.</param>
/// <param name="timeProvider">Clock.</param>
/// <param name="logger">Diagnostics. Never receives a secret.</param>
internal sealed class CredentialFlow(
    IRegistrationSteps steps,
    SessionStore store,
    AppOptions options,
    TimeProvider timeProvider,
    ILogger<CredentialFlow> logger)
{
    /// <summary>Runs steps 1-3 then 5 for a session whose Steam login has just landed.</summary>
    /// <param name="session">The session the callback belonged to.</param>
    /// <param name="login">The parsed callback result.</param>
    /// <param name="cancellationToken">Token to cancel the flow.</param>
    internal async Task CompleteRegistrationAsync(
        Session session,
        SteamLoginResult login,
        CancellationToken cancellationToken)
    {
        session.SetSteamLogin(login);
        session.Advance(SessionState.Authenticated, Deadline(options.SessionTtl));
        session.Advance(SessionState.Registering, Deadline(options.SessionTtl));

        var step = "device registration";

        try
        {
            var credentials = await steps.AcquireDeviceCredentialsAsync(cancellationToken).ConfigureAwait(false);

            // Pattern rather than string.IsNullOrEmpty so the compiler sees the non-null narrowing.
            if (credentials.ExpoPushToken is not { Length: > 0 })
            {
                throw new InvalidOperationException("Device registration returned no Expo push token.");
            }

            step = "Rust Companion registration";
            var steamToken = session.SteamToken
                ?? throw new InvalidOperationException("The session carries no Steam token.");

            await steps.RegisterWithCompanionAsync(steamToken, credentials.ExpoPushToken, cancellationToken)
                .ConfigureAwait(false);

            session.ClearSteamToken();
            session.SetCredentials(credentials);
            store.RecordCompletion(session.ClientIp);
            session.Advance(SessionState.Ready, Deadline(options.SessionTtl));

            session.Events.Publish(new SessionEvent("credentials", new CredentialsPayload(
                session.SteamId.ToString(CultureInfo.InvariantCulture),
                CredentialsStore.Serialize(credentials))));
        }
        catch (OperationCanceledException)
        {
            // The session was disposed or the host is shutting down. Nothing to report.
            session.ClearSteamToken();
        }
#pragma warning disable CA1031 // Any upstream failure must land the session in Failed rather than crash the host.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            session.ClearSteamToken();
            logger.LogError(ex, "Credential flow failed during {Step} for session {SessionId}.",
                step, session.SessionId);

            session.Advance(SessionState.Failed, Deadline(options.SessionTtl));
            session.Events.Publish(new SessionEvent("error", new ErrorPayload(
                $"Something went wrong during {step}. Start over — nothing was saved.")));
        }
    }

    /// <summary>Now plus <paramref name="ttl"/>, from the injected clock.</summary>
    /// <param name="ttl">How long the session should live from now.</param>
    private DateTimeOffset Deadline(TimeSpan ttl) => timeProvider.GetUtcNow().Add(ttl);
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dtk dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests --filter "FullyQualifiedName~CredentialFlowTests"`
Expected: PASS, 9 tests.

- [ ] **Step 7: Commit**

```bash
git add apps/RustPlusApi.CredentialsWeb/Upstream/ apps/RustPlusApi.CredentialsWeb/Flow/ tests/RustPlusApi.CredentialsWeb.UnitTests/FakeRegistrationSteps.cs tests/RustPlusApi.CredentialsWeb.UnitTests/CredentialFlowTests.cs
git commit -m "feat(web): run the credential flow Steam-first behind a testable upstream seam"
```

---

### Task 8: The pairing continuation

Step 6, the opt-in half. This is the only path that holds a socket, so it is also the only path that takes a pairing slot — and it must release that slot on every exit, including cancellation.

**Files:**
- Modify: `apps/RustPlusApi.CredentialsWeb/Flow/CredentialFlow.cs`
- Test: `tests/RustPlusApi.CredentialsWeb.UnitTests/CredentialFlowPairingTests.cs`

**Interfaces:**
- Consumes: everything from Task 7.
- Produces, added to `CredentialFlow`: `internal Task WaitForPairingAsync(Session session, CancellationToken cancellationToken)`. Assumes the caller already holds a pairing slot from `SessionStore.TryAcquirePairingSlot` and releases it before returning.

- [ ] **Step 1: Write the failing tests**

`tests/RustPlusApi.CredentialsWeb.UnitTests/CredentialFlowPairingTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using RustPlusApi.CredentialsWeb;
using RustPlusApi.CredentialsWeb.Flow;
using RustPlusApi.CredentialsWeb.Sessions;
using RustPlusApi.Fcm.Registration;

namespace RustPlusApi.CredentialsWeb.UnitTests;

public sealed class CredentialFlowPairingTests
{
    private static readonly DateTimeOffset Origin = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    private static async Task<(CredentialFlow Flow, SessionStore Store, FakeRegistrationSteps Steps, Session Session)>
        ReadySessionAsync(Action<AppOptions>? configure = null)
    {
        var time = new FakeTimeProvider(Origin);
        var options = new AppOptions { PublicBaseUrl = "https://creds.example.org" };
        configure?.Invoke(options);
        var store = new SessionStore(options, time);
        var steps = new FakeRegistrationSteps();
        var flow = new CredentialFlow(steps, store, options, time, NullLogger<CredentialFlow>.Instance);

        store.TryCreate("203.0.113.7", out var session, out _);
        await flow.CompleteRegistrationAsync(
            session!,
            new SteamLoginResult(76561198249527954, "steam-token"),
            CancellationToken.None);

        steps.Calls.Clear();
        return (flow, store, steps, session!);
    }

    /// <summary>Drains the buffered events; the short window is what ends the open-ended stream.</summary>
    private static async Task<List<SessionEvent>> EventsOfAsync(Session session)
    {
        var received = new List<SessionEvent>();
        using var window = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        try
        {
            await foreach (var item in session.Events.SubscribeAsync(window.Token))
            {
                received.Add(item);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected: the window closed.
        }

        return received;
    }

    [Fact]
    public async Task WaitForPairingAsync_ReachesPairedAndPublishesTheFourValues()
    {
        var (flow, store, _, session) = await ReadySessionAsync();
        using var _s = store;
        store.TryAcquirePairingSlot();

        await flow.WaitForPairingAsync(session, CancellationToken.None);

        Assert.Equal(SessionState.Paired, session.State);

        var events = await EventsOfAsync(session);
        var payload = Assert.IsType<PairedPayload>(Assert.Single(events, e => e.Type == "paired").Data);

        Assert.Equal("10.0.0.1", payload.Ip);
        Assert.Equal(28082, payload.Port);
        Assert.Equal("76561198249527954", payload.PlayerId);
        Assert.Equal(987654321, payload.PlayerToken);
        Assert.Equal("Test Server", payload.Name);
    }

    [Fact]
    public async Task WaitForPairingAsync_ReleasesThePairingSlotOnSuccess()
    {
        var (flow, store, _, session) = await ReadySessionAsync();
        using var _s = store;
        store.TryAcquirePairingSlot();

        await flow.WaitForPairingAsync(session, CancellationToken.None);

        Assert.Equal(0, store.ActivePairings);
    }

    [Fact]
    public async Task WaitForPairingAsync_MovesToAwaitingPairingWithThePairingTtl()
    {
        var (flow, store, steps, session) = await ReadySessionAsync(o => o.PairingTtl = TimeSpan.FromMinutes(10));
        using var _s = store;
        steps.PairingWaitsForGate = true;
        store.TryAcquirePairingSlot();

        var pending = flow.WaitForPairingAsync(session, CancellationToken.None);
        // Bounded: a regression that never reaches AwaitingPairing must fail as an assertion,
        // not park a foreground thread and hang the test process.
        using var reachTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (session.State != SessionState.AwaitingPairing)
        {
            Assert.False(reachTimeout.IsCancellationRequested, "The flow never reached AwaitingPairing.");
            await Task.Delay(10);
        }

        Assert.Equal(Origin.AddMinutes(10), session.ExpiresAt);

        steps.PairingGate.SetResult();
        await pending;
    }

    [Fact]
    public async Task WaitForPairingAsync_ReleasesTheSlotWhenTheWaitFails()
    {
        var (flow, store, steps, session) = await ReadySessionAsync();
        using var _s = store;
        steps.PairingFailure = new InvalidOperationException("socket died");
        store.TryAcquirePairingSlot();

        await flow.WaitForPairingAsync(session, CancellationToken.None);

        Assert.Equal(0, store.ActivePairings);
        Assert.Equal(SessionState.Failed, session.State);

        var events = await EventsOfAsync(session);
        var error = Assert.IsType<ErrorPayload>(Assert.Single(events, e => e.Type == "error").Data);
        Assert.DoesNotContain("socket died", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WaitForPairingAsync_ReturnsToReadyAndReleasesTheSlotWhenCancelled()
    {
        var (flow, store, steps, session) = await ReadySessionAsync();
        using var _s = store;
        steps.PairingWaitsForGate = true;
        store.TryAcquirePairingSlot();

        using var cancellation = new CancellationTokenSource();
        var pending = flow.WaitForPairingAsync(session, cancellation.Token);
        // Bounded: a regression that never reaches AwaitingPairing must fail as an assertion,
        // not park a foreground thread and hang the test process.
        using var reachTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (session.State != SessionState.AwaitingPairing)
        {
            Assert.False(reachTimeout.IsCancellationRequested, "The flow never reached AwaitingPairing.");
            await Task.Delay(10);
        }

        await cancellation.CancelAsync();
        await pending;

        Assert.Equal(0, store.ActivePairings);
        Assert.Equal(SessionState.Ready, session.State);

        var events = await EventsOfAsync(session);
        Assert.Contains(events, e => e.Type == "expired");
    }

    [Fact]
    public async Task WaitForPairingAsync_CanBeRetriedAfterATimeout()
    {
        var (flow, store, steps, session) = await ReadySessionAsync();
        using var _s = store;
        steps.PairingWaitsForGate = true;
        store.TryAcquirePairingSlot();

        using var cancellation = new CancellationTokenSource();
        var pending = flow.WaitForPairingAsync(session, cancellation.Token);
        // Bounded: a regression that never reaches AwaitingPairing must fail as an assertion,
        // not park a foreground thread and hang the test process.
        using var reachTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (session.State != SessionState.AwaitingPairing)
        {
            Assert.False(reachTimeout.IsCancellationRequested, "The flow never reached AwaitingPairing.");
            await Task.Delay(10);
        }

        await cancellation.CancelAsync();
        await pending;

        // The second attempt must be accepted: this is what the "retry without redoing the Steam
        // login" promise actually depends on.
        steps.PairingWaitsForGate = false;
        Assert.True(store.TryAcquirePairingSlot());
        await flow.WaitForPairingAsync(session, CancellationToken.None);

        Assert.Equal(SessionState.Paired, session.State);
    }

    [Fact]
    public async Task WaitForPairingAsync_RefusesWhenTheSessionIsNotReady()
    {
        var (flow, store, steps, session) = await ReadySessionAsync();
        using var _s = store;
        session.Advance(SessionState.Failed, Origin.AddMinutes(15));
        store.TryAcquirePairingSlot();

        await flow.WaitForPairingAsync(session, CancellationToken.None);

        Assert.Empty(steps.Calls);
        Assert.Equal(0, store.ActivePairings);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dtk dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests --filter "FullyQualifiedName~CredentialFlowPairingTests"`
Expected: FAIL — `CredentialFlow.WaitForPairingAsync` does not exist.

- [ ] **Step 3: Add WaitForPairingAsync to CredentialFlow**

The `finally` is the point of the method: a slot leaked here permanently shrinks the instance's pairing capacity, and nothing would ever report it.

```csharp
    /// <summary>Step 6: hold an MCS socket until a pairing push arrives, the TTL runs out, or the
    /// session is disposed. The caller must already hold a pairing slot; this method always
    /// releases it.</summary>
    /// <param name="session">A session in <see cref="SessionState.Ready"/>.</param>
    /// <param name="cancellationToken">Token to abandon the wait.</param>
    internal async Task WaitForPairingAsync(Session session, CancellationToken cancellationToken)
    {
        if (session.State != SessionState.Ready || session.Credentials is not { } credentials)
        {
            store.ReleasePairingSlot();
            return;
        }

        session.Advance(SessionState.AwaitingPairing, Deadline(options.PairingTtl));

        try
        {
            var pairing = await steps.WaitForPairingAsync(credentials, cancellationToken).ConfigureAwait(false);

            session.SetPairing(pairing);
            session.Advance(SessionState.Paired, Deadline(options.SessionTtl));
            session.Events.Publish(new SessionEvent("paired", new PairedPayload(
                pairing.Ip,
                pairing.Port,
                pairing.PlayerId.ToString(CultureInfo.InvariantCulture),
                pairing.PlayerToken,
                pairing.Name)));
        }
        catch (OperationCanceledException)
        {
            // Back to Ready, not a terminal state: the credentials are still good, so the visitor
            // can start another pairing wait without repeating the Steam login.
            session.Advance(SessionState.Ready, Deadline(options.SessionTtl));
            session.Events.Publish(new SessionEvent("expired", null));
        }
#pragma warning disable CA1031 // A failed socket must land the session in Failed rather than crash the host.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogError(ex, "Pairing wait failed for session {SessionId}.", session.SessionId);
            session.Advance(SessionState.Failed, Deadline(options.SessionTtl));
            session.Events.Publish(new SessionEvent("error", new ErrorPayload(
                "The pairing listener stopped unexpectedly. Your credentials are still valid — "
                + "try the pairing step again.")));
        }
        finally
        {
            // A leaked slot permanently shrinks this instance's pairing capacity and nothing reports it.
            store.ReleasePairingSlot();
        }
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dtk dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests --filter "FullyQualifiedName~CredentialFlowPairing"`
Expected: PASS, 6 tests.

- [ ] **Step 5: Run the whole suite so far**

Run: `dtk dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests`
Expected: PASS, all tests from Tasks 1-8.

- [ ] **Step 6: Commit**

```bash
git add apps/RustPlusApi.CredentialsWeb/Flow/CredentialFlow.cs tests/RustPlusApi.CredentialsWeb.UnitTests/CredentialFlowPairingTests.cs
git commit -m "feat(web): add the opt-in pairing continuation with slot accounting"
```

---

### Task 9: Session creation endpoint and host wiring

The first endpoint, plus the DI graph everything else hangs off. This is also where the reverse-proxy trap gets closed: without `ForwardedHeaders`, every visitor behind a proxy presents as the proxy and shares one per-IP bucket.

**Files:**
- Modify: `apps/RustPlusApi.CredentialsWeb/Program.cs`
- Create: `apps/RustPlusApi.CredentialsWeb/Endpoints/SessionEndpoints.cs`
- Create: `apps/RustPlusApi.CredentialsWeb/Endpoints/ClientAddress.cs`
- Create: `tests/RustPlusApi.CredentialsWeb.UnitTests/CredentialsWebFactory.cs`
- Create: `tests/RustPlusApi.CredentialsWeb.UnitTests/AssemblyInfo.cs`
- Test: `tests/RustPlusApi.CredentialsWeb.UnitTests/SessionEndpointTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1-8.
- Produces:
  - `internal sealed record CreateSessionResponse(string SessionId, string LoginUrl)` — serialized as `{"sessionId":…,"loginUrl":…}`.
  - `internal static class ClientAddress` with `internal static string Of(HttpContext context)`.
  - `internal static class SessionEndpoints` with `internal static void MapSessionEndpoints(this IEndpointRouteBuilder app)`.
  - Test harness `CredentialsWebFactory : WebApplicationFactory<Program>` exposing `Steps` (a `FakeRegistrationSteps`) and `Time` (a `FakeTimeProvider`).

- [ ] **Step 1: Write the test harness**

Configuration is read in `Program.cs` *before* `builder.Build()`, and `WebApplicationFactory`'s configuration hooks only apply during `Build()`. Environment variables are therefore the only way to inject settings that the pre-`Build()` validation will see.

`tests/RustPlusApi.CredentialsWeb.UnitTests/CredentialsWebFactory.cs`:

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using RustPlusApi.CredentialsWeb.Upstream;

namespace RustPlusApi.CredentialsWeb.UnitTests;

/// <summary>Boots the real app with the upstream seam and the clock replaced.</summary>
internal sealed class CredentialsWebFactory : WebApplicationFactory<Program>
{
    internal const string BaseUrl = "https://creds.example.org";

    private static readonly DateTimeOffset Origin = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    internal CredentialsWebFactory(IDictionary<string, string>? settings = null)
    {
        // Program.cs binds configuration before builder.Build(), which is earlier than any
        // WebApplicationFactory configuration hook runs — so these must be environment variables.
        SetEnvironment("CredentialsWeb__PublicBaseUrl", BaseUrl);
        foreach (var (key, value) in settings ?? new Dictionary<string, string>())
        {
            SetEnvironment(key, value);
        }
    }

    internal FakeRegistrationSteps Steps { get; } = new();

    internal FakeTimeProvider Time { get; } = new(Origin);

    private List<string> EnvironmentKeys { get; } = [];

    /// <inheritdoc/>
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IRegistrationSteps>();
            services.AddSingleton<IRegistrationSteps>(Steps);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
        });

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var key in EnvironmentKeys)
            {
                Environment.SetEnvironmentVariable(key, null);
            }
        }

        base.Dispose(disposing);
    }

    private void SetEnvironment(string key, string value)
    {
        Environment.SetEnvironmentVariable(key, value);
        EnvironmentKeys.Add(key);
    }
}
```

- [ ] **Step 1b: Disable test parallelization**

`CredentialsWebFactory` sets **process-global** environment variables, because that is the only
channel `Program.cs` reads before `builder.Build()`. xUnit runs test classes in parallel by
default, so two factories with different settings would clobber each other and the endpoint tests
would fail intermittently and unreproducibly. Serialize the assembly.

`tests/RustPlusApi.CredentialsWeb.UnitTests/AssemblyInfo.cs`:

```csharp
using Xunit;

// CredentialsWebFactory configures the app through process-global environment variables — the only
// channel Program.cs reads before builder.Build(), which is earlier than any WebApplicationFactory
// hook runs. Parallel classes would overwrite each other's settings, so the assembly runs serially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
```

- [ ] **Step 2: Write the failing tests**

`tests/RustPlusApi.CredentialsWeb.UnitTests/SessionEndpointTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using RustPlusApi.CredentialsWeb.Endpoints;
using RustPlusApi.CredentialsWeb.Sessions;
using System.Net;
using System.Net.Http.Json;

namespace RustPlusApi.CredentialsWeb.UnitTests;

public sealed class SessionEndpointTests
{
    [Fact]
    public async Task CreateSession_ReturnsSessionIdAndFacepunchLoginUrl()
    {
        using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync(new Uri("/api/sessions", UriKind.Relative), null);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CreateSessionResponse>();

        Assert.NotNull(body);
        Assert.Matches("^[0-9a-f]{32}$", body.SessionId);
        Assert.StartsWith(
            "https://companion-rust.facepunch.com/login?returnUrl=",
            body.LoginUrl,
            StringComparison.Ordinal);
        Assert.Contains(
            Uri.EscapeDataString($"{CredentialsWebFactory.BaseUrl}/callback/"),
            body.LoginUrl,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateSession_ReturnsADifferentReturnTokenEachTime()
    {
        using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();

        var first = await client.PostAsync(new Uri("/api/sessions", UriKind.Relative), null);
        var firstBody = await first.Content.ReadFromJsonAsync<CreateSessionResponse>();
        var second = await client.PostAsync(new Uri("/api/sessions", UriKind.Relative), null);
        var secondBody = await second.Content.ReadFromJsonAsync<CreateSessionResponse>();

        Assert.NotEqual(firstBody!.LoginUrl, secondBody!.LoginUrl);
    }

    [Fact]
    public async Task CreateSession_TheLoginUrlNeverCarriesTheSessionId()
    {
        using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync(new Uri("/api/sessions", UriKind.Relative), null);
        var body = await response.Content.ReadFromJsonAsync<CreateSessionResponse>();

        Assert.DoesNotContain(body!.SessionId, body.LoginUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateSession_EvictsThisAddressesAbandonedCreatedSession()
    {
        using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<SessionStore>();

        var first = await client.PostAsync(new Uri("/api/sessions", UriKind.Relative), null);
        var firstBody = await first.Content.ReadFromJsonAsync<CreateSessionResponse>();

        var second = await client.PostAsync(new Uri("/api/sessions", UriKind.Relative), null);
        second.EnsureSuccessStatusCode();

        Assert.False(store.TryGet(firstBody!.SessionId, out _));
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public async Task CreateSession_Returns429WithASelfHostPointer_WhenTheGlobalCapIsFull()
    {
        using var factory = new CredentialsWebFactory(
            new Dictionary<string, string> { ["CredentialsWeb__MaxConcurrentSessions"] = "1" });
        using var client = factory.CreateClient();

        // Occupy the only slot from a different address, and move it out of Created so the
        // eviction rule cannot reclaim it.
        var store = factory.Services.GetRequiredService<SessionStore>();
        store.TryCreate("198.51.100.1", out var occupant, out _);
        occupant!.Advance(SessionState.Authenticated, factory.Time.GetUtcNow().AddMinutes(15));

        var response = await client.PostAsync(new Uri("/api/sessions", UriKind.Relative), null);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("docker run", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Responses_CarryTheSecurityHeaders()
    {
        using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync(new Uri("/api/sessions", UriKind.Relative), null);

        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dtk dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests --filter "FullyQualifiedName~SessionEndpointTests"`
Expected: FAIL — 404 for `/api/sessions`, and `CreateSessionResponse` does not exist.

- [ ] **Step 4: Write ClientAddress**

`apps/RustPlusApi.CredentialsWeb/Endpoints/ClientAddress.cs`:

```csharp
namespace RustPlusApi.CredentialsWeb.Endpoints;

/// <summary>Resolves the caller's address for per-IP accounting.</summary>
internal static class ClientAddress
{
    /// <summary>The remote address, already rewritten by the forwarded-headers middleware when the
    /// instance is configured with known proxies. Falls back to a constant so accounting still
    /// happens (conservatively, as one shared bucket) rather than silently disappearing.</summary>
    /// <param name="context">The current request.</param>
    internal static string Of(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
```

- [ ] **Step 5: Write SessionEndpoints**

`apps/RustPlusApi.CredentialsWeb/Endpoints/SessionEndpoints.cs`:

```csharp
using RustPlusApi.CredentialsWeb.Sessions;
using RustPlusApi.Fcm.Registration.Steps;

namespace RustPlusApi.CredentialsWeb.Endpoints;

/// <summary>What the browser needs to start a flow.</summary>
/// <param name="SessionId">The handle for the event stream and follow-up calls.</param>
/// <param name="LoginUrl">The Facepunch login URL to send the visitor to.</param>
internal sealed record CreateSessionResponse(string SessionId, string LoginUrl);

/// <summary>Session lifecycle endpoints.</summary>
internal static class SessionEndpoints
{
    private const string OverCapacityMessage =
        "This instance is at capacity. Try again in a few minutes — or run your own: "
        + "docker run -p 8080:8080 ghcr.io/handys11/rustplusapi-credentials";

    /// <summary>Maps <c>POST /api/sessions</c>.</summary>
    /// <param name="app">The route builder.</param>
    internal static void MapSessionEndpoints(this IEndpointRouteBuilder app) =>
        app.MapPost("/api/sessions", (HttpContext context, SessionStore store, AppOptions options) =>
        {
            if (!store.TryCreate(ClientAddress.Of(context), out var session, out _))
            {
                return Results.Json(new ErrorPayload(OverCapacityMessage), statusCode: 429);
            }

            var returnUrl = $"{options.PublicBaseUrl}/callback/{session.ReturnToken}";
            return Results.Ok(new CreateSessionResponse(
                session.SessionId,
                SteamLoginService.BuildLoginUrl(returnUrl)));
        });
}
```

- [ ] **Step 6: Write the security headers middleware**

`apps/RustPlusApi.CredentialsWeb/Security/SecurityHeaders.cs`:

```csharp
namespace RustPlusApi.CredentialsWeb.Security;

/// <summary>Response headers that back the page's trust claims: no referrer leakage, no caching of
/// anything that carries a credential, and a content policy admitting no third-party origin — which
/// is also what keeps the client a single auditable file.</summary>
internal static class SecurityHeaders
{
    private const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; "
        + "connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    /// <summary>Adds the headers to every response.</summary>
    /// <param name="app">The application pipeline.</param>
    internal static void UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.ContentSecurityPolicy = ContentSecurityPolicy;
                headers["Referrer-Policy"] = "no-referrer";
                headers.XContentTypeOptions = "nosniff";
                headers.CacheControl = "no-store";
                return Task.CompletedTask;
            });

            await next(context).ConfigureAwait(false);
        });
}
```

- [ ] **Step 7: Wire up Program.cs**

Replace the body of `Program.cs` between `builder.Services.AddSingleton(options);` and `await app.RunAsync();`:

```csharp
builder.Services.AddSingleton(options);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<SessionStore>();
builder.Services.AddSingleton<CredentialFlow>();
builder.Services.AddHostedService<SessionSweeper>();
builder.Services.AddHttpClient(LiveRegistrationSteps.HttpClientName);
builder.Services.AddSingleton<IRegistrationSteps>(serviceProvider => new LiveRegistrationSteps(
    serviceProvider.GetRequiredService<IHttpClientFactory>(),
    serviceProvider.GetRequiredService<ILoggerFactory>()));

// Without this, every visitor behind a reverse proxy presents as the proxy and shares one per-IP
// bucket, silently voiding the caps. Configured too loosely it is worse: trusting X-Forwarded-For
// from anyone lets a caller spoof their way past the limits. So the operator must name their proxy
// explicitly, and with none named the headers are ignored.
builder.Services.Configure<ForwardedHeadersOptions>(forwarded =>
{
    forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var proxy in options.KnownProxies)
    {
        forwarded.KnownProxies.Add(IPAddress.Parse(proxy));
    }
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseSecurityHeaders();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapSessionEndpoints();

await app.RunAsync();
return 0;
```

Add the usings at the top of `Program.cs`:

```csharp
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
using RustPlusApi.CredentialsWeb;
using RustPlusApi.CredentialsWeb.Endpoints;
using RustPlusApi.CredentialsWeb.Flow;
using RustPlusApi.CredentialsWeb.Security;
using RustPlusApi.CredentialsWeb.Sessions;
using RustPlusApi.CredentialsWeb.Upstream;
using System.Diagnostics.CodeAnalysis;
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dtk dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests --filter "FullyQualifiedName~SessionEndpointTests"`
Expected: PASS, 6 tests.

- [ ] **Step 9: Commit**

```bash
git add apps/RustPlusApi.CredentialsWeb/ tests/RustPlusApi.CredentialsWeb.UnitTests/
git commit -m "feat(web): add session creation endpoint, DI wiring and security headers"
```

---

### Task 10: The Facepunch callback endpoint

The security-critical route. Three things must hold: the response is a 302 so no history entry carries the token, the redirect target puts the session handle in a fragment so it never reaches a server log, and a replayed callback URL finds nothing.

**Files:**
- Create: `apps/RustPlusApi.CredentialsWeb/Endpoints/CallbackEndpoints.cs`
- Modify: `apps/RustPlusApi.CredentialsWeb/Program.cs`
- Test: `tests/RustPlusApi.CredentialsWeb.UnitTests/CallbackEndpointTests.cs`

**Interfaces:**
- Consumes: `SessionStore`, `CredentialFlow`, `AppOptions`.
- Produces: `internal static class CallbackEndpoints` with `internal static void MapCallbackEndpoints(this IEndpointRouteBuilder app)`.

- [ ] **Step 1: Write the failing tests**

`tests/RustPlusApi.CredentialsWeb.UnitTests/CallbackEndpointTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using RustPlusApi.CredentialsWeb.Sessions;

namespace RustPlusApi.CredentialsWeb.UnitTests;

public sealed class CallbackEndpointTests
{
    private static HttpClient NoRedirectClient(CredentialsWebFactory factory) =>
        factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

    private static Session NewSession(CredentialsWebFactory factory)
    {
        var store = factory.Services.GetRequiredService<SessionStore>();
        store.TryCreate("203.0.113.7", out var session, out _);
        return session!;
    }

    private static Uri CallbackUri(string returnToken) =>
        new($"/callback/{returnToken}?steamId=76561198249527954&token=steam-token", UriKind.Relative);

    [Fact]
    public async Task Callback_Redirects302ToTheFragmentCarryingTheSessionId()
    {
        using var factory = new CredentialsWebFactory();
        using var client = NoRedirectClient(factory);
        var session = NewSession(factory);

        var response = await client.GetAsync(CallbackUri(session.ReturnToken));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal($"/#session={session.SessionId}", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Callback_DrivesTheFlowToReady()
    {
        using var factory = new CredentialsWebFactory();
        using var client = NoRedirectClient(factory);
        var session = NewSession(factory);

        await client.GetAsync(CallbackUri(session.ReturnToken));
        await session.BackgroundWork;

        Assert.Equal(SessionState.Ready, session.State);
        Assert.Equal("steam-token", factory.Steps.SteamTokenSeen);
        Assert.Null(session.SteamToken);
    }

    [Fact]
    public async Task Callback_Returns404_ForAnUnknownReturnToken()
    {
        using var factory = new CredentialsWebFactory();
        using var client = NoRedirectClient(factory);

        var response = await client.GetAsync(CallbackUri("0123456789abcdef0123456789abcdef"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Callback_Returns404_WhenReplayed()
    {
        using var factory = new CredentialsWebFactory();
        using var client = NoRedirectClient(factory);
        var session = NewSession(factory);

        var first = await client.GetAsync(CallbackUri(session.ReturnToken));
        var second = await client.GetAsync(CallbackUri(session.ReturnToken));

        Assert.Equal(HttpStatusCode.Found, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
    }

    [Fact]
    public async Task Callback_WithNoToken_FailsTheSessionButStillRedirects()
    {
        using var factory = new CredentialsWebFactory();
        using var client = NoRedirectClient(factory);
        var session = NewSession(factory);

        var response = await client.GetAsync(
            new Uri($"/callback/{session.ReturnToken}?steamId=76561198249527954", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal($"/#session={session.SessionId}", response.Headers.Location!.ToString());
        Assert.Equal(SessionState.Failed, session.State);
        Assert.Empty(factory.Steps.Calls);
    }

    [Fact]
    public async Task Callback_WithANonNumericSteamId_FailsTheSession()
    {
        using var factory = new CredentialsWebFactory();
        using var client = NoRedirectClient(factory);
        var session = NewSession(factory);

        await client.GetAsync(
            new Uri($"/callback/{session.ReturnToken}?steamId=not-a-number&token=steam-token", UriKind.Relative));

        Assert.Equal(SessionState.Failed, session.State);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dtk dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests --filter "FullyQualifiedName~CallbackEndpointTests"`
Expected: FAIL — 404 for every case, because the route does not exist.

- [ ] **Step 3: Write CallbackEndpoints**

`apps/RustPlusApi.CredentialsWeb/Endpoints/CallbackEndpoints.cs`:

```csharp
using RustPlusApi.CredentialsWeb.Flow;
using RustPlusApi.CredentialsWeb.Sessions;
using RustPlusApi.Fcm.Registration.Steps;

namespace RustPlusApi.CredentialsWeb.Endpoints;

/// <summary>The Facepunch redirect target.</summary>
internal static class CallbackEndpoints
{
    /// <summary>Maps <c>GET /callback/{returnToken}</c>.</summary>
    /// <param name="app">The route builder.</param>
    internal static void MapCallbackEndpoints(this IEndpointRouteBuilder app) =>
        app.MapGet("/callback/{returnToken}", (
            string returnToken,
            HttpContext context,
            SessionStore store,
            CredentialFlow flow,
            AppOptions options) =>
        {
            // Single-use: a callback URL replayed from browser history finds nothing, and an
            // unknown token is indistinguishable from a consumed one.
            if (!store.TryConsumeReturnToken(returnToken, out var session))
            {
                return Results.NotFound();
            }

            // 302 rather than 200: a redirect leaves no back-button entry, so the token-bearing URL
            // never becomes one. The session handle rides in the fragment, which browsers never send
            // to a server and which therefore cannot reach an access log or a Referer header.
            var redirect = Results.Redirect($"/#session={session.SessionId}");

            try
            {
                var callbackUri = new Uri(
                    options.PublicBaseUrl + context.Request.Path + context.Request.QueryString);
                var login = SteamLoginService.ParseCallback(callbackUri);

                session.BackgroundWork = flow.CompleteRegistrationAsync(
                    session,
                    login,
                    session.Lifetime.Token);
            }
            catch (InvalidOperationException)
            {
                // ParseCallback rejects a callback with no usable token or steamId. The message is
                // not surfaced: the page says the login did not complete and offers a restart.
                session.Advance(SessionState.Failed, session.ExpiresAt);
                session.Events.Publish(new SessionEvent("error", new ErrorPayload(
                    "The Steam login didn't complete. Start over — nothing was saved.")));
            }

            return redirect;
        });
}
```

- [ ] **Step 4: Map it in Program.cs**

Add below `app.MapSessionEndpoints();`:

```csharp
app.MapCallbackEndpoints();
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dtk dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests --filter "FullyQualifiedName~CallbackEndpointTests"`
Expected: PASS, 6 tests.

- [ ] **Step 6: Commit**

```bash
git add apps/RustPlusApi.CredentialsWeb/Endpoints/CallbackEndpoints.cs apps/RustPlusApi.CredentialsWeb/Program.cs tests/RustPlusApi.CredentialsWeb.UnitTests/CallbackEndpointTests.cs
git commit -m "feat(web): handle the Facepunch callback with single-use tokens and a 302"
```

---

### Task 11: The SSE endpoint

**Files:**
- Create: `apps/RustPlusApi.CredentialsWeb/Endpoints/EventEndpoints.cs`
- Modify: `apps/RustPlusApi.CredentialsWeb/Program.cs`
- Test: `tests/RustPlusApi.CredentialsWeb.UnitTests/EventEndpointTests.cs`

**Interfaces:**
- Consumes: `SessionStore`, `Session.Events`.
- Produces: `internal static class EventEndpoints` with `internal static void MapEventEndpoints(this IEndpointRouteBuilder app)`. Wire format is standard SSE: `event: <type>\ndata: <json>\n\n`, camelCase JSON, `{}` when an event carries no payload.

- [ ] **Step 1: Write the failing tests**

`tests/RustPlusApi.CredentialsWeb.UnitTests/EventEndpointTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using RustPlusApi.CredentialsWeb.Sessions;
using System.Net;

namespace RustPlusApi.CredentialsWeb.UnitTests;

public sealed class EventEndpointTests
{
    /// <summary>Reads SSE frames until <paramref name="count"/> <c>event:</c> lines have arrived.</summary>
    private static async Task<List<string>> ReadEventNamesAsync(HttpClient client, string sessionId, int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var response = await client.GetAsync(
            new Uri($"/api/sessions/{sessionId}/events", UriKind.Relative),
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);

        response.EnsureSuccessStatusCode();

        var names = new List<string>();
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var reader = new StreamReader(stream);

        while (names.Count < count)
        {
            var line = await reader.ReadLineAsync(timeout.Token);
            if (line is null)
            {
                break;
            }

            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                names.Add(line["event: ".Length..]);
            }
        }

        return names;
    }

    private static Session NewSession(CredentialsWebFactory factory)
    {
        var store = factory.Services.GetRequiredService<SessionStore>();
        store.TryCreate("203.0.113.7", out var session, out _);
        return session!;
    }

    [Fact]
    public async Task Events_Returns404_ForAnUnknownSession()
    {
        using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            new Uri("/api/sessions/0123456789abcdef0123456789abcdef/events", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Events_UsesTheEventStreamContentType()
    {
        using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();
        var session = NewSession(factory);
        session.Advance(SessionState.Authenticated, DateTimeOffset.MaxValue);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var response = await client.GetAsync(
            new Uri($"/api/sessions/{session.SessionId}/events", UriKind.Relative),
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);

        Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Events_ReplaysEventsPublishedBeforeTheStreamOpened()
    {
        using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();
        var session = NewSession(factory);
        session.Advance(SessionState.Authenticated, DateTimeOffset.MaxValue);
        session.Advance(SessionState.Registering, DateTimeOffset.MaxValue);

        var names = await ReadEventNamesAsync(client, session.SessionId, 2);

        Assert.Equal(["step", "step"], names);
    }

    [Fact]
    public async Task Events_ReplaysTheSameHistoryToAReconnectingClient()
    {
        using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();
        var session = NewSession(factory);
        session.Advance(SessionState.Authenticated, DateTimeOffset.MaxValue);

        var first = await ReadEventNamesAsync(client, session.SessionId, 1);
        var second = await ReadEventNamesAsync(client, session.SessionId, 1);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Events_CarriesTheJsonPayload()
    {
        using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();
        var session = NewSession(factory);
        session.Advance(SessionState.Registering, DateTimeOffset.MaxValue);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var response = await client.GetAsync(
            new Uri($"/api/sessions/{session.SessionId}/events", UriKind.Relative),
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var reader = new StreamReader(stream);

        await reader.ReadLineAsync(timeout.Token);
        var dataLine = await reader.ReadLineAsync(timeout.Token);

        Assert.Equal("""data: {"state":"Registering"}""", dataLine);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dtk dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests --filter "FullyQualifiedName~EventEndpointTests"`
Expected: FAIL — the route does not exist.

- [ ] **Step 3: Write EventEndpoints**

`apps/RustPlusApi.CredentialsWeb/Endpoints/EventEndpoints.cs`:

```csharp
using RustPlusApi.CredentialsWeb.Sessions;
using System.Text.Json;

namespace RustPlusApi.CredentialsWeb.Endpoints;

/// <summary>The server-to-client push channel. One-directional by design: the only thing the server
/// ever tells the browser is where the flow has got to.</summary>
internal static class EventEndpoints
{
    /// <summary>Maps <c>GET /api/sessions/{sessionId}/events</c>.</summary>
    /// <param name="app">The route builder.</param>
    internal static void MapEventEndpoints(this IEndpointRouteBuilder app) =>
        app.MapGet("/api/sessions/{sessionId}/events", async (
            string sessionId,
            HttpContext context,
            SessionStore store,
            CancellationToken cancellationToken) =>
        {
            if (!store.TryGet(sessionId, out var session))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Response.ContentType = "text/event-stream";
            // Tells nginx not to buffer the stream; without it a proxy can hold events for minutes.
            context.Response.Headers["X-Accel-Buffering"] = "no";
            await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);

            await foreach (var sessionEvent in session.Events
                               .SubscribeAsync(cancellationToken)
                               .ConfigureAwait(false))
            {
                var json = sessionEvent.Data is null
                    ? "{}"
                    : JsonSerializer.Serialize(sessionEvent.Data, JsonSerializerOptions.Web);

                await context.Response
                    .WriteAsync($"event: {sessionEvent.Type}\ndata: {json}\n\n", cancellationToken)
                    .ConfigureAwait(false);
                await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        });
}
```

- [ ] **Step 4: Map it in Program.cs**

Add below `app.MapCallbackEndpoints();`:

```csharp
app.MapEventEndpoints();
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dtk dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests --filter "FullyQualifiedName~EventEndpointTests"`
Expected: PASS, 5 tests.

- [ ] **Step 6: Commit**

```bash
git add apps/RustPlusApi.CredentialsWeb/Endpoints/EventEndpoints.cs apps/RustPlusApi.CredentialsWeb/Program.cs tests/RustPlusApi.CredentialsWeb.UnitTests/EventEndpointTests.cs
git commit -m "feat(web): stream session progress over server-sent events"
```

---

### Task 12: The pairing endpoint

**Files:**
- Modify: `apps/RustPlusApi.CredentialsWeb/Endpoints/SessionEndpoints.cs`
- Test: `tests/RustPlusApi.CredentialsWeb.UnitTests/PairingEndpointTests.cs`

**Interfaces:**
- Consumes: `SessionStore.TryAcquirePairingSlot`, `CredentialFlow.WaitForPairingAsync`.
- Produces: `POST /api/sessions/{sessionId}/pairing` → 202 when started, 404 unknown session, 409 when the session is not `Ready`, 429 when the pairing cap is full.

- [ ] **Step 1: Write the failing tests**

`tests/RustPlusApi.CredentialsWeb.UnitTests/PairingEndpointTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using RustPlusApi.CredentialsWeb.Sessions;
using System.Net;

namespace RustPlusApi.CredentialsWeb.UnitTests;

public sealed class PairingEndpointTests
{
    private static Uri PairingUri(string sessionId) =>
        new($"/api/sessions/{sessionId}/pairing", UriKind.Relative);

    /// <summary>Runs a session all the way to Ready through the real callback route.</summary>
    private static async Task<Session> ReadySessionAsync(CredentialsWebFactory factory)
    {
        using var client = factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var store = factory.Services.GetRequiredService<SessionStore>();
        store.TryCreate("203.0.113.7", out var session, out _);

        await client.GetAsync(new Uri(
            $"/callback/{session!.ReturnToken}?steamId=76561198249527954&token=steam-token",
            UriKind.Relative));
        await session.BackgroundWork;

        factory.Steps.Calls.Clear();
        return session;
    }

    [Fact]
    public async Task Pairing_Returns404_ForAnUnknownSession()
    {
        using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync(PairingUri("0123456789abcdef0123456789abcdef"), null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Pairing_Returns409_WhenTheSessionIsNotReady()
    {
        using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<SessionStore>();
        store.TryCreate("203.0.113.7", out var session, out _);

        var response = await client.PostAsync(PairingUri(session!.SessionId), null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Pairing_Returns202AndStartsTheWait()
    {
        using var factory = new CredentialsWebFactory();
        factory.Steps.PairingWaitsForGate = true;
        var session = await ReadySessionAsync(factory);
        using var client = factory.CreateClient();

        var response = await client.PostAsync(PairingUri(session.SessionId), null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        // Bounded: a regression that never reaches AwaitingPairing must fail as an assertion,
        // not park a foreground thread and hang the test process.
        using var reachTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (session.State != SessionState.AwaitingPairing)
        {
            Assert.False(reachTimeout.IsCancellationRequested, "The flow never reached AwaitingPairing.");
            await Task.Delay(10);
        }

        factory.Steps.PairingGate.SetResult();
        await session.BackgroundWork;
        Assert.Equal(SessionState.Paired, session.State);
    }

    [Fact]
    public async Task Pairing_Returns429_WhenThePairingCapIsFull()
    {
        using var factory = new CredentialsWebFactory(
            new Dictionary<string, string> { ["CredentialsWeb__MaxConcurrentPairings"] = "1" });
        var session = await ReadySessionAsync(factory);
        using var client = factory.CreateClient();

        var store = factory.Services.GetRequiredService<SessionStore>();
        store.TryAcquirePairingSlot();

        var response = await client.PostAsync(PairingUri(session.SessionId), null);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Empty(factory.Steps.Calls);
    }

    [Fact]
    public async Task Pairing_ReleasesTheSlotOnceTheWaitFinishes()
    {
        using var factory = new CredentialsWebFactory();
        var session = await ReadySessionAsync(factory);
        using var client = factory.CreateClient();

        await client.PostAsync(PairingUri(session.SessionId), null);
        await session.BackgroundWork;

        var store = factory.Services.GetRequiredService<SessionStore>();
        Assert.Equal(0, store.ActivePairings);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dtk dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests --filter "FullyQualifiedName~PairingEndpointTests"`
Expected: FAIL — the route does not exist.

- [ ] **Step 3: Add the endpoint**

In `SessionEndpoints`, add the second constant and convert `MapSessionEndpoints` from an expression body to a block that maps both routes:

```csharp
    private const string PairingBusyMessage =
        "This instance is already holding as many pairing listeners as it allows. Try again in a "
        + "few minutes — or run your own: docker run -p 8080:8080 ghcr.io/handys11/rustplusapi-credentials";

    /// <summary>Maps <c>POST /api/sessions</c> and <c>POST /api/sessions/{sessionId}/pairing</c>.</summary>
    /// <param name="app">The route builder.</param>
    internal static void MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/sessions", (HttpContext context, SessionStore store, AppOptions options) =>
        {
            if (!store.TryCreate(ClientAddress.Of(context), out var session, out _))
            {
                return Results.Json(new ErrorPayload(OverCapacityMessage), statusCode: 429);
            }

            var returnUrl = $"{options.PublicBaseUrl}/callback/{session.ReturnToken}";
            return Results.Ok(new CreateSessionResponse(
                session.SessionId,
                SteamLoginService.BuildLoginUrl(returnUrl)));
        });

        app.MapPost("/api/sessions/{sessionId}/pairing", (
            string sessionId,
            SessionStore store,
            CredentialFlow flow) =>
        {
            if (!store.TryGet(sessionId, out var session))
            {
                return Results.NotFound();
            }

            if (session.State != SessionState.Ready)
            {
                return Results.Conflict(new ErrorPayload(
                    "This session is not ready to wait for a pairing."));
            }

            // The slot is taken here rather than inside the flow so that a refusal is a plain 429
            // with nothing started; CredentialFlow.WaitForPairingAsync always releases it.
            if (!store.TryAcquirePairingSlot())
            {
                return Results.Json(new ErrorPayload(PairingBusyMessage), statusCode: 429);
            }

            session.BackgroundWork = flow.WaitForPairingAsync(session, session.Lifetime.Token);
            return Results.Accepted();
        });
    }
```

Add `using RustPlusApi.CredentialsWeb.Flow;` to the file's usings.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests --filter "FullyQualifiedName~PairingEndpointTests"`
Expected: PASS, 5 tests.

- [ ] **Step 5: Commit**

```bash
git add apps/RustPlusApi.CredentialsWeb/Endpoints/SessionEndpoints.cs tests/RustPlusApi.CredentialsWeb.UnitTests/PairingEndpointTests.cs
git commit -m "feat(web): add the opt-in pairing endpoint behind the pairing cap"
```

---

### Task 13: The promise test — secrets never reach a log

The test that defends the trust section of the spec. Without it, "never logged" is a comment rather than a property.

**Files:**
- Create: `tests/RustPlusApi.CredentialsWeb.UnitTests/CapturingLoggerProvider.cs`
- Modify: `tests/RustPlusApi.CredentialsWeb.UnitTests/CredentialsWebFactory.cs`
- Test: `tests/RustPlusApi.CredentialsWeb.UnitTests/SecretsAreNeverLoggedTests.cs`

**Interfaces:**
- Consumes: the whole app.
- Produces: `internal sealed class CapturingLoggerProvider : ILoggerProvider` with `internal IReadOnlyList<string> Records { get; }`; `CredentialsWebFactory.Logs` exposing it.

- [ ] **Step 1: Write the capturing provider**

`tests/RustPlusApi.CredentialsWeb.UnitTests/CapturingLoggerProvider.cs`:

```csharp
namespace RustPlusApi.CredentialsWeb.UnitTests;

/// <summary>Captures every log record the app writes, at every level, so a test can assert on what
/// is absent from them.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly List<string> _records = [];

    /// <summary>Formatted messages, each with its exception appended.</summary>
    internal IReadOnlyList<string> Records
    {
        get
        {
            lock (_records)
            {
                return _records.ToArray();
            }
        }
    }

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(_records, categoryName);

    /// <inheritdoc/>
    public void Dispose()
    {
        // Nothing to release.
    }

    private sealed class CapturingLogger(List<string> records, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var line = $"{category}: {formatter(state, exception)} {exception}";
            lock (records)
            {
                records.Add(line);
            }
        }
    }
}
```

- [ ] **Step 2: Expose it from the factory**

In `CredentialsWebFactory`, add the property and extend `ConfigureWebHost`:

```csharp
    internal CapturingLoggerProvider Logs { get; } = new();

    /// <inheritdoc/>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureLogging(logging =>
        {
            // Trace, deliberately: the point is to prove the secret is absent even when everything
            // the app is willing to emit is captured.
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(Logs);
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IRegistrationSteps>();
            services.AddSingleton<IRegistrationSteps>(Steps);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
        });
    }
```

Add `using Microsoft.Extensions.Logging;` to the factory's usings.

- [ ] **Step 3: Write the failing test**

`tests/RustPlusApi.CredentialsWeb.UnitTests/SecretsAreNeverLoggedTests.cs`:

```csharp
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using RustPlusApi.CredentialsWeb.Sessions;
using RustPlusApi.Fcm.Registration;

namespace RustPlusApi.CredentialsWeb.UnitTests;

public sealed class SecretsAreNeverLoggedTests
{
    private const string SteamTokenSentinel = "SENTINEL-STEAM-TOKEN-b3a1f0c2";
    private const int PlayerTokenSentinel = 1928374650;

    private static async Task<(CredentialsWebFactory Factory, Session Session)> RunFullFlowAsync()
    {
        var factory = new CredentialsWebFactory();
        factory.Steps.PairingToReturn = new ServerPairing
        {
            Ip = "10.0.0.1",
            Port = 28082,
            PlayerId = 76561198249527954,
            PlayerToken = PlayerTokenSentinel,
            Name = "Test Server"
        };

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var store = factory.Services.GetRequiredService<SessionStore>();
        store.TryCreate("203.0.113.7", out var session, out _);

        await client.GetAsync(new Uri(
            $"/callback/{session!.ReturnToken}?steamId=76561198249527954&token={SteamTokenSentinel}",
            UriKind.Relative));
        await session.BackgroundWork;

        await client.PostAsync(new Uri($"/api/sessions/{session.SessionId}/pairing", UriKind.Relative), null);
        await session.BackgroundWork;

        return (factory, session);
    }

    [Fact]
    public async Task TheHarnessActuallyCapturesLogs()
    {
        // Guards against the whole suite passing vacuously because nothing was ever captured.
        var (factory, _) = await RunFullFlowAsync();
        using var _f = factory;

        Assert.NotEmpty(factory.Logs.Records);
    }

    [Fact]
    public async Task TheSteamTokenNeverReachesALogRecord()
    {
        var (factory, _) = await RunFullFlowAsync();
        using var _f = factory;

        Assert.DoesNotContain(
            factory.Logs.Records,
            record => record.Contains(SteamTokenSentinel, StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheCallbackQueryStringNeverReachesALogRecord()
    {
        var (factory, _) = await RunFullFlowAsync();
        using var _f = factory;

        Assert.DoesNotContain(
            factory.Logs.Records,
            record => record.Contains("token=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ThePlayerTokenNeverReachesALogRecord()
    {
        var (factory, session) = await RunFullFlowAsync();
        using var _f = factory;

        Assert.Equal(SessionState.Paired, session.State);
        Assert.DoesNotContain(
            factory.Logs.Records,
            record => record.Contains(
                PlayerTokenSentinel.ToString(System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheExpoPushTokenNeverReachesALogRecord()
    {
        var (factory, _) = await RunFullFlowAsync();
        using var _f = factory;

        Assert.DoesNotContain(
            factory.Logs.Records,
            record => record.Contains("ExponentPushToken", StringComparison.Ordinal));
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dtk dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests --filter "FullyQualifiedName~SecretsAreNeverLogged"`
Expected: PASS, 5 tests.

If `TheCallbackQueryStringNeverReachesALogRecord` fails, the `Microsoft.AspNetCore.Hosting.Diagnostics` filter added in Task 1 is missing or misspelled — that filter is the only thing suppressing the "Request starting … ?steamId=…&token=…" line, and it is written before any middleware could redact it. Do not fix this by weakening the test.

If `TheSteamTokenNeverReachesALogRecord` fails inside an exception, an upstream client has embedded a response body in its exception message. In that case, replace the `logger.LogError(ex, …)` calls in `CredentialFlow` with `logger.LogError("… {ExceptionType}", ex.GetType().Name)` rather than dropping the assertion.

- [ ] **Step 5: Commit**

```bash
git add tests/RustPlusApi.CredentialsWeb.UnitTests/
git commit -m "test(web): assert no credential ever reaches a log record"
```

---

### Task 14: The page

One HTML file, one stylesheet, one script, no build step and no third-party origin — which is what makes the CSP in Task 9 possible and what lets a security-conscious visitor read the whole client before trusting it with a Steam login.

**Files:**
- Create: `apps/RustPlusApi.CredentialsWeb/wwwroot/index.html`
- Create: `apps/RustPlusApi.CredentialsWeb/wwwroot/app.css`
- Create: `apps/RustPlusApi.CredentialsWeb/wwwroot/app.js`

**Interfaces:**
- Consumes: `POST /api/sessions`, `GET /callback/{returnToken}` (indirectly, via the redirect), `GET /api/sessions/{id}/events`, `POST /api/sessions/{id}/pairing`.
- Produces: nothing other tasks consume.

- [ ] **Step 1: Write index.html**

```html
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>Rust+ credentials</title>
    <link rel="stylesheet" href="app.css">
</head>
<body>
<main>
    <h1>Get your Rust+ credentials</h1>

    <section id="intro">
        <p>
            Sign in through Steam, then pair in game. You end up with the four values a Rust+ client
            needs, and a <code>rustplus.config.json</code> you can download.
        </p>
        <details open>
            <summary>What this server sees, and what it keeps</summary>
            <ul>
                <li>Your Steam auth token reaches this server, because Facepunch sends it here as a
                    query parameter and nothing can change that. It is dropped the moment your device
                    is registered.</li>
                <li>Nothing is written to disk or to a database. Everything lives in memory and is
                    discarded when your session ends or the process restarts.</li>
                <li>No credential is written to any log. The web server's request logging is turned
                    off precisely because it would record that query parameter.</li>
                <li>Your <code>playerToken</code> is full access to your Rust+ account. Treat it like
                    a password and don't paste it anywhere public.</li>
                <li>Prefer not to trust someone else's server? <a href="https://github.com/HandyS11/RustPlusApi">Run your own.</a></li>
            </ul>
        </details>
        <button id="start" type="button">Sign in with Steam</button>
    </section>

    <section id="progress" hidden>
        <h2>Working…</h2>
        <p id="status">Registering a device with Google and Rust Companion.</p>
    </section>

    <section id="ready" hidden>
        <h2>Credentials ready</h2>
        <p>Signed in as <code id="steam-id"></code>.</p>
        <button id="download" type="button">Download rustplus.config.json</button>
        <h3>Optional: get your pairing values</h3>
        <p>
            To get <code>ip</code>, <code>port</code>, <code>playerId</code> and
            <code>playerToken</code>, this server has to hold a connection open to Google while you
            pair in game. Start it only when you're ready to alt-tab into Rust.
        </p>
        <button id="pair" type="button">Wait for my pairing</button>
        <p id="pair-note"></p>
    </section>

    <section id="waiting" hidden>
        <h2>Waiting for your pairing</h2>
        <p>Open Rust, join a server, and choose <strong>Pair with Server</strong>.</p>
    </section>

    <section id="paired" hidden>
        <h2>Paired</h2>
        <dl>
            <dt>Server</dt><dd id="server-name"></dd>
            <dt>IP</dt><dd id="pair-ip"></dd>
            <dt>Port</dt><dd id="pair-port"></dd>
            <dt>Player ID</dt><dd id="pair-player-id"></dd>
            <dt>Player token</dt><dd id="pair-player-token"></dd>
        </dl>
        <pre id="snippet"></pre>
        <button id="download-paired" type="button">Download rustplus.config.json</button>
    </section>

    <section id="failed" hidden>
        <h2>Something went wrong</h2>
        <p id="error"></p>
        <button id="restart" type="button">Start over</button>
    </section>
</main>
<script src="app.js"></script>
</body>
</html>
```

- [ ] **Step 2: Write app.css**

```css
:root {
    color-scheme: light dark;
    --bg: #fbfbfa;
    --fg: #1d1c1a;
    --muted: #5c5952;
    --line: #d9d6cf;
    --accent: #b4552d;
}

@media (prefers-color-scheme: dark) {
    :root {
        --bg: #171614;
        --fg: #ecebe7;
        --muted: #a19d94;
        --line: #35322d;
        --accent: #e07a4c;
    }
}

* { box-sizing: border-box; }

body {
    margin: 0;
    background: var(--bg);
    color: var(--fg);
    font: 16px/1.6 system-ui, -apple-system, "Segoe UI", sans-serif;
}

main {
    max-width: 42rem;
    margin: 0 auto;
    padding: 3rem 1.25rem 6rem;
}

h1 { font-size: 1.75rem; margin-bottom: 1.5rem; }
h2 { font-size: 1.25rem; }
h3 { font-size: 1rem; margin-top: 2rem; }

section { border-top: 1px solid var(--line); padding-top: 1.5rem; margin-top: 1.5rem; }
section:first-of-type { border-top: none; margin-top: 0; padding-top: 0; }

details {
    border: 1px solid var(--line);
    border-radius: 6px;
    padding: 0.75rem 1rem;
    margin: 1.5rem 0;
}

summary { cursor: pointer; font-weight: 600; }
details ul { margin: 0.75rem 0 0; padding-left: 1.1rem; color: var(--muted); }
details li { margin-bottom: 0.5rem; }

button {
    font: inherit;
    font-weight: 600;
    color: #fff;
    background: var(--accent);
    border: none;
    border-radius: 6px;
    padding: 0.6rem 1.1rem;
    cursor: pointer;
}

button:disabled { opacity: 0.5; cursor: default; }

code, pre {
    font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace;
    font-size: 0.9em;
}

pre {
    background: color-mix(in srgb, var(--fg) 6%, transparent);
    border: 1px solid var(--line);
    border-radius: 6px;
    padding: 0.9rem;
    overflow-x: auto;
}

dl { display: grid; grid-template-columns: max-content 1fr; gap: 0.4rem 1.25rem; }
dt { color: var(--muted); }
dd { margin: 0; font-family: ui-monospace, Menlo, Consolas, monospace; word-break: break-all; }

a { color: var(--accent); }
```

- [ ] **Step 3: Write app.js**

The session handle is read from the URL fragment first and from `sessionStorage` second. The fragment is what carries the handle back from the callback redirect, and it also lets the Steam login be completed in a different browser from the one that started the flow.

```javascript
"use strict";

const SESSION_KEY = "rustplus-credentials-session";

const view = {
    intro: document.getElementById("intro"),
    progress: document.getElementById("progress"),
    ready: document.getElementById("ready"),
    waiting: document.getElementById("waiting"),
    paired: document.getElementById("paired"),
    failed: document.getElementById("failed")
};

let sessionId = null;
let configJson = null;

function show(name) {
    for (const [key, element] of Object.entries(view)) {
        element.hidden = key !== name;
    }
}

function fail(message) {
    document.getElementById("error").textContent = message;
    show("failed");
}

function readSessionId() {
    const match = /^#session=([0-9a-f]{32})$/.exec(location.hash);
    if (match) {
        // Drop the fragment so a shared or bookmarked URL does not carry the session handle.
        history.replaceState({}, "", location.pathname);
        sessionStorage.setItem(SESSION_KEY, match[1]);
        return match[1];
    }
    return sessionStorage.getItem(SESSION_KEY);
}

function download(name, text) {
    const url = URL.createObjectURL(new Blob([text], { type: "application/json" }));
    const link = document.createElement("a");
    link.href = url;
    link.download = name;
    link.click();
    URL.revokeObjectURL(url);
}

function onStep(payload) {
    if (payload.state === "AwaitingPairing") {
        show("waiting");
    } else if (payload.state === "Registering" || payload.state === "Authenticated") {
        show("progress");
    }
}

function onCredentials(payload) {
    configJson = payload.configJson;
    document.getElementById("steam-id").textContent = payload.steamId;
    show("ready");
}

function onPaired(payload) {
    document.getElementById("server-name").textContent = payload.name ?? "(unnamed)";
    document.getElementById("pair-ip").textContent = payload.ip;
    document.getElementById("pair-port").textContent = payload.port;
    document.getElementById("pair-player-id").textContent = payload.playerId;
    document.getElementById("pair-player-token").textContent = payload.playerToken;
    document.getElementById("snippet").textContent =
        "new RustPlus(new RustPlusConnection(\"" + payload.ip + "\", " + payload.port +
        ", " + payload.playerId + ", " + payload.playerToken + "));";
    show("paired");
}

function listen(id) {
    const source = new EventSource("/api/sessions/" + id + "/events");

    source.addEventListener("step", e => onStep(JSON.parse(e.data)));
    source.addEventListener("credentials", e => onCredentials(JSON.parse(e.data)));
    source.addEventListener("paired", e => { onPaired(JSON.parse(e.data)); source.close(); });
    source.addEventListener("error", e => {
        // A named "error" event carries our payload; a transport error has no data and
        // EventSource reconnects on its own, so it is not surfaced.
        if (e.data) {
            fail(JSON.parse(e.data).message);
            source.close();
        }
    });
    source.addEventListener("expired", () => {
        // Not a failure: the session is back in Ready, so the stream stays open and the visitor
        // can start another wait without repeating the Steam login.
        document.getElementById("pair").disabled = false;
        document.getElementById("pair-note").textContent =
            "No pairing arrived in time. Your credentials are still valid — try again when you're in game.";
        show("ready");
    });
}

async function start() {
    const button = document.getElementById("start");
    button.disabled = true;

    const response = await fetch("/api/sessions", { method: "POST" });
    if (!response.ok) {
        const body = await response.json().catch(() => ({ message: "This instance is busy." }));
        fail(body.message);
        return;
    }

    const body = await response.json();
    sessionStorage.setItem(SESSION_KEY, body.sessionId);
    location.href = body.loginUrl;
}

async function pair() {
    const button = document.getElementById("pair");
    button.disabled = true;

    const response = await fetch("/api/sessions/" + sessionId + "/pairing", { method: "POST" });
    if (!response.ok) {
        const body = await response.json().catch(() => ({ message: "Could not start the pairing wait." }));
        fail(body.message);
        button.disabled = false;
    }
}

document.getElementById("start").addEventListener("click", start);
document.getElementById("pair").addEventListener("click", pair);
document.getElementById("download").addEventListener("click",
    () => download("rustplus.config.json", configJson));
document.getElementById("download-paired").addEventListener("click",
    () => download("rustplus.config.json", configJson));
document.getElementById("restart").addEventListener("click", () => {
    sessionStorage.removeItem(SESSION_KEY);
    location.href = "/";
});

sessionId = readSessionId();
if (sessionId) {
    show("progress");
    listen(sessionId);
} else {
    show("intro");
}
```

- [ ] **Step 4: Run the app and walk the flow by hand**

```bash
dotnet run --project apps/RustPlusApi.CredentialsWeb \
  -- --CredentialsWeb:PublicBaseUrl=http://localhost:5000 --CredentialsWeb:AllowInsecureBaseUrl=true
```

Open `http://localhost:5000`. Expected: the intro renders, the trust panel is open by default, and "Sign in with Steam" navigates to `companion-rust.facepunch.com`. Do not complete a real login here — the end-to-end run belongs to Task 17's verification.

- [ ] **Step 5: Verify the whole suite still passes**

Run: `dotnet test tests/RustPlusApi.CredentialsWeb.UnitTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add apps/RustPlusApi.CredentialsWeb/wwwroot/
git commit -m "feat(web): add the single-page client with an explicit trust disclosure"
```

---

### Task 15: Container image and the deployment footguns

**Files:**
- Create: `apps/RustPlusApi.CredentialsWeb/Dockerfile`
- Create: `apps/RustPlusApi.CredentialsWeb/docker-compose.yml`
- Create: `apps/RustPlusApi.CredentialsWeb/Caddyfile.example`
- Create: `.dockerignore`

**Interfaces:**
- Consumes: the built app.
- Produces: an image exposing port 8080, run as a non-root user.

- [ ] **Step 1: Write .dockerignore**

The build context is the repository root, because `ProjectReference`, `Directory.Build.props` and `Directory.Packages.props` all live above the app.

`.dockerignore` at the repository root:

```
**/bin/
**/obj/
**/TestResults/
**/.git/
**/.idea/
**/.vs/
docs/_site/
rustplus.config.json
```

`rustplus.config.json` is listed because it holds real credentials on a developer machine and must never enter an image layer.

- [ ] **Step 2: Write the Dockerfile**

```dockerfile
# Build context is the repository root:
#   docker build -f apps/RustPlusApi.CredentialsWeb/Dockerfile -t rustplusapi-credentials .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props NuGet.config ./
COPY src/ src/
COPY apps/ apps/

RUN dotnet publish apps/RustPlusApi.CredentialsWeb/RustPlusApi.CredentialsWeb.csproj \
    --configuration Release \
    --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# The aspnet image ships a non-root user; APP_UID is its id.
USER $APP_UID

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app .
ENTRYPOINT ["dotnet", "RustPlusApi.CredentialsWeb.dll"]
```

- [ ] **Step 3: Write docker-compose.yml**

This file is copyable documentation, not a supported second install path. It exists because the two settings most likely to be got wrong — the read-only filesystem and the proxy trust list — are easier to show than to describe.

```yaml
services:
  credentials:
    image: ghcr.io/handys11/rustplusapi-credentials:latest
    restart: unless-stopped
    ports:
      - "127.0.0.1:8080:8080"
    environment:
      # Must be the externally reachable origin, with no trailing slash. This is the value
      # Facepunch redirects back to, so it cannot be inferred from what Kestrel sees.
      CredentialsWeb__PublicBaseUrl: "https://creds.example.org"
      # Per-IP limits are silently void unless the proxy's address is trusted here: without it
      # every visitor presents as the proxy and shares one bucket. Set this to the proxy's address
      # on the container network, and never to a host you do not control. Leave it unset when the
      # app is reached directly, so X-Forwarded-For from a caller is ignored.
      CredentialsWeb__KnownProxies__0: "172.18.0.2"
    # Enforces "nothing is written to disk" at the runtime level rather than by intent.
    read_only: true
    tmpfs:
      - /tmp
    security_opt:
      - no-new-privileges:true
    cap_drop:
      - ALL
```

- [ ] **Step 4: Write Caddyfile.example**

The default access log of every common reverse proxy records the full request line, including the query — which is where the Steam auth token is. This is the single most likely way a careful self-hoster still ends up leaking one.

```
# Example only. The important part is the log format: the default one records the full URI,
# including the ?steamId=…&token=… that Facepunch appends to the callback.
creds.example.org {
	reverse_proxy 127.0.0.1:8080

	log {
		output file /var/log/caddy/creds.log
		# Drop the query string entirely. Without this filter the Steam auth token is written
		# to disk on every successful login.
		format filter {
			request>uri query {
				delete steamId
				delete token
			}
		}
	}
}
```

- [ ] **Step 5: Build and smoke-test the image**

```bash
docker build -f apps/RustPlusApi.CredentialsWeb/Dockerfile -t rustplusapi-credentials .
docker run --rm -p 8080:8080 \
  -e CredentialsWeb__PublicBaseUrl=http://localhost:8080 \
  -e CredentialsWeb__AllowInsecureBaseUrl=true \
  rustplusapi-credentials
```

Then, in another shell:

```bash
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:8080/
curl -s -X POST http://localhost:8080/api/sessions
```

Expected: `200`, then a JSON body with `sessionId` and a `loginUrl` beginning
`https://companion-rust.facepunch.com/login?returnUrl=`.

- [ ] **Step 6: Verify the insecure-base-url guard in the container**

```bash
docker run --rm -e CredentialsWeb__PublicBaseUrl=http://localhost:8080 rustplusapi-credentials
```

Expected: the container exits non-zero, printing a configuration error that names
`CredentialsWeb:AllowInsecureBaseUrl`.

- [ ] **Step 7: Commit**

```bash
git add .dockerignore apps/RustPlusApi.CredentialsWeb/Dockerfile apps/RustPlusApi.CredentialsWeb/docker-compose.yml apps/RustPlusApi.CredentialsWeb/Caddyfile.example
git commit -m "feat(web): containerize the app with safe proxy and logging examples"
```

---

### Task 16: Coverage split and pipelines

The library's 95/90 gate is computed from a merged report across every assembly. Dropping a web app into that merge would quietly lower the bar the *library* has to clear. This task splits the report in two and holds both halves to the same 95/90.

**Files:**
- Modify: `tools/coverage/check_threshold.py`
- Modify: `tools/coverage/report.sh`
- Modify: `.github/workflows/CI.yml`
- Modify: `.github/workflows/CD.yml`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `check_threshold.py <line_min> <branch_min> [report_path]`; two merged reports at `TestResults/merged` (libraries) and `TestResults/merged-web` (the app).

- [ ] **Step 1: Let check_threshold.py take a report path**

In `tools/coverage/check_threshold.py`, replace the usage block and the hard-coded report path:

```python
if len(sys.argv) not in (3, 4):
    print("Usage: check_threshold.py <line_min> <branch_min> [report_path]")
    sys.exit(2)

line_min, branch_min = float(sys.argv[1]), float(sys.argv[2])
report = sys.argv[3] if len(sys.argv) == 4 else 'TestResults/merged/Cobertura.xml'
```

Also update the module docstring's second paragraph to read:

```
Reads a merged Cobertura report produced by ReportGenerator (by default
TestResults/merged/Cobertura.xml, the union across every library test project
and TFM) and checks its overall line-rate and branch-rate against the floors.
```

- [ ] **Step 2: Split the local report script**

Replace everything in `tools/coverage/report.sh` from the `dotnet tool run reportgenerator` line to the end with:

```bash
dotnet tool restore

# Two reports, two gates. The library gate must stay exactly what it was before the web app
# existed: merging a net10.0-only ASP.NET app into it would lower the bar the libraries clear.
dotnet tool run reportgenerator -- \
  "-reports:TestResults/**/coverage.opencover.xml" \
  "-targetdir:TestResults/merged" \
  "-assemblyfilters:-RustPlusApi.CredentialsWeb" \
  "-reporttypes:Cobertura"

dotnet tool run reportgenerator -- \
  "-reports:TestResults/**/coverage.opencover.xml" \
  "-targetdir:TestResults/merged-web" \
  "-assemblyfilters:+RustPlusApi.CredentialsWeb" \
  "-reporttypes:Cobertura"

python3 - <<'PY'
import xml.etree.ElementTree as ET
for label, path in (('libraries', 'TestResults/merged/Cobertura.xml'),
                    ('web app  ', 'TestResults/merged-web/Cobertura.xml')):
    root = ET.parse(path).getroot()
    line = float(root.attrib['line-rate']) * 100
    branch = float(root.attrib['branch-rate']) * 100
    print(f"{label} -> line={line:.2f}% branch={branch:.2f}%")
    for cls in root.iter('class'):
        name = cls.attrib.get('name', '?')
        lr = float(cls.attrib.get('line-rate', '1')) * 100
        br = float(cls.attrib.get('branch-rate', '1')) * 100
        if lr < 100 or br < 100:
            print(f"  line={lr:6.2f}% branch={br:6.2f}%  {name}")
PY

python3 tools/coverage/check_threshold.py 95 90
python3 tools/coverage/check_threshold.py 95 90 TestResults/merged-web/Cobertura.xml
```

- [ ] **Step 3: Split the CI gate**

In `.github/workflows/CI.yml`, replace the "Merge coverage reports" and "Enforce coverage threshold" steps with:

```yaml
    - name: Merge coverage reports (libraries)
      run: >
        dotnet tool run reportgenerator --
        "-reports:${{ github.workspace }}/TestResults/**/coverage.opencover.xml"
        "-targetdir:${{ github.workspace }}/TestResults/merged"
        "-assemblyfilters:-RustPlusApi.CredentialsWeb"
        "-reporttypes:Cobertura"

    - name: Merge coverage reports (web app)
      run: >
        dotnet tool run reportgenerator --
        "-reports:${{ github.workspace }}/TestResults/**/coverage.opencover.xml"
        "-targetdir:${{ github.workspace }}/TestResults/merged-web"
        "-assemblyfilters:+RustPlusApi.CredentialsWeb"
        "-reporttypes:Cobertura"

    - name: Enforce coverage threshold (libraries)
      # Merged aggregate across every library test project and both TFMs (ReportGenerator union).
      # The web app is filtered out so it cannot dilute the libraries' number.
      # Metric: ReportGenerator-merged Cobertura line-rate/branch-rate.
      # Gate floor: line 95 / branch 90 — keep in sync with docs/development/testing.md.
      working-directory: ${{ github.workspace }}
      run: python3 tools/coverage/check_threshold.py 95 90

    - name: Enforce coverage threshold (web app)
      # Same floor. The app can meet it because its two untestable regions — host wiring and the
      # live-network adapter — are [ExcludeFromCodeCoverage] with justifications.
      working-directory: ${{ github.workspace }}
      run: python3 tools/coverage/check_threshold.py 95 90 TestResults/merged-web/Cobertura.xml
```

- [ ] **Step 4: Publish the image from CD**

In `.github/workflows/CD.yml`, add a second job after `build-and-deploy`:

```yaml
  publish-image:
    runs-on: ubuntu-latest
    needs: build-and-deploy
    permissions:
      contents: read
      packages: write

    steps:
    - name: Checkout Repository
      uses: actions/checkout@v7

    - name: Resolve Version from Tag
      run: |
        VERSION="${GITHUB_REF_NAME#v}"
        echo "VERSION=$VERSION" >> "$GITHUB_ENV"
        if [[ "$VERSION" == *-* ]]; then
          echo "IS_PRERELEASE=true" >> "$GITHUB_ENV"
        else
          echo "IS_PRERELEASE=false" >> "$GITHUB_ENV"
        fi

    - name: Set up QEMU
      uses: docker/setup-qemu-action@v3

    - name: Set up Buildx
      uses: docker/setup-buildx-action@v3

    - name: Log in to GitHub Container Registry
      uses: docker/login-action@v3
      with:
        registry: ghcr.io
        username: ${{ github.actor }}
        password: ${{ secrets.GITHUB_TOKEN }}

    - name: Compute image tags
      run: |
        TAGS="ghcr.io/${{ github.repository_owner }}/rustplusapi-credentials:$VERSION"
        # 'latest' only follows stable releases, so a prerelease never becomes the default pull.
        if [[ "$IS_PRERELEASE" == "false" ]]; then
          TAGS="$TAGS,ghcr.io/${{ github.repository_owner }}/rustplusapi-credentials:latest"
        fi
        echo "TAGS=$TAGS" >> "$GITHUB_ENV"

    - name: Build and push
      uses: docker/build-push-action@v6
      with:
        context: .
        file: apps/RustPlusApi.CredentialsWeb/Dockerfile
        platforms: linux/amd64,linux/arm64
        push: true
        tags: ${{ env.TAGS }}
```

The repository owner is lowercased by GHCR automatically; `HandyS11` resolves to `handys11`, matching the `docker run` line quoted in the 429 messages and the README.

- [ ] **Step 5: Verify the split locally**

Run: `tools/coverage/report.sh`
Expected: two summary blocks — `libraries` and `web app` — followed by two `[OK]` lines. If the web app's line is below 95, the gap report above it names the class; add tests rather than lowering the floor, or `[ExcludeFromCodeCoverage]` with a justification if the code is genuinely a live-network path.

- [ ] **Step 6: Commit**

```bash
git add tools/coverage/ .github/workflows/
git commit -m "ci: gate library and web-app coverage separately, publish the container image"
```

---

### Task 17: Documentation and end-to-end verification

**Files:**
- Create: `apps/RustPlusApi.CredentialsWeb/README.md`
- Modify: `docs/articles/credentials.md`
- Modify: `docs/articles/getting-started.md`
- Modify: `docs/articles/troubleshooting.md`
- Modify: `docs/development/testing.md`
- Modify: `README.md`
- Modify: `CLAUDE.md`

**Interfaces:**
- Consumes: the finished app.
- Produces: nothing.

- [ ] **Step 1: Write the self-host README**

`apps/RustPlusApi.CredentialsWeb/README.md`:

````markdown
# Rust+ credentials website

A single-page web app that walks a visitor from nothing to working Rust+ credentials. It is both a
self-host starter and the code behind the public instance.

## Run it

```bash
docker run -p 8080:8080 \
  -e CredentialsWeb__PublicBaseUrl=https://creds.example.org \
  ghcr.io/handys11/rustplusapi-credentials
```

`PublicBaseUrl` is **required** and must be the externally reachable origin, with no trailing
slash. It cannot be inferred: it is the URL Facepunch redirects the browser back to, and behind a
reverse proxy that is not what Kestrel sees. The app refuses to start if it is not `https`, unless
you set `CredentialsWeb__AllowInsecureBaseUrl=true` for local development.

For local development without a container:

```bash
dotnet run --project apps/RustPlusApi.CredentialsWeb \
  -- --CredentialsWeb:PublicBaseUrl=http://localhost:5000 --CredentialsWeb:AllowInsecureBaseUrl=true
```

## Two things that will bite you behind a reverse proxy

**Your access log will record Steam auth tokens.** Facepunch appends the token to the callback URL
as a query parameter. The app itself never logs it — request logging is switched off for exactly
this reason — but your proxy's default log format records the full request line. Filter the query
before it reaches disk; `Caddyfile.example` in this directory shows how.

**Your per-IP limits will silently do nothing.** Without `ForwardedHeaders` configured with the
proxy's address in `CredentialsWeb__KnownProxies__0`, every visitor presents as the proxy and
shares one bucket. Configured too loosely, anyone can spoof `X-Forwarded-For` past the limits.
With none set, forwarded headers are ignored — the right default when the app is reached directly.
See `docker-compose.yml`.

## Configuration

All settings live under the `CredentialsWeb` section (`CredentialsWeb__Name` as an environment
variable, `--CredentialsWeb:Name` on the command line).

| Setting | Default | What it does |
|---|---|---|
| `PublicBaseUrl` | *(required)* | Externally reachable origin, no trailing slash |
| `AllowInsecureBaseUrl` | `false` | Permits a non-https base URL. Development only |
| `KnownProxies__0`, `__1`, … | *(empty)* | Reverse proxy addresses whose `X-Forwarded-For` is trusted |
| `MaxConcurrentSessions` | `200` | Global cap on live sessions |
| `MaxConcurrentPairings` | `50` | Global cap on live MCS sockets |
| `MaxCompletionsPerIpPerHour` | `5` | Rolling per-IP cap on completed flows |
| `CreatedTtl` | `00:05:00` | Lifetime of a session before the Steam login |
| `SessionTtl` | `00:15:00` | Lifetime of a session after it |
| `PairingTtl` | `00:10:00` | How long an MCS socket is held |

## What it does with credentials

- **In memory only.** No database, no session cache, no disk. A restart wipes everything.
- **The Steam auth token is dropped** the moment your device is registered with Rust Companion.
- **Nothing is logged.** Asserted by `SecretsAreNeverLoggedTests`, not just intended.
- **The callback responds 302**, so the token-bearing URL never becomes a browser history entry,
  and the session handle travels in a URL fragment, which browsers never send to a server.
- **The server does see the token** in the request line. Facepunch decides the callback shape and
  nothing can change that; the design minimises its lifetime rather than pretending otherwise.

## Flow

The steps run in the order `4 → 1,2,3 → 5`, not the console app's `1,2,3 → 4 → 5`. Putting the
Steam login first means an anonymous visitor cannot trigger Google device registrations: they cost
one dictionary entry with a five-minute TTL and nothing else. Step 6, the pairing wait, is an
opt-in continuation because it is the only step that holds a socket open.
````

- [ ] **Step 2: Update the user-facing docs**

- `docs/articles/credentials.md`: add a section at the top presenting the website as the recommended
  route (`docker run` line plus the public instance URL), and keep the existing
  `RustPlus.Register.ConsoleApp` walkthrough below it as the local route. Update the Mermaid
  sequence diagram to show the browser redirecting to the app's own `/callback/<nonce>` rather than
  to `localhost:3000`.
- `docs/articles/getting-started.md`: point at the website first, then the console app.
- `docs/articles/troubleshooting.md`: add a "Credentials website" section with three entries —
  *"The callback returns 404"* (the return token is single-use; a refreshed or bookmarked callback
  URL will always 404, start over); *"I get a 429"* (the instance is at capacity or you have run
  too many flows this hour; wait, or self-host); *"The pairing never arrives"* (the wait is capped
  at `PairingTtl`; press Pair with Server while the page says it is waiting; the credentials stay
  valid, so retry the pairing step rather than the whole flow).
- `README.md` (root): one paragraph linking the public instance and the self-host image.

- [ ] **Step 3: Update the developer docs**

- `docs/development/testing.md`: add a "Credentials web app" subsection recording that the app is
  gated separately at the same 95/90 via `TestResults/merged-web`, and listing its two coverage
  exclusions with their justifications — `Program` (host wiring) and `LiveRegistrationSteps`
  (live-network seam). Note the app is `net10.0` only and therefore outside multi-TFM parity.
- `CLAUDE.md`: add `apps/RustPlusApi.CredentialsWeb` to the package-layering section, describing it
  as a net10.0-only ASP.NET Core app that consumes `RustPlusApi.Fcm.Registration`'s public API,
  reorders the flow to `4 → 1,2,3 → 5`, and is excluded from the netstandard2.0 parity story. Add
  the two-gate coverage note to the Commands section.

- [ ] **Step 4: Full verification**

```bash
dotnet build RustPlusApi.sln
dotnet test RustPlusApi.sln
tools/coverage/report.sh
dotnet tool restore && dotnet jb cleanupcode RustPlusApi.sln --profile="ReformatAndReorder"
git diff --exit-code
```

Expected: a clean build with no warnings; every test passing on both TFM hosts; both coverage gates
`[OK]`; and `git diff --exit-code` returning 0, proving the formatter changed nothing.

- [ ] **Step 5: Live end-to-end run**

This is the only step that proves the thing works, and it is also the outstanding verification the
spec records as an open assumption. Deploy the image behind a real `https` hostname you control,
then:

1. Complete the flow in a browser: Steam login, credentials shown, `rustplus.config.json` downloads.
2. Confirm the redirect landed on **your external hostname** — this closes the last unproven
   assumption from the 2026-09-02 spec.
3. Opt into the pairing step, pair in game, and confirm the four values appear.
4. Construct a `RustPlus` client from those four values and confirm it connects.
5. Grep the container logs and the proxy access log for the Steam token and the `playerToken`.
   Expected: no match in either.

Record the outcome in the spec's *Assumptions to re-verify* section.

- [ ] **Step 6: Commit**

```bash
git add apps/RustPlusApi.CredentialsWeb/README.md docs/ README.md CLAUDE.md
git commit -m "docs: document the credentials website for self-hosters and contributors"
```
