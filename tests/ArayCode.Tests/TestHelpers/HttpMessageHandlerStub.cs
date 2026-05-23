using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ArayCode.Tests;

/// <summary>
/// Minimal HttpMessageHandler stub that calls a user-supplied delegate per request.
/// </summary>
internal sealed class HttpMessageHandlerStub : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _respond;

    public HttpMessageHandlerStub(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
        => _respond = respond;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => await _respond(request);

    protected override void Dispose(bool disposing) { }
}
