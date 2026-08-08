using System.Net;
using System.Net.Http.Headers;

namespace GitHubGoal.Core.Services;

/// <summary>Builds the shared <see cref="HttpClient"/> used for every GitHub call.</summary>
public static class GitHubHttp
{
    public static HttpClient Create()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            // Long enough to notice DNS/route changes when the machine wakes or switches network.
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };

        var client = new HttpClient(handler)
        {
            // Keeps a hung request from stalling the refresh timer.
            Timeout = TimeSpan.FromSeconds(20),
        };

        // GitHub rejects requests without a User-Agent.
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GitHubGoal", "1.0"));

        return client;
    }
}
