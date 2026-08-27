using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Npgsql;
using TfLens.Core;
using TfLens.Core.Abstractions;
using TfLens.Core.Contracts;

namespace TfLens.Services.Auth;

/// <summary>
/// Stores the AppManager tokens behind a TfLens cookie, encrypted at rest.
/// </summary>
/// <remarks>
/// <para>
/// The browser only ever holds the session id (inside the auth cookie); the access and refresh tokens
/// live here (BRD-93). Both token columns are protected with ASP.NET Data Protection under the purpose
/// <c>TfLens.AuthSession</c>, so a database dump does not hand the reader a working AppManager session.
/// </para>
/// <para>
/// Every identifier is double-quoted: PostgreSQL folds unquoted identifiers to lower case, which would
/// silently destroy the PascalCase column names the Coding Standards fix.
/// </para>
/// </remarks>
public sealed class AuthSessionStore : IAuthSessionStore
{
    /// <summary>The Data Protection purpose string the token columns are protected under.</summary>
    public const string ProtectorPurpose = "TfLens.AuthSession";

    private const string InsertSql = """
        INSERT INTO "AuthSession"
            ("SessionId", "UserId", "Email", "DisplayName", "AccessToken", "RefreshToken",
             "TokenExpiresAt", "CreatedTs", "LastValidatedTs")
        VALUES
            (@SessionId, @UserId, @Email, @DisplayName, @AccessToken, @RefreshToken,
             @TokenExpiresAt, @CreatedTs, @LastValidatedTs)
        """;

    private const string SelectSql = """
        SELECT "SessionId", "UserId", "Email", "DisplayName", "AccessToken", "RefreshToken",
               "TokenExpiresAt", "CreatedTs", "LastValidatedTs"
        FROM "AuthSession"
        WHERE "SessionId" = @SessionId
        """;

    private const string UpdateSql = """
        UPDATE "AuthSession"
        SET "AccessToken" = @AccessToken,
            "RefreshToken" = @RefreshToken,
            "TokenExpiresAt" = @TokenExpiresAt,
            "LastValidatedTs" = @LastValidatedTs
        WHERE "SessionId" = @SessionId
        """;

    private const string DeleteSql = """DELETE FROM "AuthSession" WHERE "SessionId" = @SessionId""";

    private readonly TfLensOptions objOptions;
    private readonly IDataProtector objProtector;
    private readonly ILogger<AuthSessionStore> objLogger;

    /// <summary>
    /// Creates the store.
    /// </summary>
    /// <param name="aOptions">TfLens configuration, supplying the PostgreSQL connection string.</param>
    /// <param name="aDataProtectionProvider">Supplies the protector the token columns are encrypted with.</param>
    /// <param name="aLogger">Diagnostics; never receives a token.</param>
    public AuthSessionStore(
        IOptions<TfLensOptions> aOptions,
        IDataProtectionProvider aDataProtectionProvider,
        ILogger<AuthSessionStore> aLogger)
    {
        objOptions = aOptions.Value;
        objProtector = aDataProtectionProvider.CreateProtector(ProtectorPurpose);
        objLogger = aLogger;
    }

    /// <inheritdoc />
    public async Task CreateAsync(AuthSessionRow aSession, CancellationToken aCancellationToken = default)
    {
        await using var vConnection = OpenConnection();

        await vConnection.ExecuteAsync(new CommandDefinition(
            InsertSql,
            new
            {
                aSession.SessionId,
                aSession.UserId,
                aSession.Email,
                aSession.DisplayName,
                AccessToken = objProtector.Protect(aSession.AccessToken),
                RefreshToken = objProtector.Protect(aSession.RefreshToken),
                aSession.TokenExpiresAt,
                aSession.CreatedTs,
                aSession.LastValidatedTs
            },
            cancellationToken: aCancellationToken)).ConfigureAwait(false);

        objLogger.LogInformation("Opened session for user {UserId}.", aSession.UserId);
    }

    /// <inheritdoc />
    public async Task<AuthSessionRow?> GetAsync(string aSessionId, CancellationToken aCancellationToken = default)
    {
        await using var vConnection = OpenConnection();

        var vRow = await vConnection.QuerySingleOrDefaultAsync(new CommandDefinition(
            SelectSql,
            new { SessionId = aSessionId },
            cancellationToken: aCancellationToken)).ConfigureAwait(false);

        if (vRow is null)
        {
            return null;
        }

        var vUserId = (int)vRow.UserId;

        try
        {
            return new AuthSessionRow
            {
                SessionId = (string)vRow.SessionId,
                UserId = vUserId,
                Email = (string)vRow.Email,
                DisplayName = (string)vRow.DisplayName,
                AccessToken = objProtector.Unprotect((string)vRow.AccessToken),
                RefreshToken = objProtector.Unprotect((string)vRow.RefreshToken),
                TokenExpiresAt = (string)vRow.TokenExpiresAt,
                CreatedTs = (string)vRow.CreatedTs,
                LastValidatedTs = (string?)vRow.LastValidatedTs
            };
        }
        catch (System.Security.Cryptography.CryptographicException vEx)
        {
            // The Data Protection key ring changed under the row: the session is unusable, so treat it
            // as gone rather than serving a half-decrypted one.
            objLogger.LogWarning(vEx, "Session for user {UserId} could not be unprotected; discarding it.", vUserId);
            await DeleteAsync(aSessionId, aCancellationToken).ConfigureAwait(false);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task UpdateAsync(AuthSessionRow aSession, CancellationToken aCancellationToken = default)
    {
        await using var vConnection = OpenConnection();

        await vConnection.ExecuteAsync(new CommandDefinition(
            UpdateSql,
            new
            {
                aSession.SessionId,
                AccessToken = objProtector.Protect(aSession.AccessToken),
                RefreshToken = objProtector.Protect(aSession.RefreshToken),
                aSession.TokenExpiresAt,
                aSession.LastValidatedTs
            },
            cancellationToken: aCancellationToken)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string aSessionId, CancellationToken aCancellationToken = default)
    {
        await using var vConnection = OpenConnection();

        await vConnection.ExecuteAsync(new CommandDefinition(
            DeleteSql,
            new { SessionId = aSessionId },
            cancellationToken: aCancellationToken)).ConfigureAwait(false);
    }

    /// <summary>
    /// Opens a pooled connection to the configured database.
    /// </summary>
    /// <returns>A new <see cref="NpgsqlConnection"/>; Npgsql pools the underlying socket.</returns>
    /// <exception cref="InvalidOperationException">No connection string is configured (BRD-9).</exception>
    private NpgsqlConnection OpenConnection()
    {
        if (string.IsNullOrWhiteSpace(objOptions.DbConnection))
        {
            throw new InvalidOperationException(
                "TfLens cannot reach the database — TfLensDbConnection is not set.");
        }

        return new NpgsqlConnection(objOptions.DbConnection);
    }
}
