using System;
using System.IO;

namespace VictumPOS.PrintBridge.Service.Services
{
    internal static class BridgeLogger
    {
        private static readonly string LogPath = Path.Combine(ResolveSettingsFolderPath(), "print-bridge.log");

        public static void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " - " + message + Environment.NewLine);
            }
            catch
            {
            }
        }

        private static string ResolveSettingsFolderPath()
        {
            var commonFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "VictumPOS");
            var localFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VictumPOS");
            return Directory.Exists(commonFolderPath) ? commonFolderPath : localFolderPath;
        }
    }
}
