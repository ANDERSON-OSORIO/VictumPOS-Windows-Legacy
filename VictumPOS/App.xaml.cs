using CefSharp;
using CefSharp.Wpf;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using VictumPOS.Services;

namespace VictumPOS
{
    /// <summary>
    /// Logica de interaccion para App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            RegisterGlobalExceptionHandlers();
            InitializeCefSharp();
            SystemNotificationService.Initialize();
        }

        private void RegisterGlobalExceptionHandlers()
        {
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            EventManager.RegisterClassHandler(typeof(TextBox), UIElement.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(TextInput_GotKeyboardFocus), true);
            EventManager.RegisterClassHandler(typeof(PasswordBox), UIElement.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(TextInput_GotKeyboardFocus), true);
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                Logger.Log("Error fatal no controlado: " + e.ExceptionObject);
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                Logger.Log("Error Task no observado: " + e.Exception);
                e.SetObserved();
            };
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Logger.Log("Error UI no controlado: " + e.Exception);
            e.Handled = true;

            try
            {
                MessageBox.Show(
                    "VictumPOS encontro un error, pero la terminal seguira abierta. Si vuelve a ocurrir, revisa logs.txt en ProgramData\\VictumPOS.",
                    "VictumPOS",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch
            {
            }
        }

        private void TextInput_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            try
            {
                if (new SettingsService().IsTouchKeyboardEnabled())
                    TouchKeyboardService.Show();
            }
            catch
            {
            }
        }

        private static void InitializeCefSharp()
        {
            if (Cef.IsInitialized == true)
                return;

            var cachePath = SettingsService.ResolveDataDirectoryPath("CefSharp");
            Directory.CreateDirectory(cachePath);

            var settings = new CefSettings
            {
                CachePath = cachePath,
                RootCachePath = cachePath,
                UserDataPath = cachePath,
                UserAgent = "VictumPOS/PRO (Windows; ESC-POS Enabled)",
                AcceptLanguageList = "es-CO,es,en-US,en",
                Locale = "es-CO",
                PersistSessionCookies = true,
                PersistUserPreferences = true,
                LogSeverity = LogSeverity.Warning,
                LogFile = SettingsService.ResolveDataPath("cefsharp.log")
            };

            AddCefArg(settings, "disable-gpu", "1");
            AddCefArg(settings, "disable-gpu-compositing", "1");
            AddCefArg(settings, "disable-gpu-vsync", "1");
            AddCefArg(settings, "disable-smooth-scrolling", "1");
            AddCefArg(settings, "touch-events", "enabled");
            AddCefArg(settings, "renderer-process-limit", "1");
            AddCefArg(settings, "process-per-site", "1");
            AddCefArg(settings, "disable-site-isolation-trials", "1");
            AddCefArg(settings, "disable-component-update", "1");
            AddCefArg(settings, "disable-default-apps", "1");
            AddCefArg(settings, "disable-extensions", "1");
            AddCefArg(settings, "disable-pdf-extension", "1");
            AddCefArg(settings, "disable-plugins", "1");
            AddCefArg(settings, "disable-sync", "1");
            AddCefArg(settings, "disable-translate", "1");
            AddCefArg(settings, "enable-low-end-device-mode", "1");
            AddCefArg(settings, "disk-cache-size", "67108864");
            AddCefArg(settings, "media-cache-size", "16777216");
            AddCefArg(settings, "disable-features", "AudioServiceOutOfProcess,AutofillServerCommunication,BackForwardCache,CalculateNativeWinOcclusion,MediaRouter,OptimizationHints");

            if (new SettingsService().IsWebZoomLocked())
                AddCefArg(settings, "disable-pinch", "1");

            if (!Cef.Initialize(settings))
                throw new InvalidOperationException("No se pudo inicializar CefSharp.");
        }

        private static void AddCefArg(CefSettings settings, string key, string value)
        {
            settings.CefCommandLineArgs[key] = value;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            SystemNotificationService.Shutdown();
            if (Cef.IsInitialized == true)
                Cef.Shutdown();
            base.OnExit(e);
        }
    }
}
