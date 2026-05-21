using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VictumPOS.Services;

namespace VictumPOS.Views
{
    public partial class TerminalConfigPage : Page
    {
        private readonly SettingsService _settings;
        private readonly PrintBridgeControlService _printBridge;

        public TerminalConfigPage()
        {
            InitializeComponent();
            _settings = new SettingsService();
            _printBridge = new PrintBridgeControlService(_settings);
            LoadSettings();
            _ = RefreshPrintBridgeStatusAsync();
        }

        private void LoadSettings()
        {
            TerminalName.Text = _settings.GetTerminalName();
            BranchName.Text = _settings.GetBranchName();
            TerminalCode.Text = _settings.GetTerminalCode();
            SystemUrl.Text = _settings.GetSystemUrl();
            AutoReload.IsChecked = _settings.IsAutoReloadEnabled();
            KioskMode.IsChecked = _settings.IsKioskModeEnabled();
            AppAutoStart.IsChecked = _settings.IsAppAutoStartEnabled() || AppStartupService.IsEnabled();
            TouchKeyboard.IsChecked = _settings.IsTouchKeyboardEnabled();
            KeepScreenOn.IsChecked = _settings.IsKeepScreenOnEnabled();
            LockWebZoom.IsChecked = _settings.IsWebZoomLocked();
            ClearCacheOnStart.IsChecked = _settings.ShouldClearCacheOnStart();
            LocationValidation.IsChecked = _settings.IsLocationValidationEnabled();
            BranchLatitude.Text = _settings.GetBranchLatitude();
            BranchLongitude.Text = _settings.GetBranchLongitude();
            BranchRadius.Text = _settings.GetBranchRadiusMeters().ToString();
            PrintTimeout.Text = Math.Max(1, _settings.GetPrintTimeoutMs() / 1000).ToString();
            PrintRetries.Text = _settings.GetPrintRetries().ToString();
            PrintBridgeEnabled.IsChecked = _settings.IsPrintBridgeEnabled();
            PrintBridgeHost.Text = string.IsNullOrWhiteSpace(_settings.GetPrintBridgeHost()) ? LocalIPv4() : _settings.GetPrintBridgeHost();
            PrintBridgePort.Text = _settings.GetPrintBridgePort().ToString();
            SelectComboByTag(PrintBridgeDefaultPrinter, _settings.GetPrintBridgeDefaultPrinter());
            AutoCut.IsChecked = _settings.IsAutoCutEnabled();
            PrintLogo.IsChecked = _settings.IsPrintLogoEnabled();
            PrintLogoPath.Text = _settings.GetPrintLogoPath();
            SelectComboByContent(OperationMode, _settings.GetOperationMode());
            SelectComboByTag(PrintWidth, _settings.GetPrintWidth().ToString());
            UpdatePrintBridgeUrlText();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            ShowSuccess("Configuracion guardada correctamente. Algunos cambios aplican al reiniciar la ventana principal.");
        }

        private void SaveSettings()
        {
            _settings.SaveTerminalSettings(
                TerminalName.Text,
                BranchName.Text,
                TerminalCode.Text,
                SelectedComboText(OperationMode, "Caja"),
                SystemUrl.Text,
                IsChecked(AutoReload),
                Math.Max(1, ParseInt(PrintTimeout.Text, 5)) * 1000,
                SelectedComboTag(PrintWidth, "384") == "576" ? 576 : 384,
                IsChecked(AutoCut),
                IsChecked(PrintLogo),
                PrintLogoPath.Text,
                IsChecked(KioskMode),
                IsChecked(AppAutoStart),
                IsChecked(TouchKeyboard),
                IsChecked(KeepScreenOn),
                IsChecked(LockWebZoom),
                IsChecked(ClearCacheOnStart),
                ParseInt(PrintRetries.Text, 1),
                IsChecked(PrintBridgeEnabled),
                PrintBridgeHost.Text,
                Clamp(ParseInt(PrintBridgePort.Text, 9123), 1, 65535),
                SelectedComboTag(PrintBridgeDefaultPrinter, "cash"),
                IsChecked(LocationValidation),
                BranchLatitude.Text,
                BranchLongitude.Text,
                ParseInt(BranchRadius.Text, 80));

            AppStartupService.Apply(IsChecked(AppAutoStart));
        }

