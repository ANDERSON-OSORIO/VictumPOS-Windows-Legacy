using System;
using System.IO;
using System.Web.Script.Serialization;

namespace VictumPOS.PrintBridge.Service.Services
{
    internal sealed class BridgeSettingsService
    {
        private readonly string _settingsPath;
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();

        public BridgeSettingsService(string settingsPath)
        {
            _settingsPath = settingsPath;
            EnsureFileExists();
        }

        public PrinterConfig LoadFull()
        {
            try
            {
                if (!File.Exists(_settingsPath))
                    return new PrinterConfig();

                var json = File.ReadAllText(_settingsPath);
                if (string.IsNullOrWhiteSpace(json))
                    return new PrinterConfig();

                return _serializer.Deserialize<PrinterConfig>(json) ?? new PrinterConfig();
            }
            catch (Exception ex)
            {
                BridgeLogger.Log("Error cargando settings: " + ex.Message);
                return new PrinterConfig();
            }
        }

        public string GetPrinter(string type)
        {
            var config = LoadFull();
            switch ((type ?? "").Trim().ToLowerInvariant())
            {
                case "kitchen":
                    return config.KitchenType == "network" ? BuildNetworkPrinter(config.KitchenIP, config.KitchenPort) : Safe(config.KitchenPrinter).Trim();
                case "cash":
                    return config.CashType == "network" ? BuildNetworkPrinter(config.CashIP, config.CashPort) : Safe(config.CashPrinter).Trim();
                case "bar":
                    return config.BarType == "network" ? BuildNetworkPrinter(config.BarIP, config.BarPort) : Safe(config.BarPrinter).Trim();
                default:
                    return "";
            }
        }

        public int GetPrintTimeoutMs() { return Math.Max(2000, LoadFull().PrintTimeoutMs); }
        public int GetPrintWidth() { return LoadFull().PrintWidth == 576 ? 576 : 384; }
        public bool IsAutoCutEnabled() { return LoadFull().AutoCut; }
        public bool IsPrintLogoEnabled() { return LoadFull().PrintLogo; }
        public string GetPrintLogoPath() { return Safe(LoadFull().PrintLogoPath); }
        public int GetPrintRetries() { return Clamp(LoadFull().PrintRetries, 0, 5); }
        public string GetPrintBridgeDefaultPrinter() { return NormalizePrinterSelector(LoadFull().PrintBridgeDefaultPrinter); }

        private void EnsureFileExists()
        {
            var folder = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(folder))
                Directory.CreateDirectory(folder);

            if (!File.Exists(_settingsPath))
                File.WriteAllText(_settingsPath, _serializer.Serialize(new PrinterConfig()));
        }

        private static string BuildNetworkPrinter(string ip, string port)
        {
            ip = Safe(ip).Trim();
            port = Safe(port).Trim();
            return string.IsNullOrWhiteSpace(ip) || string.IsNullOrWhiteSpace(port) ? "" : ip + ":" + port;
        }

        private static string NormalizePrinterSelector(string value)
        {
            switch (Safe(value).Trim().ToLowerInvariant())
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
                    return "cash";
            }
        }

        private static int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private static string Safe(string value)
        {
            return value ?? "";
        }
    }

    internal sealed class PrinterConfig
    {
        public string KitchenType { get; set; } = "network";
        public string KitchenIP { get; set; } = "192.168.1.98";
        public string KitchenPort { get; set; } = "9100";
        public string KitchenPrinter { get; set; } = "";

        public string CashType { get; set; } = "network";
        public string CashIP { get; set; } = "192.168.1.99";
        public string CashPort { get; set; } = "9100";
        public string CashPrinter { get; set; } = "";

        public string BarType { get; set; } = "network";
        public string BarIP { get; set; } = "192.168.1.100";
        public string BarPort { get; set; } = "9100";
        public string BarPrinter { get; set; } = "";

        public int PrintTimeoutMs { get; set; } = 5000;
        public int PrintWidth { get; set; } = 384;
        public bool AutoCut { get; set; } = true;
        public bool PrintLogo { get; set; } = true;
        public string PrintLogoPath { get; set; } = "";
        public int PrintRetries { get; set; } = 1;
        public string PrintBridgeDefaultPrinter { get; set; } = "cash";
    }
}
