# RustPlusApi.Fcm

This is a C# client for the Rust+ websocket. It allows you to receive notification via FCM.

## Prerequisites

- **.NET 8** or later

## Usage

First, instantiate the `FcmListener` class with the necessary parameters:

```csharp
var fcmListener = new FcmListener(credentials, persistentIds);
```

Parameters:

- `credentials`: The `Credentials`* object containing the FCM & GCM credentials + the keys to decrypt the notification.
- `persistentIds`: A list of notification IDs that should be ignored. Default is null.

\* Go to the next section to see how to create a `Credentials` object.

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

## Credentials

Currenlty, there is not simple way to get the FCM & GCM credentials using .NET.
I've planned to implement a solution but it's not ready yet.

To use this library, you need to get the FCM & GCM credentials manually.
To do so I recommand you to use [this project](https://github.com/liamcottle/rustplus.js) to get the credentials.

I'm sorry for the inconvenience but since the API is not fully complete it's the easiest way.