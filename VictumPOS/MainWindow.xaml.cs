using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VictumPOS.Helpers;
using VictumPOS.Services;

namespace VictumPOS
{
    public partial class MainWindow : Window
    {
        private const uint ES_CONTINUOUS = 0x80000000;
        private const uint ES_SYSTEM_REQUIRED = 0x00000001;
        private const uint ES_DISPLAY_REQUIRED = 0x00000002;
        private const int SW_RESTORE = 9;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        private readonly SettingsService _settingsService;
        private readonly DispatcherTimer _autoReloadTimer;
        private readonly DispatcherTimer _notificationTimer;
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();
        private CoreWebView2Environment _webViewEnvironment;
        private PrintService _printService;
        private string _offlineCacheFile = "";
        private bool _requestedWebNotificationPermission;
        private Point? _touchStartPoint;
        private DateTime _touchStartTime;
        private readonly Dictionary<int, Point> _activeTouchPoints = new Dictionary<int, Point>();
        private double? _lastTwoFingerAverageY;
        private Point? _twoFingerStartPoint;
        private DateTime _twoFingerStartTime;
        private bool _twoFingerMoved;
        private string _lastTouchGestureAction = "";
        private DateTime _lastTouchGestureTime = DateTime.MinValue;

        public MainWindow()
        {
            InitializeComponent();

            _settingsService = new SettingsService();
            _autoReloadTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _autoReloadTimer.Tick += (_, __) =>
            {
                _autoReloadTimer.Stop();
                LoadHome();
            };
            _notificationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            _notificationTimer.Tick += (_, __) =>
            {
                _notificationTimer.Stop();
                NotificationBar.Visibility = Visibility.Collapsed;
            };

            Closing += (_, __) => SetThreadExecutionState(ES_CONTINUOUS);
            KeyDown += MainWindow_KeyDown;
            PreviewKeyDown += MainWindow_KeyDown;
            webView.PreviewMouseWheel += Browser_PreviewMouseWheel;
            webView.PreviewTouchDown += Browser_PreviewTouchDown;
            webView.PreviewTouchMove += Browser_PreviewTouchMove;
            webView.PreviewTouchUp += Browser_PreviewTouchUp;

            EnableKioskMode();
            ApplyTouchSettings();
            ApplyKeepScreenOn();
            ApplyAppAutoStart();
            InitAsync();
        }

        [DllImport("kernel32.dll")]
        private static extern uint SetThreadExecutionState(uint esFlags);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        private async void InitAsync()
        {
            try
            {
                _printService = new PrintService();
                _offlineCacheFile = SettingsService.ResolveDataPath("offline-cache.html");
                ShowLoader(true);

                var userDataFolder = SettingsService.ResolveDataDirectoryPath("WebView2");
                _webViewEnvironment = await CoreWebView2Environment.CreateAsync(ResolveWebView2RuntimeFolder(), userDataFolder, null);

                await webView.EnsureCoreWebView2Async(_webViewEnvironment);
                webView.CoreWebView2.PermissionRequested += CoreWebView2_PermissionRequested;
                webView.CoreWebView2.NavigationStarting += NavigationStarting;
                webView.CoreWebView2.NavigationCompleted += NavigationCompleted;
                webView.CoreWebView2.WebMessageReceived += WebMessageReceived;
                webView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
                webView.CoreWebView2.ServerCertificateErrorDetected += CoreWebView2_ServerCertificateErrorDetected;
                webView.CoreWebView2.DocumentTitleChanged += CoreWebView2_DocumentTitleChanged;
                webView.CoreWebView2.ProcessFailed += CoreWebView2_ProcessFailed;
                webView.PreviewKeyDown += MainWindow_KeyDown;
                webView.KeyDown += MainWindow_KeyDown;

                webView.CoreWebView2.Settings.UserAgent = "VictumPOS/PRO (Windows; ESC-POS Enabled)";
                webView.CoreWebView2.Settings.IsScriptEnabled = true;
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                webView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = true;
                webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                webView.CoreWebView2.Settings.IsWebMessageEnabled = true;
                webView.CoreWebView2.Settings.IsZoomControlEnabled = !_settingsService.IsWebZoomLocked();

                if (_settingsService.ShouldClearCacheOnStart())
                    await ClearWebCacheAsync();

                await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(BuildBridgeScript());
                LoadHome();
            }
            catch (Exception ex)
            {
                Logger.Log("Error inicializando WebView: " + ex.Message);
                ShowError("Error inicializando WebView: " + ex.Message);
            }
        }

