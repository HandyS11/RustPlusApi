# RustPlusApi.Fcm

This is a C# client for the Rust+ websocket. It allows you to receive notification via FCM.

## Prerequisites

- **.NET 8** or later

## Sumary

- [RustPlusApi.Fcm](#rustplusapifcm)
  - [Prerequisites](#prerequisites)
  - [Sumary](#sumary)
    - [RustPlusFcmListenerClient](#rustplusfcmlistenerclient)
    - [RustPlusFcmListener](#rustplusfcmlistener)
  - [Credentials](#credentials)

The library provides two classes to interact with the Rust+ API: `RustPlusFcmListenerClient` and `RustPlusFcmListener`.

- `RustPlusFcmListenerClient` is the base client to interact with FCM.
- `RustPlusFcmListener` is a new implementation that own more events.

Since `RustPlusFcmListener` inherit from `RustPlusFcmListenerClient`, you can use both classes to interact with FCM. The `RustPlus` class is recommended for new projects, as it provides more events.

### RustPlusFcmListenerClient

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

### RustPlusFcmListener

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

## Credentials

Currenlty, there is not simple way to get the FCM credentials & keys using .NET.
I've planned to implement a solution but it's not ready yet.

To use this library, you need to get the FCM credentials manually.
To do so I recommand you to use [this project](https://github.com/liamcottle/rustplus.js) to get the credentials.

I'm sorry for the inconvenience but since the API is not fully complete it's the easiest way.
