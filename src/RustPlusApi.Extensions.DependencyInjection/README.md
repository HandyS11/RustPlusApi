# RustPlusApi.Extensions.DependencyInjection

`Microsoft.Extensions.DependencyInjection` registration extensions for
[`RustPlusApi`](https://www.nuget.org/packages/RustPlusApi).

```csharp
// one configured client (singleton, container-disposed)
services.AddRustPlus(new RustPlusConnection("12.34.56.78", 28082, playerId, playerToken));

// or bound from configuration (keys: Server, Port, PlayerId, PlayerToken, UseFacepunchProxy)
services.AddRustPlus(configuration.GetSection("Rust"));

// many / runtime connections: caller-owned clients via the factory
services.AddRustPlusFactory();
await using var client = provider.GetRequiredService<IRustPlusFactory>().Create(connection);
```

Logging auto-wires from the host's `ILoggerFactory`; tuning via `IOptions<RustPlusSocketOptions>`.
Nothing auto-connects — call `ConnectAsync` when ready.

## Documentation

- [Dependency Injection guide](https://handys11.github.io/RustPlusApi/articles/dependency-injection.html)