        private string BuildBridgeScript()
        {
            return @"
                (function() {
                    if (window.__victumBridgeHooked) return;
                    window.__victumBridgeHooked = true;
                    window.VictumPOS = " + TerminalStateJson() + @";
                    window.VictumTerminalName = window.VictumPOS.terminalName || '';
                    window.chrome = window.chrome || {};
                    window.chrome.webview = window.chrome.webview || {};
                    const original = window.Notification;
                    if (original) {
                        function HookNotification(title, options) {
                            try {
                                chrome.webview.postMessage({
                                    type: 'notify',
                                    title: String(title || 'Notificacion'),
                                    body: options && options.body ? String(options.body) : ''
                                });
                            } catch (_) {}
                            return new original(title, options);
                        }
                        HookNotification.requestPermission = original.requestPermission.bind(original);
                        Object.defineProperty(HookNotification, 'permission', { get: function() { return original.permission; } });
                        HookNotification.prototype = original.prototype;
                        window.Notification = HookNotification;
                        window.VictumPOS.requestNotificationPermission = function() {
                            try { if (original.permission === 'default') original.requestPermission(); } catch (_) {}
                        };
                    }
                    document.addEventListener('keydown', function(e) {
                        const key = String(e.key || '').toLowerCase();
                        let action = '';
                        if (e.altKey && key === 'arrowleft') action = 'back';
                        else if (e.altKey && key === 'arrowright') action = 'forward';
                        else if (e.altKey && key === 'home') action = 'home';
                        else if (key === 'f5' || ((e.ctrlKey || e.metaKey) && key === 'r')) action = 'reload';
                        else if ((e.ctrlKey || e.metaKey) && key === 'p') action = 'printers';
                        else if ((e.ctrlKey || e.metaKey) && key === ',') action = 'config';
                        else if ((e.ctrlKey || e.metaKey) && key === '/') action = 'shortcuts';
                        else if ((e.ctrlKey || e.metaKey) && key === 'q') action = 'exit';
                        if (!action) return;
                        e.preventDefault();
                        e.stopPropagation();
                        chrome.webview.postMessage({ type: 'shortcut', action: action });
                    }, true);
                    function isTextInput(el) {
                        while (el && el !== document.documentElement) {
                            const tag = String(el.tagName || '').toLowerCase();
                            if (el.isContentEditable) return true;
                            if (tag === 'textarea' || tag === 'select') return true;
                            if (tag === 'input') {
                                const type = String(el.type || 'text').toLowerCase();
                                return ['button','checkbox','color','file','hidden','image','radio','range','reset','submit'].indexOf(type) < 0;
                            }
                            el = el.parentElement;
                        }
                        return false;
                    }
                    function showTouchKeyboard(target) {
                        if (!isTextInput(target)) return;
                        try { chrome.webview.postMessage({ type: 'touchKeyboard', action: 'show' }); } catch (_) {}
                    }
                    document.addEventListener('focusin', function(e) {
                        setTimeout(function() { showTouchKeyboard(e.target); }, 80);
                    }, true);
                    document.addEventListener('touchend', function(e) {
                        setTimeout(function() { showTouchKeyboard(e.target); }, 80);
                    }, true);
                    let touchStart = null;
                    let touchStartTime = 0;
                    let touchMoved = false;
                    let lastTwoFingerY = null;
                    function touchGesturesEnabled() {
                        return !!(window.VictumPOS && window.VictumPOS.touchGesturesEnabled);
                    }
                    function postTouchGesture(action) {
                        if (!touchGesturesEnabled()) return;
                        try { chrome.webview.postMessage({ type: 'touchGesture', action: action }); } catch (_) {}
                    }
                    function touchList(e) {
                        const list = [];
                        for (let i = 0; i < e.touches.length; i++) list.push({ x: e.touches[i].clientX, y: e.touches[i].clientY });
                        return list;
                    }
                    function avg(points) {
                        let x = 0, y = 0;
                        for (let i = 0; i < points.length; i++) { x += points[i].x; y += points[i].y; }
                        return { x: x / Math.max(1, points.length), y: y / Math.max(1, points.length) };
                    }
                    function findScrollable(el) {
                        while (el && el !== document.body && el !== document.documentElement) {
                            const style = window.getComputedStyle(el);
                            if (/(auto|scroll)/.test(style.overflowY || '') && el.scrollHeight > el.clientHeight) return el;
                            el = el.parentElement;
                        }
                        return document.scrollingElement || document.documentElement;
                    }
                    document.addEventListener('touchstart', function(e) {
                        if (!touchGesturesEnabled()) {
                            touchStart = null;
                            return;
                        }
                        touchStart = touchList(e);
                        touchStartTime = Date.now();
                        touchMoved = false;
                        lastTwoFingerY = touchStart.length === 2 ? avg(touchStart).y : null;
                    }, true);
                    document.addEventListener('touchmove', function(e) {
                        if (!touchGesturesEnabled()) return;
                        if (!touchStart || e.touches.length !== 2) return;
                        const current = avg(touchList(e));
                        if (lastTwoFingerY === null) lastTwoFingerY = current.y;
                        const dy = current.y - lastTwoFingerY;
                        lastTwoFingerY = current.y;
                        if (Math.abs(dy) < 3) return;
                        touchMoved = true;
                        const target = document.elementFromPoint(current.x, current.y) || document.activeElement;
                        findScrollable(target).scrollTop += (-dy * 1.35);
                        e.preventDefault();
                    }, { capture: true, passive: false });
                    document.addEventListener('touchend', function(e) {
                        if (!touchGesturesEnabled()) {
                            touchStart = null;
                            return;
                        }
                        if (!touchStart) return;
                        const elapsed = Date.now() - touchStartTime;
                        const start = touchStart[0];
                        const changed = e.changedTouches && e.changedTouches.length ? e.changedTouches[0] : null;
                        if (touchStart.length === 2 && !touchMoved && elapsed < 550) {
                            postTouchGesture('gestures');
                            touchStart = null;
                            return;
                        }
                        if (touchStart.length !== 1 || !changed || elapsed > 900) {
                            touchStart = null;
                            return;
                        }
                        const dx = changed.clientX - start.x;
                        const dy = changed.clientY - start.y;
                        if (Math.abs(dx) >= 120 && Math.abs(dx) >= Math.abs(dy) * 1.6) {
                            postTouchGesture(dx > 0 ? 'back' : 'forward');
                        } else if (Math.abs(dy) >= 140 && Math.abs(dy) >= Math.abs(dx) * 1.4) {
                            if (dy > 0 && start.y <= 96) postTouchGesture('reload');
                            else if (dy < 0 && start.y >= (window.innerHeight - 120)) postTouchGesture('home');
                        }
                        touchStart = null;
                    }, true);
                })();";
        }

