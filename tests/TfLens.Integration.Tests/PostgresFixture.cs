using Dapper;
using Npgsql;

namespace TfLens.Integration.Tests;

/// <summary>
/// A live PostgreSQL 16 with the schema applied, shared by every test in the collection.
/// </summary>
/// <remarks>
/// Deliberately a real database and not an in-memory double. The property under test — that no read
/// can reach another user's rows — is a property of the SQL and of the schema, and a fake store would
/// prove only that the fake was written correctly.
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    /// <summary>The environment variable the app itself reads (Coding Standards §Environment Variables).</summary>
    public const string ConnectionVariable = "TfLensDbConnection";

    /// <summary>The documented local connection — compose publishes 5433 via the override file (D-007).</summary>
    private const string LocalDefault =
        "Host=localhost;Port=5433;Database=tflens;Username=tflens;Password=tflensdev";

    /// <summary>The connection string every test in this project uses.</summary>
    public string ConnectionString { get; private set; } = LocalDefault;

    /// <summary>Whether the database answered, so a test can report "blocked" rather than "failed".</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>Why the database was unreachable, when it was.</summary>
    public string? UnavailableReason { get; private set; }

    /// <summary>Opens a connection to the fixture database.</summary>
    /// <returns>An open connection the caller disposes.</returns>
    public async Task<NpgsqlConnection> OpenAsync()
    {
        var vConnection = new NpgsqlConnection(ConnectionString);
        await vConnection.OpenAsync();
        return vConnection;
    }

    /// <summary>Applies the schema script and confirms the database answers.</summary>
    /// <returns>A task that completes when the fixture is ready.</returns>
    public async Task InitializeAsync()
    {
        var vFromEnvironment = Environment.GetEnvironmentVariable(ConnectionVariable);
        ConnectionString = string.IsNullOrWhiteSpace(vFromEnvironment) ? LocalDefault : vFromEnvironment;

        // The head reads the same variable through the PascalCase provider, so setting it here is what
        // makes the app-level tests talk to the database the raw-SQL tests just seeded.
        Environment.SetEnvironmentVariable(ConnectionVariable, ConnectionString);

        try
        {
            await using var vConnection = await OpenAsync();

            var vSchema = await File.ReadAllTextAsync(
                Path.Combine(RepoTree.Root.FullName, "database", "001-schema.sql"));

            await vConnection.ExecuteAsync(vSchema);

            IsAvailable = true;
        }
        catch (Exception vEx)
        {
            IsAvailable = false;

            // The type and message only — never the connection string, which carries the password
            // (BRD-10).
            UnavailableReason = $"{vEx.GetType().Name}: {vEx.Message}";
        }
    }

    /// <summary>Nothing to tear down — each test cleans its own rows.</summary>
    /// <returns>A completed task.</returns>
    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>Binds the PostgreSQL fixture to every test class in the project.</summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    /// <summary>The collection name.</summary>
    public const string Name = "postgres";
}

/// <summary>Locates the repository root from the test binaries.</summary>
public static class RepoTree
{
    private static readonly Lazy<DirectoryInfo> objRoot = new(FindRoot);

    /// <summary>The directory holding <c>TfLens.slnx</c>.</summary>
    public static DirectoryInfo Root => objRoot.Value;

    /// <summary>Walks up from the test binaries until the solution file appears.</summary>
    /// <returns>The repository root.</returns>
    /// <exception cref="InvalidOperationException">No ancestor directory holds <c>TfLens.slnx</c>.</exception>
    private static DirectoryInfo FindRoot()
    {
        var vCurrent = new DirectoryInfo(AppContext.BaseDirectory);

        while (vCurrent is not null)
        {
            if (File.Exists(Path.Combine(vCurrent.FullName, "TfLens.slnx")))
            {
                return vCurrent;
            }

            vCurrent = vCurrent.Parent;
        }

        throw new InvalidOperationException($"Could not find TfLens.slnx above {AppContext.BaseDirectory}.");
    }
}
