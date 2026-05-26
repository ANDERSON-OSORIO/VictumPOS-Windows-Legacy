using System;
using System.IO;
using System.Web.Script.Serialization;

namespace VictumPOS.Services
{
    public class SettingsService
    {
        public const string DefaultSystemUrl = "https://system.grupocamachos.com/pos";

        private readonly string _folderPath;
        private readonly string _filePath;
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();

        public SettingsService()
        {
            _folderPath = ResolveSettingsFolderPath();
            _filePath = ResolveDataPath("settings.json");
            EnsureFileExists();
        }

        public string SettingsFilePath { get { return _filePath; } }
        public string SettingsFolderPath { get { return _folderPath; } }

        public static string ResolveSettingsFolderPath()
        {
            var commonFolderPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "VictumPOS");
            var localFolderPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VictumPOS");

            return Directory.Exists(commonFolderPath) ? commonFolderPath : localFolderPath;
        }

        public static string ResolveDataPath(string fileName)
        {
            var folderPath = ResolveSettingsFolderPath();
            Directory.CreateDirectory(folderPath);
            return Path.Combine(folderPath, fileName);
        }

        public static string ResolveDataDirectoryPath(string directoryName)
        {
            var folderPath = Path.Combine(ResolveSettingsFolderPath(), directoryName);
            Directory.CreateDirectory(folderPath);
            return folderPath;
        }

        private void EnsureFileExists()
        {
            try
            {
                Directory.CreateDirectory(_folderPath);

                if (!File.Exists(_filePath))
                    SaveFull(new PrinterConfig());
            }
            catch (Exception ex)
            {
                Logger.Log("SETTINGS ERROR: " + ex.Message);
            }
        }

        public PrinterConfig LoadFull()
        {
            try
            {
                if (!File.Exists(_filePath))
                    return new PrinterConfig();

                var json = File.ReadAllText(_filePath);
                if (string.IsNullOrWhiteSpace(json))
                    return new PrinterConfig();

                return _serializer.Deserialize<PrinterConfig>(json) ?? new PrinterConfig();
            }
            catch (Exception ex)
            {
                Logger.Log("ERROR CARGANDO: " + ex.Message);
                return new PrinterConfig();
            }
        }

        public void SaveFull(PrinterConfig config)
        {
            try
            {
                Directory.CreateDirectory(_folderPath);
                var json = _serializer.Serialize(config ?? new PrinterConfig());
                File.WriteAllText(_filePath, json);
                Logger.Log("GUARDADO OK: " + _filePath);
            }
            catch (Exception ex)
            {
                Logger.Log("ERROR GUARDANDO: " + ex.Message);
            }
        }

        public string GetPrinter(string type)
        {
            try
            {
                var config = LoadFull();
                var selector = (type ?? "").Trim().ToLowerInvariant();

                if (selector == "kitchen")
                    return config.KitchenType == "network" ? BuildNetworkPrinter(config.KitchenIP, config.KitchenPort) : Safe(config.KitchenPrinter).Trim();
                if (selector == "cash")
                    return config.CashType == "network" ? BuildNetworkPrinter(config.CashIP, config.CashPort) : Safe(config.CashPrinter).Trim();
                if (selector == "bar")
                    return config.BarType == "network" ? BuildNetworkPrinter(config.BarIP, config.BarPort) : Safe(config.BarPrinter).Trim();

                Logger.Log("PRINTER NO VALIDA: " + type);
                return "";
            }
            catch
            {
                return "";
            }
        }

        public string GetTerminalName() { return Safe(LoadFull().TerminalName); }
        public string GetBranchName() { return Safe(LoadFull().BranchName); }
        public string GetTerminalCode() { return Safe(LoadFull().TerminalCode); }
        public string GetOperationMode()
        {
            var value = Safe(LoadFull().OperationMode);
            return string.IsNullOrWhiteSpace(value) ? "Caja" : value;
        }

        public string GetSystemUrl()
        {
            var value = Safe(LoadFull().SystemUrl);
            return string.IsNullOrWhiteSpace(value) ? DefaultSystemUrl : value.Trim();
        }

        public bool IsAutoReloadEnabled() { return LoadFull().AutoReload; }
        public int GetPrintTimeoutMs() { return Math.Max(2000, LoadFull().PrintTimeoutMs); }
        public int GetPrintWidth() { return LoadFull().PrintWidth == 576 ? 576 : 384; }
        public bool IsAutoCutEnabled() { return LoadFull().AutoCut; }
        public bool IsPrintLogoEnabled() { return LoadFull().PrintLogo; }
        public string GetPrintLogoPath() { return Safe(LoadFull().PrintLogoPath); }
        public bool IsKioskModeEnabled() { return LoadFull().KioskMode; }
        public bool IsAppAutoStartEnabled() { return LoadFull().AppAutoStart; }
        public bool IsTouchKeyboardEnabled() { return LoadFull().TouchKeyboardEnabled; }
        public bool IsTouchGesturesEnabled() { return LoadFull().TouchGesturesEnabled; }
        public bool IsKeepScreenOnEnabled() { return LoadFull().KeepScreenOn; }
        public bool IsWebZoomLocked() { return LoadFull().LockWebZoom; }
        public bool ShouldClearCacheOnStart() { return LoadFull().ClearCacheOnStart; }
        public int GetPrintRetries() { return Clamp(LoadFull().PrintRetries, 0, 5); }
        public bool IsPrintBridgeEnabled() { return LoadFull().PrintBridgeEnabled; }
        public string GetPrintBridgeHost() { return Safe(LoadFull().PrintBridgeHost); }
        public int GetPrintBridgePort() { return Clamp(LoadFull().PrintBridgePort, 1, 65535); }
        public string GetPrintBridgeDefaultPrinter() { return NormalizePrinterSelector(LoadFull().PrintBridgeDefaultPrinter); }
        public bool IsLocationValidationEnabled() { return LoadFull().LocationValidation; }
        public string GetBranchLatitude() { return Safe(LoadFull().BranchLatitude); }
        public string GetBranchLongitude() { return Safe(LoadFull().BranchLongitude); }
        public int GetBranchRadiusMeters() { return Math.Max(10, LoadFull().BranchRadiusMeters); }

