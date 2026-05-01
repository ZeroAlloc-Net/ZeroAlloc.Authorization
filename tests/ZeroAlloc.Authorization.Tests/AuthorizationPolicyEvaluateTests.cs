using ZeroAlloc.Results;

namespace ZeroAlloc.Authorization.Tests;

public class AuthorizationPolicyEvaluateTests
{
    private static readonly TestContext Ctx = new("alice",
        new HashSet<string> { "Admin" },
        new Dictionary<string, string>());

    [Fact]
    public void Evaluate_DefaultsToWrapping_IsAuthorized_True()
    {
        IAuthorizationPolicy policy = new AllowingPolicy();
        var result = policy.Evaluate(Ctx);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Evaluate_DefaultsToWrapping_IsAuthorized_False_WithDefaultDenyCode()
    {
        IAuthorizationPolicy policy = new DenyingPolicy();
        var result = policy.Evaluate(Ctx);
        Assert.False(result.IsSuccess);
        Assert.Equal(AuthorizationFailure.DefaultDenyCode, result.Error.Code);
        Assert.Null(result.Error.Reason);
    }

    [Fact]
    public async Task EvaluateAsync_DefaultsToWrapping_IsAuthorizedAsync_True()
    {
        IAuthorizationPolicy policy = new AllowingPolicy();
        var result = await policy.EvaluateAsync(Ctx);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task EvaluateAsync_DefaultsToWrapping_IsAuthorizedAsync_False_WithDefaultDenyCode()
    {
        IAuthorizationPolicy policy = new DenyingPolicy();
        var result = await policy.EvaluateAsync(Ctx);
        Assert.False(result.IsSuccess);
        Assert.Equal(AuthorizationFailure.DefaultDenyCode, result.Error.Code);
    }

    [Fact]
    public void Evaluate_OverrideEmitsCustomCode()
    {
        IAuthorizationPolicy policy = new RichDenyPolicy();
        var result = policy.Evaluate(Ctx);
        Assert.False(result.IsSuccess);
        Assert.Equal("policy.deny.role", result.Error.Code);
        Assert.Equal("user is not Admin", result.Error.Reason);
    }

    private sealed class AllowingPolicy : IAuthorizationPolicy
    {
        public bool IsAuthorized(ISecurityContext ctx) => true;
    }

    private sealed class DenyingPolicy : IAuthorizationPolicy
    {
        public bool IsAuthorized(ISecurityContext ctx) => false;
    }

    private sealed class RichDenyPolicy : IAuthorizationPolicy
    {
        public bool IsAuthorized(ISecurityContext ctx) => false;
        public UnitResult<AuthorizationFailure> Evaluate(ISecurityContext ctx)
            => new AuthorizationFailure("policy.deny.role", "user is not Admin");
    }

    private sealed record TestContext(string Id,
                                      IReadOnlySet<string> Roles,
                                      IReadOnlyDictionary<string, string> Claims) : ISecurityContext;
}
