# RustPlusApi.Fcm

This is a C# client for the Rust+ websocket. It allows you to receive notification via FCM.

## Prerequisites

- **.NET 8** or later

## Sumary 

- [RustPlusFcmListenerClient](#RustPlusFcmListenerClient)
- [RustPlusFcmListener](#RustPlusFcmListener)

The library provides two classes to interact with the Rust+ API: `RustPlusFcmListenerClient` and `RustPlusFcmListener`.

- `RustPlusFcmListenerClient` is the base client to interact with FCM.
- `RustPlusFcmListener` is a new implementation that own more events.

Since `RustPlusFcmListener` inherit from `RustPlusFcmListenerClient`, you can use both classes to interact with FCM. The `RustPlus` class is recommended for new projects, as it provides more events.

### RustPlusFcmListenerClient


### RustPlusFcmListener

## Credentials

Currenlty, there is not simple way to get the FCM credentials & keys using .NET.
I've planned to implement a solution but it's not ready yet.

To use this library, you need to get the FCM credentials manually.
To do so I recommand you to use [this project](https://github.com/liamcottle/rustplus.js) to get the credentials.

I'm sorry for the inconvenience but since the API is not fully complete it's the easiest way.