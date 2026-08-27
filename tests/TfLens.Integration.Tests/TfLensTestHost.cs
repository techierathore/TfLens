using Microsoft.AspNetCore.Mvc.Testing;

namespace TfLens.Integration.Tests;

/// <summary>
/// The real TfLens host, started in-process.
/// </summary>
/// <remarks>
/// Nothing is stubbed. REQ-NFR-010 is a claim about the application that ships, so the test drives the
/// application that ships — including its startup validation, its schema application and its database
/// ping. Startup failure is captured rather than thrown, so a test can report <i>why</i> the proof
/// could not run instead of collapsing into an unreadable constructor exception.
/// </remarks>
public sealed class TfLensTestHost : IAsyncDisposable
{
    private WebApplicationFactory<Program>? objFactory;
    private string? objFailure;

    /// <summary>
    /// Builds the host and returns its service provider.
    /// </summary>
    /// <param name="aWhyNot">Set to the reason when the host could not start.</param>
    /// <returns>The root service provider, or <c>null</c> when startup failed.</returns>
    public IServiceProvider? TryGetServices(out string? aWhyNot)
    {
        if (objFactory is not null)
        {
            aWhyNot = null;
            return objFactory.Services;
        }

        if (objFailure is not null)
        {
            aWhyNot = objFailure;
            return null;
        }

        try
        {
            var vFactory = new WebApplicationFactory<Program>();

            // Services is lazy — touching it is what actually runs Program.cs.
            _ = vFactory.Services;

            objFactory = vFactory;
            aWhyNot = null;
            return vFactory.Services;
        }
        catch (Exception vEx)
        {
            // Type and message only: a startup exception can carry the connection string in its inner
            // detail, and this string ends up in a test report (BRD-10).
            objFailure = $"{vEx.GetType().Name}: {FirstLine(vEx.Message)}";
            aWhyNot = objFailure;
            return null;
        }
    }

    /// <summary>Creates an HTTP client against the started host.</summary>
    /// <returns>A client that does not follow redirects, so a sign-in redirect is observable.</returns>
    /// <exception cref="InvalidOperationException">The host never started.</exception>
    public HttpClient CreateClient()
    {
        if (objFactory is null)
        {
            throw new InvalidOperationException("The host did not start; call TryGetServices first.");
        }

        return objFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    /// <summary>Shuts the host down.</summary>
    /// <returns>A task that completes when the host is disposed.</returns>
    public async ValueTask DisposeAsync()
    {
        if (objFactory is not null)
        {
            await objFactory.DisposeAsync();
        }
    }

    /// <summary>Keeps only the first line of a message, so a multi-line dump cannot smuggle detail out.</summary>
    /// <param name="aMessage">The exception message.</param>
    /// <returns>The first line.</returns>
    private static string FirstLine(string aMessage)
    {
        var vBreak = aMessage.IndexOfAny(['\r', '\n']);
        return vBreak < 0 ? aMessage : aMessage[..vBreak];
    }
}
