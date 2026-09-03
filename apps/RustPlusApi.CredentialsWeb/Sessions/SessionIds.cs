using System.Security.Cryptography;

namespace RustPlusApi.CredentialsWeb.Sessions;

/// <summary>Generates the opaque identifiers used for both the session handle and the return token.</summary>
internal static class SessionIds
{
    /// <summary>A fresh 128-bit identifier as 32 lowercase hex characters.</summary>
    internal static string New() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
}
