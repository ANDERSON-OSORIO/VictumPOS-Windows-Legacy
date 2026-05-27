using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace VictumPOS.Services
{
    public class EscPosService
    {
        private static byte[] _cachedLogo;
        private static string _cachedLogoKey = "";
        private readonly SettingsService _settings = new SettingsService();

        public Task PrintAsync(string ip, int port, string content, string logoBase64 = null)
        {
            return PrintAsync(ip, port, content, 5000, logoBase64);
        }

        public async Task PrintAsync(string ip, int port, string content, int timeoutMs, string logoBase64 = null)
        {
            if (string.IsNullOrWhiteSpace(content))
                throw new Exception("Contenido de impresion vacio");

            Logger.Log("Intentando imprimir en " + ip + ":" + port);
            using (var client = new TcpClient())
            {
                var connectTask = client.ConnectAsync(ip, port);
                var timeoutTask = Task.Delay(Math.Max(2000, timeoutMs));
                if (await Task.WhenAny(connectTask, timeoutTask) != connectTask)
                    throw new TimeoutException("No se pudo conectar a " + ip + ":" + port);

                await connectTask;
                Logger.Log("Conectado a impresora");
                using (var stream = client.GetStream())
                {
                    var isRaster = IsRasterImage(content);
                    var isEscPos = IsEscPosContent(content);
                    Logger.Log("Raster detected: " + isRaster + ". ESC/POS detected: " + isEscPos + ". Content length: " + content.Length);

                    var bytes = isRaster
                        ? BuildRasterImageTicket(content)
                        : (isEscPos ? BuildRawEscPosTicket(content, logoBase64) : BuildTicket(content, logoBase64));

                    await stream.WriteAsync(bytes, 0, bytes.Length);
                    await stream.FlushAsync();
                    Logger.Log("Impresion enviada correctamente. Bytes: " + bytes.Length);
                }
            }
        }

        private byte[] BuildRasterImageTicket(string imageBase64)
        {
            var bytes = new List<byte>();
            bytes.AddRange(new byte[] { 0x1B, 0x40 });
            bytes.AddRange(AlignCenter());

            var imageBytes = ConvertImageToEscPos(imageBase64, _settings.GetPrintWidth(), false);
            if (imageBytes == null || imageBytes.Length == 0)
                throw new Exception("Factura rasterizada invalida");

            bytes.AddRange(imageBytes);
            bytes.AddRange(FeedLines(3));
            if (_settings.IsAutoCutEnabled())
                bytes.AddRange(CutPaper());

            return bytes.ToArray();
        }

        private byte[] BuildRawEscPosTicket(string content, string logoBase64)
        {
            var encoding = Encoding.GetEncoding(858);
            var bytes = new List<byte>();
            bytes.AddRange(new byte[] { 0x1B, 0x40 });

            var logoBytes = LoadLogoBytes(logoBase64);
            if (logoBytes != null)
            {
                bytes.AddRange(AlignCenter());
                bytes.AddRange(logoBytes);
                bytes.AddRange(FeedLines(2));
            }

            bytes.AddRange(encoding.GetBytes(content));

            if (_settings.IsAutoCutEnabled() && !ContainsCutCommand(bytes))
            {
                bytes.AddRange(FeedLines(2));
                bytes.AddRange(CutPaper());
            }

            return bytes.ToArray();
        }

        private byte[] BuildTicket(string content, string logoBase64)
        {
            var bytes = new List<byte>();
            var encoding = Encoding.GetEncoding(858);

            bytes.AddRange(new byte[] { 0x1B, 0x40 });

            var logoBytes = LoadLogoBytes(logoBase64);
            if (logoBytes != null)
            {
                bytes.AddRange(AlignCenter());
                bytes.AddRange(logoBytes);
                bytes.AddRange(FeedLines(2));
            }

            bytes.AddRange(encoding.GetBytes("================================================\n"));
            bytes.AddRange(AlignLeft());
            bytes.AddRange(encoding.GetBytes(content + "\n"));
            bytes.AddRange(encoding.GetBytes("================================================\n"));
            bytes.AddRange(AlignCenter());
            bytes.AddRange(encoding.GetBytes(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n\n"));

            if (_settings.IsAutoCutEnabled())
                bytes.AddRange(CutPaper());

            return bytes.ToArray();
        }

        private byte[] LoadLogoBytes(string logoBase64)
        {
            try
            {
                if (!_settings.IsPrintLogoEnabled())
                    return null;

                if (!string.IsNullOrWhiteSpace(logoBase64))
                {
                    if (_cachedLogo == null || _cachedLogoKey != logoBase64)
                    {
                        _cachedLogo = ConvertImageToEscPos(logoBase64, Math.Min(_settings.GetPrintWidth(), 240), true);
                        _cachedLogoKey = logoBase64;
                    }

                    return _cachedLogo;
                }

                var path = ResolveLogoPath();
                if (string.IsNullOrWhiteSpace(path))
                    return null;

                if (_cachedLogo == null || !string.Equals(_cachedLogoKey, path, StringComparison.OrdinalIgnoreCase))
                {
                    var base64 = Convert.ToBase64String(File.ReadAllBytes(path));
                    _cachedLogo = ConvertImageToEscPos(base64, Math.Min(_settings.GetPrintWidth(), 240), true);
                    _cachedLogoKey = path;
                }

                return _cachedLogo;
            }
            catch (Exception ex)
            {
                Logger.Log("Error cargando logo: " + ex.Message);
                return null;
            }
        }

        private string ResolveLogoPath()
        {
            var configured = _settings.GetPrintLogoPath();
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
                return configured;

            var defaultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "logo.png");
            return File.Exists(defaultPath) ? defaultPath : "";
        }

        private byte[] ConvertImageToEscPos(string base64, int targetWidth, bool skipDenseImage)
        {
            try
            {
                var cleanBase64 = base64.Contains(",") ? base64.Split(',')[1] : base64;
                var imageBytes = Convert.FromBase64String(cleanBase64);

                using (var ms = new MemoryStream(imageBytes))
                using (var original = new Bitmap(ms))
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

                        if (skipDenseImage && IsMostlyDark(normalized))
                            RemoveDarkLogoBackground(normalized);

                        var bytes = new List<byte>();
                        var bytesPerLine = (width + 7) / 8;
                        var blackPixels = 0;
                        var totalPixels = width * height;

                        bytes.AddRange(new byte[] { 0x1D, 0x76, 0x30, 0x00 });
                        bytes.Add((byte)(bytesPerLine % 256));
                        bytes.Add((byte)(bytesPerLine / 256));
                        bytes.Add((byte)(height % 256));
                        bytes.Add((byte)(height / 256));

                        for (var y = 0; y < height; y++)
                        {
                            for (var x = 0; x < bytesPerLine; x++)
                            {
                                byte b = 0;
                                for (var bit = 0; bit < 8; bit++)
                                {
                                    var px = x * 8 + bit;
                                    if (px < width)
                                    {
                                        var color = normalized.GetPixel(px, y);
                                        var gray = (int)(0.3 * color.R + 0.59 * color.G + 0.11 * color.B);
                                        if (gray < 160)
                                        {
                                            b |= (byte)(1 << (7 - bit));
                                            blackPixels++;
                                        }
                                    }
                                }

                                bytes.Add(b);
                            }
                        }

                        Logger.Log("Raster ESC/POS bytes generated: " + bytes.Count);
                        if (skipDenseImage && totalPixels > 0 && blackPixels / (double)totalPixels > 0.85)
                        {
                            Logger.Log("Logo omitido porque la conversion quedaria como bloque negro.");
                            return null;
                        }

                        return bytes.ToArray();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Error convirtiendo imagen: " + ex.Message);
                return null;
            }
        }

        private static bool IsMostlyDark(Bitmap image)
        {
            var darkPixels = 0;
            var totalPixels = image.Width * image.Height;
            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < image.Width; x++)
                {
                    if (IsDarkNeutral(image.GetPixel(x, y)))
                        darkPixels++;
                }
            }

            return totalPixels > 0 && darkPixels / (double)totalPixels > 0.60;
        }

        private static void RemoveDarkLogoBackground(Bitmap image)
        {
            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < image.Width; x++)
                {
                    var color = image.GetPixel(x, y);
                    image.SetPixel(x, y, IsDarkNeutral(color) ? Color.White : Color.Black);
                }
            }
        }

        private static bool IsDarkNeutral(Color color)
        {
            var max = Math.Max(color.R, Math.Max(color.G, color.B));
            var min = Math.Min(color.R, Math.Min(color.G, color.B));
            return color.A < 24 || (max < 55 && max - min < 18);
        }

        private bool IsRasterImage(string content)
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

        private bool IsEscPosContent(string content)
        {
            return !string.IsNullOrEmpty(content) && (content.IndexOf('\x1B') >= 0 || content.IndexOf('\x1D') >= 0);
        }

        private bool ContainsCutCommand(IReadOnlyList<byte> bytes)
        {
            for (var i = 0; i < bytes.Count - 1; i++)
                if (bytes[i] == 0x1D && bytes[i + 1] == 0x56)
                    return true;

            return false;
        }

        private byte[] AlignLeft() { return new byte[] { 0x1B, 0x61, 0x00 }; }
        private byte[] AlignCenter() { return new byte[] { 0x1B, 0x61, 0x01 }; }
        private byte[] FeedLines(int n) { return new byte[] { 0x1B, 0x64, (byte)n }; }
        private byte[] CutPaper() { return new byte[] { 0x1D, 0x56, 0x41, 0x10 }; }
    }
}
