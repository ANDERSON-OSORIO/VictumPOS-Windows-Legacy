using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace VictumPOS.Services
{
    public sealed class PrintBridgeControlService
    {
        private const string ServiceName = "VictumPOSPrintBridge";
        private const string UserRunValueName = "VictumPOSPrintBridgeUser";
        private readonly SettingsService _settings;

        public PrintBridgeControlService(SettingsService settings)
        {
            _settings = settings;
        }

        public async Task<PrintBridgeStatus> GetStatusAsync()
        {
            var query = await RunProcessAsync("sc.exe", "query " + ServiceName);
            if (query.ExitCode != 0)
            {
                if (await IsHealthyAsync(_settings.GetPrintBridgePort()))
                    return new PrintBridgeStatus(false, true, true, "Bridge local activo (sin administrador)");

                return new PrintBridgeStatus(false, false, false, BuildQueryErrorMessage(query.Output));
            }

            var state = ParseState(query.Output);
            var running = state == ServiceRunState.Running;
            var healthy = running && await IsHealthyAsync(_settings.GetPrintBridgePort());
            var message = healthy ? "Instalado y respondiendo" : "Instalado: " + FormatState(state, query.Output);

            return new PrintBridgeStatus(true, running, healthy, message);
        }

        public async Task<string> StartAsync()
        {
            var query = await RunProcessAsync("sc.exe", "query " + ServiceName);
            if (query.ExitCode != 0)
                return await StartUserBridgeAsync();

            var result = await RunProcessAsync("sc.exe", "start " + ServiceName);
            await Task.Delay(1200);

            var status = await GetStatusAsync();
            if (status.IsRunning || result.ExitCode == 0)
                return status.Message;

            return "No se pudo iniciar: " + BuildActionError(result.Output, status.Message);
        }

        public async Task<string> StopAsync()
        {
            var query = await RunProcessAsync("sc.exe", "query " + ServiceName);
            if (query.ExitCode != 0)
                return await StopUserBridgeAsync();

            var result = await RunProcessAsync("sc.exe", "stop " + ServiceName);
            await Task.Delay(1200);

            var status = await GetStatusAsync();
            if (!status.IsRunning)
                return status.Message;

            if (result.ExitCode == 0)
            {
                await Task.Delay(2500);
                status = await GetStatusAsync();
                if (!status.IsRunning)
                    return status.Message;
            }

            var elevated = await RunServiceCommandElevatedAsync("stop");
            await Task.Delay(1200);
            status = await GetStatusAsync();

            if (!status.IsRunning)
                return "Servicio detenido";

            return "No se pudo detener: " + BuildActionError(elevated, BuildActionError(result.Output, status.Message));
        }

        public async Task<string> UninstallWindowsServiceAsync()
        {
            UnregisterUserBridgeAutoStart();
            await StopUserBridgeAsync();

            var query = await RunProcessAsync("sc.exe", "query " + ServiceName);
            if (query.ExitCode != 0)
                return "Servicio no instalado. Bridge de usuario removido si existia.";

            await RunServiceCommandElevatedAsync("stop");
            await Task.Delay(1000);

            var output = await RunServiceCommandElevatedAsync("uninstall");
            await Task.Delay(1000);

            var status = await GetStatusAsync();
            return status.IsInstalled
                ? "No se pudo desinstalar el servicio: " + output
                : "Servicio Print Bridge desinstalado";
        }

        public async Task<string> InstallAndStartAsync()
        {
            var current = await GetStatusAsync();
            if (current.IsHealthy)
                return current.Message;

            if (current.IsInstalled)
                return current.IsRunning ? current.Message : await StartAsync();

            return await StartUserBridgeAsync();
        }

        public async Task<string> InstallWindowsServiceAndStartAsync()
        {
            var current = await GetStatusAsync();
            if (current.IsInstalled)
                return current.IsRunning ? current.Message : await StartAsync();

            var serviceExe = FindServiceExecutable();
            if (string.IsNullOrWhiteSpace(serviceExe))
                return "No se encontro VictumPOS.PrintBridge.Service.exe en la instalacion.";

            var startInfo = new ProcessStartInfo(serviceExe)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(serviceExe),
                Arguments = "install --port " + _settings.GetPrintBridgePort() + " --settings " + Quote(_settings.SettingsFilePath)
            };

            try
            {
                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                        return "No se pudo abrir el instalador del servicio.";

                    await Task.Run(() => process.WaitForExit());
                    if (process.ExitCode != 0)
                        return "El instalador del servicio termino con codigo " + process.ExitCode + ". Ejecuta la instalacion como administrador.";
                }
            }
            catch (Exception ex)
            {
                return "No se pudo abrir el instalador del servicio: " + ex.Message;
            }

            return await StartAsync();
        }

        public async Task<string> SendTestPrintAsync(string bridgeUrl, string printerSelector)
        {
            var payload = new PrintBridgeTestRequest
            {
                Content = "VictumPOS bridge OK\n" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                PrinterSelector = string.IsNullOrWhiteSpace(printerSelector) ? _settings.GetPrintBridgeDefaultPrinter() : printerSelector
            };

            var json = new JavaScriptSerializer().Serialize(payload);
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
            using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
            using (var response = await client.PostAsync(bridgeUrl.TrimEnd('/') + "/print", content))
            {
                var body = await response.Content.ReadAsStringAsync();
                return response.IsSuccessStatusCode
                    ? "Bridge respondio OK: " + body
                    : "Bridge respondio HTTP " + (int)response.StatusCode + ": " + body;
            }
        }

        private static async Task<bool> IsHealthyAsync(int port)
        {
            try
            {
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) })
                using (var response = await client.GetAsync("http://127.0.0.1:" + port + "/health"))
                    return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private static string FindServiceExecutable()
        {
            var fileName = "VictumPOS.PrintBridge.Service.exe";
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new List<string>
            {
                Path.Combine(baseDir, fileName),
                Path.Combine(baseDir, "VictumPOS.PrintBridge.Service", fileName),
                Path.Combine(baseDir, "PrintBridge", fileName),
                Path.GetFullPath(Path.Combine(baseDir, "..", fileName)),
                Path.GetFullPath(Path.Combine(baseDir, "..", "PrintBridge", fileName)),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "VictumPOS.PrintBridge.Service", "bin", "Debug", fileName)),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "VictumPOS.PrintBridge.Service", "bin", "Release", fileName)),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "VictumPOS.PrintBridge.Service", "bin", "Debug", "net8.0-windows10.0.19041.0", fileName)),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "VictumPOS.PrintBridge.Service", "bin", "Release", "net8.0-windows10.0.19041.0", fileName))
            };

            foreach (var candidate in candidates)
                if (File.Exists(candidate))
                    return candidate;

            try
            {
                foreach (var candidate in Directory.EnumerateFiles(baseDir, fileName, SearchOption.AllDirectories))
                    return candidate;
            }
            catch
            {
            }

            return "";
        }

        private async Task<string> StartUserBridgeAsync()
        {
            if (await IsHealthyAsync(_settings.GetPrintBridgePort()))
            {
                RegisterUserBridgeAutoStart(FindServiceExecutable());
                return "Bridge local activo (sin administrador)";
            }

            var serviceExe = FindServiceExecutable();
            if (string.IsNullOrWhiteSpace(serviceExe))
                return "No se encontro VictumPOS.PrintBridge.Service.exe en la instalacion.";

            RegisterUserBridgeAutoStart(serviceExe);

            var startInfo = new ProcessStartInfo(serviceExe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(serviceExe),
                Arguments = "user --port " + _settings.GetPrintBridgePort() + " --settings " + Quote(_settings.SettingsFilePath)
            };

            using (var process = Process.Start(startInfo))
            {
                if (process == null)
                    return "No se pudo iniciar el bridge local.";

                SaveUserBridgePid(process.Id);
            }

            await Task.Delay(1500);
            return await IsHealthyAsync(_settings.GetPrintBridgePort())
                ? "Bridge local iniciado y registrado en el inicio de Windows (sin administrador)"
                : "Bridge local iniciado, pero aun no responde en el puerto " + _settings.GetPrintBridgePort();
        }

        private async Task<string> StopUserBridgeAsync()
        {
            UnregisterUserBridgeAutoStart();
            var stopped = false;

            try
            {
                var pidPath = UserBridgePidPath();
                int pid;
                if (File.Exists(pidPath) && int.TryParse(File.ReadAllText(pidPath).Trim(), out pid))
                {
                    using (var process = Process.GetProcessById(pid))
                    {
                        if (process.ProcessName.IndexOf("VictumPOS.PrintBridge.Service", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            process.Kill();
                            await Task.Run(() => process.WaitForExit());
                            stopped = true;
                        }
                    }
                }
            }
            catch
            {
            }

            stopped = KillUserBridgeProcesses() || stopped;
            TryDelete(UserBridgePidPath());
            await Task.Delay(500);

            if (!await IsHealthyAsync(_settings.GetPrintBridgePort()))
                return stopped ? "Bridge local detenido" : "Bridge local no estaba activo";

            return "Autoarranque del bridge local removido, pero el proceso aun responde.";
        }

        private static bool KillUserBridgeProcesses()
        {
            var stopped = false;

            try
            {
                foreach (var process in Process.GetProcesses())
                {
                    try
                    {
                        if (process.ProcessName.IndexOf("VictumPOS.PrintBridge.Service", StringComparison.OrdinalIgnoreCase) < 0)
                            continue;

                        process.Kill();
                        process.WaitForExit(3000);
                        stopped = true;
                    }
                    catch
                    {
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch
            {
            }

            return stopped;
        }

        private void RegisterUserBridgeAutoStart(string serviceExe)
        {
            if (string.IsNullOrWhiteSpace(serviceExe))
                return;

            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                    key?.SetValue(
                        UserRunValueName,
                        Quote(serviceExe) + " user --port " + _settings.GetPrintBridgePort() + " --settings " + Quote(_settings.SettingsFilePath),
                        RegistryValueKind.String);
            }
            catch
            {
            }
        }

        private static void UnregisterUserBridgeAutoStart()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                    key?.DeleteValue(UserRunValueName, false);
            }
            catch
            {
            }
        }

        private static void SaveUserBridgePid(int pid)
        {
            try
            {
                var path = UserBridgePidPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, pid.ToString());
            }
            catch
            {
            }
        }

        private static string UserBridgePidPath()
        {
            return SettingsService.ResolveDataPath("print-bridge-user.pid");
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }

        private static ServiceRunState ParseState(string output)
        {
            foreach (var line in (output ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("STATE", StringComparison.OrdinalIgnoreCase) &&
                    !trimmed.StartsWith("ESTADO", StringComparison.OrdinalIgnoreCase))
                    continue;

                var parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    int code;
                    if (int.TryParse(part, out code))
                    {
                        switch (code)
                        {
                            case 1: return ServiceRunState.Stopped;
                            case 2: return ServiceRunState.StartPending;
                            case 3: return ServiceRunState.StopPending;
                            case 4: return ServiceRunState.Running;
                            case 5: return ServiceRunState.ContinuePending;
                            case 6: return ServiceRunState.PausePending;
                            case 7: return ServiceRunState.Paused;
                        }
                    }
                }
            }

            return ServiceRunState.Unknown;
        }

        private static string FormatState(ServiceRunState state, string output)
        {
            switch (state)
            {
                case ServiceRunState.Stopped: return "detenido";
                case ServiceRunState.StartPending: return "iniciando";
                case ServiceRunState.StopPending: return "deteniendo";
                case ServiceRunState.Running: return "ejecutando, sin respuesta HTTP";
                case ServiceRunState.ContinuePending: return "reanudando";
                case ServiceRunState.PausePending: return "pausando";
                case ServiceRunState.Paused: return "pausado";
                default: return "estado no reconocido (" + CleanOutput(output) + ")";
            }
        }

        private static string BuildQueryErrorMessage(string output)
        {
            var clean = CleanOutput(output);
            if (clean.IndexOf("1060", StringComparison.OrdinalIgnoreCase) >= 0 ||
                clean.IndexOf("no existe", StringComparison.OrdinalIgnoreCase) >= 0 ||
                clean.IndexOf("does not exist", StringComparison.OrdinalIgnoreCase) >= 0)
                return "No instalado";

            return string.IsNullOrWhiteSpace(clean) ? "No se pudo consultar el servicio" : "No se pudo consultar el servicio: " + clean;
        }

        private static string BuildActionError(string output, string statusMessage)
        {
            var clean = CleanOutput(output);
            return !string.IsNullOrWhiteSpace(clean) ? clean : statusMessage;
        }

        private static Task<ProcessResult> RunProcessAsync(string fileName, string arguments)
        {
            return Task.Run(() =>
            {
                var startInfo = new ProcessStartInfo(fileName, arguments)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                        return new ProcessResult(1, "No se pudo iniciar " + fileName);

                    var output = process.StandardOutput.ReadToEnd() + Environment.NewLine + process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    return new ProcessResult(process.ExitCode, output);
                }
            });
        }

        private async Task<string> RunServiceCommandElevatedAsync(string command)
        {
            var serviceExe = FindServiceExecutable();
            if (string.IsNullOrWhiteSpace(serviceExe))
                return "No se encontro VictumPOS.PrintBridge.Service.exe en la instalacion.";

            var startInfo = new ProcessStartInfo(serviceExe)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(serviceExe),
                Arguments = command
            };

            try
            {
                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                        return "No se pudo abrir el comando " + command + ".";

                    await Task.Run(() => process.WaitForExit());
                    return process.ExitCode == 0
                        ? "Comando " + command + " ejecutado"
                        : "Comando " + command + " termino con codigo " + process.ExitCode;
                }
            }
            catch (Exception ex)
            {
                return "No se pudo ejecutar " + command + " como administrador: " + ex.Message;
            }
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
        }

        private static string CleanOutput(string value)
        {
            return string.Join(" ", (value ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
        }
    }

    public sealed class PrintBridgeStatus
    {
        public PrintBridgeStatus(bool isInstalled, bool isRunning, bool isHealthy, string message)
        {
            IsInstalled = isInstalled;
            IsRunning = isRunning;
            IsHealthy = isHealthy;
            Message = message;
        }

        public bool IsInstalled { get; private set; }
        public bool IsRunning { get; private set; }
        public bool IsHealthy { get; private set; }
        public string Message { get; private set; }
    }

    internal sealed class ProcessResult
    {
        public ProcessResult(int exitCode, string output)
        {
            ExitCode = exitCode;
            Output = output;
        }

        public int ExitCode { get; private set; }
        public string Output { get; private set; }
    }

    internal enum ServiceRunState
    {
        Unknown,
        Stopped,
        StartPending,
        StopPending,
        Running,
        ContinuePending,
        PausePending,
        Paused
    }

    internal sealed class PrintBridgeTestRequest
    {
        public string Content { get; set; }
        public string PrinterSelector { get; set; }
    }
}
