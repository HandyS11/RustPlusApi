using RustPlusApi.Fcm.Registration.Steps;

namespace RustPlusApi.Fcm.Registration;

/// <summary>Optional inputs for the Steam login (<see cref="SteamLoginService"/>, or
/// <see cref="FcmRegistration.SteamLoginOptions"/>).</summary>
public sealed class SteamLoginOptions
{
    private readonly string? _browserPath;

    /// <summary>
    /// Gets the callback that asks the user for the login address by hand. It is used only when no browser can be
    /// driven: none was found, only Snap browsers were found, its temporary profile could not be created, or the
    /// browser failed to start. The login URL has
    /// already been reported through <c>onLoginUrl</c> by then. Return what the user pastes, the
    /// <c>rust-plus://rust-auth-session?…</c> address; surrounding whitespace, quotes and a leading <c>Location:</c>
    /// label are tolerated.
    /// </summary>
    /// <remarks>
    /// <para>The first argument is the reason the browser route was not used, as one or more full sentences:
    /// <c>No Chrome, Edge, Brave or Chromium browser was found.</c>, <c>Snap browsers are not supported (&lt;paths&gt;).</c>,
    /// <c>Could not create the temporary browser profile: &lt;error&gt;.</c> or
    /// <c>Could not start &lt;browser&gt;: &lt;error&gt;.</c> The sample prints it, followed by
    /// <see cref="SteamLoginService.PasteInstructions"/>, before reading the pasted line. The second argument is the
    /// login's cancellation token.</para>
    /// <para>When this is <see langword="null"/>, the login throws an <see cref="InvalidOperationException"/> instead,
    /// whose message is that reason, then the instructions, then the login URL.</para>
    /// </remarks>
    public Func<string, CancellationToken, Task<string>>? ReadPastedAddress { get; init; }

    /// <summary>
    /// Gets the absolute path of a Chromium-family browser executable (Chrome, Edge, Brave or Chromium) to use instead
    /// of searching the usual install locations. <see langword="null"/> or empty searches.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when set to a relative path: a relative name would be resolved from
    /// the current directory, where a planted look-alike could sit.</exception>
    /// <remarks>The login throws an <see cref="InvalidOperationException"/> when the file does not exist.</remarks>
    public string? BrowserPath
    {
        get => _browserPath;
        init => _browserPath = string.IsNullOrEmpty(value) || Path.IsPathRooted(value)
            ? value
            : throw new ArgumentException("The browser path must be absolute.", nameof(value));
    }
}
