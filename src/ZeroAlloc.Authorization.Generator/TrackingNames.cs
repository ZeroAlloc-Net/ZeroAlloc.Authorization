namespace ZeroAlloc.Authorization.Generator;

/// <summary>
/// Names of the pipeline steps, so tests can check that an unrelated edit leaves them cached.
/// </summary>
internal static class TrackingNames
{
    public const string SourcePolicies = nameof(SourcePolicies);
    public const string SourceRequirePolicies = nameof(SourceRequirePolicies);
    public const string SourceRequireAnyPolicies = nameof(SourceRequireAnyPolicies);
    public const string ReferenceScan = nameof(ReferenceScan);
    public const string EmitInputs = nameof(EmitInputs);
    public const string DiagnosticInputs = nameof(DiagnosticInputs);
}
