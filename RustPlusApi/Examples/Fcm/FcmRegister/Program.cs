using RustPlusApi.Fcm;

var register = new FcmRegister();
var credentials = await register.RegisterApp("976529667804");

Console.ReadKey();