using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using System.Net;

namespace RustPlusApi.CredentialsWeb.UnitTests;

/// <summary>Stamps a connection address onto every request, because <c>TestServer</c> leaves
/// <c>RemoteIpAddress</c> null and the app's local/remote decision reads it. Registered ahead of the
/// app's own pipeline through <see cref="IStartupFilter"/>, so it runs before any endpoint.</summary>
/// <param name="address">Supplies the address per request, so a test can change it after the host
/// has started.</param>
internal sealed class RemoteIpStartupFilter(Func<IPAddress?> address) : IStartupFilter
{
    /// <inheritdoc/>
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        builder =>
        {
            builder.Use(async (context, chain) =>
            {
                context.Connection.RemoteIpAddress = address();
                await chain(context).ConfigureAwait(false);
            });

            next(builder);
        };
}
