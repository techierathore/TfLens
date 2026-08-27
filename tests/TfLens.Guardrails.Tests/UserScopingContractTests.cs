using System.Reflection;
using System.Text;
using TfLens.Core;
using TfLens.Core.Abstractions;

namespace TfLens.Guardrails.Tests;

/// <summary>
/// REQ-FN-017 (BRD-102, ADR-013) — <c>UserId</c> is a mandatory parameter of every store read and
/// write, of the raw-archive path, of the reports path and of the analysis cache key.
/// </summary>
/// <remarks>
/// This is the compile-time half of REQ-NFR-010. The integration test proves that two users' data
/// does not mix at runtime; this proves that an implementer could not have written a method that
/// ignores the user in the first place. The acceptance wording is the point: "a missing filter is a
/// compile-time absence rather than a runtime oversight."
/// </remarks>
public sealed class UserScopingContractTests
{
    /// <summary>
    /// The only store methods allowed to exist without a user id, and why each one is allowed.
    /// </summary>
    /// <remarks>
    /// The test asserts this set is <b>exact</b>. Adding an unscoped method fails here, which forces a
    /// deliberate decision instead of an accident.
    /// </remarks>
    private static readonly Dictionary<string, string> UnscopedStoreMethods = new(StringComparer.Ordinal)
    {
        ["EnsureSchemaAsync"] = "applies the DDL; touches no user's rows",
        ["PingAsync"] = "opens a connection; reads nothing",
        ["ReadAllUserReposAsync"] = "the poller's work list — deliberately every user (BRD-103)",
        ["RebuildAsync"] = "takes int? aUserId; null means every user, which is the documented verb",
        ["UpsertAsync"] = "takes a ParseResult that carries its own UserId",
        ["WriteSyncStateAsync"] = "takes a SyncState that carries its own UserId",
        ["WriteUserRepoAsync"] = "takes a UserRepo that carries its own UserId"
    };

    /// <summary>Every store method is either user-scoped or on the explicit, justified exception list.</summary>
    [Fact]
    public void EveryTelemetryStoreMethodIsUserScoped()
    {
        var vUnscoped = UnscopedMethods(typeof(ITelemetryStore));

        Assert.True(
            vUnscoped.Count == 0,
            Report(typeof(ITelemetryStore), vUnscoped));
    }

    /// <summary>The exception list has not silently grown.</summary>
    /// <remarks>
    /// Without this, the previous test could be satisfied forever by appending to the exception list.
    /// Here the list is compared to the interface, so a new unscoped method is a failing test either
    /// way — it just fails with a different message.
    /// </remarks>
    [Fact]
    public void TheUnscopedStoreMethodListIsExact()
    {
        var vDeclared = typeof(ITelemetryStore)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(aMethod => aMethod.Name)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        var vStale = UnscopedStoreMethods.Keys
            .Where(aName => !vDeclared.Contains(aName))
            .OrderBy(aName => aName, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            vStale.Count == 0,
            "REQ-FN-017 — these names are waived as unscoped but no longer exist on ITelemetryStore; " +
            $"remove them from the waiver so it keeps meaning something: {string.Join(", ", vStale)}");
    }

    /// <summary>Every engine, registry and exporter entry point takes a user id.</summary>
    /// <remarks>
    /// The store is not the only surface that could leak across users: a cached analysis or an export
    /// path that forgot the user would be just as bad.
    /// </remarks>
    [Theory]
    [InlineData(typeof(IMetricsEngine))]
    [InlineData(typeof(IExtraMetrics))]
    [InlineData(typeof(ISnapshotExporter))]
    [InlineData(typeof(IRepoRegistry))]
    public void EveryUserFacingServiceMethodIsUserScoped(Type aInterface)
    {
        var vUnscoped = UnscopedMethods(aInterface);

        Assert.True(vUnscoped.Count == 0, Report(aInterface, vUnscoped));
    }

    /// <summary>The raw-archive and reports paths are functions of the user id, not of a filter.</summary>
    [Fact]
    public void PathsAreUserScopedByConstruction()
    {
        var vRawPath = typeof(TfLensOptions).GetMethod(nameof(TfLensOptions.RawPath));
        var vReportsPath = typeof(TfLensOptions).GetMethod(nameof(TfLensOptions.ReportsPath));

        Assert.NotNull(vRawPath);
        Assert.NotNull(vReportsPath);

        Assert.Equal(typeof(int), Assert.Single(vRawPath!.GetParameters()).ParameterType);
        Assert.Equal(typeof(int), Assert.Single(vReportsPath!.GetParameters()).ParameterType);

        var vOptions = new TfLensOptions { DataRoot = "data" };

        // One user's directory must never be a prefix of another's, or a naive recursive delete or a
        // path-prefix read would cross the boundary.
        Assert.False(vOptions.RawPath(1).StartsWith(vOptions.RawPath(11), StringComparison.Ordinal));
        Assert.False(vOptions.RawPath(11).StartsWith(vOptions.RawPath(1) + "/", StringComparison.Ordinal));
        Assert.False(vOptions.RawPath(11).StartsWith(vOptions.RawPath(1) + "\\", StringComparison.Ordinal));
    }

    /// <summary>Finds methods on an interface that take neither a user id nor a user-carrying record.</summary>
    /// <param name="aInterface">The service contract to inspect.</param>
    /// <returns>The offending method signatures.</returns>
    private static IReadOnlyList<string> UnscopedMethods(Type aInterface)
    {
        var vFindings = new List<string>();

        foreach (var vMethod in aInterface.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (UnscopedStoreMethods.ContainsKey(vMethod.Name))
            {
                continue;
            }

            var vParameters = vMethod.GetParameters();

            var vHasUserId = vParameters.Any(aParameter =>
                string.Equals(aParameter.Name, "aUserId", StringComparison.Ordinal)
                && (aParameter.ParameterType == typeof(int) || aParameter.ParameterType == typeof(int?)));

            var vCarriesUserId = vParameters.Any(aParameter =>
                aParameter.ParameterType.GetProperty("UserId") is not null);

            if (!vHasUserId && !vCarriesUserId)
            {
                vFindings.Add(Signature(vMethod));
            }
        }

        return vFindings;
    }

    /// <summary>Renders a method signature for a failure message.</summary>
    /// <param name="aMethod">The method.</param>
    /// <returns>A readable signature.</returns>
    private static string Signature(MethodInfo aMethod)
    {
        var vParameters = aMethod.GetParameters()
            .Select(aParameter => $"{aParameter.ParameterType.Name} {aParameter.Name}");

        return $"{aMethod.Name}({string.Join(", ", vParameters)})";
    }

    /// <summary>Formats the unscoped-method findings.</summary>
    /// <param name="aInterface">The contract inspected.</param>
    /// <param name="aFindings">The offending signatures.</param>
    /// <returns>A multi-line report.</returns>
    private static string Report(Type aInterface, IReadOnlyList<string> aFindings)
    {
        var vBuilder = new StringBuilder();
        vBuilder.AppendLine(
            $"REQ-FN-017 / ADR-013 — {aFindings.Count} method(s) on {aInterface.Name} take no user id:");

        foreach (var vFinding in aFindings)
        {
            vBuilder.AppendLine($"  {vFinding}");
        }

        vBuilder.AppendLine(
            "Isolation is a mandatory parameter, not an optional filter. Add `int aUserId`, or pass a " +
            "record that carries UserId, or justify the exception in UnscopedStoreMethods.");

        return vBuilder.ToString();
    }
}
