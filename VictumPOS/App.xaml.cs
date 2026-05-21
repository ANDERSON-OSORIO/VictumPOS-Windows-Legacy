using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
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
            SystemNotificationService.Initialize();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            SystemNotificationService.Shutdown();
            base.OnExit(e);
        }
    }
}
