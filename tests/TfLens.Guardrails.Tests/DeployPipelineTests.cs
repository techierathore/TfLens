using System.Text.RegularExpressions;

namespace TfLens.Guardrails.Tests;

/// <summary>
/// REQ-NFR-025 (BRD-188) — production deployment is a pipeline: a push to <c>main</c> builds the
/// image, deploys it to the VPS, and fails the run when <c>/healthz</c> does not answer afterwards.
/// </summary>
/// <remarks>
/// A push cannot be made from a test, so these read <c>.github/workflows/deploy.yml</c> itself and
/// prove the three links of the chain and their order. Whether the last run actually went green is
/// a fact about GitHub, not about this tree; the live site answering <c>/healthz</c> with 200 is the
/// evidence for that, and it is recorded beside the verdict rather than asserted here.
/// </remarks>
public sealed class DeployPipelineTests
{
    private static string WorkflowText =>
        File.ReadAllText(Path.Combine(RepoTree.Root.FullName, ".github", "workflows", "deploy.yml"));

    /// <summary>The workflow runs on a push to <c>main</c>, so no human step starts a deploy.</summary>
    [Fact(DisplayName = "REQ-NFR-025 — the deploy workflow runs on a push to main")]
    public void DeployRunsOnPushToMain()
    {
        var vWorkflow = WorkflowText;

        Assert.Matches(new Regex(@"(?m)^on:\s*\n\s+push:\s*\n\s+branches:\s*\[\s*main\s*\]"), vWorkflow);
    }

    /// <summary>Build, deploy and verify are three jobs, each waiting on the one before.</summary>
    [Fact(DisplayName = "REQ-NFR-025 — build, deploy and verify run in that order")]
    public void BuildDeployVerifyAreChained()
    {
        var vWorkflow = WorkflowText;

        Assert.Matches(new Regex(@"(?ms)^  deploy:\s*\n.*?^    needs:\s*build\s*$"), vWorkflow);
        Assert.Matches(new Regex(@"(?ms)^  verify:\s*\n.*?^    needs:\s*deploy\s*$"), vWorkflow);
    }

    /// <summary>
    /// The verify job probes the public <c>/healthz</c> with a failing curl and ends the run with exit 1
    /// when no attempt answers.
    /// </summary>
    [Fact(DisplayName = "REQ-NFR-025 — the run fails when /healthz does not answer after the deploy")]
    public void UnansweredHealthCheckFailsTheRun()
    {
        var vWorkflow = WorkflowText;
        var vVerify = vWorkflow[vWorkflow.IndexOf("\n  verify:", StringComparison.Ordinal)..];

        Assert.Contains("curl -sf", vVerify, StringComparison.Ordinal);
        Assert.Contains("https://tflens.techierathore.com/healthz", vVerify, StringComparison.Ordinal);
        Assert.Contains("exit 1", vVerify, StringComparison.Ordinal);
    }
}
