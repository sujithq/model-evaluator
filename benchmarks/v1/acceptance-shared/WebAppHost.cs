using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ModelEvaluator.Acceptance;

/// <summary>
/// Starts a generated ASP.NET Core application on a free loopback port and waits until it answers requests.
/// </summary>
public sealed class WebAppHost : IAsyncDisposable
{
    private readonly Process _process;
    private readonly StringBuilder _output;

    private WebAppHost(Process process, HttpClient client, string baseAddress, string workingDirectory, StringBuilder output)
    {
        _process = process;
        _output = output;
        Client = client;
        BaseAddress = baseAddress;
        WorkingDirectory = workingDirectory;
    }

    public HttpClient Client { get; }

    public string BaseAddress { get; }

    public string WorkingDirectory { get; }

    public string Output
    {
        get
        {
            lock (_output)
            {
                return _output.ToString();
            }
        }
    }

    public static async Task<WebAppHost> StartAsync(
        string assemblyPath,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment = null,
        string readinessPath = "/",
        int timeoutSeconds = 90)
    {
        var port = GetFreePort();
        var baseAddress = $"http://127.0.0.1:{port}";
        var variables = new Dictionary<string, string>(environment ?? new Dictionary<string, string>())
        {
            ["ASPNETCORE_URLS"] = baseAddress,
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
            ["DOTNET_ENVIRONMENT"] = "Production",
        };

        var output = new StringBuilder();
        var process = AppRunner.Start(assemblyPath, [], workingDirectory, variables, output);
        var client = new HttpClient { BaseAddress = new Uri(baseAddress), Timeout = TimeSpan.FromSeconds(30) };
        var host = new WebAppHost(process, client, baseAddress, workingDirectory, output);

        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                await host.DisposeAsync().ConfigureAwait(false);
                throw new InvalidOperationException(
                    $"The application exited with code {process.ExitCode} before serving requests.{Environment.NewLine}{output}");
            }

            try
            {
                using var response = await client.GetAsync(readinessPath).ConfigureAwait(false);
                if (response.StatusCode != HttpStatusCode.ServiceUnavailable)
                {
                    return host;
                }
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }
            catch (TaskCanceledException)
            {
                // Request timed out; retry until the deadline.
            }

            await Task.Delay(250).ConfigureAwait(false);
        }

        await host.DisposeAsync().ConfigureAwait(false);
        throw new TimeoutException(
            $"The application did not answer on {baseAddress}{readinessPath} within {timeoutSeconds} s.{Environment.NewLine}{output}");
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        AppRunner.Kill(_process);
        _process.Dispose();
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
