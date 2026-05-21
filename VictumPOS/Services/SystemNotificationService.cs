using System;
using System.IO;
using System.Windows;
using System.Windows.Forms;

namespace VictumPOS.Services
{
    public static class SystemNotificationService
    {
        private static NotifyIcon _notifyIcon;
        private static System.Drawing.Icon _icon;
        private static bool _ownsIcon;

        public static string Initialize()
        {
            try
            {
                if (_notifyIcon == null)
                {
                    _icon = ResolveIcon();
                    _notifyIcon = new NotifyIcon
                    {
                        Icon = _icon,
                        Visible = true,
                        Text = "VictumPOS"
                    };
                }
                else
                {
                    _notifyIcon.Visible = true;
                }

                return "Activadas (bandeja Windows)";
            }
            catch (Exception ex)
            {
                Logger.Log("No se pudo registrar notificaciones Windows: " + ex.Message);
                return "No registrado: " + ex.Message;
            }
        }

        public static string NotificationStatus()
        {
            return _notifyIcon == null ? "No inicializadas" : "Activadas (bandeja Windows)";
        }

        public static bool Show(string title, string message)
        {
            try
            {
                Initialize();
                if (_notifyIcon != null)
                {
                    _notifyIcon.BalloonTipTitle = string.IsNullOrWhiteSpace(title) ? "VictumPOS" : title;
                    _notifyIcon.BalloonTipText = message ?? "";
                    _notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
                    _notifyIcon.Visible = true;
                    _notifyIcon.ShowBalloonTip(7000);
                    Logger.Log("Notificacion Windows enviada: " + _notifyIcon.BalloonTipTitle);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Log("No se pudo mostrar notificacion Windows: " + ex.Message);
            }

            try
            {
                System.Windows.MessageBox.Show(message ?? "", title ?? "VictumPOS", MessageBoxButton.OK, MessageBoxImage.Information);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void Shutdown()
        {
            try
            {
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                    _notifyIcon = null;
                }

                if (_icon != null && _ownsIcon)
                {
                    _icon.Dispose();
                    _icon = null;
                }

                _ownsIcon = false;
            }
            catch
            {
            }
        }

        private static System.Drawing.Icon ResolveIcon()
        {
            try
            {
                var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "favicon.ico");
                if (File.Exists(iconPath))
                {
                    _ownsIcon = true;
                    return new System.Drawing.Icon(iconPath);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("No se pudo cargar icono de notificaciones: " + ex.Message);
            }

            _ownsIcon = false;
            return System.Drawing.SystemIcons.Application;
        }
    }
}
