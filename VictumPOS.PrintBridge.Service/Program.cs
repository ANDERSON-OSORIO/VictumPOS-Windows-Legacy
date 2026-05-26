using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Threading;
using VictumPOS.PrintBridge.Service.Services;

namespace VictumPOS.PrintBridge.Service
{
    internal static class Program
    {
        private const string ServiceName = "VictumPOSPrintBridge";
        private const string DisplayName = "VictumPOS Print Bridge";
        private const int DefaultPort = 9123;

        public static int Main(string[] args)
        {
            try
            {
                if (args.Any(a => EqualsArg(a, "install")))
                    return Install(args);

                if (args.Any(a => EqualsArg(a, "uninstall")))
                    return Uninstall();

                if (args.Any(a => EqualsArg(a, "start")))
                    return RunSc("start", ServiceName);

                if (args.Any(a => EqualsArg(a, "stop")))
                {
                    RunSc("stop", ServiceName);
                    return 0;
                }

                var options = BridgeOptions.FromArgs(args, DefaultPort);
                if (args.Any(a => EqualsArg(a, "user")) || args.Any(a => EqualsArg(a, "run")))
                    return RunUser(options);

                if (args.Any(a => EqualsArg(a, "console")) || Environment.UserInteractive)
                    return RunConsole(options);

                ServiceBase.Run(new WindowsPrintBridgeService(options));
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                BridgeLogger.Log("Fatal: " + ex);
                return 1;
            }
        }

        private static int RunConsole(BridgeOptions options)
        {
            using (var server = new PrintBridgeServer(options))
            {
                server.Start();
                Console.WriteLine("VictumPOS Print Bridge activo en puerto " + options.Port + ".");
                Console.WriteLine("Presiona Enter para detener.");
                Console.ReadLine();
            }

            return 0;
        }

        private static int RunUser(BridgeOptions options)
        {
            using (var server = new PrintBridgeServer(options))
            using (var stopped = new ManualResetEvent(false))
            {
                Console.CancelKeyPress += (_, e) =>
                {
                    e.Cancel = true;
                    stopped.Set();
                };

                server.Start();
                BridgeLogger.Log("Bridge de usuario activo en puerto " + options.Port + ".");
                stopped.WaitOne();
            }

            return 0;
        }

        private static int Install(string[] args)
        {
            var exe = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(exe))
                throw new InvalidOperationException("No se pudo detectar la ruta del ejecutable.");

            var options = BridgeOptions.FromArgs(args, DefaultPort);
            var settingsPath = options.SettingsPath;
            if (string.IsNullOrWhiteSpace(settingsPath))
                settingsPath = Path.Combine(ResolveSettingsFolderPath(), "settings.json");

            var binPath = Quote(exe) + " --port " + options.Port + " --settings " + Quote(settingsPath);
            RunScChecked("create", ServiceName, "binPath=", binPath, "start=", "auto", "DisplayName=", DisplayName);
            RunScChecked("description", ServiceName, "Recibe impresiones Android y las envia a impresoras Windows configuradas en VictumPOS.");

            TryRunNetsh("firewall", "add", "portopening", "TCP", options.Port.ToString(), "VictumPOS Print Bridge");
            TryRunNetsh("advfirewall", "firewall", "add", "rule",
                "name=VictumPOS Print Bridge",
                "dir=in",
                "action=allow",
                "protocol=TCP",
                "localport=" + options.Port);

            Console.WriteLine("Servicio instalado.");
            Console.WriteLine("Settings: " + settingsPath);
            Console.WriteLine("Puerto: " + options.Port);
            Console.WriteLine("Inicia con: sc start " + ServiceName);
            return 0;
        }

        private static int Uninstall()
        {
            RunSc("stop", ServiceName);
            RunSc("delete", ServiceName);
            TryRunNetsh("firewall", "delete", "portopening", "TCP", DefaultPort.ToString());
            TryRunNetsh("advfirewall", "firewall", "delete", "rule", "name=VictumPOS Print Bridge");
            Console.WriteLine("Servicio removido.");
            return 0;
        }

        private static bool EqualsArg(string value, string expected)
        {
            return value.Equals("--" + expected, StringComparison.OrdinalIgnoreCase)
                || value.Equals(expected, StringComparison.OrdinalIgnoreCase);
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static string ResolveSettingsFolderPath()
        {
            var commonFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "VictumPOS");
            var localFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VictumPOS");
            return Directory.Exists(commonFolderPath) ? commonFolderPath : localFolderPath;
        }

        private static void RunScChecked(params string[] args)
        {
            var code = RunProcess("sc.exe", args);
            if (code != 0)
                throw new InvalidOperationException("sc.exe fallo con codigo " + code);
        }

        private static int RunSc(params string[] args)
        {
            return RunProcess("sc.exe", args);
        }

        private static void TryRunNetsh(params string[] args)
        {
            try
            {
                RunProcess("netsh.exe", args);
            }
            catch (Exception ex)
            {
                BridgeLogger.Log("No se pudo ajustar firewall automaticamente: " + ex.Message);
            }
        }

        private static int RunProcess(string fileName, params string[] args)
        {
            var startInfo = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            startInfo.Arguments = string.Join(" ", args.Select(QuoteProcessArg));

            using (var process = Process.Start(startInfo))
            {
                if (process == null)
                    return 1;

                process.WaitForExit();
                Console.Write(process.StandardOutput.ReadToEnd());
                Console.Error.Write(process.StandardError.ReadToEnd());
                return process.ExitCode;
            }
        }

        private static string QuoteProcessArg(string arg)
        {
            if (arg == null)
                return "";

            return arg.IndexOfAny(new[] { ' ', '\t', '"' }) >= 0
                ? "\"" + arg.Replace("\"", "\\\"") + "\""
                : arg;
        }
    }
}
