namespace InventarioApp
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();

            // Debe correr antes de crear Form1: extrae index.html/appsettings por defecto a
            // %LocalAppData%\InventarioApp, y SQL/Bridge ya asumen que existen al construirse.
            AppPaths.EnsureWebAssets();

            Application.Run(new Form1());
        }
    }
}