using System;
using System.Net.Http;
using SteamAuth;
using SteamKit2;

namespace Steam_Desktop_Authenticator
{
    static class SteamClientFactory
    {
        public static SteamClient Create()
        {
            int timeoutSeconds = AppConfig.GetSteamConnectTimeoutSeconds();
            var config = SteamConfiguration.Create(builder =>
            {
                builder.WithProtocolTypes(ProtocolTypes.WebSocket);
                builder.WithConnectionTimeout(TimeSpan.FromSeconds(timeoutSeconds));
                builder.WithHttpClientFactory(CreateHttpClient);
            });
            return new SteamClient(config);
        }

        static HttpClient CreateHttpClient()
        {
            var handler = new SocketsHttpHandler
            {
                UseProxy = true,
                Proxy = HttpClient.DefaultProxy,
                ConnectTimeout = TimeSpan.FromSeconds(AppConfig.GetSteamConnectTimeoutSeconds()),
            };

            var client = new HttpClient(handler);
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", SteamWeb.MOBILE_APP_USER_AGENT);
            return client;
        }
    }
}
