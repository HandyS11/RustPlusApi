# RustPlusApi.Fcm.Extensions.DependencyInjection

`Microsoft.Extensions.DependencyInjection` registration extensions for
[`RustPlusApi.Fcm`](https://www.nuget.org/packages/RustPlusApi.Fcm).

```csharp
// one configured listener (singleton, container-disposed)
services.AddRustPlusFcm(credentials);

// runtime credentials (e.g. from FcmRegistration): caller-owned listeners via the factory
services.AddRustPlusFcmFactory();
await using var fcm = provider.GetRequiredService<IRustPlusFcmFactory>().Create(credentials);
```

Logging auto-wires from the host's `ILoggerFactory`; tuning via `IOptions<RustPlusFcmSocketOptions>`.
FCM listeners are single-connection — create a new one to reconnect.

## Documentation

- [Dependency Injection guide](https://handys11.github.io/RustPlusApi/articles/dependency-injection.html)
