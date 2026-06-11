using RustPlusApi.Data;
using RustPlusContracts;

namespace RustPlusApi.Extensions;

/// <summary>Mapping extensions from protobuf Nexus auth messages to model types.</summary>
public static class AppNexusAuthToModel
{
    /// <summary>Maps an <see cref="AppNexusAuth"/> to a <see cref="NexusAuth"/>.</summary>
    /// <param name="appNexusAuth">The protobuf Nexus auth message.</param>
    public static NexusAuth ToNexusAuth(this AppNexusAuth appNexusAuth)
    {
        return new NexusAuth
        {
            ServerId = appNexusAuth.ServerId, PlayerToken = appNexusAuth.PlayerToken
        };
    }
}
