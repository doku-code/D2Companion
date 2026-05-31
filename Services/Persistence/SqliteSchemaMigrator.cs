using Microsoft.Data.Sqlite;

namespace D2CompanionMvc.Services.Persistence;

internal static class SqliteSchemaMigrator
{
    internal static async Task EnsureDatabaseAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = Schema.Sql;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await TryAddColumnAsync(connection, "Characters", "Realm", "TEXT NULL", cancellationToken);

        // Apply the back-compat column upgrades declared in Schema. Each call
        // is idempotent (duplicate-column errors are swallowed).
        foreach (var (table, column, type) in Schema.BackCompatColumns)
            await TryAddColumnAsync(connection, table, column, type, cancellationToken);
    }

    private static async Task TryAddColumnAsync(SqliteConnection connection, string table, string column, string definition, CancellationToken cancellationToken)
    {
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 1 && exception.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
        {
        }
    }
}
