# RustPlusApi

## 📊 Features

### RustPlusApi

This is a list of the features that the Rust+ API provides:

- `GetEntityInfo` Get current state of a Smart Device
- `GetInfo` Get info about the Rust Server
- `GetMap` Fetch map info, which inclues a jpg image
- `GetMapMarkers` Get map markers
- `GetTeamInfo` Get list of team members and positions on map
- `GetTime` Get the current in game time
- `SendTeamMessage` Send messages to Team Chat
- `SetEntityValue` Set the value of a Smart Device
- `StrobeAsync` Strobe a Smart Device

Feel free to **explore** the `./RustPlusApi/Examples/` folder to see how to **use** the API.

### RustPlusApi.Fcm



## 🖊️ Versions 

![skills](https://skillicons.dev/icons?i=cs,dotnet)

- [.NET 8](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-8) or later

## 📍 NuGet

Simply use this library in your project by running the following commands:

```dotnet
dotnet add package RustPlusApi
```

```dotnet
dotnet add package RustPlusApi.Fcm
```

## ⚙️ Usage

### RustPlusApi

First, instantiate the `RustPlus` class with the necessary parameters:

```csharp
var rustPlusApi = new RustPlus(server, port, playerId, playerToken, useFacepunchProxy);
```

Parameters:

- `server`: The IP address of the Rust+ server.
- `port`: The port dedicated for the Rust+ companion app (not the one used to connect in-game).
- `playerId`: Your Steam ID.
- `playerToken`\*: Your player token acquired with FCM.
- `useFacepunchProxy`: Specifies whether to use the Facepunch proxy. Default is false.

\* To aquired the player token, you can use the `FcmListener` and received at least one notification. Go to the next section to see how to use it.

Then, connect to the Rust+ server:

```csharp
await rustPlusApi.ConnectAsync();
```

You can subscribe to events to handle connection, disconnection, errors, and received messages:

```csharp
rustPlusApi.Connecting += (sender, e) => { /* handle connecting event */ };
rustPlusApi.Connected += (sender, e) => { /* handle connected event */ };
rustPlusApi.Disconnected += (sender, e) => { /* handle disconnected event */ };
rustPlusApi.ErrorOccurred += (sender, e) => { /* handle error event */ };
rustPlusApi.MessageReceived += (sender, e) => { /* handle received message event */ };
```

Remember to dispose the `RustPlus` instance when you're done:

```csharp
rustPlusApi.Dispose(); 
```

---

### RustPlusApi.Fcm

First, instantiate the `FcmListener` class with the necessary parameters:

```csharp
var fcmListener = new FcmListener(credentials, persistentIds);
```

Parameters:

- `credentials`: The `Credentials`\* object containing the FCM & GCM credentials + the keys to decrypt the notification.
- `persistentIds`: A list of notification IDs that should be ignored. Default is null.

\* Go to the [Credentials](#credentials) section to know how to get it.

Then, connect to the FCM socket:

```csharp
await fcmListener.ConnectAsync();
```

You can subscribe to events to handle connection, disconnection, errors, and received messages:

```csharp
fcmListener.Connecting += (sender, e) => { /* handle connecting event */ };
fcmListener.Connected += (sender, e) => { /* handle connected event */ };
fcmListener.Disconnected += (sender, e) => { /* handle disconnected event */ };
fcmListener.ErrorOccurred += (sender, e) => { /* handle error event */ };
fcmListener.MessageReceived += (sender, e) => { /* handle received message event */ };
```

Remember to dispose the `FcmListener` instance when you're done:

```csharp
fcmListener.Dispose(); 
```

### Credentials

Currenlty, there is not simple way to get the FCM & GCM credentials using **.NET**.
I've planned to implement a solution but it's not ready yet.

To use this library, you need to get the FCM & GCM credentials manually.
To do so I recommand you to use [this project](https://github.com/liamcottle/rustplus.js) to get the credentials.

I'm sorry for the inconvenience but since the API is not fully complete it's the easiest way.

## 🖼️ Credits

*This project is grandly inspired by [liamcottle/rustplus.js](https://github.com/liamcottle/rustplus.js).*

Special thanks to [**Versette**](https://github.com/Versette) for his work on the `RustPlusApi.Fcm` socket.

* Author: [**HandyS11**](https://github.com/HandyS11)