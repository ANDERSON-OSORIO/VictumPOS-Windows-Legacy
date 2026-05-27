using System;
using System.Threading;
using System.Threading.Tasks;

namespace VictumPOS.PrintBridge.Service.Services
{
    internal sealed class BridgePrintService
    {
        private readonly BridgeSettingsService _settings;
        private readonly EscPosBridgePrinter _esc;
        private readonly WindowsSpoolerPrinter _windows;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        public BridgePrintService(string settingsPath)
        {
            _settings = new BridgeSettingsService(settingsPath);
            _esc = new EscPosBridgePrinter(_settings);
            _windows = new WindowsSpoolerPrinter(_settings);
        }

        public async Task Print(string content, string printerSelector)
        {
            await _lock.WaitAsync();
            try
            {
                if (string.IsNullOrWhiteSpace(content))
                    throw new InvalidOperationException("Contenido de impresion vacio");

                var printer = ResolvePrinter(printerSelector);
                if (string.IsNullOrWhiteSpace(printer))
                    throw new InvalidOperationException("No hay impresora configurada para: " + printerSelector);

                BridgeLogger.Log("Print selector='" + printerSelector + "' resolved='" + printer + "'");

                if (IsNetworkPrinter(printer))
                    await PrintEscPosWithRetries(printer, content);
                else
                    _windows.Print(content, printer);
            }
            finally
            {
                _lock.Release();
            }
        }

        public string DefaultPrinterSelector()
        {
            return _settings.GetPrintBridgeDefaultPrinter();
        }

        private async Task PrintEscPosWithRetries(string printer, string content)
        {
            var parts = printer.Split(':');
            int port;
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || !int.TryParse(parts[1], out port))
                throw new InvalidOperationException("Formato IP:PUERTO invalido");

            var attempts = Math.Max(1, _settings.GetPrintRetries() + 1);
            Exception lastError = null;

            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                try
                {
                    await _esc.PrintAsync(parts[0].Trim(), port, content, _settings.GetPrintTimeoutMs());
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    BridgeLogger.Log("Intento ESC/POS " + attempt + "/" + attempts + " fallo: " + ex.Message);
                    if (attempt < attempts)
                        await Task.Delay(300);
                }
            }

            throw lastError ?? new InvalidOperationException("No se pudo imprimir en ESC/POS");
        }

        private static bool IsNetworkPrinter(string printer)
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

        private static string NormalizeConfiguredPrinterType(string printerSelector)
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
    }
}