        private async void TestConnectivity_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            var url = string.IsNullOrWhiteSpace(SystemUrl.Text) ? SettingsService.DefaultSystemUrl : SystemUrl.Text.Trim();
            ShowInfo("Ejecutando diagnostico de conectividad...");
            var report = await BuildConnectivityReport(url);
            ShowReportDialog(
                "Test de conectividad",
                "Revision de red, DNS, web, bridge de impresion y notificaciones.",
                report);

            if (report.HasErrors)
                ShowError("Diagnostico terminado con errores.");
            else
                ShowSuccess("Diagnostico terminado correctamente.");
        }

        private async Task<DiagnosticReport> BuildConnectivityReport(string url)
        {
            var report = new DiagnosticReport();
            var summary = report.AddSection("Resumen");
            summary.Add("Fecha", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), "info");
            summary.Add("Equipo", Environment.MachineName, "info");
            summary.Add("Red disponible", NetworkInterface.GetIsNetworkAvailable() ? "SI" : "NO", NetworkInterface.GetIsNetworkAvailable() ? "ok" : "error");
            summary.Add("URL configurada", url, "info");

            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
            {
                report.HasErrors = true;
                report.AddSection("Sistema web").Add("URL", "URL invalida", "error");
                return report;
            }

            var web = report.AddSection("Sistema web");
            web.Add("Host", uri.Host, "info");
            web.Add("Esquema", uri.Scheme, uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? "ok" : "warning");

            await AddDnsCheck(report, uri.Host);
            await AddHttpCheck(report, uri);
            await AddBridgeCheck(report);

