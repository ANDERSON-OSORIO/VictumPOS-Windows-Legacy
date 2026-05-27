using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace VictumPOS.PrintBridge.Service.Services
{
    internal sealed class EscPosBridgePrinter
    {
        private readonly BridgeSettingsService _settings;
        private byte[] _cachedLogo;
        private string _cachedLogoPath = "";

        public EscPosBridgePrinter(BridgeSettingsService settings)
        {
            _settings = settings;
        }

        public async Task PrintAsync(string ip, int port, string content, int timeoutMs)
        {
            using (var client = new TcpClient())
            {
                var connectTask = client.ConnectAsync(ip, port);
                if (await Task.WhenAny(connectTask, Task.Delay(Math.Max(2000, timeoutMs))) != connectTask)
                    throw new TimeoutException("No se pudo conectar a " + ip + ":" + port);

                await connectTask;
                using (var stream = client.GetStream())
                {
                    var bytes = IsRasterImage(content)
                        ? BuildRasterImageTicket(content)
                        : (IsEscPosContent(content) ? BuildRawEscPosTicket(content) : BuildTicket(content));

                    await stream.WriteAsync(bytes, 0, bytes.Length);
                    await stream.FlushAsync();
                    BridgeLogger.Log("ESC/POS enviado a " + ip + ":" + port + ". Bytes=" + bytes.Length);
                }
            }
        }

        private byte[] BuildRasterImageTicket(string imageBase64)
        {
            using (var ms = new MemoryStream())
            {
                Write(ms, new byte[] { 0x1B, 0x40 });
                Write(ms, AlignCenter());
                Write(ms, ConvertRasterImageToEscPos(imageBase64));
                Write(ms, FeedLines(3));
                if (_settings.IsAutoCutEnabled())
                    Write(ms, CutPaper());
                return ms.ToArray();
            }
        }

        private byte[] BuildRawEscPosTicket(string content)
        {
            var encoding = GetPrinterEncoding();
            using (var ms = new MemoryStream())
            {
                Write(ms, new byte[] { 0x1B, 0x40 });
                var logoBytes = LoadLogoBytes();
                if (logoBytes != null && logoBytes.Length > 0)
                {
                    Write(ms, AlignCenter());
                    Write(ms, logoBytes);
                    Write(ms, FeedLines(2));
                }

                var body = encoding.GetBytes(EscPosTextNormalizer.Normalize(content));
                Write(ms, body);
                if (_settings.IsAutoCutEnabled() && !ContainsCutCommand(body))
                {
                    Write(ms, FeedLines(2));
                    Write(ms, CutPaper());
                }

                return ms.ToArray();
            }
        }

        private byte[] BuildTicket(string content)
        {
            var encoding = GetPrinterEncoding();
            using (var ms = new MemoryStream())
            {
                Write(ms, new byte[] { 0x1B, 0x40 });
                var logoBytes = LoadLogoBytes();
                if (logoBytes != null && logoBytes.Length > 0)
                {
                    Write(ms, AlignCenter());
                    Write(ms, logoBytes);
                    Write(ms, FeedLines(2));
                }

                Write(ms, encoding.GetBytes("================================================\n"));
                Write(ms, AlignLeft());
                Write(ms, encoding.GetBytes(EscPosTextNormalizer.Normalize(content) + "\n"));
                Write(ms, encoding.GetBytes("================================================\n"));
                Write(ms, AlignCenter());
                Write(ms, encoding.GetBytes(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n\n"));
                if (_settings.IsAutoCutEnabled())
                    Write(ms, CutPaper());
                return ms.ToArray();
            }
        }

        private byte[] LoadLogoBytes()
        {
            try
            {
                if (!_settings.IsPrintLogoEnabled())
                    return null;

                var path = ResolveLogoPath();
                if (string.IsNullOrWhiteSpace(path))
                    return null;

                if (_cachedLogo != null && string.Equals(_cachedLogoPath, path, StringComparison.OrdinalIgnoreCase))
                    return _cachedLogo;

                var base64 = Convert.ToBase64String(File.ReadAllBytes(path));
                _cachedLogo = ConvertLogoToEscPos(base64);
                _cachedLogoPath = path;
                return _cachedLogo;
            }
            catch (Exception ex)
            {
                BridgeLogger.Log("Error cargando logo ESC/POS: " + ex.Message);
                return null;
            }
        }

        private string ResolveLogoPath()
        {
            var configured = _settings.GetPrintLogoPath();
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
                return configured;

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "Assets", "logo.png"),
                Path.Combine(baseDir, "..", "Assets", "logo.png"),
                Path.Combine(baseDir, "..", "VictumPOS", "Assets", "logo.png")
            };

            foreach (var candidate in candidates)
            {
                var fullPath = Path.GetFullPath(candidate);
                if (File.Exists(fullPath))
                    return fullPath;
            }

            BridgeLogger.Log("Logo de impresion no encontrado.");
            return "";
        }

        private byte[] ConvertRasterImageToEscPos(string base64)
        {
            var bytes = ConvertImageToEscPos(base64, _settings.GetPrintWidth(), false);
            if (bytes == null)
                throw new InvalidOperationException("Factura rasterizada invalida");

            return bytes;
        }

        private byte[] ConvertLogoToEscPos(string base64)
        {
            return ConvertImageToEscPos(base64, Math.Min(_settings.GetPrintWidth(), 240), true);
        }

        private byte[] ConvertImageToEscPos(string base64, int targetWidth, bool skipDenseImage)
        {
            try
            {
                var commaIndex = base64.IndexOf(',');
                var cleanBase64 = commaIndex >= 0 ? base64.Substring(commaIndex + 1) : base64;
                var imageBytes = Convert.FromBase64String(cleanBase64);

                using (var sourceStream = new MemoryStream(imageBytes))
                using (var original = new Bitmap(sourceStream))
                {
                    var width = Math.Max(1, targetWidth);
                    var height = Math.Max(1, (int)((double)original.Height / original.Width * width));

                    using (var normalized = new Bitmap(width, height, PixelFormat.Format32bppArgb))
                    {
                        using (var g = Graphics.FromImage(normalized))
                        {
                            g.Clear(Color.White);
                            g.CompositingMode = CompositingMode.SourceOver;
                            g.CompositingQuality = CompositingQuality.HighQuality;
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            g.SmoothingMode = SmoothingMode.HighQuality;
                            g.DrawImage(original, new Rectangle(0, 0, width, height));
                        }

                        using (var ms = new MemoryStream())
                        {
                            var bytesPerLine = (width + 7) / 8;
                            var blackPixels = 0;
                            var totalPixels = width * height;

                            Write(ms, new byte[] { 0x1D, 0x76, 0x30, 0x00 });
                            ms.WriteByte((byte)(bytesPerLine % 256));
                            ms.WriteByte((byte)(bytesPerLine / 256));
                            ms.WriteByte((byte)(height % 256));
                            ms.WriteByte((byte)(height / 256));

                            for (var y = 0; y < height; y++)
                            {
                                for (var xByte = 0; xByte < bytesPerLine; xByte++)
                                {
                                    byte value = 0;
                                    for (var bit = 0; bit < 8; bit++)
                                    {
                                        var x = xByte * 8 + bit;
                                        if (x >= width)
                                            continue;

                                        var color = normalized.GetPixel(x, y);
                                        var gray = (int)(0.3 * color.R + 0.59 * color.G + 0.11 * color.B);
                                        if (gray < 160)
                                        {
                                            value |= (byte)(1 << (7 - bit));
                                            blackPixels++;
                                        }
                                    }

                                    ms.WriteByte(value);
                                }
                            }

                            if (skipDenseImage && totalPixels > 0 && blackPixels / (double)totalPixels > 0.85)
                            {
                                BridgeLogger.Log("Logo ESC/POS omitido porque la conversion quedaria como bloque negro.");
                                return null;
                            }

                            return ms.ToArray();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                BridgeLogger.Log("Error convirtiendo imagen ESC/POS: " + ex.Message);
                return null;
            }
        }

        private static bool IsRasterImage(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return false;

            var value = content.Trim();
            if (value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                return true;

            if (value.Length < 128 || value.Contains("\n") || value.Contains("\r"))
                return false;

            try
            {
                var imageBytes = Convert.FromBase64String(value);
                using (var ms = new MemoryStream(imageBytes))
                using (new Bitmap(ms))
                    return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsEscPosContent(string content)
        {
            return !string.IsNullOrEmpty(content) && (content.IndexOf('\x1B') >= 0 || content.IndexOf('\x1D') >= 0);
        }

        private static bool ContainsCutCommand(IReadOnlyList<byte> bytes)
        {
            for (var i = 0; i < bytes.Count - 1; i++)
                if (bytes[i] == 0x1D && bytes[i + 1] == 0x56)
                    return true;

            return false;
        }

        private static Encoding GetPrinterEncoding()
        {
            try
            {
                return Encoding.GetEncoding(858);
            }
            catch
            {
                return Encoding.GetEncoding(850);
            }
        }

        private static void Write(Stream stream, byte[] bytes)
        {
            stream.Write(bytes, 0, bytes.Length);
        }

        private static byte[] AlignLeft() { return new byte[] { 0x1B, 0x61, 0x00 }; }
        private static byte[] AlignCenter() { return new byte[] { 0x1B, 0x61, 0x01 }; }
        private static byte[] FeedLines(int n) { return new byte[] { 0x1B, 0x64, (byte)n }; }
        private static byte[] CutPaper() { return new byte[] { 0x1D, 0x56, 0x41, 0x10 }; }
    }
}
