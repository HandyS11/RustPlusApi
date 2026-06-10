using System.Net;
using System.Net.Http;

namespace RustPlusApi.Fcm.Registration.UnitTests.TestHelpers;

/// <summary>
/// A scriptable <see cref="HttpMessageHandler"/> for offline-testing the registration HTTP steps.
/// Records every outgoing request and returns responses produced by the supplied factory.
/// </summary>
/// <param name="responder">Factory that maps each request and its zero-based call index to a response.</param>
public sealed class StubHttpMessageHandler(
    Func<HttpRequestMessage, int, HttpResponseMessage> responder) : HttpMessageHandler
{
    private int _callIndex;

    /// <summary>Every request the client sent, in order.</summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>The buffered request bodies, index-aligned with <see cref="Requests"/>.</summary>
    public List<byte[]> RequestBodies { get; } = [];

    /// <summary>Convenience ctor: always return the same status + body.</summary>
    /// <param name="status">The HTTP status code to return for every request.</param>
    /// <param name="body">The raw byte body to include in every response.</param>
    public static StubHttpMessageHandler Always(HttpStatusCode status, byte[] body) =>
        new((_, _) => new HttpResponseMessage(status)
        {
            Content = new ByteArrayContent(body)
        });

    /// <summary>Convenience ctor: always return the same status + string body.</summary>
    /// <param name="status">The HTTP status code to return for every request.</param>
    /// <param name="body">The string body to include in every response.</param>
    public static StubHttpMessageHandler Always(HttpStatusCode status, string body) =>
        new((_, _) => new HttpResponseMessage(status)
        {
            Content = new StringContent(body)
        });

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var index = _callIndex++;
        Requests.Add(request);
        RequestBodies.Add(request.Content is null
            ? []
            : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false));
        return responder(request, index);
    }

    /// <summary>Builds an <see cref="HttpClient"/> bound to this handler.</summary>
    public HttpClient CreateClient() => new(this);
}
