using System.Net;
using System.Text;

namespace BudgetApp.Tests;

/// <summary>Answers HTTP requests with a canned response and remembers what was sent. No real network.</summary>
internal sealed class FakeHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public HttpRequestMessage? Request { get; private set; }
    public string? RequestBody { get; private set; }
    public int Calls { get; private set; }

    public static FakeHttpHandler Returning(HttpStatusCode status, string body) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        }));

    /// <summary>Never answers; only the caller's cancellation (the timeout) ends the wait.</summary>
    public static FakeHttpHandler Hanging() =>
        new(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("unreachable");
        });

    public static FakeHttpHandler Throwing(Exception e) => new((_, _) => throw e);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        Request = request;
        RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return await respond(request, cancellationToken);
    }
}
