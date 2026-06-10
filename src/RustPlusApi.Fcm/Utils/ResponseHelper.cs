using RustPlusApi.Fcm.Data;
using System.Globalization;

namespace RustPlusApi.Fcm.Utils;

/// <summary>Builds typed <see cref="Notification{T}"/> wrappers from raw FCM notification bodies.</summary>
public static class ResponseHelper
{
    /// <summary>Wraps a typed pairing payload with the player and server context from the notification body.</summary>
    /// <typeparam name="T">The type of the pairing payload.</typeparam>
    /// <param name="body">The raw notification body providing player and server context.</param>
    /// <param name="data">The typed pairing payload to wrap.</param>
    public static Notification<T?> BuildGenericOutput<T>(Body body, T data)
    {
        return new Notification<T?>
        {
            PlayerId = body.PlayerId,
            PlayerToken = int.Parse(body.PlayerToken, CultureInfo.InvariantCulture),
            ServerId = body.Id,
            Data = data
        };
    }
}
