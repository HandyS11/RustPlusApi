using RustPlusApi.Data;

using RustPlusContracts;

namespace RustPlusApi.Extensions;

public static class AppNexusAuthToModel
{
    public static NexusAuth ToNexusAuth(this AppNexusAuth appNexusAuth)
    {
        return new NexusAuth
        {
            ServerId = appNexusAuth.ServerId,
            PlayerToken = appNexusAuth.PlayerToken
        };
    }
}
