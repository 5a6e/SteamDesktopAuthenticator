using System;

namespace Steam_Desktop_Authenticator
{
    internal static class SteamDeviceName
    {
        private static readonly string[] Models =
        {
            "Galaxy S24",
            "Galaxy S23",
            "Galaxy A54",
            "Pixel 8",
            "Pixel 7",
            "Pixel 8a",
            "Redmi Note 13",
            "Redmi K70",
            "Xiaomi 14",
            "OPPO Find X7",
            "vivo X100",
            "OnePlus 12",
            "POCO F6",
            "motorola edge 40"
        };

        public static string Generate()
        {
            string model = Models[System.Random.Shared.Next(Models.Length)];
            int suffix = System.Random.Shared.Next(1000, 10000);
            return $"{model} {suffix}";
        }
    }
}
