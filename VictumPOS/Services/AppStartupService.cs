using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace VictumPOS.Services
{
    public static class AppStartupService
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "VictumPOS";

        public static bool IsEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                    return !string.IsNullOrWhiteSpace(key?.GetValue(RunValueName)?.ToString());
            }
            catch
            {
                return false;
            }
        }

        public static string Apply(bool enabled)
        {
            try
            {
                if (!enabled)
                {
                    Disable();
                    return "Inicio con Windows desactivado.";
                }

                var exe = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(exe))
                    return "No se pudo detectar la ruta de la app para el inicio de Windows.";

                using (var key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                    key?.SetValue(RunValueName, Quote(exe), RegistryValueKind.String);

                return "Inicio con Windows activado.";
            }
            catch (Exception ex)
            {
                Logger.Log("No se pudo actualizar inicio con Windows: " + ex.Message);
                return "No se pudo actualizar inicio con Windows: " + ex.Message;
            }
        }

        public static void Disable()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                    key?.DeleteValue(RunValueName, false);
            }
            catch
            {
            }
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