        private string TerminalStateJson()
        {
            var state = new Dictionary<string, object>
            {
                { "terminalName", TerminalName() },
                { "branchName", BranchName() },
                { "terminalCode", TerminalCode() },
                { "operationMode", OperationMode() },
                { "platform", "Windows" },
                { "locationValidation", _settingsService.IsLocationValidationEnabled() },
                { "branchLatitude", _settingsService.GetBranchLatitude() },
                { "branchLongitude", _settingsService.GetBranchLongitude() },
                { "branchRadiusMeters", _settingsService.GetBranchRadiusMeters() },
                { "printBridgeEnabled", _settingsService.IsPrintBridgeEnabled() },
                { "printBridgeUrl", PrintBridgeUrl() },
                { "printBridgePort", _settingsService.GetPrintBridgePort() },
                { "printBridgeDefaultPrinter", _settingsService.GetPrintBridgeDefaultPrinter() },
                { "touchGesturesEnabled", _settingsService.IsTouchGesturesEnabled() }
            };

            return _serializer.Serialize(state);
        }

        private void InjectTerminalState()
        {
            try
            {
                _ = webView.CoreWebView2.ExecuteScriptAsync(
                    "window.VictumPOS=" + TerminalStateJson() + ";window.VictumTerminalName=window.VictumPOS.terminalName||'';");
            }
            catch (Exception ex)
            {
                Logger.Log("No se pudo inyectar estado de terminal: " + ex.Message);
            }
        }

        private async Task ClearWebCacheAsync()
        {
            try
            {
                webView.CoreWebView2.CookieManager.DeleteAllCookies();
                await webView.CoreWebView2.CallDevToolsProtocolMethodAsync("Network.clearBrowserCache", "{}");
                await webView.CoreWebView2.CallDevToolsProtocolMethodAsync("Network.clearBrowserCookies", "{}");
                Logger.Log("Cache WebView limpiado al iniciar por configuracion");
            }
            catch (Exception ex)
            {
                Logger.Log("No se pudo limpiar cache WebView: " + ex.Message);
            }
        }

        private string ResolveWebView2RuntimeFolder()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var fixedRuntimeRoot = Path.Combine(baseDir, "WebView2FixedRuntime");
                if (Directory.Exists(fixedRuntimeRoot) && File.Exists(Path.Combine(fixedRuntimeRoot, "msedgewebview2.exe")))
                    return fixedRuntimeRoot;

                foreach (var folder in Directory.GetDirectories(baseDir, "Microsoft.WebView2.FixedVersionRuntime.*"))
                    if (File.Exists(Path.Combine(folder, "msedgewebview2.exe")))
                        return folder;
            }
            catch (Exception ex)
            {
                Logger.Log("No se pudo resolver runtime fijo WebView2: " + ex.Message);
            }

