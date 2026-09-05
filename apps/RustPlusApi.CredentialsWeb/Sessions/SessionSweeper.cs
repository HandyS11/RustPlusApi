using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RustPlusApi.CredentialsWeb.Sessions;

/// <summary>Disposes sessions past their TTL, which is what actually bounds how long an MCS socket
/// and a set of credentials can live in memory.</summary>
/// <param name="store">The registry to sweep.</param>
/// <param name="timeProvider">Clock, injected so the interval is testable.</param>
/// <param name="logger">For logging sweep failures without losing the exception and retrying on the next tick.</param>
internal sealed class SessionSweeper(SessionStore store, TimeProvider timeProvider, ILogger<SessionSweeper> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    store.SweepExpired();
                }
                catch (Exception ex)
                {
                    logger.LogSweepFailed(ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
    }
}
