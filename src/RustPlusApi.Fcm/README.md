# RustPlusApi.Fcm

This is a C# client for the Rust+ websocket. It allows you to receive notification via FCM.

## RustPlusFcm

First, instantiate the `RustPlusFcm` class with the necessary parameters:

```csharp
var listener = new RustPlusFcm(credentials, notificationIds);
```

Parameters:

- `credentials`: The FCM credentials\*.
- `notificationIds`: The notification ids to mark as read.

\* See the [Credentials](#credentials) section below for how to obtain these.

---

Then you can connect to the FCM server:

```csharp
await listener.ConnectAsync();
```

---

You can subscribe to events to handle specific actions:

```csharp
listener.OnServerPairing += (sender, e) =>
{
    Console.WriteLine($"Server pairing: {e.ServerPairing}");
};

listener.OnEntityParing += (sender, e) =>
{
    Console.WriteLine($"Entity pairing: {e.EntityPairing}");
};

listener.OnAlarmTriggered += (sender, e) =>
{
    Console.WriteLine($"Alarm triggered: {e.Alarm}");
};
```

---

Remember to disconnect from the FCM server when you're done:

```csharp
listener.Disconnect();
```

## Credentials

Currently, there is no simple way to get the FCM credentials using .NET.

To use this library, you need to get the FCM credentials manually.
To do, so I recommend you to use [this project](https://github.com/liamcottle/rustplus.js) to get the credentials.

1. Clone the repository.
2. Install the dependencies using `npm install`.
3. Run `npm run cli/index.js fcm-register`
4. Proceed to log in with your Steam account.
5. The credentials will be in a file named `rustplus.config.json`.

I'm sorry for the inconvenience, but since the API is not fully complete, it's the easiest way.
