using System;
using System.Text.Json;

namespace InventarioApp
{
    public class Bridge
    {
        private SQL SQL;

        public Bridge()
        {
            SQL = new SQL();
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

        public string AgregarMaterial(string numeroParte, string descripcion, int cantidad, string proyecto, string equipo, string marca, string usuario)
        {
            try
            {
                var material = new Material
                {
                    NumeroParte = numeroParte,
                    Descripcion = descripcion,
                    Cantidad = cantidad,
                    Marca = marca
                };
                SQL.GuardarMaterialMultiplo(material, equipo, proyecto, usuario);
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
                SQL.EliminarMaterial(id, usuario);
                return "OK";
            }
            catch (Exception ex)
            {
                return "ERROR:" + ex.Message;
            }
        }

        public string ActualizarMaterial(int id, string descripcion, int cantidad, string proyecto, string equipo, string marca, string usuario)
        {
            try
            {
                SQL.ActualizarMaterial(id, descripcion, cantidad, proyecto, equipo, marca, usuario);
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
    }
}
