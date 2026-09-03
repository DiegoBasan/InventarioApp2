using System;
using System.IO;
using System.Windows.Forms;

namespace InventarioApp
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
            InicializarWebView();
        }

        private async void InicializarWebView()
        {
            // Inicializar el motor de WebView2
            await webView21.EnsureCoreWebView2Async();

            // Inyectar el Bridge C# para que JavaScript pueda llamarlo
            webView21.CoreWebView2.AddHostObjectToScript("chrome", new Bridge());

            // Cargar el archivo index.html local
            string htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "index.html");
            if (File.Exists(htmlPath))
            {
                webView21.CoreWebView2.Navigate(htmlPath);
            }
            else
            {
                MessageBox.Show("No se encontró el archivo index.html en la carpeta de ejecución.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            new Bridge().InicializarUsuarios();
        }
    }
}
