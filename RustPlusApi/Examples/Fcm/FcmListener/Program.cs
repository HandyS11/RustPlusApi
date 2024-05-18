using RustPlusApi.Fcm;
using RustPlusApi.Fcm.Data;
using Newtonsoft.Json;

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

var listener = new FcmListener(credentials, []);

listener.NotificationReceived += (_, message) =>
{
    Console.WriteLine(JsonConvert.SerializeObject(message, Formatting.Indented));
};

await listener.ConnectAsync();