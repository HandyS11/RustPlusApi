using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Tools;

namespace RustPlusApi.Fcm
{
    public class FcmRegister
    {
        public async Task<Credentials> RegisterApp(string senderId)
        {
            return await Register.RegisterAsync(senderId);
        }
    }
}
