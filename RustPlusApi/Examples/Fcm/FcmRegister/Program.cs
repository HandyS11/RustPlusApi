using RustPlusApi.Fcm;
using Newtonsoft.Json;

var senderId = "976529667804";
var credentials = await FcmRegister.RegisterAsync(senderId);

Console.WriteLine(JsonConvert.SerializeObject(credentials, Formatting.Indented));