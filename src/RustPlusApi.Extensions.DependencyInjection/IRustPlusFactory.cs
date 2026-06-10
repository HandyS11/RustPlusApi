using RustPlusApi.Interfaces;

namespace RustPlusApi.Extensions.DependencyInjection;

/// <summary>
/// Creates <see cref="IRustPlus"/> clients on demand for connections known only at runtime.
/// Returned clients are owned by the caller, who must dispose them (prefer <c>await using</c>).
/// </summary>
public interface IRustPlusFactory
{
    /// <summary>Creates a new, unconnected client for <paramref name="connection"/>.</summary>
    /// <param name="connection">The server endpoint and player credentials the client connects as.</param>
    /// <returns>A caller-owned <see cref="IRustPlus"/>; call <c>ConnectAsync</c> to connect.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/> is <see langword="null"/>.</exception>
    IRustPlus Create(RustPlusConnection connection);
}
