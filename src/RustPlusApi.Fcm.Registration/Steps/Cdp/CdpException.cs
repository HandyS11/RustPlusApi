namespace RustPlusApi.Fcm.Registration.Steps.Cdp;

/// <summary>The browser rejected a DevTools command, or closed the debugging connection.</summary>
#pragma warning disable CA1064, S3871 // Deliberately internal: callers outside the package never see CDP details.
internal sealed class CdpException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="CdpException"/> class.</summary>
    public CdpException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="CdpException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public CdpException(string message) : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="CdpException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying exception, or <see langword="null"/> when there is none (for
    /// example when the browser closed the connection normally).</param>
    public CdpException(string message, Exception? innerException) : base(message, innerException)
    {
    }
}
#pragma warning restore CA1064, S3871
