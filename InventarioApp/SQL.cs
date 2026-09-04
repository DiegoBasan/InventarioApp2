using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace InventarioApp
{
    public class SQL
    {
        private readonly string connStr;
        private const int StockMinimoDefault = 5;

        public SQL()
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(AppPaths.DataDir)
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
                    CREATE TABLE IF NOT EXISTS Contadores (Id INTEGER PRIMARY KEY, Siguiente INTEGER NOT NULL);
                    CREATE TABLE IF NOT EXISTS Config (Clave TEXT PRIMARY KEY, Valor TEXT);
                    CREATE TABLE IF NOT EXISTS AccesosLog (Id INTEGER PRIMARY KEY AUTOINCREMENT, Usuario TEXT NOT NULL, Fecha DATETIME DEFAULT CURRENT_TIMESTAMP);
                    PRAGMA foreign_keys = ON;
                ");

                // Para poder deshacer un retiro de forma confiable hace falta saber a qué
                // Asignación exacta pertenece cada movimiento (un mismo Repuesto puede tener
                // varias filas de Asignaciones, una por proyecto/gaveta).
                if (!ColumnaExiste(db, "Movimientos", "AsignacionId"))
                    db.Execute("ALTER TABLE Movimientos ADD COLUMN AsignacionId INTEGER");

                if (db.QueryFirstOrDefault<int>("SELECT COUNT(*) FROM Contadores WHERE Id = 1") == 0)
                {
                    db.Execute("INSERT INTO Contadores (Id, Siguiente) VALUES (1, 1)");
                }

                // Columnas nuevas: Categoria (en Repuestos) y Lugar (en Asignaciones).
                if (!ColumnaExiste(db, "Repuestos", "Categoria"))
                    db.Execute("ALTER TABLE Repuestos ADD COLUMN Categoria TEXT");

                if (!ColumnaExiste(db, "Asignaciones", "Lugar"))
                    db.Execute("ALTER TABLE Asignaciones ADD COLUMN Lugar TEXT");

                // Alerta de stock bajo por artículo: no todos los materiales manejan la misma
                // cantidad "normal" (una cámara Keyence puede ser 1 de sobra, un tornillo no).
                if (!ColumnaExiste(db, "Repuestos", "AlertaStockActiva"))
                    db.Execute("ALTER TABLE Repuestos ADD COLUMN AlertaStockActiva INTEGER NOT NULL DEFAULT 0");

                if (!ColumnaExiste(db, "Repuestos", "StockMinimoPersonalizado"))
                    db.Execute("ALTER TABLE Repuestos ADD COLUMN StockMinimoPersonalizado INTEGER");

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
            RealizarRespaldo();
        }

        // ===== NÚMERO DE PARTE AUTOMÁTICO =====
        // Formato: AUT-<Categoria abreviada>-<Proyecto abreviado>-#### (ej. AUT-SEN-GEC-0001)
        // "MUL" se usa cuando el material aplica a más de un proyecto, "GEN" si no se eligió ninguno.
        private static string AbreviarCategoria(string categoria)
        {
            if (string.IsNullOrWhiteSpace(categoria)) return "GEN";
            var limpio = categoria.Trim().ToUpperInvariant();
            return limpio.Length <= 3 ? limpio : limpio.Substring(0, 3);
        }

        private static string AbreviarProyectos(string proyectos)
        {
            var lista = (proyectos ?? "").Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
            if (lista.Count == 0) return "GEN";
            if (lista.Count > 1) return "MUL";
            var unico = lista[0].ToUpperInvariant();
            return unico.Length <= 3 ? unico : unico.Substring(0, 3);
        }

        // consumir:false = solo vista previa (no gasta el siguiente número).
        // consumir:true  = reserva el número atómicamente (usado al guardar).
        public string GenerarNumeroParte(string categoria, string proyectos, bool consumir)
        {
            using (var db = new SqliteConnection(connStr))
            {
                int siguiente;
                if (consumir)
                {
                    db.Open();
                    using (var trans = db.BeginTransaction())
                    {
                        siguiente = db.QuerySingle<int>("SELECT Siguiente FROM Contadores WHERE Id = 1", transaction: trans);
                        db.Execute("UPDATE Contadores SET Siguiente = Siguiente + 1 WHERE Id = 1", transaction: trans);
                        trans.Commit();
                    }
                }
                else
                {
                    siguiente = db.QuerySingle<int>("SELECT Siguiente FROM Contadores WHERE Id = 1");
                }

                return $"AUT-{AbreviarCategoria(categoria)}-{AbreviarProyectos(proyectos)}-{siguiente:0000}";
            }
        }

        // ===== EDICIÓN EN LOTE (corrección masiva de datos ya ingresados) =====
        public void ActualizarMaterialesLote(List<int> asignacionIds, string proyecto, string categoria, string lugar, string usuario)
        {
            using (var db = new SqliteConnection(connStr))
            {
                db.Open();
                using (var trans = db.BeginTransaction())
                {
                    foreach (var id in asignacionIds)
                    {
                        var asignacion = db.QueryFirstOrDefault<dynamic>("SELECT RepuestoId, Cantidad FROM Asignaciones WHERE Id = @Id", new { Id = id }, trans);
                        if (asignacion == null) continue;

                        if (!string.IsNullOrEmpty(proyecto) || !string.IsNullOrEmpty(lugar))
                        {
                            db.Execute(@"UPDATE Asignaciones SET
                                    Proyecto = COALESCE(NULLIF(@proyecto, ''), Proyecto),
                                    Lugar = COALESCE(NULLIF(@lugar, ''), Lugar)
                                WHERE Id = @id",
                                new { proyecto = proyecto ?? "", lugar = lugar ?? "", id = id }, trans);
                        }

                        if (!string.IsNullOrEmpty(categoria))
                        {
                            db.Execute("UPDATE Repuestos SET Categoria = @categoria WHERE Id = @rid",
                                new { categoria = categoria, rid = (int)asignacion.RepuestoId }, trans);
                        }

                        db.Execute(@"INSERT INTO Movimientos (RepuestoId, Cantidad, Fecha, Usuario, Tipo)
                            VALUES (@rid, @cant, datetime('now'), @usuario, 'Edicion')",
                            new { rid = (int)asignacion.RepuestoId, cant = (int)asignacion.Cantidad, usuario = usuario }, trans);
                    }
                    trans.Commit();
                }
            }
        }

        // ===== STOCK MÍNIMO (alertas) =====
        public int ObtenerStockMinimo()
        {
            using (var db = new SqliteConnection(connStr))
            {
                var valor = db.QueryFirstOrDefault<string>("SELECT Valor FROM Config WHERE Clave = 'StockMinimo'");
                return int.TryParse(valor, out var n) ? n : StockMinimoDefault;
            }
        }

        public void GuardarStockMinimo(int valor)
        {
            using (var db = new SqliteConnection(connStr))
            {
                db.Execute("INSERT INTO Config (Clave, Valor) VALUES ('StockMinimo', @v) ON CONFLICT(Clave) DO UPDATE SET Valor = @v",
                    new { v = valor.ToString() });
            }
        }

        // ===== IMPRESORA DE ETIQUETAS =====
        public string? ObtenerImpresoraEtiquetas()
        {
            using (var db = new SqliteConnection(connStr))
            {
                return db.QueryFirstOrDefault<string>("SELECT Valor FROM Config WHERE Clave = 'ImpresoraEtiquetas'");
            }
        }

        public void GuardarImpresoraEtiquetas(string nombreImpresora)
        {
            using (var db = new SqliteConnection(connStr))
            {
                db.Execute("INSERT INTO Config (Clave, Valor) VALUES ('ImpresoraEtiquetas', @v) ON CONFLICT(Clave) DO UPDATE SET Valor = @v",
                    new { v = nombreImpresora });
            }
        }

        // ===== RESPALDO AUTOMÁTICO =====
        // Copia el archivo .sqlite (con fecha) una vez al día a una carpeta "Backups" junto a la
        // base de datos, y borra copias de más de 30 días. Nunca debe tumbar el arranque de la app.
        public void RealizarRespaldo()
        {
            try
            {
                var builder = new SqliteConnectionStringBuilder(connStr);
                string dbPath = builder.DataSource;
                if (string.IsNullOrWhiteSpace(dbPath) || !File.Exists(dbPath)) return;

                string carpetaBackups = Path.Combine(Path.GetDirectoryName(dbPath) ?? ".", "Backups");
                Directory.CreateDirectory(carpetaBackups);

                string destino = Path.Combine(carpetaBackups, $"{Path.GetFileNameWithoutExtension(dbPath)}_{DateTime.Now:yyyy-MM-dd}.sqlite");
                if (!File.Exists(destino))
                {
                    File.Copy(dbPath, destino);
                }

                foreach (var archivo in Directory.GetFiles(carpetaBackups, "*.sqlite"))
                {
                    if (File.GetCreationTime(archivo) < DateTime.Now.AddDays(-30))
                        File.Delete(archivo);
                }
            }
            catch
            {
                // El respaldo es una conveniencia; un fallo aquí (permisos, red caída, etc.)
                // nunca debe impedir que la aplicación arranque.
            }
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
                    SELECT a.Id, r.NumeroParte, r.Descripcion, r.Categoria, m.Nombre as Marca, a.Equipo, a.Proyecto, a.Lugar, a.Cantidad, a.Cambio,
                           r.AlertaStockActiva, r.StockMinimoPersonalizado
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
                                @"INSERT INTO Repuestos (NumeroParte, Descripcion, MarcaId, Categoria, AlertaStockActiva, StockMinimoPersonalizado)
                                  VALUES (@NP, @Desc, @Mid, @Cat, @Alerta, @StockMin) RETURNING Id;",
                                new { NP = infoBase.NumeroParte, Desc = infoBase.Descripcion, Mid = marcaId, Cat = infoBase.Categoria, Alerta = infoBase.AlertaStockActiva, StockMin = infoBase.StockMinimoPersonalizado }, transaccion);
                        }
                        else
                        {
                            db.Execute(@"UPDATE Repuestos SET Descripcion = @Desc, MarcaId = @Mid, Categoria = @Cat,
                                    AlertaStockActiva = @Alerta, StockMinimoPersonalizado = @StockMin
                                WHERE Id = @Id",
                                new { Desc = infoBase.Descripcion, Mid = marcaId, Cat = infoBase.Categoria, Alerta = infoBase.AlertaStockActiva, StockMin = infoBase.StockMinimoPersonalizado, Id = repuestoId }, transaccion);
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
                    db.Execute("INSERT INTO Movimientos (RepuestoId, AsignacionId, Cantidad, Fecha, Usuario, Tipo) VALUES (@Rid, @Aid, @Cant, datetime('now'), @Usr, 'Retiro')",
                        new { Rid = (int)asignacion.RepuestoId, Aid = idAsignacion, Cant = cantidad, Usr = usuario }, trans);

                    trans.Commit();
                }
            }
        }

        // Deshace el retiro más reciente hecho por ese mismo usuario en los últimos 5 minutos.
        // No borra el movimiento original (se conserva el rastro de auditoría); en vez de eso
        // regresa la cantidad y registra un movimiento tipo 'Deshacer'.
        public string DeshacerUltimoRetiro(string usuario)
        {
            using (var db = new SqliteConnection(connStr))
            {
                db.Open();
                using (var trans = db.BeginTransaction())
                {
                    var ultimo = db.QueryFirstOrDefault<dynamic>(@"
                        SELECT m.Id, m.RepuestoId, m.AsignacionId, m.Cantidad, r.NumeroParte
                        FROM Movimientos m
                        JOIN Repuestos r ON r.Id = m.RepuestoId
                        WHERE m.Tipo = 'Retiro' AND m.Usuario = @usuario AND m.AsignacionId IS NOT NULL
                          AND m.Fecha >= datetime('now', '-5 minutes')
                        ORDER BY m.Fecha DESC LIMIT 1",
                        new { usuario = usuario }, trans);

                    if (ultimo == null)
                        throw new Exception("No hay un retiro tuyo en los últimos 5 minutos para deshacer.");

                    int asignacionId = (int)ultimo.AsignacionId;
                    int cantidad = (int)ultimo.Cantidad;

                    db.Execute("UPDATE Asignaciones SET Cantidad = Cantidad + @Cant WHERE Id = @Id", new { Cant = cantidad, Id = asignacionId }, trans);
                    db.Execute(@"INSERT INTO Movimientos (RepuestoId, AsignacionId, Cantidad, Fecha, Usuario, Tipo)
                        VALUES (@Rid, @Aid, @Cant, datetime('now'), @Usr, 'Deshacer')",
                        new { Rid = (int)ultimo.RepuestoId, Aid = asignacionId, Cant = cantidad, Usr = usuario }, trans);

                    trans.Commit();
                    return (string)ultimo.NumeroParte;
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

        // Historial completo de un solo material (todas sus asignaciones), para verlo desde su fila en Inicio.
        public List<HistorialMovimiento> ObtenerHistorialPorRepuesto(string numeroParte)
        {
            using (var db = new SqliteConnection(connStr))
            {
                return db.Query<HistorialMovimiento>(@"
                    SELECT r.NumeroParte, m.Cantidad, m.Fecha, m.Tipo, m.Usuario
                    FROM Movimientos m
                    JOIN Repuestos r ON m.RepuestoId = r.Id
                    WHERE r.NumeroParte = @NumeroParte
                    ORDER BY m.Fecha DESC", new { NumeroParte = numeroParte }).ToList();
            }
        }

        // ===== ACCESOS (auditoría de inicios de sesión) =====
        public void RegistrarLogin(string usuario)
        {
            using (var db = new SqliteConnection(connStr))
            {
                db.Execute("INSERT INTO AccesosLog (Usuario, Fecha) VALUES (@Usuario, datetime('now'))", new { Usuario = usuario });
            }
        }

        public List<dynamic> ObtenerHistorialAccesos()
        {
            using (var db = new SqliteConnection(connStr))
            {
                return db.Query("SELECT Usuario, Fecha FROM AccesosLog ORDER BY Fecha DESC LIMIT 50").Cast<dynamic>().ToList();
            }
        }

        // ===== RESTAURAR DESDE RESPALDO =====
        public List<string> ListarBackups()
        {
            var builder = new SqliteConnectionStringBuilder(connStr);
            string carpetaBackups = Path.Combine(Path.GetDirectoryName(builder.DataSource) ?? ".", "Backups");
            if (!Directory.Exists(carpetaBackups)) return new List<string>();

            return Directory.GetFiles(carpetaBackups, "*.sqlite")
                .Select(Path.GetFileName)
                .Where(n => n != null && !n.Contains("_antes_de_restaurar_"))
                .OrderByDescending(n => n)
                .ToList()!;
        }

        public void RestaurarBackup(string nombreArchivo)
        {
            var builder = new SqliteConnectionStringBuilder(connStr);
            string dbPath = builder.DataSource;
            string carpetaBackups = Path.Combine(Path.GetDirectoryName(dbPath) ?? ".", "Backups");
            string origen = Path.Combine(carpetaBackups, nombreArchivo);

            if (!File.Exists(origen)) throw new Exception("El archivo de respaldo no existe.");

            // Por si acaso: guarda una copia de la base actual antes de sobrescribirla.
            string copiaSeguridad = Path.Combine(carpetaBackups, $"{Path.GetFileNameWithoutExtension(dbPath)}_antes_de_restaurar_{DateTime.Now:yyyy-MM-dd_HHmmss}.sqlite");
            if (File.Exists(dbPath)) File.Copy(dbPath, copiaSeguridad, overwrite: true);

            File.Copy(origen, dbPath, overwrite: true);
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

        public void ActualizarMaterial(int id, string descripcion, int cantidad, string proyecto, string equipo, string marca, string categoria, string lugar, string usuario, bool alertaStockActiva, int? stockMinimoPersonalizado)
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

                        connection.Execute(@"UPDATE Repuestos SET Descripcion = @descripcion, Categoria = @categoria,
                                AlertaStockActiva = @alerta, StockMinimoPersonalizado = @stockMin
                            WHERE Id = @id",
                            new { descripcion = descripcion, categoria = categoria, alerta = alertaStockActiva, stockMin = stockMinimoPersonalizado, id = repuestoId }, trans);

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
        public bool AlertaStockActiva { get; set; }
        public int? StockMinimoPersonalizado { get; set; }
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
