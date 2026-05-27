using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using VictumPOS.Services;

namespace VictumPOS.Helpers
{
    public static class WebViewBridge
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        public static async Task HandleMessage(string json, PrintService printService)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(json))
                    return;

                var root = Serializer.DeserializeObject(json) as Dictionary<string, object>;
                if (root == null)
                    return;

                var type = GetString(root, "type", "action", "event");
                if (string.IsNullOrWhiteSpace(type))
                    return;

                switch (type.Trim().ToLowerInvariant())
                {
                    case "print":
                    case "printticket":
                    case "ticket.print":
                        await HandlePrint(root, printService);
                        break;
                    case "ping":
                        Logger.Log("Ping recibido desde web");
                        break;
                    case "bridgeerror":
                        Logger.Log("Error bridge JS: " + GetString(root, "message", "error"));
                        break;
                    case "openCashDrawer":
                        Logger.Log("Comando abrir cajon recibido");
                        break;
                    default:
                        Logger.Log("Evento no reconocido: " + type);
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Error procesando mensaje: " + ex.Message);
            }
        }

        private static async Task HandlePrint(Dictionary<string, object> root, PrintService printService)
        {
            var payload = GetPayload(root);
            var content = GetString(payload, "image", "imageBase64", "imageData", "raster", "rasterImage", "receiptImage", "ticketImage", "bitmap", "base64", "dataUrl", "dataURL", "src");
            if (string.IsNullOrWhiteSpace(content))
                content = GetString(root, "image", "imageBase64", "imageData", "raster", "rasterImage", "receiptImage", "ticketImage", "bitmap", "base64", "dataUrl", "dataURL", "src");

            if (string.IsNullOrWhiteSpace(content))
                content = GetString(payload, "content", "text", "body", "html", "ticket", "raw");
            if (string.IsNullOrWhiteSpace(content))
                content = GetString(root, "content", "text", "body", "html", "ticket", "raw");

            var printer = GetString(payload, "printer", "printerSelector", "printerType", "printerName", "printer_name", "destination", "target", "station", "device");
            if (string.IsNullOrWhiteSpace(printer))
                printer = GetString(root, "printer", "printerSelector", "printerType", "printerName", "printer_name", "destination", "target", "station", "device");
            if (string.IsNullOrWhiteSpace(printer))
                printer = printService.DefaultPrinterSelector();

            content = NormalizeContent(content);
            if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(printer))
            {
                Logger.Log("Print invalido. contentLen=" + (content == null ? 0 : content.Length) + ", printer='" + printer + "'");
                return;
            }

            Logger.Log("Print recibido desde bridge. printer='" + printer + "', contentLen=" + content.Length + ", raster=" + IsRasterContent(content) + ", escpos=" + IsEscPosContent(content));
            await printService.Print(content, printer);
            Logger.Log("Print enviado -> " + printer);
        }

        public static bool TryReadType(string json, out Dictionary<string, object> root, out string type)
        {
            root = null;
            type = "";

            try
            {
                root = Serializer.DeserializeObject(json) as Dictionary<string, object>;
                if (root == null)
                    return false;

                type = GetString(root, "type");
                return !string.IsNullOrWhiteSpace(type);
            }
            catch
            {
                return false;
            }
        }

        public static string GetString(Dictionary<string, object> root, params string[] names)
        {
            if (root == null)
                return "";

            foreach (var name in names)
            {
                object value;
                if (root.TryGetValue(name, out value) && value != null)
                    return Convert.ToString(value) ?? "";
            }

            return "";
        }

        public static Dictionary<string, object> GetPayload(Dictionary<string, object> root)
        {
            if (root == null)
                return new Dictionary<string, object>();

            foreach (var name in new[] { "payload", "data", "detail" })
            {
                object value;
                if (root.TryGetValue(name, out value) && value is Dictionary<string, object>)
                    return (Dictionary<string, object>)value;
            }

            return root;
        }

        private static string NormalizeContent(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return "";

            if (IsEscPosContent(content))
                return content.Replace("\r\n", "\n").Replace('\r', '\n');

            var text = content.Trim();
            if (IsRasterContent(text))
                return text;

            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            if (text.Contains("<") && text.Contains(">"))
            {
                text = Regex.Replace(text, @"<\s*br\s*/?\s*>", "\n", RegexOptions.IgnoreCase);
                text = Regex.Replace(text, @"</\s*(p|div|tr|li|h[1-6])\s*>", "\n", RegexOptions.IgnoreCase);
                text = Regex.Replace(text, @"<[^>]+>", "");
                text = WebUtility.HtmlDecode(text);
            }

            text = text.Replace('\u00A0', ' ');
            text = Regex.Replace(text, @"[ \t]+\n", "\n");
            text = Regex.Replace(text, @"\n{3,}", "\n\n");

            return text.Trim();
        }

        private static bool IsEscPosContent(string content)
        {
            return !string.IsNullOrEmpty(content) && (content.IndexOf('\x1B') >= 0 || content.IndexOf('\x1D') >= 0);
        }

        private static bool IsRasterContent(string content)
        {
            return !string.IsNullOrWhiteSpace(content) && content.TrimStart().StartsWith("data:image/", StringComparison.OrdinalIgnoreCase);
        }
    }
}