        public void SaveTerminalSettings(
            string terminalName,
            string branchName,
            string terminalCode,
            string operationMode,
            string systemUrl,
            bool autoReload,
            int printTimeoutMs,
            int printWidth,
            bool autoCut,
            bool printLogo,
            string printLogoPath,
            bool kioskMode,
            bool appAutoStart,
            bool touchKeyboardEnabled,
            bool touchGesturesEnabled,
            bool keepScreenOn,
            bool lockWebZoom,
            bool clearCacheOnStart,
            int printRetries,
            bool printBridgeEnabled,
            string printBridgeHost,
            int printBridgePort,
            string printBridgeDefaultPrinter,
            bool locationValidation,
            string branchLatitude,
            string branchLongitude,
            int branchRadiusMeters)
        {
            var config = LoadFull();
            config.TerminalName = Safe(terminalName).Trim();
            config.BranchName = Safe(branchName).Trim();
            config.TerminalCode = Safe(terminalCode).Trim();
            config.OperationMode = string.IsNullOrWhiteSpace(operationMode) ? "Caja" : operationMode.Trim();
            config.SystemUrl = string.IsNullOrWhiteSpace(systemUrl) ? DefaultSystemUrl : systemUrl.Trim();
            config.AutoReload = autoReload;
            config.PrintTimeoutMs = Math.Max(2000, printTimeoutMs);
            config.PrintWidth = printWidth == 576 ? 576 : 384;
            config.AutoCut = autoCut;
            config.PrintLogo = printLogo;
            config.PrintLogoPath = Safe(printLogoPath).Trim();
            config.KioskMode = kioskMode;
            config.AppAutoStart = appAutoStart;
            config.TouchKeyboardEnabled = touchKeyboardEnabled;
            config.TouchGesturesEnabled = touchGesturesEnabled;
            config.KeepScreenOn = keepScreenOn;
            config.LockWebZoom = lockWebZoom;
            config.ClearCacheOnStart = clearCacheOnStart;
            config.PrintRetries = Clamp(printRetries, 0, 5);
            config.PrintBridgeEnabled = printBridgeEnabled;
            config.PrintBridgeHost = Safe(printBridgeHost).Trim();
            config.PrintBridgePort = Clamp(printBridgePort, 1, 65535);
            config.PrintBridgeDefaultPrinter = NormalizePrinterSelector(printBridgeDefaultPrinter);
            config.LocationValidation = locationValidation;
            config.BranchLatitude = Safe(branchLatitude).Trim();
            config.BranchLongitude = Safe(branchLongitude).Trim();
            config.BranchRadiusMeters = Math.Max(10, branchRadiusMeters);
            SaveFull(config);
        }

        private string BuildNetworkPrinter(string ip, string port)
        {
            ip = Safe(ip).Trim();
            port = Safe(port).Trim();
            return string.IsNullOrWhiteSpace(ip) || string.IsNullOrWhiteSpace(port) ? "" : ip + ":" + port;
        }

        private static string Safe(string value)
        {
            return value ?? "";
        }

        private static int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private string NormalizePrinterSelector(string value)
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
    }

    public class PrinterConfig
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

        public string TerminalName { get; set; } = "";
        public string BranchName { get; set; } = "";
        public string TerminalCode { get; set; } = "";
        public string OperationMode { get; set; } = "Caja";
        public string SystemUrl { get; set; } = SettingsService.DefaultSystemUrl;
        public bool AutoReload { get; set; } = true;
        public int PrintTimeoutMs { get; set; } = 5000;
        public int PrintWidth { get; set; } = 384;
        public bool AutoCut { get; set; } = true;
        public bool PrintLogo { get; set; } = true;
        public string PrintLogoPath { get; set; } = "";
        public bool KioskMode { get; set; } = true;
        public bool AppAutoStart { get; set; } = true;
        public bool TouchKeyboardEnabled { get; set; } = true;
        public bool TouchGesturesEnabled { get; set; } = true;
        public bool KeepScreenOn { get; set; } = true;
        public bool LockWebZoom { get; set; } = true;
        public bool ClearCacheOnStart { get; set; } = false;
        public int PrintRetries { get; set; } = 1;
        public bool PrintBridgeEnabled { get; set; } = true;
        public string PrintBridgeHost { get; set; } = "";
        public int PrintBridgePort { get; set; } = 9123;
        public string PrintBridgeDefaultPrinter { get; set; } = "cash";
        public bool LocationValidation { get; set; } = false;
        public string BranchLatitude { get; set; } = "";
        public string BranchLongitude { get; set; } = "";
        public int BranchRadiusMeters { get; set; } = 80;
    }
}
