using System;
using System.Drawing;
using System.Drawing.Printing;
using ZXing;
using ZXing.Common;
using ZXing.Windows.Compatibility;

namespace InventarioApp
{
    // Manda a imprimir una etiqueta (código de barras Code128 + número de parte) a una
    // impresora de etiquetas instalada como impresora normal de Windows (ej. Brother P-touch
    // D610BT por USB). Pensado para carrete continuo de 18mm (~0.7").
    public static class LabelPrinter
    {
        public static void Imprimir(string nombreImpresora, string numeroParte, string descripcion)
        {
            if (string.IsNullOrWhiteSpace(nombreImpresora))
                throw new Exception("No hay una impresora de etiquetas configurada. Ve a Configuración > General.");

            var writer = new BarcodeWriter
            {
                Format = BarcodeFormat.CODE_128,
                Options = new EncodingOptions { Height = 160, Width = 420, Margin = 0 }
            };
            using var barcodeBitmap = writer.Write(numeroParte);

            using var doc = new PrintDocument();
            doc.PrinterSettings.PrinterName = nombreImpresora;
            if (!doc.PrinterSettings.IsValid)
                throw new Exception($"La impresora \"{nombreImpresora}\" no está disponible o no está conectada.");

            // Etiqueta de ~0.7" (18mm) de alto por ~2.6" de largo. Algunos drivers de
            // impresoras de etiquetas ignoran/ajustan esto según el carrete cargado; si el
            // driver no acepta tamaño personalizado, se sigue imprimiendo con su tamaño por defecto.
            try
            {
                doc.DefaultPageSettings.PaperSize = new PaperSize("Etiqueta 18mm", 260, 71);
                doc.DefaultPageSettings.Margins = new Margins(5, 5, 0, 0);
            }
            catch
            {
                // Se ignora: se imprime con el tamaño de página por defecto del driver.
            }

            doc.PrintPage += (s, e) =>
            {
                var bounds = e.MarginBounds.Width > 0 && e.MarginBounds.Height > 0 ? e.MarginBounds : e.PageBounds;

                using var fontParte = new Font("Segoe UI", 13, FontStyle.Bold);
                var formatoCentrado = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near };

                int altoBarcode = (int)(bounds.Height * 0.55);
                var rectBarcode = new Rectangle(bounds.Left, bounds.Top, bounds.Width, altoBarcode);
                e.Graphics.DrawImage(barcodeBitmap, rectBarcode);

                var rectTexto = new RectangleF(bounds.Left, rectBarcode.Bottom, bounds.Width, bounds.Height - altoBarcode);
                e.Graphics.DrawString(numeroParte, fontParte, Brushes.Black, rectTexto, formatoCentrado);
            };

            doc.Print();
        }
    }
}
