using RustPlusApi.Fcm.Data;

namespace RustPlusApi.Fcm.Tools
{
    public static class Register
    {
        public static async Task<Credentials> RegisterAsync(string senderId)
        {
            var appId = $"wp:receiver.push.com#{Guid.NewGuid()}";

            var gcmSubscription = await GcmTools.RegisterAsync(appId);
            var fcmRegistration = await FcmTools.RegisterFcmAsync(senderId, gcmSubscription.Token);

            return new Credentials
            {
                Keys = fcmRegistration.Item1,
                Gcm = gcmSubscription,
                Fcm = fcmRegistration.Item2,
            };
        }
    }
}
