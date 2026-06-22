using RustPlusApi.Fcm.Data;
using System.Globalization;

namespace RustPlusApi.Fcm.Utils;

/// <summary>Builds typed <see cref="Notification{T}"/> wrappers from raw FCM notification bodies.</summary>
public static class ResponseHelper
{
    /// <summary>Wraps a typed pairing payload with the player/server context and persistent id from the notification.</summary>
    /// <typeparam name="T">The type of the pairing payload.</typeparam>
    /// <param name="body">The raw notification body providing player and server context.</param>
    /// <param name="data">The typed pairing payload to wrap.</param>
    /// <param name="persistentId">The FCM persistent id of the underlying message (may be <see langword="null"/>).</param>
    public static Notification<T?> BuildGenericOutput<T>(Body body, T data, string? persistentId)
    {
        return new Notification<T?>
        {
            PlayerId = body.PlayerId,
            PlayerToken = int.Parse(body.PlayerToken, CultureInfo.InvariantCulture),
            ServerId = body.Id,
            PersistentId = persistentId,
            Data = data
        };
    }
}
