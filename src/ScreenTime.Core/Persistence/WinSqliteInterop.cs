using System.Runtime.InteropServices;

namespace ScreenTime.Core.Persistence;

/// <summary>
/// Nativer Wrapper für das in Windows 10/11 integrierte winsqlite3.dll (System32).
/// Nutzt die nativen UTF-16-Funktionen von SQLite für verlustfreie Unicode-Verarbeitung ohne externe Abhängigkeiten.
/// </summary>
internal static class WinSqliteInterop
{
    private const string DllName = "winsqlite3.dll";

    public const int SQLITE_OK = 0;
    public const int SQLITE_BUSY = 5;
    public const int SQLITE_LOCKED = 6;
    public const int SQLITE_ROW = 100;
    public const int SQLITE_DONE = 101;
    public const int SQLITE_NULL = 5;

    public static readonly nint SQLITE_TRANSIENT = new(-1);

    [DllImport(DllName, EntryPoint = "sqlite3_open16", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public static extern int Open16([MarshalAs(UnmanagedType.LPWStr)] string filename, out nint db);

    [DllImport(DllName, EntryPoint = "sqlite3_close_v2", CallingConvention = CallingConvention.Cdecl)]
    public static extern int CloseV2(nint db);

    [DllImport(DllName, EntryPoint = "sqlite3_busy_timeout", CallingConvention = CallingConvention.Cdecl)]
    public static extern int BusyTimeout(nint db, int ms);

    [DllImport(DllName, EntryPoint = "sqlite3_prepare16_v2", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public static extern int Prepare16V2(
        nint db,
        [MarshalAs(UnmanagedType.LPWStr)] string sql,
        int numBytes,
        out nint stmt,
        out nint pzTail);

    [DllImport(DllName, EntryPoint = "sqlite3_step", CallingConvention = CallingConvention.Cdecl)]
    public static extern int Step(nint stmt);

    [DllImport(DllName, EntryPoint = "sqlite3_reset", CallingConvention = CallingConvention.Cdecl)]
    public static extern int Reset(nint stmt);

    [DllImport(DllName, EntryPoint = "sqlite3_finalize", CallingConvention = CallingConvention.Cdecl)]
    public static extern int Finalize(nint stmt);

    [DllImport(DllName, EntryPoint = "sqlite3_last_insert_rowid", CallingConvention = CallingConvention.Cdecl)]
    public static extern long LastInsertRowId(nint db);

    [DllImport(DllName, EntryPoint = "sqlite3_errmsg16", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint ErrMsg16Ptr(nint db);

    [DllImport(DllName, EntryPoint = "sqlite3_bind_int64", CallingConvention = CallingConvention.Cdecl)]
    public static extern int BindInt64(nint stmt, int index, long value);

    [DllImport(DllName, EntryPoint = "sqlite3_bind_null", CallingConvention = CallingConvention.Cdecl)]
    public static extern int BindNull(nint stmt, int index);

    [DllImport(DllName, EntryPoint = "sqlite3_bind_text16", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public static extern int BindText16(
        nint stmt,
        int index,
        [MarshalAs(UnmanagedType.LPWStr)] string value,
        int byteLength,
        nint destructor);

    [DllImport(DllName, EntryPoint = "sqlite3_column_type", CallingConvention = CallingConvention.Cdecl)]
    public static extern int ColumnType(nint stmt, int iCol);

    [DllImport(DllName, EntryPoint = "sqlite3_column_int64", CallingConvention = CallingConvention.Cdecl)]
    public static extern long ColumnInt64(nint stmt, int iCol);

    [DllImport(DllName, EntryPoint = "sqlite3_column_text16", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint ColumnText16Ptr(nint stmt, int iCol);

    public static string GetErrorMessage(nint db)
    {
        if (db == nint.Zero)
        {
            return "SQLite-Handle ist nicht initialisiert.";
        }
        nint ptr = ErrMsg16Ptr(db);
        return ptr == nint.Zero ? "Unbekannter SQLite-Fehler" : Marshal.PtrToStringUni(ptr) ?? "Unbekannter SQLite-Fehler";
    }

    public static void BindStringOrNull(nint stmt, int index, string? value)
    {
        int rc;
        if (value is null)
        {
            rc = BindNull(stmt, index);
        }
        else
        {
            rc = BindText16(stmt, index, value, -1, SQLITE_TRANSIENT);
        }
        if (rc != SQLITE_OK)
        {
            throw new InvalidOperationException($"SQLite Bind-Fehler an Parameter {index} (Code {rc}).");
        }
    }

    public static void BindNullableLong(nint stmt, int index, long? value)
    {
        int rc = value.HasValue
            ? BindInt64(stmt, index, value.Value)
            : BindNull(stmt, index);
        if (rc != SQLITE_OK)
        {
            throw new InvalidOperationException($"SQLite Bind-Fehler an Parameter {index} (Code {rc}).");
        }
    }

    public static string? GetNullableString(nint stmt, int col)
    {
        if (ColumnType(stmt, col) == SQLITE_NULL)
        {
            return null;
        }
        nint ptr = ColumnText16Ptr(stmt, col);
        return ptr == nint.Zero ? null : Marshal.PtrToStringUni(ptr);
    }

    public static string GetString(nint stmt, int col) => GetNullableString(stmt, col) ?? string.Empty;

    public static long? GetNullableLong(nint stmt, int col)
    {
        if (ColumnType(stmt, col) == SQLITE_NULL)
        {
            return null;
        }
        return ColumnInt64(stmt, col);
    }

    public static void ExecuteNonQuery(nint db, string sql, Action<nint>? bindParameters = null)
    {
        int rc = Prepare16V2(db, sql, -1, out nint stmt, out _);
        if (rc != SQLITE_OK)
        {
            throw new InvalidOperationException($"SQLite Prepare fehlgeschlagen ({rc}): {GetErrorMessage(db)} | SQL: {sql}");
        }

        try
        {
            bindParameters?.Invoke(stmt);
            int stepRc = Step(stmt);
            if (stepRc != SQLITE_DONE && stepRc != SQLITE_ROW)
            {
                throw new InvalidOperationException($"SQLite Step fehlgeschlagen ({stepRc}): {GetErrorMessage(db)}");
            }
        }
        finally
        {
            Finalize(stmt);
        }
    }
}
