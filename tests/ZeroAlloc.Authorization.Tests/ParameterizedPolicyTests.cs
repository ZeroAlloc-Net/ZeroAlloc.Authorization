using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ZeroAlloc.Authorization;
using ZeroAlloc.Authorization.Generated;
using ZeroAlloc.Results;

namespace ZeroAlloc.Authorization.Tests;

/// <summary>
/// Runtime semantics for typed-arg dispatch through IAuthorizationPolicy&lt;int&gt;.
/// Exercises the v2.1 generator emit for [RequirePolicy("Name", arg0, ...)] —
/// the generator must dispatch to EvaluateAsync(ctx, 18, ct) with a TYPED int
/// (not a boxed object) on the wire.
/// </summary>
public sealed class ParameterizedPolicyTests
{
    [Fact]
    public async Task RequirePolicy_WithIntArg_DispatchesTypedArgument()
    {
        MinAgePolicy.LastArgReceived = -1;
        MinAgePolicy.LastArgRuntimeType = null;

        var services = new ServiceCollection();
        services.AddZeroAllocAuthorization();
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var authorizer = scope.ServiceProvider
            .GetRequiredService<AuthorizerFor<MinAgeProtectedRequest>>();

        // The configured threshold matches the request's [RequirePolicy("MinAge", 18)] arg —
        // success path proves the generator forwarded the typed 18 correctly.
        var ctx = new AgedTestContext("alice", age: 21);
        var result = await authorizer.EvaluateAsync(ctx);

        Assert.True(result.IsSuccess);
        Assert.Equal(18, MinAgePolicy.LastArgReceived);
        // CLR runtime type at the receiving side must be int, NOT boxed object —
        // proves typed dispatch through IAuthorizationPolicy<int>.
        Assert.Equal(typeof(int), MinAgePolicy.LastArgRuntimeType);
    }

    [Fact]
    public async Task RequirePolicy_WithIntArg_FailsWhenContextDoesNotMeet()
    {
        MinAgePolicy.LastArgReceived = -1;

        var services = new ServiceCollection();
        services.AddZeroAllocAuthorization();
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var authorizer = scope.ServiceProvider
            .GetRequiredService<AuthorizerFor<MinAgeProtectedRequest>>();

        var ctx = new AgedTestContext("alice", age: 16);
        var result = await authorizer.EvaluateAsync(ctx);

        Assert.False(result.IsSuccess);
        Assert.Equal("min_age.too_young", result.Error.Code);
        Assert.Equal(18, MinAgePolicy.LastArgReceived);
    }
}

/// <summary>Test context that carries an explicit age claim for the MinAge policy.</summary>
internal sealed class AgedTestContext : ISecurityContext
{
    public AgedTestContext(string id, int age)
    {
        Id = id;
        Age = age;
        Roles = new HashSet<string>();
        Claims = new Dictionary<string, string>();
    }
    public string Id { get; }
    public int Age { get; }
    public IReadOnlySet<string> Roles { get; }
    public IReadOnlyDictionary<string, string> Claims { get; }
}

/// <summary>Parameterized policy: receives an int threshold at dispatch time.
/// Records the typed argument it received so the test can prove the generator
/// forwarded a typed int (not a boxed object).</summary>
[Policy("MinAge")]
internal sealed class MinAgePolicy : IAuthorizationPolicy<int>
{
    public static int LastArgReceived;
    public static Type? LastArgRuntimeType;

    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, int arg1, CancellationToken ct = default)
    {
        LastArgReceived = arg1;
        LastArgRuntimeType = arg1.GetType();

        var age = ctx is AgedTestContext withAge ? withAge.Age : 0;
        return age >= arg1
            ? new ValueTask<UnitResult<AuthorizationFailure>>(
                UnitResult<AuthorizationFailure>.Success())
            : new ValueTask<UnitResult<AuthorizationFailure>>(
                new AuthorizationFailure("min_age.too_young", $"age {age} < {arg1}"));
    }
}

[RequirePolicy("MinAge", 18)]
internal sealed record MinAgeProtectedRequest;
