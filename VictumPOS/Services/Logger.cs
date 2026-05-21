using System;
using System.IO;
using System.Linq;

namespace VictumPOS.Services
{
    public static class Logger
    {
        private static readonly string LogFilePath = SettingsService.ResolveDataPath("logs.txt");

        public static string LogPath { get { return LogFilePath; } }

        public static void Log(string message)
        {
            try
            {
                var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " - " + message;
                System.Diagnostics.Debug.WriteLine(line);
                Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath));
                File.AppendAllText(LogFilePath, line + Environment.NewLine);
            }
            catch
            {
            }
        }

        public static string ReadRecent(int maxLines = 80)
        {
            try
            {
                if (!File.Exists(LogFilePath))
                    return "No hay logs disponibles.";

                var lines = File.ReadAllLines(LogFilePath);
                return string.Join(Environment.NewLine, lines.Skip(Math.Max(0, lines.Length - Math.Max(1, maxLines))));
            }
            catch (Exception ex)
            {
                return "No se pudieron leer los logs: " + ex.Message;
            }
        }
    }
}
