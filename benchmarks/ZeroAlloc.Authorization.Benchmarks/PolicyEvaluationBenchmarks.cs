using BenchmarkDotNet.Attributes;
using ZeroAlloc.Authorization;
using ZeroAlloc.Results;

namespace ZeroAlloc.Authorization.Benchmarks;

[MemoryDiagnoser(displayGenColumns: false)]
public class PolicyEvaluationBenchmarks
{
    private readonly IAuthorizationPolicy _policy = new AdminOnlyPolicy();
    private readonly TestContext _ctx = new("alice",
        new HashSet<string> { "Admin" },
        new Dictionary<string, string>());

    [Benchmark]
    public async ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync()
        => await _policy.EvaluateAsync(_ctx);

    private sealed class AdminOnlyPolicy : IAuthorizationPolicy
    {
        public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
            ISecurityContext ctx, CancellationToken ct = default)
            => new(ctx.Roles.Contains("Admin")
                ? UnitResult<AuthorizationFailure>.Success()
                : new AuthorizationFailure(AuthorizationFailure.DefaultDenyCode));
    }

    private sealed record TestContext(string Id,
                                      IReadOnlySet<string> Roles,
                                      IReadOnlyDictionary<string, string> Claims) : ISecurityContext;
}
