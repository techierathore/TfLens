using TfLens.Core.Contracts;

namespace TfLens.Core.Playbook;

/// <summary>
/// What TfLens actually knows about the Playbook <c>events.ndjson</c> shape today (REQ-FN-068, ADR-010).
/// </summary>
/// <remarks>
/// <para>
/// This is the single switch that says whether the Playbook columns and the phase-gate verdict mapping
/// were confirmed against real evidence or are still guesses. It exists so that the caveat is a value
/// the code carries into every figure and every export, not something someone has to remember to write
/// on a page.
/// </para>
/// <para>
/// <b>Flip these only together with a <c>DECISIONS.md</c> §Playbook entry that predates the change</b> —
/// that ordering is REQ-FN-068's acceptance.
/// </para>
/// </remarks>
public static class PlaybookSchemaState
{
    /// <summary>
    /// How the <c>"PbEvent"</c> column set was arrived at.
    /// </summary>
    /// <remarks>
    /// 2026-08-26: no captured <c>events.ndjson</c> exists anywhere TfLens can reach — the file is
    /// runtime output written into a <i>consuming</i> project by an opt-in OpenCode plugin and is never
    /// committed to the Playbook repository. The columns were instead read off the emitter itself,
    /// <c>harness/opencode/plugin/telemetry.ts</c>, and its joiner
    /// <c>scripts/playbook-telemetry.mjs</c>. See <c>DECISIONS.md</c> §Playbook for the full record.
    /// </remarks>
    public const PlaybookSchemaStatus Status = PlaybookSchemaStatus.EmitterSourceDerived;

    /// <summary>
    /// Whether the <c>phase_gate</c> → verdict vocabulary mapping can be computed from the stream.
    /// </summary>
    /// <remarks>
    /// <b>It cannot, and this is a finding rather than a gap in the implementation.</b>
    /// <c>events.ndjson</c> carries no verdict of any kind: the emitter writes only <c>phase-start</c>,
    /// <c>turn</c> and <c>phase-end</c> records. The Playbook's verdict vocabulary
    /// (<c>PASS</c>, <c>PASS (code-audit)</c>, <c>FAIL</c>, <c>FAIL (code-audit)</c>, <c>DATA-GAP</c>,
    /// <c>BLOCKED</c>) is parsed out of the project's <i>checklist</i> by the joiner, not out of the
    /// stream — so the three questions need the joiner output, which BRD-73 calls for only "if
    /// committed" and which no repository has been observed to commit.
    /// </remarks>
    public const bool IsVerdictMapRecorded = false;

    /// <summary>The reason the gate-outcomes figures are not applicable.</summary>
    public const string VerdictMapUnavailableReason =
        "events.ndjson carries no verdict field — the Playbook's gate_verdict vocabulary is parsed from "
        + "the project checklist by scripts/playbook-telemetry.mjs, not emitted into the stream. The "
        + "three questions need that joiner output (BRD-73, \"if committed\").";

    /// <summary>The caveats every Playbook figure and every Playbook snapshot carries today.</summary>
    public static readonly IReadOnlyList<string> ProvisionalNotes =
    [
        "PbEvent columns are derived from the Playbook emitter source, not from a captured events.ndjson: "
        + "field names and types are authoritative, value ranges are unverified (DECISIONS.md §Playbook).",
        VerdictMapUnavailableReason,
        "PhaseGate is derived, not emitted: it is the 'command' of the enclosing phase-start record, "
        + "carried onto the turn and phase-end records that follow it, exactly as the Playbook's own joiner does."
    ];
}
