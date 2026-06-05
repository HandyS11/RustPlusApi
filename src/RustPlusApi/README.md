# RustPlusApi

This is a C# client for the Rust+ API. It allows you to interact with the Rust+ server.

The library exposes the `RustPlus` client to interact with the Rust+ API. Every request
returns a typed response based on the `./Data/Response.cs` object.

## RustPlus

First, instantiate the `RustPlus` class with the necessary parameters:

```csharp
var rustPlusApi = new RustPlus(server, port, playerId, playerToken, useFacepunchProxy);
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

You can subscribe to the socket lifecycle events to handle specific actions:

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

Every request returns a typed response that is a direct representation of the `./Data/Response.cs` object.

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

You can also subscribe to more events to handle specific actions:

```csharp
rustPlusApi.OnSmartSwitchTriggered += (sender, smartSwitch) => { /* handle smart switch triggered event */ };
rustPlusApi.OnStorageMonitorTriggered += (sender, storageMonitor) => { /* handle storage monitor triggered event */ };

rustPlusApi.OnTeamChatReceived += (sender, message) => { /* handle team chat received event */ };
rustPlusApi.OnClanChatReceived += (sender, message) => { /* handle clan chat received event */ };
rustPlusApi.OnClanChanged += (sender, clan) => { /* handle clan changed event */ };
```

To be able to receive these events, you need to previously make a request on the given entity or chat.

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

Remember to dispose the `RustPlus` instance when you're done:

```csharp
rustPlusApi.DisconnectAsync(); 
```
