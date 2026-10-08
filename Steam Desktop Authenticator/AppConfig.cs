using Newtonsoft.Json;
using System;
using System.IO;

namespace Steam_Desktop_Authenticator
{
    public class AppConfig
    {
        public const int DefaultSteamConnectTimeoutSeconds = 15;
        public const string FileName = "config.json";

        [JsonProperty("steamConnectTimeoutSeconds")]
        public int SteamConnectTimeoutSeconds { get; set; } = DefaultSteamConnectTimeoutSeconds;

        public static int GetSteamConnectTimeoutSeconds()
        {
            AppConfig config = Load();
            if (config.SteamConnectTimeoutSeconds <= 0)
                return DefaultSteamConnectTimeoutSeconds;
            return config.SteamConnectTimeoutSeconds;
        }

        public static AppConfig Load()
        {
            try
            {
                string path = Path.Combine(Manifest.GetExecutableDir(), FileName);
                if (!File.Exists(path))
                    return new AppConfig();

                string json = File.ReadAllText(path);
                AppConfig loaded = JsonConvert.DeserializeObject<AppConfig>(json);
                return loaded ?? new AppConfig();
            }
            catch
            {
                return new AppConfig();
            }
        }
    }
}
