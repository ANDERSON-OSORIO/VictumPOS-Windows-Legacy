using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace VictumPOS.Services
{
    public class PrintService
    {
        private readonly SettingsService _settings;
        private readonly EscPosService _esc;
        private readonly WindowsPrintService _win;
        private readonly QueueService _queue;
        private readonly SemaphoreSlim _printLock = new SemaphoreSlim(1, 1);

        public PrintService()
        {
            _settings = new SettingsService();
            _esc = new EscPosService();
            _win = new WindowsPrintService();
            _queue = new QueueService(this);
        }

        public Task Print(string content, string printerSelector)
        {
            return PrintInternal(content, printerSelector, true, null);
        }

        public Task PrintTest(string content, string printerSelector)
        {
            string logoBase64 = null;
            var logoPath = ResolveAssetPath("logo.png");
            if (_settings.IsPrintLogoEnabled() && File.Exists(logoPath))
                logoBase64 = Convert.ToBase64String(File.ReadAllBytes(logoPath));

            return PrintInternal(content, printerSelector, true, logoBase64);
        }

        public string DefaultPrinterSelector()
        {
            return _settings.GetPrintBridgeDefaultPrinter();
        }

        internal Task PrintFromQueue(string content, string printerSelector)
        {
            return PrintInternal(content, printerSelector, false, null);
        }

        private async Task PrintInternal(string content, string printerSelector, bool enqueueOnFailure, string logoBase64)
        {
            await _printLock.WaitAsync();

            try
            {
                if (string.IsNullOrWhiteSpace(content))
                    throw new Exception("Contenido de impresion vacio");
                if (string.IsNullOrWhiteSpace(printerSelector))
                    throw new Exception("Impresora vacia");

                var printer = ResolvePrinter(printerSelector);
                Logger.Log("Print selector='" + printerSelector + "' resolved='" + printer + "'");

                if (string.IsNullOrWhiteSpace(printer))
                    throw new Exception("No hay impresora configurada para: " + printerSelector);

                if (IsNetworkPrinter(printer))
                {
                    Logger.Log("Print mode: ESC/POS network");
                    await PrintEscPos(printer, content, logoBase64);
                }
                else
                {
                    Logger.Log("Print mode: Windows spooler");
                    _win.Print(content, printer, ResolveAssetPath("logo.png"));
                }
            }
            catch (Exception ex)
            {
                if (enqueueOnFailure && !string.IsNullOrWhiteSpace(content) && !string.IsNullOrWhiteSpace(printerSelector))
                    _queue.Enqueue(content, printerSelector);

                Logger.Log("Print error: " + ex.Message);
                throw;
            }
            finally
            {
                _printLock.Release();
            }
        }

        private async Task PrintEscPos(string printer, string content, string logoBase64)
        {
            var parts = printer.Split(':');
            if (parts.Length != 2)
                throw new Exception("Formato IP:PUERTO invalido");

            int port;
            if (!int.TryParse(parts[1], out port))
                throw new Exception("Puerto invalido");

            var attempts = Math.Max(1, _settings.GetPrintRetries() + 1);
            Exception lastError = null;

            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                try
                {
                    await _esc.PrintAsync(parts[0].Trim(), port, content, _settings.GetPrintTimeoutMs(), logoBase64);
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    Logger.Log("Intento ESC/POS " + attempt + "/" + attempts + " fallo: " + ex.Message);
                    if (attempt < attempts)
                        await Task.Delay(250);
                }
            }

            throw lastError ?? new Exception("No se pudo imprimir en ESC/POS");
        }

        private bool IsNetworkPrinter(string printer)
        {
            if (string.IsNullOrWhiteSpace(printer) || printer.StartsWith(@"\\", StringComparison.Ordinal))
                return false;

            var parts = printer.Split(':');
            int port;
            return parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && int.TryParse(parts[1], out port);
        }

        private string ResolvePrinter(string printerSelector)
        {
            var selector = (printerSelector ?? "").Trim();
            var configuredType = NormalizeConfiguredPrinterType(selector);
            return string.IsNullOrWhiteSpace(configuredType) ? selector : _settings.GetPrinter(configuredType);
        }

        private string NormalizeConfiguredPrinterType(string printerSelector)
        {
            switch ((printerSelector ?? "").Trim().ToLowerInvariant())
            {
                case "kitchen":
                case "cocina":
                    return "kitchen";
                case "cash":
                case "caja":
                    return "cash";
                case "bar":
                case "barra":
                    return "bar";
                default:
                    return "";
            }
        }

        private string ResolveAssetPath(string fileName)
        {
            foreach (var root in EnumerateAssetRoots())
            {
                var candidate = Path.Combine(root, "Assets", fileName);
                if (File.Exists(candidate))
                    return candidate;
            }

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", fileName);
        }

        private static System.Collections.Generic.IEnumerable<string> EnumerateAssetRoots()
        {
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var start in new[] { AppDomain.CurrentDomain.BaseDirectory, Environment.CurrentDirectory })
            {
                var directory = new DirectoryInfo(start);
                while (directory != null)
                {
                    foreach (var root in new[] { directory.FullName, Path.Combine(directory.FullName, "VictumPOS") })
                    {
                        var fullPath = Path.GetFullPath(root);
                        if (seen.Add(fullPath))
                            yield return fullPath;
                    }

                    directory = directory.Parent;
                }
            }
        }
    }
}
