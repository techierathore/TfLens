namespace TfLens.Core.Tests.Storage;

/// <summary>
/// The one xUnit collection every PostgreSQL-backed test class belongs to, so they never run at once.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> These four classes share one real database. xUnit runs test classes in
/// parallel by default, so they were applying DDL, truncating and upserting against the same tables
/// concurrently — and PostgreSQL answered with <c>40P01: deadlock detected</c>. The tell is that a
/// <b>different</b> test failed on each run (<c>MissStoreTests.AllThreeMissKindsRoundTrip</c>,
/// <c>PostgresStoreTests.SchemaAppliesAndDatabaseAnswers</c>, <c>PhaseEffortStoreTests</c> …) while
/// every one of them passed when run alone. That is contention, not a defect in any of them.
/// </para>
/// <para>
/// It was always latent — the hazard <c>database/001-schema.sql</c> already documents — and the
/// 2026-09-01 F-EFFORT pass made it reproducible rather than occasional: <c>EnsureSchemaAsync</c> now
/// creates three more tables and six more indexes, so the DDL window each class opens is wider and the
/// odds of two overlapping went from rare to routine.
/// </para>
/// <para>
/// Sharing a collection is xUnit's own answer: classes in one collection are serialized against each
/// other and still run in parallel with the rest of the suite. It costs a few seconds and it removes a
/// red build that means nothing — which is the more expensive of the two, because a suite that cries
/// wolf is a suite people stop reading.
/// </para>
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgresCollection
{
    /// <summary>The collection name; every PostgreSQL-backed test class carries it.</summary>
    public const string Name = "postgres";
}
