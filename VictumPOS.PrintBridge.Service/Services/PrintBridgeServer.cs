using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Script.Serialization;

namespace VictumPOS.PrintBridge.Service.Services
{
    internal sealed class PrintBridgeServer : IDisposable
    {
        private readonly BridgeOptions _options;
        private readonly BridgePrintService _printService;
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();
        private TcpListener _listener;
        private CancellationTokenSource _cts;

        public PrintBridgeServer(BridgeOptions options)
        {
            _options = options;
            _printService = new BridgePrintService(options.SettingsPath);
        }

        public void Start()
        {
            if (_listener != null)
                return;

            _cts = new CancellationTokenSource();
            _listener = new TcpListener(_options.BindAddress, _options.Port);
            _listener.Start();
            Task.Run(() => AcceptLoop(_cts.Token));
            BridgeLogger.Log("Escuchando en " + _options.BindAddress + ":" + _options.Port + ". Settings=" + _options.SettingsPath);
        }

        public void Dispose()
        {
            try
            {
                if (_cts != null)
                    _cts.Cancel();
                if (_listener != null)
                    _listener.Stop();
            }
            catch
            {
            }
            finally
            {
                _listener = null;
                if (_cts != null)
                {
                    _cts.Dispose();
                    _cts = null;
                }
            }
        }

