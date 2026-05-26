using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using VictumPOS.Services;

namespace VictumPOS
{
    /// <summary>
    /// Lógica de interacción para App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            RegisterGlobalExceptionHandlers();
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

        protected override void OnExit(ExitEventArgs e)
        {
            SystemNotificationService.Shutdown();
            base.OnExit(e);
        }
    }
}
