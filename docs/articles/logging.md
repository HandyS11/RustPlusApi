# Logging

Both clients are silent by default. Supply an `ILoggerFactory` to the constructor to receive
structured diagnostics (connect/receive/teardown lifecycle, dropped frames, unknown messages, errors):

```csharp
using Microsoft.Extensions.Logging;

using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

using var rustPlus = new RustPlus(
    new RustPlusConnection("127.0.0.1", 28082, playerId, playerToken),
    loggerFactory: loggerFactory);
```

The FCM client accepts the same `loggerFactory` constructor parameter. When none is supplied, logging is a no-op (`NullLogger`) with zero overhead.
