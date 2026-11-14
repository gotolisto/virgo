using System.Net;
using System.Net.Http.Headers;

namespace TrayTranslator.App.Infrastructure;

internal static class HttpClients
{
    /// <summary>
    /// One shared client for both engines. The per-request timeout (8 s) is applied by Core, so the
    /// client's own timeout is off; connections are recycled every few minutes to pick up DNS changes.
    /// </summary>
    public static HttpClient CreateForEngines()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(5),
            AutomaticDecompression = DecompressionMethods.All,
        };

        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TrayTranslator", AppInfo.Version));
        return client;
    }
}
