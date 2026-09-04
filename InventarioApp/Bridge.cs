using System;
using System.IO;
using System.Text.Json;
using ClosedXML.Excel;

namespace InventarioApp
{
    public class Bridge
    {
        private SQL SQL;

        // Permiten que Form1 (ventana nativa) reaccione a eventos que ocurren del lado de JS,
        // como agrandar la ventana tras el login o encogerla de nuevo al cerrar sesión.
        public event Action LoginExitoso;
        public event Action SesionCerrada;

        public Bridge()
        {
            SQL = new SQL();
        }

        public void NotificarLoginExitoso(string usuario)
        {
            SQL.RegistrarLogin(usuario);
            LoginExitoso?.Invoke();
        }

        public void NotificarLogout()
        {
            SesionCerrada?.Invoke();
        }

        public string ObtenerMateriales()
        {
            try
            {
                var materiales = SQL.ObtenerTodosLosMateriales();
                return JsonSerializer.Serialize(materiales);
            }
            catch (Exception ex)
            {
                return "ERROR:" + ex.Message;
            }
        }

        public string ObtenerHistorial()
        {
            try
            {
                var historial = SQL.ObtenerHistorialMovimientos();
                return JsonSerializer.Serialize(historial);
            }
            catch (Exception ex)
            {
                return "ERROR:" + ex.Message;
            }
        }

        public string ObtenerUsuarios()
        {
            try
            {
                var usuarios = SQL.ObtenerTodosLosUsuarios();
                return JsonSerializer.Serialize(usuarios);
            }
            catch (Exception ex)
            {
                return "ERROR:" + ex.Message;
            }
        }

        public string ObtenerLugares()
        {
            try
            {
                var lugares = SQL.ObtenerLugares();
                return JsonSerializer.Serialize(lugares);
            }
            catch (Exception ex)
            {
                return "ERROR:" + ex.Message;
            }
        }

        public string CrearLugar(string nombre)
        {
            try
            {
                SQL.CrearLugar(nombre);
                return "OK";
            }
            catch (Exception ex)
            {
                return "ERROR:" + ex.Message;
            }
        }

        public string EliminarLugar(string nombre)
        {
            try
            {
                SQL.EliminarLugar(nombre);
                return "OK";
            }
            catch (Exception ex)
            {
                return "ERROR:" + ex.Message;
            }
        }

        public string ObtenerCategorias()
        {
            try
            {
                var categorias = SQL.ObtenerCategorias();
                return JsonSerializer.Serialize(categorias);
            }
            catch (Exception ex)
            {
                return "ERROR:" + ex.Message;
            }
        }

        public string CrearCategoria(string nombre)
        {
            try
            {
                SQL.CrearCategoria(nombre);
                return "OK";
            }
            catch (Exception ex)
            {
                return "ERROR:" + ex.Message;
            }
        }

        public string EliminarCategoria(string nombre)
        {
            try
            {
                SQL.EliminarCategoria(nombre);
                return "OK";
            }
            catch (Exception ex)
            {
                return "ERROR:" + ex.Message;
            }
        }

        public string AgregarMaterial(string numeroParte, string descripcion, int cantidad, string proyecto, string equipo, string marca, string categoria, string lugar, string usuario, bool autoGenerarNumeroParte, bool alertaStockActiva, int stockMinimoPersonalizado)
        {
            try
            {
                if (autoGenerarNumeroParte)
                {
                    numeroParte = SQL.GenerarNumeroParte(categoria, proyecto, consumir: true);
                }

                var material = new Material
                {
                    NumeroParte = numeroParte,
                    Descripcion = descripcion,
                    Cantidad = cantidad,
                    Marca = marca,
                    Categoria = categoria,
                    Lugar = lugar,
                    AlertaStockActiva = alertaStockActiva,
                    StockMinimoPersonalizado = alertaStockActiva ? stockMinimoPersonalizado : (int?)null
                };
                SQL.GuardarMaterialMultiplo(material, equipo, proyecto, usuario);
                return "OK:" + numeroParte;
            }
            catch (Exception ex)
            {
                return "ERROR:" + ex.Message;
            }
        }

        public string VistaPreviaNumeroParte(string categoria, string proyecto)
        {
            try
            {
                return SQL.GenerarNumeroParte(categoria, proyecto, consumir: false);
            }
            catch (Exception ex)
            {
                return "ERROR:" + ex.Message;
            }
        }

