using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ZeroAlloc.Results;

namespace ZeroAlloc.Authorization.Tests;

public sealed class AuthorizerForTests
{
    private sealed record FakeRequest(int Id);

    private sealed class AlwaysSucceedingAuthorizer : AuthorizerFor<FakeRequest>
    {
        public override ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
            ISecurityContext ctx, CancellationToken ct = default)
            => new(UnitResult<AuthorizationFailure>.Success());
    }

    [Fact]
    public async Task AuthorizerFor_Subclass_CanReturnSuccess()
    {
        var authorizer = new AlwaysSucceedingAuthorizer();
        var result = await authorizer.EvaluateAsync(AnonymousSecurityContext.Instance);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void AuthorizerFor_IsAbstract()
    {
        Assert.True(typeof(AuthorizerFor<FakeRequest>).IsAbstract);
    }
}
