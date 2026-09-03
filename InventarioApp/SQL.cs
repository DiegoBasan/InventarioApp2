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

        private static bool ColumnaExiste(SqliteConnection db, string tabla, string columna)
        {
            var columnas = db.Query($"PRAGMA table_info({tabla})");
            foreach (var col in columnas)
            {
                if (string.Equals((string)col.name, columna, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool TablaExiste(SqliteConnection db, string tabla)
        {
            return db.QueryFirstOrDefault<string>(
                "SELECT name FROM sqlite_master WHERE type='table' AND name = @Nombre", new { Nombre = tabla }) != null;
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
                    CREATE TABLE IF NOT EXISTS Lugares (Id INTEGER PRIMARY KEY AUTOINCREMENT, Nombre TEXT UNIQUE NOT NULL);
                    CREATE TABLE IF NOT EXISTS Categorias (Id INTEGER PRIMARY KEY AUTOINCREMENT, Nombre TEXT UNIQUE NOT NULL);
                    PRAGMA foreign_keys = ON;
                ");

                // Columnas nuevas: Categoria (en Repuestos) y Lugar (en Asignaciones).
                if (!ColumnaExiste(db, "Repuestos", "Categoria"))
                    db.Execute("ALTER TABLE Repuestos ADD COLUMN Categoria TEXT");

                if (!ColumnaExiste(db, "Asignaciones", "Lugar"))
                    db.Execute("ALTER TABLE Asignaciones ADD COLUMN Lugar TEXT");

                // Semilla de gavetas iniciales.
                if (db.QueryFirstOrDefault<int>("SELECT COUNT(*) FROM Lugares") == 0)
                {
                    db.Execute("INSERT INTO Lugares (Nombre) VALUES ('Gaveta1'), ('Gaveta ELK')");
                }

                // Semilla de categorías iniciales.
                if (db.QueryFirstOrDefault<int>("SELECT COUNT(*) FROM Categorias") == 0)
                {
                    db.Execute(@"INSERT INTO Categorias (Nombre) VALUES
                        ('PC'), ('KEYENCE'), ('TORQUE'), ('KASALIS'), ('FIXTURA'), ('CABLES'), ('SENSOR')");
                }
            }

            CrearTablaUsuarios();
        }

        // ===== LUGARES (GAVETAS) =====

        public List<string> ObtenerLugares()
        {
            using (var db = new SqliteConnection(connStr))
            {
                return db.Query<string>("SELECT Nombre FROM Lugares ORDER BY Nombre").ToList();
            }
        }

        public void CrearLugar(string nombre)
        {
            if (string.IsNullOrWhiteSpace(nombre)) throw new Exception("El nombre de la gaveta no puede estar vacío.");

            using (var db = new SqliteConnection(connStr))
            {
                db.Execute("INSERT OR IGNORE INTO Lugares (Nombre) VALUES (@Nombre)", new { Nombre = nombre.Trim() });
            }
        }

        public void EliminarLugar(string nombre)
        {
            using (var db = new SqliteConnection(connStr))
            {
                db.Execute("DELETE FROM Lugares WHERE Nombre = @Nombre", new { Nombre = nombre });
            }
        }

        // ===== CATEGORÍAS =====

        public List<string> ObtenerCategorias()
        {
            using (var db = new SqliteConnection(connStr))
            {
                return db.Query<string>("SELECT Nombre FROM Categorias ORDER BY Nombre").ToList();
            }
        }

        public void CrearCategoria(string nombre)
        {
            if (string.IsNullOrWhiteSpace(nombre)) throw new Exception("El nombre de la categoría no puede estar vacío.");

            using (var db = new SqliteConnection(connStr))
            {
                db.Execute("INSERT OR IGNORE INTO Categorias (Nombre) VALUES (@Nombre)", new { Nombre = nombre.Trim() });
            }
        }

        public void EliminarCategoria(string nombre)
        {
            using (var db = new SqliteConnection(connStr))
            {
                db.Execute("DELETE FROM Categorias WHERE Nombre = @Nombre", new { Nombre = nombre });
            }
        }

        // ===== MATERIALES =====

        public List<Material> ObtenerTodosLosMateriales()
        {
            using (var db = new SqliteConnection(connStr))
            {
                string sql = @"
                    SELECT a.Id, r.NumeroParte, r.Descripcion, r.Categoria, m.Nombre as Marca, a.Equipo, a.Proyecto, a.Lugar, a.Cantidad, a.Cambio
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
                                @"INSERT INTO Repuestos (NumeroParte, Descripcion, MarcaId, Categoria) VALUES (@NP, @Desc, @Mid, @Cat) RETURNING Id;",
                                new { NP = infoBase.NumeroParte, Desc = infoBase.Descripcion, Mid = marcaId, Cat = infoBase.Categoria }, transaccion);
                        }
                        else
                        {
                            db.Execute(@"UPDATE Repuestos SET Descripcion = @Desc, MarcaId = @Mid, Categoria = @Cat WHERE Id = @Id",
                                new { Desc = infoBase.Descripcion, Mid = marcaId, Cat = infoBase.Categoria, Id = repuestoId }, transaccion);
                        }

                        string cambioText = $"{usuario}   {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

                        db.Execute(@"INSERT INTO Asignaciones (RepuestoId, Equipo, Proyecto, Lugar, Cantidad, Cambio) VALUES (@Rid, @Eq, @Pro, @Lug, @Cant, @Cam)",
                            new { Rid = repuestoId, Eq = listaEquipos, Pro = listaProyectos, Lug = infoBase.Lugar, Cant = infoBase.Cantidad, Cam = cambioText }, transaccion);

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

        public void ActualizarMaterial(int id, string descripcion, int cantidad, string proyecto, string equipo, string marca, string categoria, string lugar, string usuario)
        {
            try
            {
                using (var connection = new SqliteConnection(connStr))
                {
                    connection.Open();
                    using (var trans = connection.BeginTransaction())
                    {
                        var asignacion = connection.QueryFirstOrDefault<dynamic>("SELECT RepuestoId FROM Asignaciones WHERE Id = @Id", new { Id = id }, trans);
                        if (asignacion == null) throw new Exception("Registro no encontrado.");

                        int repuestoId = (int)asignacion.RepuestoId;

                        connection.Execute(@"
                            UPDATE Asignaciones
                            SET Equipo = @equipo,
                                Proyecto = @proyecto,
                                Lugar = @lugar,
                                Cantidad = @cantidad,
                                Cambio = @cambio
                            WHERE Id = @id",
                            new
                            {
                                id = id,
                                equipo = equipo ?? "",
                                proyecto = proyecto ?? "",
                                lugar = lugar ?? "",
                                cantidad = cantidad,
                                cambio = $"{usuario}   {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
                            }, trans);

                        connection.Execute(@"UPDATE Repuestos SET Descripcion = @descripcion, Categoria = @categoria WHERE Id = @id",
                            new { descripcion = descripcion, categoria = categoria, id = repuestoId }, trans);

                        connection.Execute(@"
                            INSERT INTO Movimientos (RepuestoId, Cantidad, Fecha, Usuario, Tipo)
                            VALUES (@RepuestoId, @cantidad, datetime('now'), @usuario, 'Edicion')",
                            new { RepuestoId = repuestoId, cantidad = cantidad, usuario = usuario }, trans);

                        trans.Commit();
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error al actualizar material: {ex.Message}");
            }
        }

        // ===== USUARIOS =====

        public void CrearTablaUsuarios()
        {
            try
            {
                using (var connection = new SqliteConnection(connStr))
                {
                    connection.Open();

                    // Migración: instalaciones previas crearon una tabla Usuarios con el esquema
                    // antiguo (Usuario, Password). Si existe y no tiene la columna Nombre, se
                    // renombra para no perder datos y se crea la tabla con el esquema correcto.
                    if (TablaExiste(connection, "Usuarios") && !ColumnaExiste(connection, "Usuarios", "Nombre"))
                    {
                        connection.Execute("ALTER TABLE Usuarios RENAME TO Usuarios_Legacy");
                    }

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

        public void ActualizarUsuario(int id, string rol, bool activo)
        {
            try
            {
                using (var connection = new SqliteConnection(connStr))
                {
                    connection.Open();
                    connection.Execute("UPDATE Usuarios SET Rol = @rol, Activo = @activo WHERE Id = @id",
                        new { rol = rol, activo = activo, id = id });
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error actualizando usuario: {ex.Message}");
            }
        }

        public void CambiarPasswordUsuario(int id, string nuevaContrasena)
        {
            if (string.IsNullOrWhiteSpace(nuevaContrasena)) throw new Exception("La contraseña no puede estar vacía.");

            try
            {
                using (var connection = new SqliteConnection(connStr))
                {
                    connection.Open();
                    connection.Execute("UPDATE Usuarios SET Contrasena = @contrasena WHERE Id = @id",
                        new { contrasena = nuevaContrasena, id = id });
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error cambiando contraseña: {ex.Message}");
            }
        }
    }

    public class Material
    {
        public int Id { get; set; }
        public string NumeroParte { get; set; }
        public int Cantidad { get; set; }
        public string Descripcion { get; set; }
        public string Categoria { get; set; }
        public string Proyecto { get; set; }
        public string Equipo { get; set; }
        public string Lugar { get; set; }
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
