using System;
using System.IO;
using System.Reflection;

namespace InventarioApp
{
    // Permite que la app sea portable con solo copiar el .exe: el HTML de la interfaz y una
    // configuración por defecto viajan embebidos en el propio ensamblado y se extraen aquí la
    // primera vez que corren en una PC nueva. appsettings.json NO se sobrescribe si ya existe,
    // para no perder ajustes de red hechos a mano en esa máquina.
    public static class AppPaths
    {
        public static readonly string DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InventarioApp");

        public static string IndexHtmlPath => Path.Combine(DataDir, "index.html");
        public static string AppSettingsPath => Path.Combine(DataDir, "appsettings.json");

        public static void EnsureWebAssets()
        {
            Directory.CreateDirectory(DataDir);

            // El HTML/JS de la UI se sobrescribe siempre: el .exe es la fuente de verdad.
            ExtractResource("InventarioApp.WebAssets.index.html", IndexHtmlPath, overwrite: true);
            ExtractResource("InventarioApp.WebAssets.logo.svg", Path.Combine(DataDir, "logo.svg"), overwrite: true);

            // appsettings.json solo se crea si no existe, para respetar ediciones locales
            // (por ejemplo, si la ruta del servidor cambia en esa PC en particular).
            if (!File.Exists(AppSettingsPath))
            {
                ExtractResource("InventarioApp.WebAssets.appsettings.default.json", AppSettingsPath, overwrite: false);
            }

            // logo.svg también se acepta junto al .exe para poder cambiarlo sin recompilar.
            string logoJuntoAlExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.svg");
            if (File.Exists(logoJuntoAlExe))
            {
                File.Copy(logoJuntoAlExe, Path.Combine(DataDir, "logo.svg"), overwrite: true);
            }
        }

        private static void ExtractResource(string logicalName, string destPath, bool overwrite)
        {
            if (!overwrite && File.Exists(destPath)) return;

            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream(logicalName);
            if (stream == null) return;

            using var fileStream = File.Create(destPath);
            stream.CopyTo(fileStream);
        }
    }
}
