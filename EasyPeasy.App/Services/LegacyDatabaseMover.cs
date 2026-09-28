namespace EasyPeasy.App.Services;

/// <summary>
/// Carries a database left over from the pre-rename file name onto the current one.
///
/// The app was renamed from EasyEnglish to EasyPeasy and its database file with it. The package
/// identity did not change, so the old file is still sitting in the same app-data folder — but
/// SQLite would not look for it under the new name and would quietly create an empty database
/// instead, losing every course on the device.
/// </summary>
public static class LegacyDatabaseMover
{
    /// <summary>
    /// SQLite keeps a write-ahead log and a shared-memory file next to the database. A checkpoint
    /// may still be pending in the -wal, so moving the database alone would drop the most recent
    /// session — invisibly, and only noticed much later.
    /// </summary>
    private static readonly string[] Companions = ["-wal", "-shm"];

    /// <summary>The keys Microsoft.Data.Sqlite accepts for the file path.</summary>
    private static readonly string[] PathKeys = ["Data Source", "DataSource", "Filename"];

    /// <summary>
    /// Pulls the file path out of a connection string, or null when there is none. Whitespace
    /// around the key and the value is allowed — a connection string is a set of key/value pairs,
    /// not a fixed piece of text, and this one is written by hand in appsettings.json.
    /// </summary>
    public static string? ExtractDataSource(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return null;

        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);

            if (pair.Length != 2 || !PathKeys.Contains(pair[0].Trim(), StringComparer.OrdinalIgnoreCase))
                continue;

            var path = pair[1].Trim();

            return string.IsNullOrEmpty(path) ? null : path;
        }

        return null;
    }

    /// <summary>
    /// Renames <paramref name="legacyFileName"/> (in the same folder) to the database the
    /// connection string points at. Does nothing unless the new file is absent and the old one is
    /// there, so it runs once rather than on every start.
    /// </summary>
    /// <returns>True when a database was moved.</returns>
    public static bool Move(string? connectionString, string legacyFileName)
    {
        var target = ExtractDataSource(connectionString);

        if (string.IsNullOrEmpty(target) || File.Exists(target))
            return false;

        var directory = Path.GetDirectoryName(target);
        if (string.IsNullOrEmpty(directory))
            return false;

        var legacy = Path.Combine(directory, legacyFileName);
        if (!File.Exists(legacy))
            return false;

        // Companions first, the database last: every step checks its own destination, so a start
        // that fails half way through picks up where it left off on the next one instead of
        // leaving the database without its log.
        foreach (var suffix in Companions)
            MoveIfPresent(legacy + suffix, target + suffix);

        File.Move(legacy, target);

        return true;
    }

    private static void MoveIfPresent(string from, string to)
    {
        if (File.Exists(from) && !File.Exists(to))
            File.Move(from, to);
    }
}
