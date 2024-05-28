using Newtonsoft.Json;

using RustPlusApi.Fcm;
using RustPlusApi.Fcm.Data;

var credentials = new Credentials
{
    Keys = new Keys
    {
        PrivateKey = "PNo-juMIoy8nh45ap7CcOjmvCcXG71zxo1Kf6sG75yI",
        PublicKey = "BMLRwfGJ3poc2ih6eIQpf-7xwkP9z98K8vh-bWzxypDERTUIyAqpulccHR6WBVP8jgoecNtYePTYvSc-sHGhWcY",
        AuthSecret = "3_stBv0R105hCQJgJ_Oqag",
    },
    Fcm = new FcmCredentials
    {
        Token = "de0gThSZW5A:APA91bEpz3S-tIVNV4uoKKNc_UCA_8tcg5lxEWiXkx-zFb0-H6FG5ltQvkGYzfxcfm3GxNihFgfRvH7Ps0_IdvRpOvjVREsm8uJwhkt8ztaoOZ886osG-bvGGVfV2jt1rPDG1_22NoMm",
        PushSet = "dEhc-sLR9LY",
    },
    Gcm = new GcmCredentials
    {
        Token = "ctMfS0V2BB0:APA91bG5LKmV_pe27v7Drm4OBYMkZS8eItKHUX-wG5eUw1WmKFTSkwYr4hw43AQmP7oBwHzeKbMRDEmaml0lh2CBvh0s3pXxrDSng6au3t-iE1FkEhpBOwaDh74Hk2z6Y8GcFTcaPsA2",
        AndroidId = 5198350057269518718,
        SecurityToken = 726893779159876265,
        AppId = "wp:receiver.push.com#46616054-1728-4be7-9dc6-740f3e76d9f4",
    }
};

var listener = new FcmListener(credentials);

listener.NotificationReceived += (_, message) =>
{
    Console.WriteLine($"{DateTime.Now}:\n{message}");
};

listener.ErrorOccurred += (_, error) =>
{
    Console.WriteLine($"[ERROR]: {error}");
};

await listener.ConnectAsync();