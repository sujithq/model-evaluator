using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ModelEvaluator.Core.Tests.TestSupport;

internal sealed class LoopbackHttpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly TaskCompletionSource<CapturedLoopbackRequest> _request =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _serverTask;

    public LoopbackHttpServer(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _listener.Start();
        Endpoint = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}");
        _serverTask = ServeOneAsync(responseBody, statusCode);
    }

    public Uri Endpoint { get; }

    public Task<CapturedLoopbackRequest> Request => _request.Task;

    public async ValueTask DisposeAsync()
    {
        _listener.Stop();
        try
        {
            await _serverTask;
        }
        catch (SocketException)
        {
            // The test has already stopped listening if the SDK did not make a request.
        }
    }

    private async Task ServeOneAsync(string responseBody, HttpStatusCode statusCode)
    {
        using var client = await _listener.AcceptTcpClientAsync();
        await using var stream = client.GetStream();
        var headerBytes = new List<byte>();
        var matched = 0;
        while (matched < 4)
        {
            var value = stream.ReadByte();
            if (value < 0)
            {
                break;
            }

            headerBytes.Add((byte)value);
            matched = headerBytes.Count >= 4
                ? EndsInHeaderDelimiter(headerBytes) ? 4 : 0
                : 0;
        }

        var headerText = Encoding.ASCII.GetString(headerBytes.ToArray());
        var headerLines = headerText.Split(["\r\n"], StringSplitOptions.None);
        var requestLine = headerLines[0].Split(' ', 3);
        var contentLength = headerLines
            .Select(line => line.Split(':', 2))
            .Where(parts => parts.Length == 2
                            && parts[0].Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            .Select(parts => int.Parse(parts[1].Trim()))
            .FirstOrDefault();
        var bodyBytes = new byte[contentLength];
        var total = 0;
        while (total < contentLength)
        {
            var read = await stream.ReadAsync(bodyBytes.AsMemory(total, contentLength - total));
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        _request.TrySetResult(new CapturedLoopbackRequest
        {
            Method = requestLine[0],
            Path = requestLine[1],
            Headers = headerLines.Skip(1)
                .Select(line => line.Split(':', 2))
                .Where(parts => parts.Length == 2)
                .ToDictionary(parts => parts[0], parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase),
            Body = Encoding.UTF8.GetString(bodyBytes, 0, total),
        });

        var body = Encoding.UTF8.GetBytes(responseBody);
        var statusText = statusCode == HttpStatusCode.OK ? "200 OK" : $"{(int)statusCode} Error";
        var responseHeader =
            $"HTTP/1.1 {statusText}\r\n" +
            "Content-Type: application/json\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(responseHeader));
        await stream.WriteAsync(body);
        await stream.FlushAsync();
    }

    private static bool EndsInHeaderDelimiter(List<byte> bytes) =>
        bytes[^4] == '\r'
        && bytes[^3] == '\n'
        && bytes[^2] == '\r'
        && bytes[^1] == '\n';
}

internal sealed class CapturedLoopbackRequest
{
    public required string Method { get; init; }

    public required string Path { get; init; }

    public required IReadOnlyDictionary<string, string> Headers { get; init; }

    public required string Body { get; init; }
}