        // WebView2 no puede marshalear un array nativo (int[]) por COM desde JS — falla con
        // "Type mismatch (0x80020005)". Se recibe como JSON y se deserializa aquí.
        public string ActualizarMaterialesLote(string idsJson, string proyecto, string categoria, string lugar, string usuario)
        {
            try
            {
                var ids = JsonSerializer.Deserialize<System.Collections.Generic.List<int>>(idsJson);
                SQL.ActualizarMaterialesLote(ids, proyecto, categoria, lugar, usuario);
                return "OK";
            }
            catch (Exception ex)
            {
                return "ERROR:" + ex.Message;
            }
        }

        public string DeshacerUltimoRetiro(string usuario)
        {
            try
            {
                var numeroParte = SQL.DeshacerUltimoRetiro(usuario);
                return "OK:" + numeroParte;
            }
            catch (Exception ex)
            {
                return "ERROR:" + ex.Message;
            }
        }

        public string ObtenerHistorialMaterial(string numeroParte)
        {
            try
            {
                var historial = SQL.ObtenerHistorialPorRepuesto(numeroParte);
                return JsonSerializer.Serialize(historial);
            }
            catch (Exception ex)
            {
                return "ERROR:" + ex.Message;
            }
        }

        public string ObtenerHistorialAccesos()
        {
            try
            {
                var accesos = SQL.ObtenerHistorialAccesos();
                return JsonSerializer.Serialize(accesos);
            }
            catch (Exception ex)
            {
                return "ERROR:" + ex.Message;
            }
        }

        public string ListarBackups()
        {
            try
            {
                var backups = SQL.ListarBackups();
                return JsonSerializer.Serialize(backups);
            }
            catch (Exception ex)
            {
                return "ERROR:" + ex.Message;
            }
        }

        // Restaurar la base de datos es destructivo: solo el usuario Admin puede hacerlo,
        // igual que eliminar materiales.
        public string RestaurarBackup(string nombreArchivo, string usuario)
        {
            try
            {
                if (!string.Equals(usuario, "Admin", StringComparison.OrdinalIgnoreCase))
                    return "ERROR:Solo el usuario Admin puede restaurar un respaldo.";

                SQL.RestaurarBackup(nombreArchivo);
                return "OK";
            }
            catch (Exception ex)
            {
                return "ERROR:" + ex.Message;
            }
        }

        public string ObtenerEstadisticas()
        {
            try
            {
                var estadisticas = SQL.ObtenerEstadisticas();
                return JsonSerializer.Serialize(estadisticas);
            }
            catch (Exception ex)
            {
                return "ERROR:" + ex.Message;
            }
        }

        public int ObtenerStockMinimo()
        {
            return SQL.ObtenerStockMinimo();
        }

        public string GuardarStockMinimo(int valor)
        {
            try
            {
                SQL.GuardarStockMinimo(valor);
                return "OK";
            }
            catch (Exception ex)
            {
                return "ERROR:" + ex.Message;
            }
        }

        public string RetirarMaterial(int id, int cantidad, string usuario)
        {
            try
            {
                SQL.RegistrarRetiro(id, cantidad, usuario);
                return "OK";
            }
            catch (Exception ex)
            {
                return "ERROR:" + ex.Message;
            }
        }

        public string EliminarMaterial(int id, string usuario)
        {
            try
            {
                // Eliminar es una acción destructiva e irreversible: se restringe al usuario Admin
                // aunque el botón ya esté oculto para los demás en la interfaz.
                if (!string.Equals(usuario, "Admin", StringComparison.OrdinalIgnoreCase))
                    return "ERROR:Solo el usuario Admin puede eliminar materiales.";

                SQL.EliminarMaterial(id, usuario);
                return "OK";
            }
            catch (Exception ex)
            {
                return "ERROR:" + ex.Message;
            }
        }

        public string ActualizarMaterial(int id, string descripcion, int cantidad, string proyecto, string equipo, string marca, string categoria, string lugar, string usuario, bool alertaStockActiva, int stockMinimoPersonalizado)
        {
            try
            {
                SQL.ActualizarMaterial(id, descripcion, cantidad, proyecto, equipo, marca, categoria, lugar, usuario, alertaStockActiva, alertaStockActiva ? stockMinimoPersonalizado : (int?)null);
                return "OK";
            }
            catch (Exception ex)
            {
                return "ERROR:" + ex.Message;
            }
        }

