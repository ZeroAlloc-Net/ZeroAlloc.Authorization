using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ZeroAlloc.Authorization;
using ZeroAlloc.Authorization.Generated;
using ZeroAlloc.Results;

namespace ZeroAlloc.Authorization.Tests;

/// <summary>
/// Runtime semantics for IResourceSecurityContext&lt;TResource&gt;:
///   * A policy can type-check the context via
///     <c>ctx is IResourceSecurityContext&lt;Post&gt; rc</c> and access
///     <c>rc.Resource</c>.
///   * A non-resource context falls through to <c>false</c> on the type-check —
///     v2.1 ships the contract dormant; non-resource hosts must not produce
///     spurious success.
/// </summary>
public sealed class ResourceSecurityContextTests
{
    [Fact]
    public async Task OwnerOnly_OwnerContext_Succeeds()
    {
        var services = new ServiceCollection();
        services.AddZeroAllocAuthorization();
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var authorizer = scope.ServiceProvider
            .GetRequiredService<AuthorizerFor<OwnerOnlyRequest>>();

        // ctx.Id == post.OwnerId — owner accessing their own resource.
        var post = new Post("post-1", OwnerId: "alice");
        var ctx = new TestPostContext("alice", post);

        var result = await authorizer.EvaluateAsync(ctx);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task OwnerOnly_AnonymousContext_Fails_BecauseTypeCheckFallsThrough()
    {
        var services = new ServiceCollection();
        services.AddZeroAllocAuthorization();
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var authorizer = scope.ServiceProvider
            .GetRequiredService<AuthorizerFor<OwnerOnlyRequest>>();

        // AnonymousSecurityContext does NOT implement IResourceSecurityContext<Post>;
        // the policy's pattern match falls through to false → failure.
        var result = await authorizer.EvaluateAsync(AnonymousSecurityContext.Instance);

        Assert.False(result.IsSuccess);
        Assert.Equal("resource.not_owner", result.Error.Code);
    }

    /// <summary>Test fixture resource type. Records are convenient because the
    /// generator's emit treats them identically to classes for [RequirePolicy].</summary>
    public sealed record Post(string Id, string OwnerId);

    /// <summary>A test security context that ALSO carries a typed Post resource.
    /// Demonstrates the v2.1 dormant contract: hosts populate the typed-resource
    /// context, and policies access it via the IResourceSecurityContext&lt;T&gt;
    /// pattern match.</summary>
    private sealed class TestPostContext : IResourceSecurityContext<Post>
    {
        public TestPostContext(string id, Post resource)
        {
            Id = id;
            Resource = resource;
            Roles = new HashSet<string>();
            Claims = new Dictionary<string, string>();
        }
        public string Id { get; }
        public Post Resource { get; }
        public IReadOnlySet<string> Roles { get; }
        public IReadOnlyDictionary<string, string> Claims { get; }
    }
}

[Policy("OwnerOnly")]
internal sealed class OwnerOnlyPolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(ctx is IResourceSecurityContext<ResourceSecurityContextTests.Post> rc
                && string.Equals(rc.Resource.OwnerId, ctx.Id, StringComparison.Ordinal)
            ? UnitResult<AuthorizationFailure>.Success()
            : new AuthorizationFailure("resource.not_owner"));
}

[RequirePolicy("OwnerOnly")]
internal sealed record OwnerOnlyRequest;
