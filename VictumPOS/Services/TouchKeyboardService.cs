using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace VictumPOS.Services
{
    public static class TouchKeyboardService
    {
        public static void Show()
        {
            try
            {
                foreach (var path in ResolveTabTipPaths())
                    if (StartIfAvailable(path, "TabTip"))
                        return;

                StartIfAvailable(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "osk.exe"), "osk");
            }
            catch (Exception ex)
            {
                Logger.Log("No se pudo abrir teclado tactil: " + ex.Message);
            }
        }

        private static bool StartIfAvailable(string path, string processName)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;

            if (Process.GetProcessesByName(processName).Any())
                return true;

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }

        private static string[] ResolveTabTipPaths()
        {
            return new[]
            {
                Path.Combine(Environment.GetEnvironmentVariable("CommonProgramW6432") ?? "", @"Microsoft Shared\ink\TabTip.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), @"Microsoft Shared\ink\TabTip.exe"),
                Path.Combine(Environment.GetEnvironmentVariable("ProgramFiles") ?? "", @"Common Files\Microsoft Shared\ink\TabTip.exe"),
                Path.Combine(Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? "", @"Common Files\Microsoft Shared\ink\TabTip.exe")
            };
        }
    }
}
