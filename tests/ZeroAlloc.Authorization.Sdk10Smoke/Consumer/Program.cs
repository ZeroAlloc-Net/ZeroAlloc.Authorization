using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Authorization;
using ZeroAlloc.Authorization.Generated;
using ZeroAlloc.Results;

// AddZeroAllocAuthorization and AuthorizerFor<T> registrations exist only if the compiler
// loaded the generator.
var services = new ServiceCollection();
services.AddZeroAllocAuthorization();
using var sp = services.BuildServiceProvider();
using var scope = sp.CreateScope();

var authorizer = scope.ServiceProvider.GetRequiredService<AuthorizerFor<SmokeRequest>>();
var result = await authorizer.EvaluateAsync(AnonymousSecurityContext.Instance);
if (!result.IsSuccess)
{
    Console.Error.WriteLine("FAIL: the generated authorizer denied a request its policy allows");
    return 1;
}

Console.WriteLine("SDK 10 consumer: generator loaded, request authorized through the generated authorizer.");
return 0;

[Policy("smoke")]
internal sealed class SmokePolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(UnitResult<AuthorizationFailure>.Success());
}

[RequirePolicy("smoke")]
internal sealed record SmokeRequest;
