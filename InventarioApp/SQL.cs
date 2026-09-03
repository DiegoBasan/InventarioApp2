using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace InventarioApp
{
    public class SQL
    {
        private readonly string connStr;

        public SQL()
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .Build();

            connStr = config.GetConnectionString("InventarioDb")
                ?? throw new InvalidOperationException("No se encontró la cadena de conexión 'InventarioDb' en appsettings.json.");

            CrearTabla();
        }

        public void CrearTabla()
        {
            using (var db = new SqliteConnection(connStr))
            {
                db.Execute(@"
                    CREATE TABLE IF NOT EXISTS Marcas (Id INTEGER PRIMARY KEY AUTOINCREMENT, Nombre TEXT UNIQUE);
                    CREATE TABLE IF NOT EXISTS Repuestos (Id INTEGER PRIMARY KEY AUTOINCREMENT, NumeroParte TEXT UNIQUE, Descripcion TEXT, MarcaId INTEGER);
                    CREATE TABLE IF NOT EXISTS Equipos (Id INTEGER PRIMARY KEY AUTOINCREMENT, Nombre TEXT UNIQUE);
                    CREATE TABLE IF NOT EXISTS Proyectos (Id INTEGER PRIMARY KEY AUTOINCREMENT, Nombre TEXT UNIQUE);
                    CREATE TABLE IF NOT EXISTS Asignaciones (Id INTEGER PRIMARY KEY AUTOINCREMENT, RepuestoId INTEGER, Equipo TEXT, Proyecto TEXT, Cantidad INTEGER, Cambio TEXT);
                    CREATE TABLE IF NOT EXISTS Movimientos (Id INTEGER PRIMARY KEY AUTOINCREMENT, RepuestoId INTEGER NOT NULL, Cantidad INTEGER NOT NULL, Fecha DATETIME DEFAULT CURRENT_TIMESTAMP, Usuario TEXT, Tipo TEXT DEFAULT 'Retiro');
                    CREATE TABLE IF NOT EXISTS ELK (Id INTEGER PRIMARY KEY AUTOINCREMENT, Cantidad INTEGER NOT NULL, [Numero de Parte] TEXT UNIQUE NOT NULL, Marca TEXT NOT NULL, Descripcion TEXT NOT NULL, Comentarios TEXT, Equipos TEXT NOT NULL, Cambio TEXT NOT NULL);
                    CREATE TABLE IF NOT EXISTS SCORPION (Id INTEGER PRIMARY KEY AUTOINCREMENT, Cantidad INTEGER NOT NULL, [Numero de Parte] TEXT UNIQUE NOT NULL, Marca TEXT NOT NULL, Descripcion TEXT NOT NULL, Comentarios TEXT, Equipos TEXT NOT NULL, Cambio TEXT NOT NULL);
                    CREATE TABLE IF NOT EXISTS MOOSE (Id INTEGER PRIMARY KEY AUTOINCREMENT, Cantidad INTEGER NOT NULL, [Numero de Parte] TEXT UNIQUE NOT NULL, Marca TEXT NOT NULL, Descripcion TEXT NOT NULL, Comentarios TEXT, Equipos TEXT NOT NULL, Cambio TEXT NOT NULL);
                    CREATE TABLE IF NOT EXISTS GECKO (Id INTEGER PRIMARY KEY AUTOINCREMENT, Cantidad INTEGER NOT NULL, [Numero de Parte] TEXT UNIQUE NOT NULL, Marca TEXT, Descripcion TEXT NOT NULL, Comentarios TEXT NOT NULL, Equipos TEXT NOT NULL, Cambio TEXT NOT NULL);
                    PRAGMA foreign_keys = ON;
                ");
            }

            CrearTablaUsuarios();
        }

        public List<Material> ObtenerTodosLosMateriales()
        {
            using (var db = new SqliteConnection(connStr))
            {
                string sql = @"
                    SELECT a.Id, r.NumeroParte, r.Descripcion, m.Nombre as Marca, a.Equipo, a.Proyecto, a.Cantidad, a.Cambio 
                    FROM Asignaciones a
                    JOIN Repuestos r ON a.RepuestoId = r.Id
                    LEFT JOIN Marcas m ON r.MarcaId = m.Id
                    ORDER BY r.NumeroParte";
                return db.Query<Material>(sql).ToList();
            }
        }
        public void GuardarMaterialMultiplo(Material infoBase, string listaEquipos, string listaProyectos, string usuario)
        {
            using (var db = new SqliteConnection(connStr))
            {
                db.Open();
                using (var transaccion = db.BeginTransaction())
                {
                    try
                    {
                        int? marcaId = null;
                        if (!string.IsNullOrEmpty(infoBase.Marca))
                        {
                            int idEncontrado = db.QueryFirstOrDefault<int>("SELECT Id FROM Marcas WHERE Nombre = @Nombre", new { Nombre = infoBase.Marca }, transaccion);
                            marcaId = (idEncontrado == 0)
                                ? db.QuerySingle<int>("INSERT INTO Marcas (Nombre) VALUES (@Nombre) RETURNING Id;", new { Nombre = infoBase.Marca }, transaccion)
                                : idEncontrado;
                        }

                        int repuestoId = db.QueryFirstOrDefault<int>("SELECT Id FROM Repuestos WHERE NumeroParte = @NP", new { NP = infoBase.NumeroParte }, transaccion);
                        if (repuestoId == 0)
                        {
                            repuestoId = db.QuerySingle<int>(
                                @"INSERT INTO Repuestos (NumeroParte, Descripcion, MarcaId) VALUES (@NP, @Desc, @Mid) RETURNING Id;",
                                new { NP = infoBase.NumeroParte, Desc = infoBase.Descripcion, Mid = marcaId }, transaccion);
                        }

                        string cambioText = $"{usuario}   {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

                        db.Execute(@"INSERT INTO Asignaciones (RepuestoId, Equipo, Proyecto, Cantidad, Cambio) VALUES (@Rid, @Eq, @Pro, @Cant, @Cam)",
                            new { Rid = repuestoId, Eq = listaEquipos, Pro = listaProyectos, Cant = infoBase.Cantidad, Cam = cambioText }, transaccion);

                        db.Execute(@"INSERT INTO Movimientos (RepuestoId, Cantidad, Fecha, Usuario, Tipo) VALUES (@Rid, @Cant, datetime('now'), @Usr, 'Ingreso')",
                            new { Rid = repuestoId, Cant = infoBase.Cantidad, Usr = usuario }, transaccion);

                        transaccion.Commit();
                    }
                    catch
                    {
                        transaccion.Rollback();
                        throw;
                    }
                }
            }
        }

        public void RegistrarRetiro(int idAsignacion, int cantidad, string usuario)
        {
            using (var db = new SqliteConnection(connStr))
            {
                db.Open();
                using (var trans = db.BeginTransaction())
                {
                    var asignacion = db.QueryFirstOrDefault<dynamic>("SELECT RepuestoId, Cantidad FROM Asignaciones WHERE Id = @Id", new { Id = idAsignacion }, trans);
                    if (asignacion == null) throw new Exception("Registro no encontrado.");

                    if (cantidad > (int)asignacion.Cantidad) throw new Exception($"Stock insuficiente. Restantes: {asignacion.Cantidad}");

                    db.Execute("UPDATE Asignaciones SET Cantidad = Cantidad - @Cant WHERE Id = @Id", new { Cant = cantidad, Id = idAsignacion }, trans);
                    db.Execute("INSERT INTO Movimientos (RepuestoId, Cantidad, Fecha, Usuario, Tipo) VALUES (@Rid, @Cant, datetime('now'), @Usr, 'Retiro')",
                        new { Rid = (int)asignacion.RepuestoId, Cant = cantidad, Usr = usuario }, trans);

                    trans.Commit();
                }
            }
        }

        public void EliminarMaterial(int id, string usuario)
        {
            using (var db = new SqliteConnection(connStr))
            {
                db.Open();
                using (var trans = db.BeginTransaction())
                {
                    var asignacion = db.QueryFirstOrDefault<dynamic>("SELECT RepuestoId, Cantidad FROM Asignaciones WHERE Id = @Id", new { Id = id }, trans);
                    if (asignacion != null)
                    {
                        db.Execute("DELETE FROM Asignaciones WHERE Id = @Id", new { Id = id }, trans);
                        db.Execute("INSERT INTO Movimientos (RepuestoId, Cantidad, Fecha, Usuario, Tipo) VALUES (@Rid, @Cant, datetime('now'), @Usr, 'Borrado')",
                            new { Rid = (int)asignacion.RepuestoId, Cant = (int)asignacion.Cantidad, Usr = usuario }, trans);
                    }
                    trans.Commit();
                }
            }
        }

        public List<HistorialMovimiento> ObtenerHistorialMovimientos()
        {
            using (var db = new SqliteConnection(connStr))
            {
                return db.Query<HistorialMovimiento>(@"
                    SELECT r.NumeroParte, m.Cantidad, m.Fecha, m.Tipo, m.Usuario
                    FROM Movimientos m
                    JOIN Repuestos r ON m.RepuestoId = r.Id
                    ORDER BY m.Fecha DESC").ToList();
            }
        }

        public Dictionary<string, int> ObtenerEstadisticas()
        {
            using (var db = new SqliteConnection(connStr))
            {
                return db.Query<KeyValuePair<string, int>>(@"
                    SELECT r.Descripcion AS Key, SUM(m.Cantidad) AS Value
                    FROM Movimientos m JOIN Repuestos r ON m.RepuestoId = r.Id
                    GROUP BY r.Descripcion ORDER BY Value DESC LIMIT 5")
                    .ToDictionary(x => x.Key, x => x.Value);
            }
        }
        // ===== EDITAR Y CREAR USUARIOS =====

        public void ActualizarMaterial(int id, string descripcion, int cantidad, string proyecto, string equipo, string marca, string usuario)
        {
            try
            {
                using (var connection = new SqliteConnection(connStr))
                {
                    connection.Open();

                    string query = @"
                UPDATE Asignaciones 
                SET Equipo = @equipo, 
                    Proyecto = @proyecto, 
                    Cantidad = @cantidad,
                    Cambio = @cambio
                WHERE Id = @id
            ";

                    connection.Execute(query, new
                    {
                        id = id,
                        equipo = equipo ?? "",
                        proyecto = proyecto ?? "",
                        cantidad = cantidad,
                        cambio = $"{usuario}   {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
                    });

                    connection.Execute(@"
                INSERT INTO Movimientos (RepuestoId, Cantidad, Fecha, Usuario, Tipo) 
                SELECT RepuestoId, @cantidad, datetime('now'), @usuario, 'Edicion'
                FROM Asignaciones WHERE Id = @id
            ", new { cantidad = cantidad, usuario = usuario, id = id });
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error al actualizar material: {ex.Message}");
            }
        }

        public void CrearTablaUsuarios()
        {
            try
            {
                using (var connection = new SqliteConnection(connStr))
                {
                    connection.Open();

                    string createQuery = @"
                CREATE TABLE IF NOT EXISTS Usuarios (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Nombre TEXT NOT NULL UNIQUE,
                    Contrasena TEXT NOT NULL,
                    Rol TEXT NOT NULL,
                    Activo BOOLEAN DEFAULT 1,
                    FechaCreacion DATETIME DEFAULT CURRENT_TIMESTAMP
                )
            ";

                    connection.Execute(createQuery);

                    string checkQuery = "SELECT COUNT(*) as Count FROM Usuarios WHERE Nombre = 'Admin'";
                    var checkResult = connection.QueryFirstOrDefault<dynamic>(checkQuery);

                    if (checkResult == null || checkResult.Count == 0)
                    {
                        string insertQuery = @"
                    INSERT INTO Usuarios (Nombre, Contrasena, Rol)
                    VALUES ('Admin', 'admin123', 'Ingeniero')
                ";
                        connection.Execute(insertQuery);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error creando tabla Usuarios: {ex.Message}");
            }
        }

        public bool ValidarUsuario(string nombre, string contrasena)
        {
            try
            {
                using (var connection = new SqliteConnection(connStr))
                {
                    connection.Open();
                    string query = "SELECT COUNT(*) as Count FROM Usuarios WHERE Nombre = @nombre AND Contrasena = @contrasena AND Activo = 1";

                    var result = connection.QueryFirstOrDefault<dynamic>(query, new
                    {
                        nombre = nombre,
                        contrasena = contrasena
                    });

                    return result != null && result.Count > 0;
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error validando usuario: {ex.Message}");
            }
        }

        public dynamic ObtenerUsuarioPorNombre(string nombre)
        {
            try
            {
                using (var connection = new SqliteConnection(connStr))
                {
                    connection.Open();
                    string query = "SELECT Id, Nombre, Rol FROM Usuarios WHERE Nombre = @nombre AND Activo = 1";
                    var result = connection.QueryFirstOrDefault<dynamic>(query, new { nombre = nombre });
                    return result;
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error obteniendo usuario: {ex.Message}");
            }
        }

        public void CrearUsuario(string nombre, string contrasena, string rol)
        {
            try
            {
                using (var connection = new SqliteConnection(connStr))
                {
                    connection.Open();

                    string query = @"
                INSERT INTO Usuarios (Nombre, Contrasena, Rol)
                VALUES (@nombre, @contrasena, @rol)
            ";

                    connection.Execute(query, new
                    {
                        nombre = nombre,
                        contrasena = contrasena,
                        rol = rol
                    });
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error creando usuario: {ex.Message}");
            }
        }

        public List<dynamic> ObtenerTodosLosUsuarios()
        {
            try
            {
                using (var connection = new SqliteConnection(connStr))
                {
                    connection.Open();
                    string query = "SELECT Id, Nombre, Rol, Activo FROM Usuarios ORDER BY Rol DESC";
                    var result = connection.Query(query).ToList();
                    return result.Cast<dynamic>().ToList();
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error obteniendo usuarios: {ex.Message}");
            }
        }
    }

    public class Material
    {
        public int Id { get; set; }
        public string NumeroParte { get; set; }
        public int Cantidad { get; set; }
        public string Descripcion { get; set; }
        public string Proyecto { get; set; }
        public string Equipo { get; set; }
        public string Cambio { get; set; }
        public string Marca { get; set; }
    }

    public class HistorialMovimiento
    {
        public string NumeroParte { get; set; }
        public int Cantidad { get; set; }
        public string Fecha { get; set; }
        public string Tipo { get; set; }
        public string Usuario { get; set; }
    }
}