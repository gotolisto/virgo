using System.Net;
using System.Text;

namespace TrayTranslator.Tests.Support;

/// <summary>Answers HTTP requests from a script and records what was sent.</summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<CancellationToken, Task<HttpResponseMessage>>> _responses = new();

    public List<RecordedRequest> Requests { get; } = [];

    public FakeHttpHandler Respond(HttpStatusCode status, string body = "{}", Action<HttpResponseMessage>? configure = null)
    {
        _responses.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            configure?.Invoke(response);
            return Task.FromResult(response);
        });
        return this;
    }

    public FakeHttpHandler Respond(int status, string body = "{}") => Respond((HttpStatusCode)status, body);

    public FakeHttpHandler Throw(Exception ex)
    {
        _responses.Enqueue(_ => Task.FromException<HttpResponseMessage>(ex));
        return this;
    }

    /// <summary>Never answers; the request ends only when it is cancelled.</summary>
    public FakeHttpHandler Hang()
    {
        _responses.Enqueue(async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        });
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase),
            request.Content?.Headers.ContentType?.MediaType,
            body));

        return _responses.Count == 0
            ? throw new InvalidOperationException("No scripted response left.")
            : await _responses.Dequeue()(cancellationToken);
    }
}

public sealed record RecordedRequest(
    HttpMethod Method,
    Uri Uri,
    IReadOnlyDictionary<string, string> Headers,
    string? ContentType,
    string? Body);
