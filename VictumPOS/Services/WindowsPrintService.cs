using System;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace VictumPOS.Services
{
    public class WindowsPrintService
    {
        private string _content = "";
        private int _currentY;
        private bool _printed;
        private Image _rasterImage;
        private static Image _cachedLogo;

        public void Print(string content, string printerName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(printerName))
                    throw new Exception("Nombre de impresora vacio");

                if (!PrinterSettings.InstalledPrinters.Cast<string>().Any(p => string.Equals(p, printerName, StringComparison.OrdinalIgnoreCase)))
                    throw new Exception("Impresora Windows no encontrada: " + printerName);

                var encoding = Encoding.GetEncoding(858);
                _content = encoding.GetString(encoding.GetBytes(content ?? ""));
                _currentY = 0;
                _printed = false;
                DisposeRasterImage();
                _rasterImage = TryLoadRasterImage(content);

                if (_rasterImage != null)
                {
                    PrintRasterImage(printerName);
                    return;
                }

                if (IsEscPosContent(content))
                {
                    PrintRawEscPos(printerName, content);
                    return;
                }

                using (var doc = new PrintDocument())
                {
                    doc.PrinterSettings.PrinterName = printerName;
                    doc.DefaultPageSettings.Margins = new Margins(5, 5, 5, 5);
                    doc.DefaultPageSettings.PaperSize = new PaperSize("Ticket", 300, 2000);
                    doc.PrintPage += PrintPage;
                    doc.Print();
                    doc.PrintPage -= PrintPage;
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Error impresion Windows: " + ex.Message);
            }
            finally
            {
                DisposeRasterImage();
            }
        }

        private void PrintRasterImage(string printerName)
        {
            if (_rasterImage == null)
                throw new Exception("Factura rasterizada invalida");

            using (var doc = new PrintDocument())
            {
                doc.PrinterSettings.PrinterName = printerName;
                doc.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);

                var paperWidth = 300;
                var printableWidth = Math.Max(1, paperWidth - 10);
                var imageHeight = (int)Math.Ceiling((double)_rasterImage.Height / _rasterImage.Width * printableWidth);
                doc.DefaultPageSettings.PaperSize = new PaperSize("Factura rasterizada", paperWidth, Math.Max(imageHeight + 40, 300));

                doc.PrintPage += PrintRasterImagePage;
                doc.Print();
                doc.PrintPage -= PrintRasterImagePage;
            }
        }

        private void PrintRasterImagePage(object sender, PrintPageEventArgs e)
        {
            if (_rasterImage == null)
                throw new Exception("Factura rasterizada invalida");

            if (_printed)
            {
                e.HasMorePages = false;
                return;
            }

            var left = 5;
            var width = Math.Max(1, e.PageBounds.Width - 10);
            var height = (int)Math.Ceiling((double)_rasterImage.Height / _rasterImage.Width * width);

            e.Graphics.DrawImage(_rasterImage, new Rectangle(left, 0, width, height));
            _printed = true;
            e.HasMorePages = false;
        }

        private void PrintRawEscPos(string printerName, string content)
        {
            var bytes = BuildRawEscPosBytes(content);
            RawPrinterHelper.SendBytesToPrinter(printerName, bytes);
            Logger.Log("Windows RAW ESC/POS bytes sent: " + bytes.Length);
        }

        private byte[] BuildRawEscPosBytes(string content)
        {
            var encoding = Encoding.GetEncoding(858);
            using (var ms = new MemoryStream())
            {
                var body = encoding.GetBytes(content ?? "");
                ms.Write(body, 0, body.Length);

                if (!ContainsCutCommand(body))
                {
                    var tail = new byte[] { 0x1B, 0x64, 0x02, 0x1D, 0x56, 0x41, 0x10 };
                    ms.Write(tail, 0, tail.Length);
                }

                return ms.ToArray();
            }
        }

        private void PrintPage(object sender, PrintPageEventArgs e)
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

                TryDrawLogo(e.Graphics, width);
                DrawLine(e.Graphics, "================================", normalFont, left, width);

                foreach (var line in (_content ?? "").Replace("\r", "").Split('\n'))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                        DrawLine(e.Graphics, line, normalFont, left, width);
                }

                DrawLine(e.Graphics, "================================", normalFont, left, width);
                DrawCentered(e.Graphics, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), normalFont, width);
            }

            _printed = true;
            e.HasMorePages = false;
        }

        private void TryDrawLogo(Graphics g, int width)
        {
            try
            {
                if (_cachedLogo == null)
                {
                    var path = ResolveLogoPath();
                    if (!File.Exists(path))
                        return;

                    using (var imgTemp = Image.FromFile(path))
                    {
                        var logoWidth = 260;
                        var logoHeight = (int)((double)imgTemp.Height / imgTemp.Width * logoWidth);
                        _cachedLogo = new Bitmap(imgTemp, new Size(logoWidth, logoHeight));
                    }
                }

                var centerX = (width - _cachedLogo.Width) / 2;
                g.DrawImage(_cachedLogo, centerX, _currentY);
                _currentY += _cachedLogo.Height + 10;
            }
            catch (Exception ex)
            {
                Logger.Log("Error logo: " + ex.Message);
            }
        }

        private string ResolveLogoPath()
        {
            var settings = new SettingsService();
            var configured = settings.GetPrintLogoPath();
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
                return configured;

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "logo.png");
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

        private void DrawCentered(Graphics g, string text, Font font, int width)
        {
            var size = g.MeasureString(text, font);
            var x = (width - size.Width) / 2;
            g.DrawString(text, font, Brushes.Black, x, _currentY);
            _currentY += (int)Math.Ceiling(size.Height) + 4;
        }

        private Image TryLoadRasterImage(string content)
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

        private void DisposeRasterImage()
        {
            if (_rasterImage != null)
            {
                _rasterImage.Dispose();
                _rasterImage = null;
            }
        }

        private bool IsEscPosContent(string content)
        {
            return !string.IsNullOrEmpty(content) && (content.IndexOf('\x1B') >= 0 || content.IndexOf('\x1D') >= 0);
        }

        private bool ContainsCutCommand(byte[] bytes)
        {
            for (var i = 0; i < bytes.Length - 1; i++)
                if (bytes[i] == 0x1D && bytes[i + 1] == 0x56)
                    return true;

            return false;
        }

        private static class RawPrinterHelper
        {
            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
            private class DOCINFOA
            {
                [MarshalAs(UnmanagedType.LPStr)] public string pDocName = "VictumPOS ESC/POS";
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
                if (bytes.Length <= 0)
                    throw new Exception("Contenido RAW vacio");

                IntPtr printerHandle;
                if (!OpenPrinter(printerName, out printerHandle, IntPtr.Zero))
                    throw new Exception("No se pudo abrir impresora RAW: " + Marshal.GetLastWin32Error());

                try
                {
                    var docInfo = new DOCINFOA();
                    if (!StartDocPrinter(printerHandle, 1, docInfo))
                        throw new Exception("No se pudo iniciar documento RAW: " + Marshal.GetLastWin32Error());

                    try
                    {
                        if (!StartPagePrinter(printerHandle))
                            throw new Exception("No se pudo iniciar pagina RAW: " + Marshal.GetLastWin32Error());

                        try
                        {
                            int written;
                            if (!WritePrinter(printerHandle, bytes, bytes.Length, out written) || written != bytes.Length)
                                throw new Exception("No se pudo escribir RAW completo: " + Marshal.GetLastWin32Error());
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
