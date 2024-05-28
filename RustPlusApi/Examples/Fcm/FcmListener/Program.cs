using RustPlusApi.Fcm;
using RustPlusApi.Fcm.Data;

var credentials = new Credentials
{
    Keys = new Keys
    {
        PrivateKey = "",
        PublicKey = "",
        AuthSecret = "",
    },
    Fcm = new FcmCredentials
    {
        Token = "",
        PushSet = "",
    },
    Gcm = new GcmCredentials
    {
        Token = "",
        AndroidId = 0,
        SecurityToken = 0,
        AppId = "",
    }
};

var listener = new FcmListener(credentials);

listener.NotificationReceived += (_, message) =>
{
    Console.WriteLine($"[MESSAGE]: {DateTime.Now}:\n{message}");
};

listener.ErrorOccurred += (_, error) =>
{
    Console.WriteLine($"[ERROR]: {error}");
};

await listener.ConnectAsync();