using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Authorization;
using ZeroAlloc.Authorization.Generated;
using ZeroAlloc.Results;

namespace ZeroAlloc.Authorization.Benchmarks;

/// <summary>
/// Measures the generator-emitted <see cref="AuthorizerFor{TRequest}"/> dispatch, which is what
/// a host actually calls, rather than a hand-written policy. The scope and authorizers are
/// resolved once in setup, so each benchmark measures only <c>EvaluateAsync</c>.
/// </summary>
[MemoryDiagnoser(displayGenColumns: false)]
public class GeneratedAuthorizerBenchmarks
{
    private ServiceProvider _provider = null!;
    private IServiceScope _scope = null!;
    private AuthorizerFor<BenchSingleRequest> _single = null!;
    private AuthorizerFor<BenchAnyOfRequest> _anyOf = null!;
    private AuthorizerFor<BenchMinAgeRequest> _parameterized = null!;
    private readonly BenchAgedContext _adult = new("alice", 21);

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddZeroAllocAuthorization();
        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();
        _single = _scope.ServiceProvider.GetRequiredService<AuthorizerFor<BenchSingleRequest>>();
        _anyOf = _scope.ServiceProvider.GetRequiredService<AuthorizerFor<BenchAnyOfRequest>>();
        _parameterized = _scope.ServiceProvider.GetRequiredService<AuthorizerFor<BenchMinAgeRequest>>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _scope.Dispose();
        _provider.Dispose();
    }

    [Benchmark(Baseline = true)]
    public ValueTask<UnitResult<AuthorizationFailure>> RequirePolicy()
        => _single.EvaluateAsync(AnonymousSecurityContext.Instance);

    [Benchmark]
    public ValueTask<UnitResult<AuthorizationFailure>> RequireAnyPolicy_FirstSucceeds()
        => _anyOf.EvaluateAsync(AnonymousSecurityContext.Instance);

    [Benchmark]
    public ValueTask<UnitResult<AuthorizationFailure>> RequirePolicy_Parameterized()
        => _parameterized.EvaluateAsync(_adult);
}

[Policy("BenchAllow")]
internal sealed class BenchAllowPolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(UnitResult<AuthorizationFailure>.Success());
}

[Policy("BenchDeny")]
internal sealed class BenchDenyPolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(new AuthorizationFailure("bench.deny"));
}

[Policy("BenchMinAge")]
internal sealed class BenchMinAgePolicy : IAuthorizationPolicy<int>
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, int minAge, CancellationToken ct = default)
        => new(ctx is BenchAgedContext aged && aged.Age >= minAge
            ? UnitResult<AuthorizationFailure>.Success()
            : new AuthorizationFailure("bench.too_young"));
}

[RequirePolicy("BenchAllow")]
internal sealed record BenchSingleRequest;

[RequireAnyPolicy("BenchAllow", "BenchDeny")]
internal sealed record BenchAnyOfRequest;

[RequirePolicy("BenchMinAge", 18)]
internal sealed record BenchMinAgeRequest;

internal sealed class BenchAgedContext : ISecurityContext
{
    public BenchAgedContext(string id, int age)
    {
        Id = id;
        Age = age;
    }

    public string Id { get; }
    public int Age { get; }
    public IReadOnlySet<string> Roles { get; } = new HashSet<string>();
    public IReadOnlyDictionary<string, string> Claims { get; } = new Dictionary<string, string>();
}
