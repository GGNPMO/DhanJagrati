using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace DhanJagrati.Api.Tests;

/// <summary>
/// Throwaway database built from scheema\dhanjagrati.sql + all patches, dropped on dispose.
/// Never touches the real DhanJagrati database: the scripts' USE / CREATE DATABASE batches are skipped
/// and every batch runs on a connection to the new database.
/// </summary>
public sealed partial class TestDatabase : IAsyncDisposable
{
    private readonly string _serverConnection;
    public string Name { get; }
    public string ConnectionString { get; }

    private TestDatabase(string serverConnection, string name)
    {
        _serverConnection = serverConnection;
        Name = name;
        ConnectionString = new SqlConnectionStringBuilder(serverConnection) { InitialCatalog = name }.ConnectionString;
    }

    public static async Task<TestDatabase> CreateAsync(string serverConnection)
    {
        // Locate the scripts before creating anything on the server.
        var root = FindSchemaFolder();
        var scripts = new[] { Path.Combine(root, "dhanjagrati.sql") }
            .Concat(Directory.GetFiles(Path.Combine(root, "dhanjagrati_patches"), "*.sql").OrderBy(f => f, StringComparer.Ordinal))
            .ToList();

        var db = new TestDatabase(serverConnection, $"DhanJagrati_Test_{Guid.NewGuid():N}"[..32]);
        await using (var master = new SqlConnection(new SqlConnectionStringBuilder(serverConnection) { InitialCatalog = "master" }.ConnectionString))
        {
            await master.OpenAsync();
            await using var create = new SqlCommand($"CREATE DATABASE [{db.Name}]", master);
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            await using var c = new SqlConnection(db.ConnectionString);
            await c.OpenAsync();
            foreach (var script in scripts)
                foreach (var batch in GoSeparator().Split(await File.ReadAllTextAsync(script)))
                {
                    if (string.IsNullOrWhiteSpace(batch) || SkipBatch().IsMatch(batch)) continue;
                    await using var cmd = new SqlCommand(batch, c) { CommandTimeout = 180 };
                    await cmd.ExecuteNonQueryAsync();
                }
            return db;
        }
        catch
        {
            await db.DisposeAsync();   // never leave a half-built database behind
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await using var master = new SqlConnection(new SqlConnectionStringBuilder(_serverConnection) { InitialCatalog = "master" }.ConnectionString);
        await master.OpenAsync();
        await using var drop = new SqlCommand($"""
            IF DB_ID(N'{Name}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{Name}];
            END
            """, master);
        await drop.ExecuteNonQueryAsync();
    }

    /// <summary>DJ_SCHEMA_DIR, or the nearest "scheema" folder above the test output or working directory.</summary>
    private static string FindSchemaFolder([System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "")
    {
        if (Environment.GetEnvironmentVariable("DJ_SCHEMA_DIR") is { Length: > 0 } configured) return configured;
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory(), Path.GetDirectoryName(sourceFile) ?? "" })
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "scheema");
                if (File.Exists(Path.Combine(candidate, "dhanjagrati.sql"))) return candidate;
            }
        throw new DirectoryNotFoundException("scheema folder not found; set DJ_SCHEMA_DIR.");
    }

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GoSeparator();

    /// <summary>Batches that switch to / create the real database are not run.</summary>
    [GeneratedRegex(@"^\s*(USE\s+(master|DhanJagrati)\s*;?\s*$|IF\s+DB_ID\(N'DhanJagrati'\))", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex SkipBatch();
}
