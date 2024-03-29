# RustPlusApi

This is a C# client for the Rust+ API. It allows you to interact with the Rust+ server.

## Prerequisites

- **.NET 8** or later

## Usage

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

Remember to dispose the `RustPlus` instance when you're done: