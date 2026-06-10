# Dependency Injection

The `RustPlusApi.Extensions.DependencyInjection` and `RustPlusApi.Fcm.Extensions.DependencyInjection`
packages register the clients into a `Microsoft.Extensions.DependencyInjection` container. Logging
auto-wires from the host's `ILoggerFactory`; tuning flows through `IOptions<RustPlusSocketOptions>` /
`IOptions<RustPlusFcmSocketOptions>`. Nothing auto-connects — you call `ConnectAsync` when ready.

## One configured client

```csharp
// explicit connection
services.AddRustPlus(new RustPlusConnection("12.34.56.78", 28082, playerId, playerToken));

// or bound from configuration (keys: Server, Port, PlayerId, PlayerToken, UseFacepunchProxy)
services.AddRustPlus(configuration.GetSection("Rust"));

// or resolved from the provider
services.AddRustPlus(sp => BuildConnection(sp), o => o.RequestTimeout = TimeSpan.FromSeconds(10));
```

`IRustPlus` resolves as a singleton the container disposes (`IAsyncDisposable`) on shutdown.
Registrations are idempotent: the first `IRustPlus` registration wins, while option delegates always compose.

## Many or runtime connections — the factory

```csharp
services.AddRustPlusFactory();

// later, when the connection is known:
var factory = provider.GetRequiredService<IRustPlusFactory>();
await using var client = factory.Create(connection);
await client.ConnectAsync();
```

Factory-created clients are **caller-owned**: dispose them yourself (prefer `await using`).

## FCM

```csharp
// single listener (credentials are secrets — no configuration-binding overload)
services.AddRustPlusFcm(credentials);

// or runtime credentials via the factory (e.g. after FcmRegistration.AcquireCredentialsAsync)
services.AddRustPlusFcmFactory();
await using var fcm = provider.GetRequiredService<IRustPlusFcmFactory>().Create(credentials);
```

FCM listeners are single-connection: to reconnect, create a new one — another reason the factory fits.
Factory-created listeners always deduplicate within the session (a fresh persistent-ID list is supplied
when you don't pass one).

## Lifetimes at a glance

| Registration | Lifetime | Disposed by |
| --- | --- | --- |
| `AddRustPlus(...)` / `AddRustPlusFcm(...)` | Singleton | the container |
| `AddRustPlusFactory()` / `AddRustPlusFcmFactory()` | Singleton (factory) | the container |
| `factory.Create(...)` output | caller-defined | **you** (`await using`) |
