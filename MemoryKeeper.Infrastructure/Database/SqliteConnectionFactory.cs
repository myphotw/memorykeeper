namespace MemoryKeeper.Infrastructure.Database;

public static class SqliteConnectionFactory
{
    public const string DatabaseFileName = "MemoryKeeper.db";

    public static string CreateConnectionString(string? databaseDirectory = null)
    {
        var directory = string.IsNullOrWhiteSpace(databaseDirectory)
            ? AppContext.BaseDirectory
            : databaseDirectory;

        Directory.CreateDirectory(directory);

        var databasePath = Path.Combine(directory, DatabaseFileName);
        return $"Data Source={databasePath}";
    }
}
