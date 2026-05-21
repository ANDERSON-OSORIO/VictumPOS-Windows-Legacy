using System;
using System.Drawing.Printing;
using System.Windows;
using System.Windows.Controls;
using VictumPOS.Services;

namespace VictumPOS.Views
{
    public partial class SettingsPage : Page
    {
        private readonly SettingsService _settings;
        private readonly PrintService _print;

        public SettingsPage()
        {
            InitializeComponent();
            _settings = new SettingsService();
            _print = new PrintService();

            LoadPrinters();
            LoadSettings();

            KitchenType.SelectionChanged += (_, __) => UpdateUI();
            CashType.SelectionChanged += (_, __) => UpdateUI();
            BarType.SelectionChanged += (_, __) => UpdateUI();
        }

        private void LoadPrinters()
        {
            try
            {
                KitchenWindowsPrinter.Items.Clear();
                CashWindowsPrinter.Items.Clear();
                BarWindowsPrinter.Items.Clear();

                foreach (string printer in PrinterSettings.InstalledPrinters)
                {
                    KitchenWindowsPrinter.Items.Add(printer);
                    CashWindowsPrinter.Items.Add(printer);
                    BarWindowsPrinter.Items.Add(printer);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Error cargando impresoras: " + ex.Message);
                ShowError("Error cargando impresoras: " + ex.Message);
            }
        }

        private void LoadSettings()
        {
            try
            {
                var config = _settings.LoadFull();
                KitchenType.SelectedIndex = config.KitchenType == "windows" ? 1 : 0;
                KitchenIP.Text = config.KitchenIP ?? "";
                KitchenPort.Text = config.KitchenPort ?? "";
                SelectPrinter(KitchenWindowsPrinter, config.KitchenPrinter);

                CashType.SelectedIndex = config.CashType == "windows" ? 1 : 0;
                CashIP.Text = config.CashIP ?? "";
                CashPort.Text = config.CashPort ?? "";
                SelectPrinter(CashWindowsPrinter, config.CashPrinter);

                BarType.SelectedIndex = config.BarType == "windows" ? 1 : 0;
                BarIP.Text = config.BarIP ?? "";
                BarPort.Text = config.BarPort ?? "";
                SelectPrinter(BarWindowsPrinter, config.BarPrinter);

                UpdateUI();
            }
            catch (Exception ex)
            {
                Logger.Log("Error cargando configuracion: " + ex.Message);
                ShowError("Error cargando configuracion: " + ex.Message);
            }
        }

        private void SelectPrinter(ComboBox combo, string value)
        {
            foreach (var item in combo.Items)
            {
                if (string.Equals(item.ToString(), value, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedItem = item;
                    return;
                }
            }
        }

        private void UpdateUI()
        {
            SetPrinterMode(KitchenType.SelectedIndex == 0, KitchenNetworkPanel, KitchenWindowsPanel);
            SetPrinterMode(CashType.SelectedIndex == 0, CashNetworkPanel, CashWindowsPanel);
            SetPrinterMode(BarType.SelectedIndex == 0, BarNetworkPanel, BarWindowsPanel);
        }

        private void SetPrinterMode(bool network, FrameworkElement networkPanel, FrameworkElement windowsPanel)
        {
            networkPanel.Visibility = network ? Visibility.Visible : Visibility.Collapsed;
            windowsPanel.Visibility = network ? Visibility.Collapsed : Visibility.Visible;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var config = _settings.LoadFull();
                config.KitchenType = KitchenType.SelectedIndex == 0 ? "network" : "windows";
                config.KitchenIP = KitchenIP.Text;
                config.KitchenPort = KitchenPort.Text;
                config.KitchenPrinter = KitchenWindowsPrinter.SelectedItem?.ToString() ?? "";

                config.CashType = CashType.SelectedIndex == 0 ? "network" : "windows";
                config.CashIP = CashIP.Text;
                config.CashPort = CashPort.Text;
                config.CashPrinter = CashWindowsPrinter.SelectedItem?.ToString() ?? "";

                config.BarType = BarType.SelectedIndex == 0 ? "network" : "windows";
                config.BarIP = BarIP.Text;
                config.BarPort = BarPort.Text;
                config.BarPrinter = BarWindowsPrinter.SelectedItem?.ToString() ?? "";

                _settings.SaveFull(config);
                ShowSuccess("Configuracion guardada correctamente");
            }
            catch (Exception ex)
            {
                Logger.Log("Error guardando: " + ex.Message);
                ShowError("Error guardando: " + ex.Message);
            }
        }

        private void ReloadPrinters_Click(object sender, RoutedEventArgs e)
        {
            LoadPrinters();
            ShowInfo("Lista de impresoras actualizada");
        }

        private async void TestKitchen_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _print.PrintTest("=== TEST COCINA ===\nPedido #001\nHamburguesa x2\n\nGracias!", "kitchen");
                ShowInfo("Prueba enviada a cocina");
            }
            catch (Exception ex)
            {
                Logger.Log("Error test cocina: " + ex.Message);
                ShowError("Error test cocina: " + ex.Message);
            }
        }

        private async void TestCash_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _print.PrintTest("=== TEST FACTURA ===\nTotal: $25.000\nGracias por su compra", "cash");
                ShowInfo("Prueba enviada a caja");
            }
            catch (Exception ex)
            {
                Logger.Log("Error test caja: " + ex.Message);
                ShowError("Error test caja: " + ex.Message);
            }
        }

        private async void TestBar_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _print.PrintTest("=== TEST BAR ===\nCerveza x2\n\nGracias", "bar");
                ShowInfo("Prueba enviada a bar");
            }
            catch (Exception ex)
            {
                Logger.Log("Error test bar: " + ex.Message);
                ShowError("Error test bar: " + ex.Message);
            }
        }

        private void ShowSuccess(string msg) { ShowStatus("Exito: " + msg); }
        private void ShowError(string msg) { ShowStatus("Error: " + msg); }
        private void ShowInfo(string msg) { ShowStatus("Info: " + msg); }

        private void ShowStatus(string msg)
        {
            StatusText.Text = msg;
            StatusBar.Visibility = Visibility.Visible;
        }
    }
}
