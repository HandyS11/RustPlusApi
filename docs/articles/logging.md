# Logging

Both clients are silent by default. Supply an `ILoggerFactory` through the options object to receive
structured diagnostics (connect/receive/teardown lifecycle, dropped frames, unknown messages, errors):

```csharp
using Microsoft.Extensions.Logging;

using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

using var rustPlus = new RustPlus(
    new RustPlusConnection("127.0.0.1", 28082, playerId, playerToken),
    new RustPlusSocketOptions { LoggerFactory = loggerFactory });
```

The FCM client accepts the same `LoggerFactory` on `RustPlusFcmSocketOptions`. When no factory is
supplied, logging is a no-op (`NullLogger`) with zero overhead.
