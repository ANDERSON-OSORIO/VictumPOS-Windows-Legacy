using System;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace VictumPOS.PrintBridge.Service.Services
{
    internal sealed class WindowsSpoolerPrinter
    {
        private readonly BridgeSettingsService _settings;
        private string _content = "";
        private int _currentY;
        private bool _printed;
        private Image _rasterImage;
        private Image _logoImage;

        public WindowsSpoolerPrinter(BridgeSettingsService settings)
        {
            _settings = settings;
        }

        public void Print(string content, string printerName)
        {
            if (string.IsNullOrWhiteSpace(printerName))
                throw new InvalidOperationException("Nombre de impresora vacio");

            if (!PrinterSettings.InstalledPrinters.Cast<string>().Any(p => string.Equals(p, printerName, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Impresora Windows no encontrada: " + printerName);

            var encoding = GetPrinterEncoding();
            _content = encoding.GetString(encoding.GetBytes(content ?? ""));
            _currentY = 0;
            _printed = false;
            DisposeImage(ref _rasterImage);
            _rasterImage = TryLoadRasterImage(content);
            DisposeImage(ref _logoImage);

            try
            {
                if (_rasterImage != null)
                {
                    PrintRasterImage(printerName);
                    return;
                }

                if (IsEscPosContent(content))
                {
                    RawPrinterHelper.SendBytesToPrinter(printerName, BuildRawEscPosBytes(content, encoding, LoadLogoEscPosBytes()));
                    return;
                }

                _logoImage = LoadLogoImage();
                using (var doc = new PrintDocument())
                {
                    doc.PrinterSettings.PrinterName = printerName;
                    doc.DefaultPageSettings.Margins = new Margins(5, 5, 5, 5);
                    doc.DefaultPageSettings.PaperSize = new PaperSize("Ticket", 300, 2000);
                    doc.PrintPage += PrintTextPage;
                    doc.Print();
                    doc.PrintPage -= PrintTextPage;
                }
            }
            finally
            {
                DisposeImage(ref _rasterImage);
                DisposeImage(ref _logoImage);
            }
        }

        private void PrintRasterImage(string printerName)
        {
            if (_rasterImage == null)
                throw new InvalidOperationException("Factura rasterizada invalida");

            using (var doc = new PrintDocument())
            {
                doc.PrinterSettings.PrinterName = printerName;
                doc.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
                var paperWidth = 300;
                var imageHeight = (int)Math.Ceiling((double)_rasterImage.Height / _rasterImage.Width * Math.Max(1, paperWidth - 10));
                doc.DefaultPageSettings.PaperSize = new PaperSize("Factura rasterizada", paperWidth, Math.Max(imageHeight + 40, 300));
                doc.PrintPage += PrintRasterImagePage;
                doc.Print();
                doc.PrintPage -= PrintRasterImagePage;
            }
        }

        private void PrintRasterImagePage(object sender, PrintPageEventArgs e)
        {
            if (_rasterImage == null)
                throw new InvalidOperationException("Factura rasterizada invalida");

            if (_printed)
            {
                e.HasMorePages = false;
                return;
            }

            var width = Math.Max(1, e.PageBounds.Width - 10);
            var height = (int)Math.Ceiling((double)_rasterImage.Height / _rasterImage.Width * width);
            e.Graphics.DrawImage(_rasterImage, new Rectangle(5, 0, width, height));
            _printed = true;
            e.HasMorePages = false;
        }

        private void PrintTextPage(object sender, PrintPageEventArgs e)
        {
            if (_printed)
            {
                e.HasMorePages = false;
                return;
            }

            using (var normalFont = new Font("Consolas", 10))
            {
                var left = 5;
                var width = e.PageBounds.Width - 10;
                DrawLogo(e.Graphics, width);
                DrawLine(e.Graphics, "================================", normalFont, left, width);
                foreach (var line in _content.Replace("\r", "").Split('\n'))
                    if (!string.IsNullOrWhiteSpace(line))
                        DrawLine(e.Graphics, line, normalFont, left, width);
                DrawLine(e.Graphics, "================================", normalFont, left, width);
                DrawCentered(e.Graphics, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), normalFont, width);
            }

            _printed = true;
            e.HasMorePages = false;
        }

        private Image LoadLogoImage()
        {
            try
            {
                if (!_settings.IsPrintLogoEnabled())
                    return null;

                var path = ResolveLogoPath();
                if (string.IsNullOrWhiteSpace(path))
                    return null;

                using (var image = Image.FromFile(path))
                    return new Bitmap(image);
            }
            catch (Exception ex)
            {
                BridgeLogger.Log("Error cargando logo WindowsPrinter: " + ex.Message);
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

        private byte[] LoadLogoEscPosBytes()
        {
            try
            {
                if (!_settings.IsPrintLogoEnabled())
                    return null;

                var path = ResolveLogoPath();
                if (string.IsNullOrWhiteSpace(path))
                    return null;

                return ConvertLogoToEscPos(Convert.ToBase64String(File.ReadAllBytes(path)));
            }
            catch (Exception ex)
            {
                BridgeLogger.Log("Error cargando logo RAW WindowsPrinter: " + ex.Message);
                return null;
            }
        }

        private byte[] ConvertLogoToEscPos(string base64)
        {
            var commaIndex = base64.IndexOf(',');
            var cleanBase64 = commaIndex >= 0 ? base64.Substring(commaIndex + 1) : base64;
            var imageBytes = Convert.FromBase64String(cleanBase64);

            using (var sourceStream = new MemoryStream(imageBytes))
            using (var original = new Bitmap(sourceStream))
            {
                var width = Math.Min(_settings.GetPrintWidth(), 240);
                var height = Math.Max(1, (int)((double)original.Height / original.Width * width));

                using (var normalized = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(normalized))
                    {
                        g.Clear(Color.White);
                        g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
                        g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
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

                        if (totalPixels > 0 && blackPixels / (double)totalPixels > 0.85)
                            return null;

                        return ms.ToArray();
                    }
                }
            }
        }

        private static byte[] BuildRawEscPosBytes(string content, Encoding encoding, byte[] logoBytes)
        {
            using (var ms = new MemoryStream())
            {
                Write(ms, new byte[] { 0x1B, 0x40 });
                if (logoBytes != null && logoBytes.Length > 0)
                {
                    Write(ms, new byte[] { 0x1B, 0x61, 0x01 });
                    Write(ms, logoBytes);
                    Write(ms, new byte[] { 0x1B, 0x64, 0x02 });
                }

                var body = encoding.GetBytes(content ?? "");
                Write(ms, body);
                if (!ContainsCutCommand(body))
                    Write(ms, new byte[] { 0x1B, 0x64, 0x02, 0x1D, 0x56, 0x41, 0x10 });
                return ms.ToArray();
            }
        }

        private void DrawLine(Graphics g, string text, Font font, int left, int width)
        {
            using (var format = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near, Trimming = StringTrimming.Word })
            {
                var layout = new RectangleF(left, _currentY, width, 1000);
                var size = g.MeasureString(text, font, width, format);
                g.DrawString(text, font, Brushes.Black, layout, format);
                _currentY += (int)Math.Ceiling(size.Height) + 2;
            }
        }

        private void DrawLogo(Graphics g, int width)
        {
            if (_logoImage == null)
                return;

            var drawWidth = Math.Min(width - 20, 220);
            if (drawWidth <= 0)
                return;

            var drawHeight = (int)Math.Ceiling((double)_logoImage.Height / _logoImage.Width * drawWidth);
            var x = Math.Max(0, (width - drawWidth) / 2);
            g.DrawImage(_logoImage, new Rectangle(x, _currentY, drawWidth, drawHeight));
            _currentY += drawHeight + 8;
        }

        private void DrawCentered(Graphics g, string text, Font font, int width)
        {
            var size = g.MeasureString(text, font);
            g.DrawString(text, font, Brushes.Black, (width - size.Width) / 2, _currentY);
            _currentY += (int)Math.Ceiling(size.Height) + 4;
        }

        private static Image TryLoadRasterImage(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return null;

            var value = content.Trim();
            if (!value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)
                && (value.Length < 128 || value.Contains("\n") || value.Contains("\r")))
                return null;

            try
            {
                if (value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                {
                    var commaIndex = value.IndexOf(',');
                    if (commaIndex < 0 || commaIndex == value.Length - 1)
                        return null;

                    value = value.Substring(commaIndex + 1);
                }

                var imageBytes = Convert.FromBase64String(value);
                using (var ms = new MemoryStream(imageBytes))
                using (var image = Image.FromStream(ms))
                    return new Bitmap(image);
            }
            catch
            {
                return null;
            }
        }

        private static bool IsEscPosContent(string content)
        {
            return !string.IsNullOrEmpty(content) && (content.IndexOf('\x1B') >= 0 || content.IndexOf('\x1D') >= 0);
        }

        private static bool ContainsCutCommand(byte[] bytes)
        {
            for (var i = 0; i < bytes.Length - 1; i++)
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

        private static void DisposeImage(ref Image image)
        {
            if (image != null)
            {
                image.Dispose();
                image = null;
            }
        }

        private static void Write(Stream stream, byte[] bytes)
        {
            stream.Write(bytes, 0, bytes.Length);
        }

        private static class RawPrinterHelper
        {
            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
            private class DOCINFOA
            {
                [MarshalAs(UnmanagedType.LPStr)] public string pDocName = "VictumPOS Print Bridge";
                [MarshalAs(UnmanagedType.LPStr)] public string pOutputFile = "";
                [MarshalAs(UnmanagedType.LPStr)] public string pDataType = "RAW";
            }

            [DllImport("winspool.Drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true)]
            private static extern bool OpenPrinter(string szPrinter, out IntPtr hPrinter, IntPtr pd);

            [DllImport("winspool.Drv", EntryPoint = "ClosePrinter", SetLastError = true, ExactSpelling = true)]
            private static extern bool ClosePrinter(IntPtr hPrinter);

            [DllImport("winspool.Drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true)]
            private static extern bool StartDocPrinter(IntPtr hPrinter, int level, [In] DOCINFOA di);

            [DllImport("winspool.Drv", EntryPoint = "EndDocPrinter", SetLastError = true, ExactSpelling = true)]
            private static extern bool EndDocPrinter(IntPtr hPrinter);

            [DllImport("winspool.Drv", EntryPoint = "StartPagePrinter", SetLastError = true, ExactSpelling = true)]
            private static extern bool StartPagePrinter(IntPtr hPrinter);

            [DllImport("winspool.Drv", EntryPoint = "EndPagePrinter", SetLastError = true, ExactSpelling = true)]
            private static extern bool EndPagePrinter(IntPtr hPrinter);

            [DllImport("winspool.Drv", EntryPoint = "WritePrinter", SetLastError = true, ExactSpelling = true)]
            private static extern bool WritePrinter(IntPtr hPrinter, byte[] pBytes, int dwCount, out int dwWritten);

            public static void SendBytesToPrinter(string printerName, byte[] bytes)
            {
                IntPtr printerHandle;
                if (!OpenPrinter(printerName, out printerHandle, IntPtr.Zero))
                    throw new InvalidOperationException("No se pudo abrir impresora RAW: " + Marshal.GetLastWin32Error());

                try
                {
                    var docInfo = new DOCINFOA();
                    if (!StartDocPrinter(printerHandle, 1, docInfo))
                        throw new InvalidOperationException("No se pudo iniciar documento RAW: " + Marshal.GetLastWin32Error());

                    try
                    {
                        if (!StartPagePrinter(printerHandle))
                            throw new InvalidOperationException("No se pudo iniciar pagina RAW: " + Marshal.GetLastWin32Error());

                        try
                        {
                            int written;
                            if (!WritePrinter(printerHandle, bytes, bytes.Length, out written) || written != bytes.Length)
                                throw new InvalidOperationException("No se pudo escribir RAW completo: " + Marshal.GetLastWin32Error());
                        }
                        finally
                        {
                            EndPagePrinter(printerHandle);
                        }
                    }
                    finally
                    {
                        EndDocPrinter(printerHandle);
                    }
                }
                finally
                {
                    ClosePrinter(printerHandle);
                }
            }
        }
    }
}
