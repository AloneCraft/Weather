using Microsoft.Data.Sqlite;

namespace Weather.Infrastructure;

/// <summary>
/// SQLite(History.md「保存方式」)。スキーマの版は PRAGMA user_version で管理し、前進のみのマイグレーションを適用する。
/// </summary>
public sealed class WeatherDatabase{
    private static readonly string[] Migrations=[
        //v1: お気に入り(MVP)
        """
        CREATE TABLE favorites(
            place_id      TEXT PRIMARY KEY,
            display_name  TEXT NOT NULL,
            latitude      REAL NOT NULL,
            longitude     REAL NOT NULL,
            sort_order    INTEGER NOT NULL,
            station_key   TEXT
        );
        """,
        //v2: 観測履歴(Phase 2)
        """
        CREATE TABLE stations(
            station_key   TEXT PRIMARY KEY,
            provider      INTEGER NOT NULL,
            station_id    TEXT NOT NULL,
            name          TEXT NOT NULL,
            latitude      REAL NOT NULL,
            longitude     REAL NOT NULL,
            elevation_m   REAL
        );
        CREATE TABLE observations(
            station_key       TEXT NOT NULL REFERENCES stations(station_key),
            observed_at_utc   INTEGER NOT NULL,
            temperature_c     REAL,
            humidity_pct      REAL,
            precip_10m_mm     REAL,
            precip_1h_mm      REAL,
            precip_24h_mm     REAL,
            wind_speed_ms     REAL,
            wind_dir_deg      REAL,
            wind_gust_ms      REAL,
            pressure_hpa      REAL,
            sea_level_hpa     REAL,
            sunshine_1h_h     REAL,
            snow_depth_cm     REAL,
            visibility_m      REAL,
            suspect_mask      INTEGER NOT NULL DEFAULT 0,
            weather_text      TEXT,
            PRIMARY KEY(station_key,observed_at_utc)
        ) WITHOUT ROWID;
        CREATE TABLE daily_summaries(
            station_key   TEXT NOT NULL REFERENCES stations(station_key),
            local_date    TEXT NOT NULL,
            max_temp_c    REAL,
            max_temp_at   INTEGER,
            min_temp_c    REAL,
            min_temp_at   INTEGER,
            precip_mm     REAL,
            is_derived    INTEGER NOT NULL,
            PRIMARY KEY(station_key,local_date)
        ) WITHOUT ROWID;
        CREATE TABLE sync_state(
            station_key       TEXT PRIMARY KEY REFERENCES stations(station_key),
            last_observed_utc INTEGER,
            last_synced_utc   INTEGER
        );
        """,
    ];

    private readonly string connectionString;
    private readonly Lock gate=new();
    private bool initialized;

    public WeatherDatabase(InfrastructureOptions options){
        ArgumentNullException.ThrowIfNull(options);
        Directory.CreateDirectory(options.DataDirectory);
        this.connectionString=new SqliteConnectionStringBuilder{
            DataSource=options.DatabasePath,
            Mode=SqliteOpenMode.ReadWriteCreate,
            Cache=SqliteCacheMode.Private,
            Pooling=true,
        }.ToString();
    }

    public static int SchemaVersion=>Migrations.Length;

    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken){
        var connection=new SqliteConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        this.EnsureMigrated(connection);
        return connection;
    }

    private void EnsureMigrated(SqliteConnection connection){
        lock(this.gate){
            if(this.initialized){
                return;
            }
            Execute(connection,"PRAGMA journal_mode=WAL;");
            Execute(connection,"PRAGMA foreign_keys=ON;");
            var version=Convert.ToInt32(Scalar(connection,"PRAGMA user_version;"),System.Globalization.CultureInfo.InvariantCulture);
            for(var i=version;i<Migrations.Length;i++){
                using var transaction=connection.BeginTransaction();
                using(var command=connection.CreateCommand()){
                    command.Transaction=transaction;
                    command.CommandText=Migrations[i]+$"\nPRAGMA user_version={i+1};";
                    command.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            this.initialized=true;
        }
    }

    private static void Execute(SqliteConnection connection,string sql){
        using var command=connection.CreateCommand();
        command.CommandText=sql;
        command.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection connection,string sql){
        using var command=connection.CreateCommand();
        command.CommandText=sql;
        return command.ExecuteScalar();
    }
}