        public string ValidarUsuario(string nombre, string contrasena)
        {
            try
            {
                bool valido = SQL.ValidarUsuario(nombre, contrasena);
                if (!valido) return "ERROR:Usuario o contraseña incorrectos";

                var usuario = SQL.ObtenerUsuarioPorNombre(nombre);
                if (usuario == null) return "ERROR:Usuario no encontrado";

                return usuario.Id + "|" + usuario.Nombre + "|" + usuario.Rol;
            }
            catch (Exception ex)
            {
                return "ERROR:" + ex.Message;
            }
        }

        public string CrearUsuario(string nombre, string contrasena, string rol)
        {
            try
            {
                SQL.CrearUsuario(nombre, contrasena, rol);
                return "OK";
            }
            catch (Exception ex)
            {
                return "ERROR:" + ex.Message;
            }
        }

        public string ActualizarUsuario(int id, string rol, bool activo)
        {
            try
            {
                SQL.ActualizarUsuario(id, rol, activo);
                return "OK";
            }
            catch (Exception ex)
            {
                return "ERROR:" + ex.Message;
            }
        }

        public string CambiarPasswordUsuario(int id, string nuevaContrasena)
        {
            try
            {
                SQL.CambiarPasswordUsuario(id, nuevaContrasena);
                return "OK";
            }
            catch (Exception ex)
            {
                return "ERROR:" + ex.Message;
            }
        }

        public void InicializarUsuarios()
        {
            try
            {
                SQL.CrearTablaUsuarios();
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show("Error inicializando usuarios: " + ex.Message);
            }
        }

        // ===== EXPORTAR A EXCEL =====
        // Se genera del lado de C# con ClosedXML y se guarda con un diálogo nativo,
        // evitando depender de la descarga de archivos del WebView2 (que no está configurada).
        public string ExportarExcel()
        {
            try
            {
                using var dialog = new System.Windows.Forms.SaveFileDialog
                {
                    Filter = "Libro de Excel (*.xlsx)|*.xlsx",
                    FileName = $"inventario_{DateTime.Now:yyyy-MM-dd_HHmm}.xlsx"
                };

                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return "CANCELADO";

                var materiales = SQL.ObtenerTodosLosMateriales();
                var historial = SQL.ObtenerHistorialMovimientos();

                using var workbook = new XLWorkbook();

                var wsInventario = workbook.Worksheets.Add("Inventario");
                string[] headersInv = { "ID", "Número de Parte", "Descripción", "Categoría", "Cantidad", "Proyecto", "Equipo", "Marca", "Lugar", "Último Cambio" };
                for (int i = 0; i < headersInv.Length; i++) wsInventario.Cell(1, i + 1).Value = headersInv[i];

                int row = 2;
                foreach (var m in materiales)
                {
                    wsInventario.Cell(row, 1).Value = m.Id;
                    wsInventario.Cell(row, 2).Value = m.NumeroParte;
                    wsInventario.Cell(row, 3).Value = m.Descripcion;
                    wsInventario.Cell(row, 4).Value = m.Categoria;
                    wsInventario.Cell(row, 5).Value = m.Cantidad;
                    wsInventario.Cell(row, 6).Value = m.Proyecto;
                    wsInventario.Cell(row, 7).Value = m.Equipo;
                    wsInventario.Cell(row, 8).Value = m.Marca;
                    wsInventario.Cell(row, 9).Value = m.Lugar;
                    wsInventario.Cell(row, 10).Value = m.Cambio;
                    row++;
                }
                wsInventario.Columns().AdjustToContents();

                var wsHistorial = workbook.Worksheets.Add("Historial");
                string[] headersHist = { "Número de Parte", "Cantidad", "Fecha", "Tipo", "Usuario" };
                for (int i = 0; i < headersHist.Length; i++) wsHistorial.Cell(1, i + 1).Value = headersHist[i];

                row = 2;
                foreach (var h in historial)
                {
                    wsHistorial.Cell(row, 1).Value = h.NumeroParte;
                    wsHistorial.Cell(row, 2).Value = h.Cantidad;
                    wsHistorial.Cell(row, 3).Value = h.Fecha;
                    wsHistorial.Cell(row, 4).Value = h.Tipo;
                    wsHistorial.Cell(row, 5).Value = h.Usuario;
                    row++;
                }
                wsHistorial.Columns().AdjustToContents();

                workbook.SaveAs(dialog.FileName);
                return "OK:" + dialog.FileName;
            }
            catch (Exception ex)
            {
                return "ERROR:" + ex.Message;
            }
        }
    }
}
