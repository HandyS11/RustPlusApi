# RustPlusApi

![CI](https://github.com/HandyS11/RustPlusApi/actions/workflows/CI.yml/badge.svg)
![CD](https://github.com/HandyS11/RustPlusApi/actions/workflows/CD.yml/badge.svg)

## 📊 Features

Some of the features that the **RustPlusApi** provides:

- `GetEntityInfo` Get current state of a Smart Device
- `GetInfo` Get info about the Rust Server
- `GetMap` Fetch map info, which inclues a jpg image
- `GetMapMarkers` Get map markers
- `GetTeamInfo` Get list of team members and positions on map
- `GetTime` Get the current in game time
- `SendTeamMessage` Send messages to Team Chat
- `SetEntityValue` Set the value of a Smart Device

Some of the features that the **RustPlusApi.Fcm** provides:

- `OnServerPairing` Event fired when the server is paired
- `OnEntityParing` Event fired when an entity is paired
- `OnAlarmTriggered` Event fired when an alarm is triggered

Feel free to **explore** the `samples/` folder to see how to **use** the API.

## 🖊️ Versions

![skills](https://skillicons.dev/icons?i=cs,dotnet)

- [.NET 8](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-8) or later

## 📚 Sumary

- [RustPlusApi](#rustplusapi)
  - [📊 Features](#-features)
  - [🖊️ Versions](#️-versions)
  - [📚 Sumary](#-sumary)
  - [📍 NuGet](#-nuget)
  - [⚙️ Usage](#️-usage)
    - [RustPlusApi](#rustplusapi-1)
    - [RustPlusApi.Fcm](#rustplusapifcm)
  - [Credentials](#credentials)
  - [🖼️ Credits](#️-credits)

The library provides 4 classes to interact with the Rust+ API: `RustPlusLegacy`, `RustPlus`, `RustPlusFcmListenerClient` & `RustPlusFcmListener`.

- `RustPlusLegacy` is the original implementation based on the `./Protobuf/RustPlus.proto` file.
- `RustPlus` is a new implementation that return a response based on `./Data/Response.cs` object.

Since `RustPlus` inherit from `RustPlusLegacy`, you can use both classes to interact with the Rust+ API. The `RustPlus` class is recommended for new projects, as it provides a more user-friendly interface and better error handling.

- `RustPlusListener` is a class to listen to the FCM socket and handle notifications.
- `RustPlusFcmListener` is a new implementation that own more events.

## 📍 NuGet

Simply use this library in your project by running the following commands:

```bash
dotnet add package RustPlusApi
```

```bash
dotnet add package RustPlusApi.Fcm
```

## ⚙️ Usage

### RustPlusApi

<details><summary> RustPlusLegacy </summary>

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

</details>

---

<details><summary> RustPlus </summary>

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

</details>

---

### RustPlusApi.Fcm

<details><summary> RustPlusFcmListenerClient </summary>

First, instantiate the `RustPlusFcmListenerClient` class with the necessary parameters:

```csharp
var rustPlusFcmListenerClient = new RustPlusFcmListenerClient(credentials, notificationIds);
```

Parameters:

- `credentials`: The FCM credentials\*.
- `notificationIds`: The notification ids to mark as read.

\* See the [Credentials](#credentials) section for more information.

Then, connect to the FCM server:

```csharp
await rustPlusFcmListenerClient.ConnectAsync();
```

---

To listen to the FCM notifications, you can use the `OnNotificationReceived` event:

```csharp
rustPlusFcmListenerClient.OnNotificationReceived += (sender, e) =>
{
    Console.WriteLine($"Notification received: {e.Notification}");
};
```

---

Don't forget to disconnect from the FCM server when you're done:

```csharp
rustPlusFcmListenerClient.Disconnect();
```

</details>

---

<details><summary> RustPlusFcmListener </summary>

The `RustPlusFcmListener` inherits from `RustPlusFcmListenerClient` and provides more events.

Such as `RustPlusFcmListenerClient` you need to instantiate the `RustPlusFcmListener` class with the necessary parameters:

```csharp
var rustPlusFcmListener = new RustPlusFcmListener(credentials, notificationIds);
```

---

Then you can connect to the FCM server:

```csharp
await rustPlusFcmListener.ConnectAsync();
```

---

You can subscribe to events to handle specific actions:

```csharp
rustPlusFcmListener.OnServerPairing += (sender, e) =>
{
    Console.WriteLine($"Server pairing: {e.ServerPairing}");
};

rustPlusFcmListener.OnEntityParing += (sender, e) =>
{
    Console.WriteLine($"Entity pairing: {e.EntityPairing}");
};

rustPlusFcmListener.OnAlarmTriggered += (sender, e) =>
{
    Console.WriteLine($"Alarm triggered: {e.Alarm}");
};
```

---

Don't forget to disconnect from the FCM server when you're done:

```csharp
rustPlusFcmListener.Disconnect();
```

</details>

---

## Credentials

Currenlty, there is not simple way to get the FCM & GCM credentials using **.NET**.
I've planned to implement a solution but it's not ready yet.

To use this library, you need to get the FCM & GCM credentials manually.
To do so I recommand you to use [this project](https://github.com/liamcottle/rustplus.js) to get the credentials.

I'm sorry for the inconvenience but since the API is not fully complete it's the easiest way.

## 🖼️ Credits

*This project is grandly inspired by [liamcottle/rustplus.js](https://github.com/liamcottle/rustplus.js).*

Special thanks to [**Versette**](https://github.com/Versette) for her work on the `RustPlusApi.Fcm` socket.

- Author: [**HandyS11**](https://github.com/HandyS11)