        private async Task AcceptLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _listener != null)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync();
                    var _ = Task.Run(() => HandleClient(client, token), token);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                        BridgeLogger.Log("Accept error: " + ex.Message);
                }
            }
        }

        private async Task HandleClient(TcpClient client, CancellationToken token)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 15000;
                    client.SendTimeout = 15000;
                    var stream = client.GetStream();

                    var headerBytes = await ReadHeaders(stream, token);
                    var headerText = Encoding.ASCII.GetString(headerBytes);
                    var headerReader = new StringReader(headerText);

                    var requestLine = headerReader.ReadLine() ?? "";
                    var parts = requestLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2)
                    {
                        await WriteJson(stream, 400, new BridgeResponse(false, "Solicitud invalida"));
                        return;
                    }

                    var method = parts[0].ToUpperInvariant();
                    var path = parts[1].Split('?')[0];
                    var contentLength = 0;

                    string line;
                    while (!string.IsNullOrEmpty(line = headerReader.ReadLine()))
                    {
                        var separator = line.IndexOf(':');
                        if (separator <= 0)
                            continue;

                        var key = line.Substring(0, separator).Trim();
                        var value = line.Substring(separator + 1).Trim();
                        if (key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                            int.TryParse(value, out contentLength);
                    }

                    if (method == "GET" &&
                        (path.Equals("/", StringComparison.OrdinalIgnoreCase) ||
                         path.Equals("/health", StringComparison.OrdinalIgnoreCase)))
                    {
                        await WriteJson(stream, 200, new BridgeResponse(true, "VictumPOS Print Bridge activo"));
                        return;
                    }

                    if (method == "OPTIONS")
                    {
                        await WriteOptions(stream);
                        return;
                    }

                    if (method != "POST" || !path.Equals("/print", StringComparison.OrdinalIgnoreCase))
                    {
                        await WriteJson(stream, 404, new BridgeResponse(false, "Ruta no disponible"));
                        return;
                    }

                    if (contentLength <= 0)
                    {
                        await WriteJson(stream, 400, new BridgeResponse(false, "Contenido vacio"));
                        return;
                    }

                    var body = await ReadBody(stream, contentLength, token);
                    var json = Encoding.UTF8.GetString(body);
                    var root = _serializer.DeserializeObject(json) as Dictionary<string, object>;
                    if (root == null)
                    {
                        await WriteJson(stream, 400, new BridgeResponse(false, "JSON invalido"));
                        return;
                    }

                    var payload = GetPayload(root);
                    var content = GetString(payload, "image", "imageBase64", "imageData", "raster", "rasterImage", "receiptImage", "ticketImage", "bitmap", "base64", "dataUrl", "dataURL", "src");
                    if (string.IsNullOrWhiteSpace(content))
                        content = GetString(payload, "content", "text", "body", "html", "ticket", "raw");

                    var selector = GetString(payload, "printer", "printerSelector", "printerType", "printerName", "printer_name", "destination", "target", "station", "device");
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        await WriteJson(stream, 400, new BridgeResponse(false, "Contenido de impresion vacio"));
                        return;
                    }

                    selector = string.IsNullOrWhiteSpace(selector) ? _printService.DefaultPrinterSelector() : selector.Trim();
                    await _printService.Print(NormalizeContent(content), selector);
                    await WriteJson(stream, 200, new BridgeResponse(true, "Impresion enviada por Windows Service"));
                }
                catch (Exception ex)
                {
                    BridgeLogger.Log("Request error: " + ex);
                    try
                    {
                        await WriteJson(client.GetStream(), 500, new BridgeResponse(false, ex.Message));
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static async Task<byte[]> ReadHeaders(NetworkStream stream, CancellationToken token)
        {
            using (var ms = new MemoryStream())
            {
                var buffer = new byte[1];
                var tail = new Queue<byte>(4);

                while (ms.Length < 32 * 1024)
                {
                    token.ThrowIfCancellationRequested();
                    var read = await stream.ReadAsync(buffer, 0, 1);
                    if (read <= 0)
                        break;

                    var value = buffer[0];
                    ms.WriteByte(value);
                    tail.Enqueue(value);
                    if (tail.Count > 4)
                        tail.Dequeue();

                    if (tail.Count == 4 && tail.SequenceEqual(new byte[] { 13, 10, 13, 10 }))
                        return ms.ToArray();
                }
            }

            throw new InvalidOperationException("Headers HTTP invalidos");
        }

        private static async Task<byte[]> ReadBody(NetworkStream stream, int contentLength, CancellationToken token)
        {
            var body = new byte[contentLength];
            var read = 0;
            while (read < contentLength)
            {
                token.ThrowIfCancellationRequested();
                var count = await stream.ReadAsync(body, read, contentLength - read);
                if (count <= 0)
                    break;

                read += count;
            }

            if (read != contentLength)
                throw new InvalidOperationException("Body HTTP incompleto");

            return body;
        }

        private static Dictionary<string, object> GetPayload(Dictionary<string, object> root)
        {
            foreach (var name in new[] { "payload", "data", "detail" })
            {
                object payload;
                if (root.TryGetValue(name, out payload) && payload is Dictionary<string, object>)
                    return (Dictionary<string, object>)payload;
            }

            return root;
        }

        private static string GetString(Dictionary<string, object> root, params string[] names)
        {
            foreach (var name in names)
            {
                object value;
                if (root.TryGetValue(name, out value) && value != null)
                    return Convert.ToString(value) ?? "";
            }

            return "";
        }

        private static string NormalizeContent(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return "";

            var text = content.Trim();
            if (text.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                return text;

            if (text.IndexOf('\x1B') >= 0 || text.IndexOf('\x1D') >= 0)
                return text.Replace("\r\n", "\n").Replace('\r', '\n');

            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            if (text.Contains("<") && text.Contains(">"))
            {
                text = Regex.Replace(text, @"<\s*br\s*/?\s*>", "\n", RegexOptions.IgnoreCase);
                text = Regex.Replace(text, @"</\s*(p|div|tr|li|h[1-6])\s*>", "\n", RegexOptions.IgnoreCase);
                text = Regex.Replace(text, @"<[^>]+>", "");
                text = HttpUtility.HtmlDecode(text);
            }

            return text.Trim();
        }

        private async Task WriteJson(NetworkStream stream, int statusCode, BridgeResponse response)
        {
            var body = _serializer.Serialize(response);
            var statusText = statusCode == 200 ? "OK" : "ERROR";
            var bytes = Encoding.UTF8.GetBytes(body);
            var header = "HTTP/1.1 " + statusCode + " " + statusText + "\r\n"
                         + "Content-Type: application/json; charset=utf-8\r\n"
                         + "Access-Control-Allow-Origin: *\r\n"
                         + "Access-Control-Allow-Headers: content-type\r\n"
                         + "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n"
                         + "Connection: close\r\n"
                         + "Content-Length: " + bytes.Length + "\r\n\r\n";

            var headerBytes = Encoding.ASCII.GetBytes(header);
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length);
            await stream.WriteAsync(bytes, 0, bytes.Length);
            await stream.FlushAsync();
        }

        private static async Task WriteOptions(NetworkStream stream)
        {
            var header = "HTTP/1.1 204 OK\r\n"
                         + "Access-Control-Allow-Origin: *\r\n"
                         + "Access-Control-Allow-Headers: content-type\r\n"
                         + "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n"
                         + "Connection: close\r\n"
                         + "Content-Length: 0\r\n\r\n";

            var headerBytes = Encoding.ASCII.GetBytes(header);
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length);
            await stream.FlushAsync();
        }
    }

    internal sealed class BridgeResponse
    {
        public BridgeResponse(bool ok, string message)
        {
            Ok = ok;
            Message = message;
        }

        public bool Ok { get; set; }
        public string Message { get; set; }
    }
}
