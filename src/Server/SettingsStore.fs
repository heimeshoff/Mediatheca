namespace Mediatheca.Server

open System
open System.Data
open Microsoft.Data.Sqlite
open Donald

module SettingsStore =

    let initialize (conn: SqliteConnection) : unit =
        conn
        |> Db.newCommand """
            CREATE TABLE IF NOT EXISTS settings (
                key        TEXT PRIMARY KEY,
                value      TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
        """
        |> Db.exec

    let getSetting (conn: SqliteConnection) (key: string) : string option =
        conn
        |> Db.newCommand "SELECT value FROM settings WHERE key = @key"
        |> Db.setParams [ "key", SqlType.String key ]
        |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadString "value")

    let setSetting (conn: SqliteConnection) (key: string) (value: string) : unit =
        conn
        |> Db.newCommand """
            INSERT INTO settings (key, value, updated_at)
            VALUES (@key, @value, @updated_at)
            ON CONFLICT(key) DO UPDATE SET
                value = @value,
                updated_at = @updated_at
        """
        |> Db.setParams [
            "key", SqlType.String key
            "value", SqlType.String value
            "updated_at", SqlType.String (DateTime.UtcNow.ToString("o"))
        ]
        |> Db.exec