            var notifications = report.AddSection("Notificaciones");
            notifications.Add("Estado Windows", SystemNotificationService.NotificationStatus(), "info");
            return report;
        }

        private static async Task AddDnsCheck(DiagnosticReport report, string host)
        {
            var section = report.AddSection("DNS");
            section.Add("Host consultado", host, "info");
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(host);
                section.Add("Resultado", "OK", "ok");
                section.Add("Direcciones", string.Join(", ", addresses.Select(a => a.ToString()).Take(4)), "info");
            }
            catch (Exception ex)
            {
                report.HasErrors = true;
                section.Add("Resultado", "ERROR", "error");
                section.Add("Detalle", ex.Message, "error");
            }
        }

        private static async Task AddHttpCheck(DiagnosticReport report, Uri uri)
        {
            var section = report.AddSection("HTTP");
            try
            {
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("VictumPOS-Windows/1.0");
                    using (var response = await client.GetAsync(uri))
                    {
                        if (!response.IsSuccessStatusCode)
                            report.HasErrors = true;

                        section.Add("Resultado", response.IsSuccessStatusCode ? "OK" : "ERROR", response.IsSuccessStatusCode ? "ok" : "error");
                        section.Add("Codigo", ((int)response.StatusCode).ToString(), response.IsSuccessStatusCode ? "ok" : "error");
                        section.Add("Respuesta", response.ReasonPhrase, "info");
                    }
                }
            }
            catch (Exception ex)
            {
                report.HasErrors = true;
                section.Add("Resultado", "ERROR", "error");
                section.Add("Detalle", ex.Message, "error");
            }
        }

        private async Task AddBridgeCheck(DiagnosticReport report)
        {
            var section = report.AddSection("Bridge de impresion");
            try
            {
                var status = await _printBridge.GetStatusAsync();
                section.Add("Estado", status.Message, status.IsHealthy || status.IsRunning ? "ok" : "warning");
            }
            catch (Exception ex)
            {
                report.HasErrors = true;
                section.Add("Estado", "ERROR", "error");
                section.Add("Detalle", ex.Message, "error");
            }
        }

        private void TestNotification_Click(object sender, RoutedEventArgs e)
        {
            var status = SystemNotificationService.Initialize();
            var shown = SystemNotificationService.Show("VictumPOS", "Notificacion de prueba enviada desde configuracion.");
            if (shown)
                ShowSuccess("Notificacion enviada. Estado Windows: " + status);
            else
                ShowError("No se pudo enviar la notificacion. Estado Windows: " + status);
        }

        private void DeviceInfo_Click(object sender, RoutedEventArgs e)
        {
            var report = new DiagnosticReport();
            var device = report.AddSection("Equipo");
            device.Add("Nombre", Environment.MachineName, "info");
            device.Add("Usuario", Environment.UserName, "info");
            device.Add("Windows", Environment.OSVersion.ToString(), "info");
            device.Add(".NET Framework", Environment.Version.ToString(), "info");

            var terminal = report.AddSection("Terminal");
            terminal.Add("Terminal", TerminalName.Text, "info");
            terminal.Add("Sucursal", BranchName.Text, "info");
            terminal.Add("Codigo", TerminalCode.Text, "info");
            terminal.Add("Modo", SelectedComboText(OperationMode, "Caja"), "info");

            var paths = report.AddSection("Rutas");
            paths.Add("Aplicacion", AppDomain.CurrentDomain.BaseDirectory, "info");
            paths.Add("Configuracion", _settings.SettingsFilePath, "info");
            paths.Add("URL sistema", SystemUrl.Text, "info");

            ShowReportDialog(
                "Informacion del dispositivo",
                "Datos principales del equipo, terminal y rutas usadas por VictumPOS.",
                report);
        }

        private void Logs_Click(object sender, RoutedEventArgs e)
        {
            ShowScrollableDialog("Logs recientes", Logger.ReadRecent());
        }

        private void ClearCacheNextStart_Click(object sender, RoutedEventArgs e)
        {
            ClearCacheOnStart.IsChecked = true;
            SaveSettings();
            ShowInfo("La cache web se limpiara al proximo inicio.");
        }

        private async void RefreshPrintBridge_Click(object sender, RoutedEventArgs e)
        {
            await RefreshPrintBridgeStatusAsync();
        }

        private async void InstallStartPrintBridge_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            ShowInfo("Iniciando bridge local de impresion sin permisos de administrador.");
            try
            {
                ShowInfo(await _printBridge.InstallAndStartAsync());
                await RefreshPrintBridgeStatusAsync();
            }
            catch (Exception ex)
            {
                ShowError("No se pudo activar el bridge: " + ex.Message);
            }
        }

        private async void InstallAdminPrintBridge_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            ShowInfo("Instalando servicio Windows. Windows puede pedir permisos de administrador.");
            try
            {
                ShowInfo(await _printBridge.InstallWindowsServiceAndStartAsync());
                await RefreshPrintBridgeStatusAsync();
            }
            catch (Exception ex)
            {
                ShowError("No se pudo instalar el servicio: " + ex.Message);
            }
        }

        private async void StopPrintBridge_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ShowInfo(await _printBridge.StopAsync());
                await RefreshPrintBridgeStatusAsync();
            }
            catch (Exception ex)
            {
                ShowError("No se pudo detener el bridge: " + ex.Message);
            }
        }

        private async Task RefreshPrintBridgeStatusAsync()
        {
            try
            {
                var status = await _printBridge.GetStatusAsync();
                PrintBridgeStatusText.Text = status.Message;
                UpdatePrintBridgeUrlText();
            }
            catch (Exception ex)
            {
                PrintBridgeStatusText.Text = "No se pudo consultar el servicio: " + ex.Message;
            }
        }

        private async void TestPrintBridgeRoute_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            UpdatePrintBridgeUrlText();
            ShowInfo("Probando ruta de impresion por bridge...");

            try
            {
                var result = await _printBridge.SendTestPrintAsync(PrintBridgeUrl(), SelectedComboTag(PrintBridgeDefaultPrinter, "cash"));
                ShowInfo(result);
            }
            catch (Exception ex)
            {
                ShowError("No se pudo probar el bridge: " + ex.Message);
            }
        }

        private void CopyPrintBridgeUrl_Click(object sender, RoutedEventArgs e)
        {
            UpdatePrintBridgeUrlText();
            Clipboard.SetText(PrintBridgeUrl());
            ShowSuccess("URL del bridge copiada.");
        }

        private void BrowsePrintLogo_Click(object sender, RoutedEventArgs e)
        {
            var picker = new OpenFileDialog
            {
                Filter = "Imagenes|*.png;*.jpg;*.jpeg;*.bmp|Todos los archivos|*.*",
                Title = "Seleccionar logo de impresion"
            };

            if (picker.ShowDialog() == true)
            {
                PrintLogoPath.Text = picker.FileName;
                PrintLogo.IsChecked = true;
                SaveSettings();
                ShowSuccess("Logo de impresion actualizado.");
            }
        }

        private void UseDefaultPrintLogo_Click(object sender, RoutedEventArgs e)
        {
            PrintLogoPath.Text = "";
            PrintLogo.IsChecked = true;
            SaveSettings();
            ShowSuccess("Se usara el logo incluido en Assets/logo.png.");
        }

        private void UpdatePrintBridgeUrlText()
        {
            PrintBridgeUrlText.Text = "URL para enrutar impresion: " + PrintBridgeUrl();
        }

        private string PrintBridgeUrl()
        {
            var host = string.IsNullOrWhiteSpace(PrintBridgeHost.Text) ? LocalIPv4() : PrintBridgeHost.Text.Trim();
            var port = Clamp(ParseInt(PrintBridgePort.Text, 9123), 1, 65535);
            return "http://" + host + ":" + port;
        }

        private static string LocalIPv4()
        {
            try
            {
                foreach (var address in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                    if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                        return address.ToString();
            }
            catch
            {
            }

            return Environment.MachineName;
        }

        private void SelectComboByContent(ComboBox combo, string value)
        {
            foreach (var item in combo.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedItem = item;
                    return;
                }
            }

            combo.SelectedIndex = 0;
        }

        private void SelectComboByTag(ComboBox combo, string value)
        {
            foreach (var item in combo.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Tag?.ToString(), value, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedItem = item;
                    return;
                }
            }

            combo.SelectedIndex = 0;
        }

        private string SelectedComboText(ComboBox combo, string fallback)
        {
            var item = combo.SelectedItem as ComboBoxItem;
            return item?.Content?.ToString() ?? fallback;
        }

        private string SelectedComboTag(ComboBox combo, string fallback)
        {
            var item = combo.SelectedItem as ComboBoxItem;
            return item?.Tag?.ToString() ?? fallback;
        }

        private int ParseInt(string value, int fallback)
        {
            int result;
            return int.TryParse((value ?? "").Trim(), out result) ? result : fallback;
        }

        private int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private bool IsChecked(CheckBox checkBox)
        {
            return checkBox.IsChecked == true;
        }

        private void ShowReportDialog(string title, string subtitle, DiagnosticReport report)
        {
            var owner = Window.GetWindow(this);
            var headerContent = new StackPanel { Margin = new Thickness(20, 18, 20, 18) };
            headerContent.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 24,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White
            });
            headerContent.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(217, 255, 255)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });

            var header = new Border
            {
                Background = (Brush)FindResource("ToolbarGradientBrush"),
                CornerRadius = new CornerRadius(10, 10, 0, 0),
                Child = headerContent
            };

            var sections = new StackPanel { Margin = new Thickness(16, 14, 16, 8) };
            foreach (var section in report.Sections)
                sections.Children.Add(BuildReportSection(section));

            var scrollViewer = new ScrollViewer
            {
                Content = sections,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            var closeButton = new Button
            {
                Width = 120,
                HorizontalAlignment = HorizontalAlignment.Right,
                Style = (Style)FindResource("SecondaryButtonStyle"),
                Margin = new Thickness(16, 0, 16, 16),
                Content = new TextBlock { Text = "Cerrar", Foreground = Brushes.White }
            };

            var layout = new DockPanel { Background = (Brush)FindResource("AppBackgroundBrush") };
            DockPanel.SetDock(header, Dock.Top);
            DockPanel.SetDock(closeButton, Dock.Bottom);
            layout.Children.Add(header);
            layout.Children.Add(closeButton);
            layout.Children.Add(scrollViewer);

            var window = new Window
            {
                Title = title,
                Content = layout,
                Width = 720,
                Height = 620,
                MinWidth = 560,
                MinHeight = 420,
                WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
                Owner = owner
            };

            closeButton.Click += (_, __) => window.Close();
            window.ShowDialog();
        }

        private UIElement BuildReportSection(DiagnosticSection section)
        {
            var rows = new StackPanel();
            rows.Children.Add(new TextBlock
            {
                Text = section.Title,
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("PrimaryDarkBrush"),
                Margin = new Thickness(0, 0, 0, 8)
            });

            foreach (var row in section.Rows)
                rows.Children.Add(BuildReportRow(row));

            return new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 14, 16, 12),
                Margin = new Thickness(0, 0, 0, 10),
                Child = rows
            };
        }

        private UIElement BuildReportRow(DiagnosticRow row)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 7) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock
            {
                Text = row.Label,
                Foreground = (Brush)FindResource("AppMutedTextBrush"),
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            };

            var value = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(row.Value) ? "-" : row.Value,
                Foreground = (Brush)FindResource("AppTextBrush"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(10, 0, 10, 0)
            };

            var badge = new Border
            {
                Background = StatusBackground(row.Status),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 3, 8, 3),
                Child = new TextBlock
                {
                    Text = StatusLabel(row.Status),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = StatusForeground(row.Status)
                }
            };

            Grid.SetColumn(label, 0);
            Grid.SetColumn(value, 1);
            Grid.SetColumn(badge, 2);
            grid.Children.Add(label);
            grid.Children.Add(value);
            grid.Children.Add(badge);
            return grid;
        }

        private static Brush StatusBackground(string status)
        {
            switch ((status ?? "").ToLowerInvariant())
            {
                case "ok": return new SolidColorBrush(Color.FromRgb(220, 252, 231));
                case "warning": return new SolidColorBrush(Color.FromRgb(254, 243, 199));
                case "error": return new SolidColorBrush(Color.FromRgb(254, 226, 226));
                default: return new SolidColorBrush(Color.FromRgb(241, 245, 249));
            }
        }

        private static Brush StatusForeground(string status)
        {
            switch ((status ?? "").ToLowerInvariant())
            {
                case "ok": return new SolidColorBrush(Color.FromRgb(22, 101, 52));
                case "warning": return new SolidColorBrush(Color.FromRgb(146, 64, 14));
                case "error": return new SolidColorBrush(Color.FromRgb(153, 27, 27));
                default: return new SolidColorBrush(Color.FromRgb(71, 85, 105));
            }
        }

        private static string StatusLabel(string status)
        {
            switch ((status ?? "").ToLowerInvariant())
            {
                case "ok": return "OK";
                case "warning": return "REV";
                case "error": return "ERROR";
                default: return "INFO";
            }
        }

        private void ShowScrollableDialog(string title, string message)
        {
            var owner = Window.GetWindow(this);
            var textBox = new TextBox
            {
                Text = string.IsNullOrWhiteSpace(message) ? "No hay logs recientes." : message,
                IsReadOnly = true,
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Margin = new Thickness(12)
            };

            var closeButton = new Button
            {
                Content = "Cerrar",
                Width = 110,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(12, 0, 12, 12)
            };

            var layout = new DockPanel();
            DockPanel.SetDock(closeButton, Dock.Bottom);
            layout.Children.Add(closeButton);
            layout.Children.Add(textBox);

            var window = new Window
            {
                Title = title,
                Content = layout,
                Width = 860,
                Height = 560,
                MinWidth = 520,
                MinHeight = 320,
                WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
                Owner = owner
            };

            closeButton.Click += (_, __) => window.Close();
            window.ShowDialog();
        }

        private void ShowSuccess(string msg) { ShowStatus("Exito: " + msg); }
        private void ShowError(string msg) { ShowStatus("Error: " + msg); }
        private void ShowInfo(string msg) { ShowStatus("Info: " + msg); }

        private void ShowStatus(string msg)
        {
            StatusText.Text = msg;
            StatusBar.Visibility = Visibility.Visible;
        }

        private sealed class DiagnosticReport
        {
            public bool HasErrors { get; set; }
            public List<DiagnosticSection> Sections { get; } = new List<DiagnosticSection>();

            public DiagnosticSection AddSection(string title)
            {
                var section = new DiagnosticSection(title);
                Sections.Add(section);
                return section;
            }
        }

        private sealed class DiagnosticSection
        {
            public DiagnosticSection(string title)
            {
                Title = title;
            }

            public string Title { get; private set; }
            public List<DiagnosticRow> Rows { get; } = new List<DiagnosticRow>();

            public void Add(string label, string value, string status)
            {
                Rows.Add(new DiagnosticRow(label, value, status));
            }
        }

        private sealed class DiagnosticRow
        {
            public DiagnosticRow(string label, string value, string status)
            {
                Label = label;
                Value = value;
                Status = status;
            }

            public string Label { get; private set; }
            public string Value { get; private set; }
            public string Status { get; private set; }
        }
    }
}
