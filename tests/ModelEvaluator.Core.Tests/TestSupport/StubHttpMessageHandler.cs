using System.Text;
using Azure.Core;

namespace ModelEvaluator.ApiComparison.Core.Tests.TestSupport;

internal sealed class CapturedRequest
{
    public required string Method { get; init; }

    public required Uri Uri { get; init; }

    public required string Body { get; init; }

    public required IReadOnlyDictionary<string, string> Headers { get; init; }
}

internal sealed class StubHttpMessageHandler(
    Func<int, HttpResponseMessage> responseFactory) : HttpMessageHandler
{
    private int _requestIndex;

    public List<CapturedRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? ""
            : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new CapturedRequest
        {
            Method = request.Method.Method,
            Uri = request.RequestUri!,
            Body = body,
            Headers = request.Headers.ToDictionary(
                header => header.Key,
                header => string.Join(",", header.Value),
                StringComparer.OrdinalIgnoreCase),
        });
        return responseFactory(_requestIndex++);
    }
}

internal static class TestResponses
{
    public static HttpResponseMessage Json(
        System.Net.HttpStatusCode statusCode,
        string json,
        TimeSpan? retryAfter = null)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        if (retryAfter is not null)
        {
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(retryAfter.Value);
        }

        return response;
    }
}

internal sealed class StaticTokenCredential(string token) : TokenCredential
{
    public override AccessToken GetToken(
        TokenRequestContext requestContext,
        CancellationToken cancellationToken) =>
        new(token, DateTimeOffset.UtcNow.AddHours(1));

    public override ValueTask<AccessToken> GetTokenAsync(
        TokenRequestContext requestContext,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(GetToken(requestContext, cancellationToken));
}
