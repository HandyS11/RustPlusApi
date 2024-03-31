using RustPlusApi.Fcm;

var register = new FcmRegister();
var credentials = await register.RegisterAsync("976529667804");

Console.ReadKey();