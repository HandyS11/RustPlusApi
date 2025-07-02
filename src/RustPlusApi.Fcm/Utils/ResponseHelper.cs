using RustPlusApi.Fcm.Data;

namespace RustPlusApi.Fcm.Utils;

public static class ResponseHelper
{
    public static Notification<T?> BuildGenericOutput<T>(Body body, T data)
    {
        return new Notification<T?>
        {
            PlayerId = body.PlayerId,
            PlayerToken = int.Parse(body.PlayerToken),
            ServerId = body.Id,
            Data = data
        };
    }
}
