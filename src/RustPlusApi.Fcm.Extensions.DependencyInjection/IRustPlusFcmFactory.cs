using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Interfaces;

namespace RustPlusApi.Fcm.Extensions.DependencyInjection;

/// <summary>
/// Creates <see cref="IRustPlusFcm"/> listeners on demand for credentials acquired at runtime
/// (e.g. from <c>FcmRegistration</c>). Returned listeners are owned by the caller, who must
/// dispose them (prefer <c>await using</c>). FCM listeners are single-connection: create a new
/// one to reconnect.
/// </summary>
public interface IRustPlusFcmFactory
{
    /// <summary>Creates a new, unconnected listener for <paramref name="credentials"/>.</summary>
    /// <param name="credentials">The FCM credentials to authenticate with.</param>
    /// <param name="persistentIds">Already-processed message ids to skip, and the set new ids are
    /// harvested into. When <see langword="null"/>, the factory supplies a fresh empty list, so
    /// in-session deduplication is always enabled (unlike the
    /// <see cref="RustPlusApi.Fcm.RustPlusFcm"/> constructor, where <see langword="null"/> disables
    /// it). Read ids back via <c>PersistentIds</c> / <c>PersistentIdReceived</c> on the returned
    /// listener to persist them across reconnects.</param>
    /// <returns>A caller-owned <see cref="IRustPlusFcm"/>; call <c>ConnectAsync</c> to connect.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="credentials"/> is <see langword="null"/>.</exception>
    IRustPlusFcm Create(Credentials credentials, ICollection<string>? persistentIds = null);
}