            return null;
        }

        private void LoadHome()
        {
            LoadUrlWithTerminal(HomeUrl());
        }

        private void LoadUrlWithTerminal(string url)
        {
            try
            {
                if (webView.CoreWebView2 == null)
                {
                    webView.Source = new Uri(url);
                    return;
                }

                _autoReloadTimer.Stop();
                ShowLoader(true);
                ApplyTerminalCookies();

                if (IsTrustedUrl(url) && _webViewEnvironment != null)
                {
                    var request = _webViewEnvironment.CreateWebResourceRequest(url, "GET", null, TerminalHeaders());
                    webView.CoreWebView2.NavigateWithWebResourceRequest(request);
                    return;
                }

                webView.CoreWebView2.Navigate(url);
            }
            catch (Exception ex)
            {
                Logger.Log("Error navegando: " + ex.Message);
                ShowError("Error navegando: " + ex.Message);
            }
        }

        private string TerminalHeaders()
        {
            var headers = new List<string>();
            AddHeader(headers, "X-Victum-Terminal", TerminalName());
            AddHeader(headers, "X-Victum-Branch", BranchName());
            AddHeader(headers, "X-Victum-Terminal-Code", TerminalCode());
            AddHeader(headers, "X-Victum-Mode", OperationMode());
            headers.Add("X-Victum-App: Windows");

            if (_settingsService.IsLocationValidationEnabled())
            {
                headers.Add("X-Victum-Location-Validation: true");
                AddHeader(headers, "X-Victum-Branch-Lat", _settingsService.GetBranchLatitude());
                AddHeader(headers, "X-Victum-Branch-Lng", _settingsService.GetBranchLongitude());
                headers.Add("X-Victum-Branch-Radius: " + _settingsService.GetBranchRadiusMeters());
            }

            if (_settingsService.IsPrintBridgeEnabled())
            {
                headers.Add("X-Victum-Print-Bridge-Enabled: true");
                AddHeader(headers, "X-Victum-Print-Bridge-Url", PrintBridgeUrl());
                headers.Add("X-Victum-Print-Bridge-Port: " + _settingsService.GetPrintBridgePort());
                AddHeader(headers, "X-Victum-Print-Bridge-Default-Printer", _settingsService.GetPrintBridgeDefaultPrinter());
            }

            return string.Join("\r\n", headers) + "\r\n";
        }

        private void AddHeader(List<string> headers, string key, string value)
        {
            value = SanitizeHeaderValue(value);
            if (!string.IsNullOrWhiteSpace(value))
                headers.Add(key + ": " + value);
        }

        private void ApplyTerminalCookies()
        {
            try
            {
                Uri uri;
                if (!Uri.TryCreate(HomeUrl(), UriKind.Absolute, out uri))
                    return;

                SetTerminalCookie(uri, "VictumTerminalName", TerminalName());
                SetTerminalCookie(uri, "VictumBranchName", BranchName());
                SetTerminalCookie(uri, "VictumTerminalCode", TerminalCode());
                SetTerminalCookie(uri, "VictumOperationMode", OperationMode());
                SetTerminalCookie(uri, "VictumLocationValidation", _settingsService.IsLocationValidationEnabled() ? "true" : "");
                SetTerminalCookie(uri, "VictumBranchLatitude", _settingsService.GetBranchLatitude());
                SetTerminalCookie(uri, "VictumBranchLongitude", _settingsService.GetBranchLongitude());
                SetTerminalCookie(uri, "VictumBranchRadius", _settingsService.GetBranchRadiusMeters().ToString());
                SetTerminalCookie(uri, "VictumPrintBridgeEnabled", _settingsService.IsPrintBridgeEnabled() ? "true" : "");
                SetTerminalCookie(uri, "VictumPrintBridgeUrl", PrintBridgeUrl());
                SetTerminalCookie(uri, "VictumPrintBridgePort", _settingsService.GetPrintBridgePort().ToString());
                SetTerminalCookie(uri, "VictumPrintBridgeDefaultPrinter", _settingsService.GetPrintBridgeDefaultPrinter());
            }
            catch (Exception ex)
            {
                Logger.Log("No se pudieron aplicar cookies de terminal: " + ex.Message);
            }
        }

        private void SetTerminalCookie(Uri uri, string name, string value)
        {
            var manager = webView.CoreWebView2.CookieManager;
            var cookie = manager.CreateCookie(name, Uri.EscapeDataString(value ?? ""), uri.Host, "/");
            cookie.IsHttpOnly = false;
            cookie.IsSecure = uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
            manager.AddOrUpdateCookie(cookie);
        }

        private string HomeUrl() { return _settingsService.GetSystemUrl(); }

        private string HomeHost()
        {
            Uri uri;
            return Uri.TryCreate(HomeUrl(), UriKind.Absolute, out uri) ? uri.Host : "";
        }

        private bool IsTrustedUrl(string url)
        {
            Uri uri;
            return Uri.TryCreate(url, UriKind.Absolute, out uri) &&
                string.Equals(uri.Host, HomeHost(), StringComparison.OrdinalIgnoreCase);
        }

        private string TerminalName() { return SanitizeHeaderValue(_settingsService.GetTerminalName()); }
        private string BranchName() { return SanitizeHeaderValue(_settingsService.GetBranchName()); }
        private string TerminalCode() { return SanitizeHeaderValue(_settingsService.GetTerminalCode()); }
        private string OperationMode() { return SanitizeHeaderValue(_settingsService.GetOperationMode()); }

        private string PrintBridgeUrl()
        {
            var host = SanitizeHeaderValue(_settingsService.GetPrintBridgeHost());
            if (string.IsNullOrWhiteSpace(host))
                host = Environment.MachineName;

            return "http://" + host + ":" + _settingsService.GetPrintBridgePort();
        }

        private string SanitizeHeaderValue(string value)
        {
            return (value ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim();
        }

        private void CoreWebView2_NewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            if (!string.IsNullOrWhiteSpace(e.Uri))
                LoadUrlWithTerminal(e.Uri);
        }

        private void CoreWebView2_ServerCertificateErrorDetected(object sender, CoreWebView2ServerCertificateErrorDetectedEventArgs e)
        {
            try
            {
                Uri requestUri;
                if (Uri.TryCreate(e.RequestUri, UriKind.Absolute, out requestUri) &&
                    string.Equals(HomeHost(), requestUri.Host, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Log("Certificado aceptado para " + requestUri.Host + ". Error: " + e.ErrorStatus);
                    e.Action = CoreWebView2ServerCertificateErrorAction.AlwaysAllow;
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Error manejando certificado: " + ex.Message);
            }
        }

        private void CoreWebView2_DocumentTitleChanged(object sender, object e)
        {
            Dispatcher.Invoke(() =>
            {
                var title = webView.CoreWebView2.DocumentTitle;
                UrlText.Text = string.IsNullOrWhiteSpace(title) ? "VictumPOS" : title;
            });
        }

        private void CoreWebView2_PermissionRequested(object sender, CoreWebView2PermissionRequestedEventArgs e)
        {
            if (e.PermissionKind == CoreWebView2PermissionKind.Notifications ||
                e.PermissionKind == CoreWebView2PermissionKind.Geolocation)
            {
                e.State = CoreWebView2PermissionState.Allow;
                e.Handled = true;
            }
        }

        private void CoreWebView2_ProcessFailed(object sender, CoreWebView2ProcessFailedEventArgs e)
        {
            Logger.Log("Proceso WebView2 fallo: " + e.ProcessFailedKind);
            ShowNotification("WebView reiniciado", "Se recargara VictumPOS por estabilidad.");
            LoadHome();
        }

        private void EnableKioskMode()
        {
            if (_settingsService.IsKioskModeEnabled())
            {
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
                ResizeMode = ResizeMode.NoResize;
                Topmost = false;
            }
            else
            {
                WindowState = WindowState.Normal;
                WindowStyle = WindowStyle.SingleBorderWindow;
                ResizeMode = ResizeMode.CanResize;
                WindowState = WindowState.Maximized;
                Topmost = false;
            }
        }

        private void ApplyKeepScreenOn()
        {
            if (_settingsService.IsKeepScreenOnEnabled())
                SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);
            else
                SetThreadExecutionState(ES_CONTINUOUS);
        }

        private void ApplyTouchSettings()
        {
            try
            {
                var enabled = _settingsService.IsTouchGesturesEnabled();
                webView.IsManipulationEnabled = enabled;
                GesturesButton.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
                Stylus.SetIsPressAndHoldEnabled(webView, !enabled);
                Stylus.SetIsFlicksEnabled(webView, false);
                Stylus.SetIsTapFeedbackEnabled(webView, !enabled);
                Stylus.SetIsTouchFeedbackEnabled(webView, !enabled);
            }
            catch (Exception ex)
            {
                Logger.Log("No se pudo aplicar configuracion tactil: " + ex.Message);
            }
        }

        private void ApplyAppAutoStart()
        {
            AppStartupService.Apply(_settingsService.IsAppAutoStartEnabled());
        }

        private void NavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs args)
        {
            ShowLoader(true);
        }

        private void NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            ShowLoader(false);

            try
            {
                UrlText.Text = webView.CoreWebView2.DocumentTitle ?? "";
                InjectTerminalState();
                RequestWebNotificationPermission();

                if (args.IsSuccess)
                {
                    _ = SaveOfflineSnapshotAsync();
                    return;
                }

                Logger.Log("Navigation error status: " + args.WebErrorStatus);
                TryLoadOfflineCache();
                ScheduleAutoReload();
            }
            catch
            {
            }
        }

        private void RequestWebNotificationPermission()
        {
            if (_requestedWebNotificationPermission || webView.CoreWebView2 == null)
                return;

            if (!IsTrustedUrl(webView.Source == null ? "" : webView.Source.ToString()))
                return;

            _requestedWebNotificationPermission = true;
            _ = webView.CoreWebView2.ExecuteScriptAsync(
                "setTimeout(function(){ if(window.VictumPOS && window.VictumPOS.requestNotificationPermission) window.VictumPOS.requestNotificationPermission(); }, 1000);");
        }

        private async void WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                if (_printService == null)
                    return;

                if (TryHandleBrowserNotificationMessage(e.WebMessageAsJson))
                    return;

                if (TryHandleShellMessage(e.WebMessageAsJson))
                    return;

                await WebViewBridge.HandleMessage(e.WebMessageAsJson, _printService);
            }
            catch (Exception ex)
            {
                Logger.Log("Error procesando mensaje: " + ex.Message);
                ShowError("Error procesando mensaje: " + ex.Message);
            }
        }

        private bool TryHandleShellMessage(string json)
        {
            Dictionary<string, object> root;
            string type;
            if (!WebViewBridge.TryReadType(json, out root, out type))
                return false;

            if (string.Equals(type, "shortcut", StringComparison.OrdinalIgnoreCase))
            {
                HandleShortcut(WebViewBridge.GetString(root, "action"));
                return true;
            }

            if (string.Equals(type, "touchKeyboard", StringComparison.OrdinalIgnoreCase))
            {
                if (_settingsService.IsTouchKeyboardEnabled())
                    TouchKeyboardService.Show();
                return true;
            }

            if (string.Equals(type, "touchGesture", StringComparison.OrdinalIgnoreCase))
            {
                if (_settingsService.IsTouchGesturesEnabled())
                    DispatchTouchGesture(WebViewBridge.GetString(root, "action"));
                return true;
            }

            return false;
        }

        private bool TryHandleBrowserNotificationMessage(string json)
        {
            Dictionary<string, object> root;
            string type;
            if (!WebViewBridge.TryReadType(json, out root, out type))
                return false;

            if (string.Equals(type, "offlineRetry", StringComparison.OrdinalIgnoreCase))
            {
                LoadHome();
                return true;
            }

            if (string.Equals(type, "offlineLoadCache", StringComparison.OrdinalIgnoreCase))
            {
                NavigateToOfflineCache();
                return true;
            }

            if (!string.Equals(type, "notify", StringComparison.OrdinalIgnoreCase))
                return false;

            ShowNotification(
                WebViewBridge.GetString(root, "title"),
                WebViewBridge.GetString(root, "body"));
            return true;
        }

        private async Task SaveOfflineSnapshotAsync()
        {
            try
            {
                if (webView.CoreWebView2 == null || !IsTrustedUrl(webView.Source == null ? "" : webView.Source.ToString()))
                    return;

                var htmlJson = await webView.CoreWebView2.ExecuteScriptAsync("document.documentElement.outerHTML");
                var html = _serializer.Deserialize<string>(htmlJson) ?? "";
                if (string.IsNullOrWhiteSpace(html))
                    return;

                Directory.CreateDirectory(Path.GetDirectoryName(_offlineCacheFile));
                File.WriteAllText(_offlineCacheFile, html);
            }
            catch (Exception ex)
            {
                Logger.Log("No se pudo guardar cache offline: " + ex.Message);
            }
        }

        private void TryLoadOfflineCache()
        {
            try
            {
                ShowOfflineView(File.Exists(_offlineCacheFile));
            }
            catch (Exception ex)
            {
                Logger.Log("Error cargando cache offline: " + ex.Message);
            }
        }

        private void NavigateToOfflineCache()
        {
            try
            {
                if (!File.Exists(_offlineCacheFile))
                {
                    ShowOfflineView(false);
                    return;
                }

                webView.CoreWebView2.Navigate(new Uri(_offlineCacheFile).AbsoluteUri);
            }
            catch (Exception ex)
            {
                Logger.Log("Error navegando a cache offline: " + ex.Message);
                ShowOfflineView(false);
            }
        }

        private void ShowOfflineView(bool hasCache)
        {
            var cacheButton = hasCache
                ? "<button class='secondary' onclick=\"window.chrome.webview.postMessage({ type: 'offlineLoadCache' });\">Cargar ultima version guardada</button>"
                : "<button class='secondary' disabled>Sin cache disponible</button>";

            var html = @"
<!doctype html><html lang='es'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width, initial-scale=1'>
<title>Sin conexion</title><style>
*{box-sizing:border-box;font-family:'Segoe UI',Arial,sans-serif}body{margin:0;min-height:100vh;background:#f8fafc;display:grid;place-items:center;color:#1f2933}
.card{width:min(560px,92vw);background:white;border:1px solid rgba(13,147,165,.22);border-radius:8px;padding:28px 24px;box-shadow:0 18px 50px rgba(15,23,42,.12)}
h1{margin:0 0 10px;font-size:28px}p{margin:0 0 24px;color:#5d6978;font-size:15px;line-height:1.5}.actions{display:flex;gap:10px;flex-wrap:wrap}
button{border:0;border-radius:8px;padding:12px 14px;font-size:14px;font-weight:600;cursor:pointer}.primary{background:#0d93a5;color:white}.secondary{background:#f1f5f9;color:#1f2933;border:1px solid #cbd5e1}
</style></head><body><main class='card'><h1>Sin conexion a internet</h1><p>No se pudo abrir Victum POS en linea. Reintenta la conexion o carga la ultima version guardada en este equipo.</p><div class='actions'>
<button class='primary' onclick=""window.chrome.webview.postMessage({ type: 'offlineRetry' });"">Reintentar</button>" + cacheButton + "</div></main></body></html>";

            webView.NavigateToString(html);
        }

        private void ScheduleAutoReload()
        {
            if (_settingsService.IsAutoReloadEnabled())
            {
                _autoReloadTimer.Stop();
                _autoReloadTimer.Start();
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (webView.CanGoBack)
                webView.GoBack();
        }

        private void Home_Click(object sender, RoutedEventArgs e)
        {
            LoadHome();
        }

        private void Reload_Click(object sender, RoutedEventArgs e)
        {
            var current = webView.Source == null ? "" : webView.Source.ToString();
            if (string.IsNullOrWhiteSpace(current) ||
                (!current.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                 !current.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                LoadHome();
                return;
            }

            LoadUrlWithTerminal(current);
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            var window = new Window
            {
                Title = "Configuracion de impresoras",
                Content = new Views.SettingsPage(),
                Width = 1000,
                Height = 650
            };
            ShowOwnedWindow(window);
        }

        private void Config_Click(object sender, RoutedEventArgs e)
        {
            var window = new Window
            {
                Title = "Configuracion de terminal",
                Content = new Views.TerminalConfigPage(),
                Width = 1100,
                Height = 760
            };
            ShowOwnedWindow(window);
            ApplyRuntimeSettings();
        }

        private void ApplyRuntimeSettings()
        {
            EnableKioskMode();
            ApplyTouchSettings();
            ApplyKeepScreenOn();
            ApplyAppAutoStart();
            ApplyTerminalCookies();
            InjectTerminalState();
        }

        private void ShowOwnedWindow(Window window)
        {
            window.Owner = this;
            window.Icon = Icon;
            window.ShowInTaskbar = false;
            window.Topmost = false;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            window.Loaded += (_, __) =>
            {
                window.Activate();
                window.Focus();
            };
            window.ShowDialog();
        }

        private void Shortcuts_Click(object sender, RoutedEventArgs e)
        {
            ShowShortcutsDialog();
        }

        private void Gestures_Click(object sender, RoutedEventArgs e)
        {
            ShowGesturesDialog();
        }

        private void ShowShortcutsDialog()
        {
            var content = new StackPanel { Margin = new Thickness(18) };
            content.Children.Add(new TextBlock
            {
                Text = "Atajos de teclado",
                FontSize = 24,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White
            });
            content.Children.Add(new TextBlock
            {
                Text = "Acciones rapidas disponibles en la terminal.",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(217, 255, 255)),
                Margin = new Thickness(0, 4, 0, 0)
            });

            var header = new Border
            {
                Background = (Brush)FindResource("ToolbarGradientBrush"),
                CornerRadius = new CornerRadius(10, 10, 0, 0),
                Child = content
            };

            var list = new StackPanel { Margin = new Thickness(18, 14, 18, 10) };
            AddShortcutRow(list, "Alt + Izquierda", "Atras");
            AddShortcutRow(list, "Alt + Derecha", "Adelante");
            AddShortcutRow(list, "Alt + Inicio", "Inicio");
            AddShortcutRow(list, "F5 / Ctrl + R", "Actualizar");
            AddShortcutRow(list, "Ctrl + P", "Impresoras");
            AddShortcutRow(list, "Ctrl + ,", "Configuracion");
            AddShortcutRow(list, "Ctrl + /", "Ver atajos");
            AddShortcutRow(list, "Ctrl + Q", "Salir");

            var closeButton = new Button
            {
                Width = 120,
                HorizontalAlignment = HorizontalAlignment.Right,
                Style = (Style)FindResource("SecondaryButtonStyle"),
                Margin = new Thickness(18, 2, 18, 18),
                Content = new TextBlock { Text = "Cerrar", Foreground = Brushes.White }
            };

            var layout = new DockPanel { Background = Brushes.White };
            DockPanel.SetDock(header, Dock.Top);
            DockPanel.SetDock(closeButton, Dock.Bottom);
            layout.Children.Add(header);
            layout.Children.Add(closeButton);
            layout.Children.Add(list);

            var window = new Window
            {
                Title = "Atajos de teclado",
                Content = layout,
                Width = 480,
                Height = 540,
                MinWidth = 420,
                MinHeight = 420,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Topmost = false,
                ShowInTaskbar = false,
                Icon = Icon
            };

            closeButton.Click += (_, __) => window.Close();
            window.Loaded += (_, __) =>
            {
                window.Activate();
                window.Focus();
            };
            window.ShowDialog();
        }

        private void ShowGesturesDialog()
        {
            if (!_settingsService.IsTouchGesturesEnabled())
                return;

            var content = new StackPanel { Margin = new Thickness(18) };
            content.Children.Add(new TextBlock
            {
                Text = "Gestos tactiles",
                FontSize = 24,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White
            });
            content.Children.Add(new TextBlock
            {
                Text = "Acciones tactiles disponibles en la terminal.",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(217, 255, 255)),
                Margin = new Thickness(0, 4, 0, 0)
            });

            var header = new Border
            {
                Background = (Brush)FindResource("ToolbarGradientBrush"),
                CornerRadius = new CornerRadius(10, 10, 0, 0),
                Child = content
            };

            var list = new StackPanel { Margin = new Thickness(18, 14, 18, 10) };
            const double gestureLabelWidth = 280;
            AddShortcutRow(list, "Deslizar derecha", "Atras", gestureLabelWidth);
            AddShortcutRow(list, "Deslizar izquierda", "Adelante", gestureLabelWidth);
            AddShortcutRow(list, "Dos dedos vertical", "Scroll", gestureLabelWidth);
            AddShortcutRow(list, "Toque con dos dedos", "Ver gestos", gestureLabelWidth);
            AddShortcutRow(list, "Desde borde superior hacia abajo", "Actualizar", gestureLabelWidth);
            AddShortcutRow(list, "Desde borde inferior hacia arriba", "Inicio", gestureLabelWidth);

            var closeButton = new Button
            {
                Width = 120,
                HorizontalAlignment = HorizontalAlignment.Right,
                Style = (Style)FindResource("SecondaryButtonStyle"),
                Margin = new Thickness(18, 2, 18, 18),
                Content = new TextBlock { Text = "Cerrar", Foreground = Brushes.White }
            };

            var layout = new DockPanel { Background = Brushes.White };
            DockPanel.SetDock(header, Dock.Top);
            DockPanel.SetDock(closeButton, Dock.Bottom);
            layout.Children.Add(header);
            layout.Children.Add(closeButton);
            layout.Children.Add(list);

            var window = new Window
            {
                Title = "Gestos tactiles",
                Content = layout,
                Width = 640,
                Height = 540,
                MinWidth = 560,
                MinHeight = 420,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Topmost = false,
                ShowInTaskbar = false,
                Icon = Icon
            };

            closeButton.Click += (_, __) => window.Close();
            window.Loaded += (_, __) =>
            {
                window.Activate();
                window.Focus();
            };
            window.ShowDialog();
        }

        private void AddShortcutRow(Panel parent, string keys, string action, double keyColumnWidth = 160)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(keyColumnWidth) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var keyBox = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 7, 10, 7),
                Child = new TextBlock
                {
                    Text = keys,
                    Foreground = new SolidColorBrush(Color.FromRgb(64, 43, 37)),
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                }
            };

            var actionText = new TextBlock
            {
                Text = action,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(31, 41, 51)),
                Margin = new Thickness(12, 0, 0, 0)
            };

            Grid.SetColumn(keyBox, 0);
            Grid.SetColumn(actionText, 1);
            grid.Children.Add(keyBox);
            grid.Children.Add(actionText);
            parent.Children.Add(grid);
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            BringMainWindowToFront();
            var result = MessageBox.Show(
                this,
                "Confirma si deseas salir de la terminal POS.",
                "Cerrar VictumPOS",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);

            if (result == MessageBoxResult.Yes)
                Application.Current.Shutdown();
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            var alt = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
            var action = "";

            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.F5) action = "reload";
            else if (key == Key.Left && alt) action = "back";
            else if (key == Key.Right && alt) action = "forward";
            else if (key == Key.Home && alt) action = "home";
            else if (key == Key.R && ctrl) action = "reload";
            else if (key == Key.P && ctrl) action = "printers";
            else if (key == Key.OemComma && ctrl) action = "config";
            else if (key == Key.OemQuestion && ctrl) action = "shortcuts";
            else if (key == Key.Q && ctrl) action = "exit";

            if (string.IsNullOrWhiteSpace(action))
                return;

            e.Handled = true;
            HandleShortcut(action);
        }

        private void Browser_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!_settingsService.IsWebZoomLocked())
                return;

            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                e.Handled = true;
        }

        private void Browser_PreviewTouchDown(object sender, TouchEventArgs e)
        {
            if (!_settingsService.IsTouchGesturesEnabled())
                return;

            _activeTouchPoints[e.TouchDevice.Id] = e.GetTouchPoint(webView).Position;

            if (_activeTouchPoints.Count == 1)
            {
                _touchStartPoint = e.GetTouchPoint(this).Position;
                _touchStartTime = DateTime.UtcNow;
            }

            if (_activeTouchPoints.Count < 2)
                return;

            _lastTwoFingerAverageY = AverageTouchY();
            if (_activeTouchPoints.Count == 2)
            {
                _twoFingerStartPoint = AverageTouchPoint();
                _twoFingerStartTime = DateTime.UtcNow;
                _twoFingerMoved = false;
            }
        }

        private void Browser_PreviewTouchMove(object sender, TouchEventArgs e)
        {
            if (!_settingsService.IsTouchGesturesEnabled())
                return;

            _activeTouchPoints[e.TouchDevice.Id] = e.GetTouchPoint(webView).Position;
            if (_activeTouchPoints.Count < 2)
                return;

            var average = AverageTouchPoint();
            if (!_lastTwoFingerAverageY.HasValue)
            {
                _lastTwoFingerAverageY = average.Y;
                return;
            }

            var deltaY = average.Y - _lastTwoFingerAverageY.Value;
            _lastTwoFingerAverageY = average.Y;
            if (Math.Abs(deltaY) < 3)
                return;

            _twoFingerMoved = true;
            e.Handled = true;
            ScrollBrowserAtPoint((int)Math.Round(average.X), (int)Math.Round(average.Y), (int)Math.Round(-deltaY * 1.35));
        }

        private void Browser_PreviewTouchUp(object sender, TouchEventArgs e)
        {
            if (!_settingsService.IsTouchGesturesEnabled())
                return;

            var wasTwoFingerGesture = _activeTouchPoints.Count >= 2;
            var isSingleFingerGesture = _activeTouchPoints.Count <= 1 && _touchStartPoint.HasValue;
            _activeTouchPoints.Remove(e.TouchDevice.Id);
            if (_activeTouchPoints.Count < 2)
            {
                _lastTwoFingerAverageY = null;
                if (wasTwoFingerGesture)
                    HandleTwoFingerTap(e);
                _twoFingerStartPoint = null;
            }

            if (!isSingleFingerGesture)
                return;

            var end = e.GetTouchPoint(this).Position;
            var start = _touchStartPoint.Value;
            _touchStartPoint = null;

            if ((DateTime.UtcNow - _touchStartTime).TotalMilliseconds > 900)
                return;

            var deltaX = end.X - start.X;
            var deltaY = end.Y - start.Y;
            if (Math.Abs(deltaX) >= 120 && Math.Abs(deltaX) >= Math.Abs(deltaY) * 1.6)
            {
                e.Handled = true;
                if (deltaX > 0 && webView.CanGoBack)
                    DispatchTouchGesture("back");
                else if (deltaX < 0 && webView.CanGoForward)
                    DispatchTouchGesture("forward");
                return;
            }

            if (Math.Abs(deltaY) >= 140 && Math.Abs(deltaY) >= Math.Abs(deltaX) * 1.4)
            {
                var startedNearTop = start.Y <= 96;
                var startedNearBottom = start.Y >= Math.Max(0, ActualHeight - 120);
                if (deltaY > 0 && startedNearTop)
                {
                    e.Handled = true;
                    DispatchTouchGesture("reload");
                }
                else if (deltaY < 0 && startedNearBottom)
                {
                    e.Handled = true;
                    DispatchTouchGesture("home");
                }
            }
        }

        private void HandleTwoFingerTap(TouchEventArgs e)
        {
            if (!_twoFingerStartPoint.HasValue || _twoFingerMoved)
                return;

            if ((DateTime.UtcNow - _twoFingerStartTime).TotalMilliseconds > 550)
                return;

            e.Handled = true;
            DispatchTouchGesture("gestures");
        }

        private void DispatchTouchGesture(string action)
        {
            action = (action ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(action))
                return;

            var now = DateTime.UtcNow;
            if (string.Equals(_lastTouchGestureAction, action, StringComparison.OrdinalIgnoreCase) &&
                (now - _lastTouchGestureTime).TotalMilliseconds < 450)
                return;

            _lastTouchGestureAction = action;
            _lastTouchGestureTime = now;

            switch (action)
            {
                case "back":
                    if (webView.CanGoBack)
                        webView.GoBack();
                    break;
                case "forward":
                    if (webView.CanGoForward)
                        webView.GoForward();
                    break;
                case "reload":
                    Reload_Click(this, new RoutedEventArgs());
                    break;
                case "home":
                    Home_Click(this, new RoutedEventArgs());
                    break;
                case "shortcuts":
                    ShowShortcutsDialog();
                    break;
                case "gestures":
                    ShowGesturesDialog();
                    break;
            }
        }

        private Point AverageTouchPoint()
        {
            double x = 0;
            double y = 0;
            foreach (var point in _activeTouchPoints.Values)
            {
                x += point.X;
                y += point.Y;
            }

            var count = Math.Max(1, _activeTouchPoints.Count);
            return new Point(x / count, y / count);
        }

        private double AverageTouchY()
        {
            return AverageTouchPoint().Y;
        }

        private void ScrollBrowserAtPoint(int x, int y, int deltaY)
        {
            if (webView.CoreWebView2 == null || deltaY == 0)
                return;

            var script = @"
(function(x, y, deltaY) {
  var el = document.elementFromPoint(x, y) || document.activeElement || document.scrollingElement || document.documentElement;
  function findScrollable(node) {
    while (node && node !== document.body && node !== document.documentElement) {
      var style = window.getComputedStyle(node);
      var canScroll = /(auto|scroll)/.test(style.overflowY || '') && node.scrollHeight > node.clientHeight;
      if (canScroll) return node;
      node = node.parentElement;
    }
    return document.scrollingElement || document.documentElement;
  }
  findScrollable(el).scrollTop += deltaY;
})(" + x + "," + y + "," + deltaY + ");";

            _ = webView.CoreWebView2.ExecuteScriptAsync(script);
        }

        private void HandleShortcut(string action)
        {
            switch ((action ?? "").ToLowerInvariant())
            {
                case "back":
                    Back_Click(this, new RoutedEventArgs());
                    break;
                case "forward":
                    if (webView.CanGoForward)
                        webView.GoForward();
                    break;
                case "home":
                    Home_Click(this, new RoutedEventArgs());
                    break;
                case "reload":
                    Reload_Click(this, new RoutedEventArgs());
                    break;
                case "printers":
                    Settings_Click(this, new RoutedEventArgs());
                    break;
                case "config":
                    Config_Click(this, new RoutedEventArgs());
                    break;
                case "shortcuts":
                    Shortcuts_Click(this, new RoutedEventArgs());
                    break;
                case "exit":
                    Exit_Click(this, new RoutedEventArgs());
                    break;
            }
        }

        private void ShowLoader(bool show)
        {
            LoadingOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowError(string message)
        {
            MessageBox.Show(this, message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void ShowNotification(string title, string message)
        {
            if (!HasVisibleOwnedWindow())
                BringMainWindowToFront();

            NotificationText.Text = (string.IsNullOrWhiteSpace(title) ? "Notificacion" : title) +
                (string.IsNullOrWhiteSpace(message) ? "" : ": " + message);
            NotificationBar.Visibility = Visibility.Visible;
            _notificationTimer.Stop();
            _notificationTimer.Start();
            SystemNotificationService.Show(title, message);
        }

        private bool HasVisibleOwnedWindow()
        {
            foreach (Window window in OwnedWindows)
                if (window.IsVisible)
                    return true;

            return false;
        }

        private void BringMainWindowToFront()
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                    if (hwnd == IntPtr.Zero)
                        return;

                    if (IsIconic(hwnd))
                        ShowWindow(hwnd, SW_RESTORE);

                    Activate();
                    SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                    SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                    SetForegroundWindow(hwnd);
                }
                catch (Exception ex)
                {
                    Logger.Log("No se pudo traer la ventana al frente: " + ex.Message);
                }
            });
        }
    }
}
