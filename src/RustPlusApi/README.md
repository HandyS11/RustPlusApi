# RustPlusApi

This is a C# client for the Rust+ API. It allows you to interact with the Rust+ server.

## Prerequisites

- **.NET 8** or later

## Sumary

- [RustPlusApi](#rustplusapi)
  - [Prerequisites](#prerequisites)
  - [Sumary](#sumary)
    - [RustPlusLegacy](#rustpluslegacy)
    - [RustPlus](#rustplus)

The library provides two classes to interact with the Rust+ API: `RustPlusLegacy` and `RustPlus`.

- `RustPlusLegacy` is the original implementation based on the `./Protobuf/RustPlus.proto` file.
- `RustPlus` is a new implementation that return a response based on `./Data/Response.cs` object.

Since `RustPlus` inherit from `RustPlusLegacy`, you can use both classes to interact with the Rust+ API. The `RustPlus` class is recommended for new projects, as it provides a more user-friendly interface and better error handling.

### RustPlusLegacy

First, instantiate the `RustPlusLegacy` class with the necessary parameters:

```csharp
var rustPlusApi = new RustPlusLegacy(server, port, playerId, playerToken, useFacepunchProxy);
```

Parameters:

- `server`: The IP address of the Rust+ server.
- `port`: The port dedicated for the Rust+ companion app (not the one used to connect in-game).
- `playerId`: Your Steam ID.
- `playerToken`: Your player token acquired with FCM.
- `useFacepunchProxy`: Specifies whether to use the Facepunch proxy. Default is false.

Then, connect to the Rust+ server:

```csharp
await rustPlusApi.ConnectAsync();
```

---

There are plenty of methods to interact with the Rust+ server such as:

```csharp
uint entityId = 123456789;
var response = await rustPlus.GetEntityInfoLegacyAsync(entityId);
```

or

```csharp
var response = await rustPlus.GetInfoLegacyAsync();
```

you can also make your own request:

```csharp
var request = new AppRequest
{
    GetTime = new AppEmpty()
};
await rustPlus.SendRequestAsync(request);
```

The response with be an **AppMessage** that is a direct representation of `./Protobuf/RustPlus.proto` file.

Feel free to explore the `RustPlusLegacy` class to find all convinient methods to use.

---

You can subscribe to events to handle specific actions:

```csharp
rustPlusApi.Connecting += (sender, _) => { /* handle connecting event */ };
rustPlusApi.Connected += (sender, _) => { /* handle connected event */ };

rustPlusApi.MessageReceived += (sender, message) => { /* handle every message receive from the socket */ };
rustPlusApi.NotificationReceived += (sender, message) => { /* handle every notification (no direct request) from the socket */ };
rustPlusApi.ResponseReceived += (sender, message) => { /* handle every response (answer to a request) from the socket */ };

rustPlusApi.Disconnecting += (sender, _) => { /* handle disconnecting event */ };
rustPlusApi.Disconnected += (sender, _) => { /* handle disconnected event */ };

rustPlusApi.ErrorOccurred += (sender, ex) => { /* handle error event */ };
```

---

Remember to dispose the `RustPlusLegacy` instance when you're done:

```csharp
rustPlusApi.DisconnectAsync(); 
```

### RustPlus

The `RustPlus` classe inherit from `RustPlusLegacy` and provide a more user-friendly interface to interact with the Rust+ API. That means you can use all methods from `RustPlusLegacy` and also the new ones from `RustPlus`.

Such as the `RustPlusLegacy`, you need to instantiate the `RustPlus` class with the necessary parameters:

```csharp
var rustPlusApi = new RustPlus(server, port, playerId, playerToken, useFacepunchProxy);
```

---

There are quite the same methods as `RustPlusLegacy` but the response is a direct representation of `./Data/Response.cs` object.

```csharp
public class Response<T>
{
    public bool IsSuccess { get; set; }
    public Error? Error { get; set; }
    public T? Data { get; set; }
}

public class Error
{
    public string? Message { get; set; }
}
```

For example, to get the entity info:

```csharp
uint smartSwitchId = 123456789;
var response = await rustPlus.GetSmartSwitchInfoAsync(smartSwitchId);
```

Response will be a `Response<SmartSwitchInfo>` object.

```csharp
public class SmartSwitchInfo
{
    public bool IsActive { get; set; }
}
```

---

You can olso subscribe to more events to handle specific actions:

```csharp
rustPlusApi.OnSmartSwitchTriggered += (sender, smartSwitch) => { /* handle smart switch triggered event */ };
rustPlusApi.OnStorageMonitorTriggered += (sender, storageMonitor) => { /* handle storage monitor triggered event */ };

rustPlusApi.OnTeamChatReceived += (sender, message) => { /* handle team chat received event */ };
```

To be able to receive theses events, you need to previously make a request on the given entity or chat.

For example, to receive the smart switch triggered event, you need to make a request on the smart switch entity:

```csharp
rustPlus.OnSmartSwitchTriggered += (_, message) =>
{
    // ...
};

const uint entityId = 123456789;
var message = await rustPlus.GetSmartSwitchInfoAsync(entityId);
```

Each time the smart switch is triggered, the event will be fired.

---

Remember to dispose the `RustPlus` instance when you're done (such as `RustPlusLegacy`):

```csharp
rustPlusApi.DisconnectAsync(); 
```
