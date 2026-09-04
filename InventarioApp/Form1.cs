using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace InventarioApp
{
    public partial class Form1 : Form
    {
        private readonly Bridge bridge = new Bridge();
        private static readonly Size TamanoLogin = new Size(480, 640);

        public Form1()
        {
            InitializeComponent();

            // Ventana chica y centrada mientras se muestra el login; se agranda tras iniciar sesión.
            StartPosition = FormStartPosition.CenterScreen;
            Size = TamanoLogin;
            MinimumSize = new Size(420, 560);

            bridge.LoginExitoso += () => { if (IsHandleCreated) BeginInvoke(new Action(MostrarVentanaCompleta)); };
            bridge.SesionCerrada += () => { if (IsHandleCreated) BeginInvoke(new Action(VolverATamanoLogin)); };
            bridge.TemaCambiado += modoOscuro => { if (IsHandleCreated) BeginInvoke(new Action(() => AplicarBarraDeTitulo(modoOscuro))); };

            InicializarWebView();
        }

        private void MostrarVentanaCompleta()
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            WindowState = FormWindowState.Maximized;
        }

        private void VolverATamanoLogin()
        {
            WindowState = FormWindowState.Normal;
            Size = TamanoLogin;
            StartPosition = FormStartPosition.CenterScreen;
            CenterToScreen();
        }

        // Pinta la barra de título con el modo oscuro/claro de Windows (DWM), siguiendo el
        // tema que el usuario tenga elegido dentro de la app. Requiere Windows 10 1809+ / 11;
        // en versiones más viejas simplemente se queda con la barra de título por defecto (clara).
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private void AplicarBarraDeTitulo(bool modoOscuro)
        {
            if (!IsHandleCreated) return;

            try
            {
                int valor = modoOscuro ? 1 : 0;
                const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
                const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
                if (DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref valor, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref valor, sizeof(int));
                }
            }
            catch
            {
                // Si la versión de Windows no lo soporta, se queda con la barra de título por defecto.
            }
        }

        private async void InicializarWebView()
        {
            await webView21.EnsureCoreWebView2Async();

            // Inyectar el Bridge C# para que JavaScript pueda llamarlo
            webView21.CoreWebView2.AddHostObjectToScript("chrome", bridge);

            if (File.Exists(AppPaths.IndexHtmlPath))
            {
                webView21.CoreWebView2.Navigate(AppPaths.IndexHtmlPath);
            }
            else
            {
                MessageBox.Show("No se encontró index.html.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
