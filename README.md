# RustPlusApi

## 📊 Features

This is a list of the features that the Rust+ API provides:

- `SetEntityValue` Set the value of a Smart Device
- `StrobeAsync` Strobe a Smart Device
- `SendTeamMessage` Send messages to Team Chat
- `GetEntityInfo` Get current state of a Smart Device
- `GetMap` Fetch map info, which inclues a jpg image
- `GetTime` Get the current in game time
- `GetInfo` Get info about the Rust Server
- `GetTeamInfo` Get list of team members and positions on map

Feel free to **explore** the `./RustPlusApi/Examples/` folder to see how to **use** the API.

## 🖊️ Versions 

![skills](https://skillicons.dev/icons?i=cs,dotnet)

- [.NET 8](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-8) or later

## 📍 NuGet

Simply use this nuget by running the following command:

```dotnet
dotnet add package RustPlusApi
```

## ⚙️ Usage

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

You can subscribe to events to handle connection, disconnection, errors, and received messages:

```csharp
rustPlusApi.Connecting += (sender, e) => { /* handle connecting event */ };
rustPlusApi.Connected += (sender, e) => { /* handle connected event */ };
rustPlusApi.Disconnected += (sender, e) => { /* handle disconnected event */ };
rustPlusApi.ErrorOccurred += (sender, e) => { /* handle error event */ };
rustPlusApi.MessageReceived += (sender, e) => { /* handle received message event */ };
```

Remember to dispose of the `RustPlus` instance when you're done:

## 🖼️ Credits

This project is grandly inspired by [liamcottle/rustplus.js](https://github.com/liamcottle/rustplus.js).

* Author: [**HandyS11**](https://github.com/HandyS11)