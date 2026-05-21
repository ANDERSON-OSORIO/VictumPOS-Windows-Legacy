using System;
using System.IO;
using System.Net;

namespace VictumPOS.PrintBridge.Service.Services
{
    internal sealed class BridgeOptions
    {
        public int Port { get; set; }
        public string SettingsPath { get; set; }
        public IPAddress BindAddress { get; set; }

        public static BridgeOptions FromArgs(string[] args, int defaultPort)
        {
            var port = defaultPort;
            var settingsPath = "";
            var bindAddress = IPAddress.Any;

            for (var i = 0; i < args.Length; i++)
            {
                int parsedPort;
                IPAddress parsedBind;

                if (Is(args[i], "port") && i + 1 < args.Length && int.TryParse(args[i + 1], out parsedPort))
                {
                    port = parsedPort;
                    i++;
                    continue;
                }

                if (Is(args[i], "bind") && i + 1 < args.Length && IPAddress.TryParse(args[i + 1], out parsedBind))
                {
                    bindAddress = parsedBind;
                    i++;
                    continue;
                }

                if (Is(args[i], "settings") && i + 1 < args.Length)
                {
                    settingsPath = args[i + 1];
                    i++;
                }
            }

            if (string.IsNullOrWhiteSpace(settingsPath))
                settingsPath = Path.Combine(ResolveSettingsFolderPath(), "settings.json");

            return new BridgeOptions
            {
                Port = port <= 0 ? defaultPort : port,
                SettingsPath = settingsPath,
                BindAddress = bindAddress
            };
        }

        private static bool Is(string value, string expected)
        {
            return value.Equals("--" + expected, StringComparison.OrdinalIgnoreCase)
                || value.Equals(expected, StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveSettingsFolderPath()
        {
            var commonFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "VictumPOS");
            var localFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VictumPOS");
            return Directory.Exists(commonFolderPath) ? commonFolderPath : localFolderPath;
        }
    }
}
