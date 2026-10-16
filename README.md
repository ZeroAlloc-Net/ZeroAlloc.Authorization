# ZeroAlloc.Authorization

[![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.Authorization.svg)](https://www.nuget.org/packages/ZeroAlloc.Authorization)
[![Build](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/actions/workflows/ci.yml/badge.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/blob/main/LICENSE)
[![AOT](https://img.shields.io/badge/AOT--Compatible-passing-brightgreen)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![GitHub Sponsors](https://img.shields.io/github/sponsors/MarcelRoozekrans?style=flat&logo=githubsponsors&color=ea4aaa&label=Sponsor)](https://github.com/sponsors/MarcelRoozekrans)

Source-generated authorization policies for any .NET host — policy names checked at compile time, no reflection, 0 B allocated per check, NativeAOT-ready.

## Why ZeroAlloc.Authorization

- **0 B per authorization check.** `AllocationGate` tests assert a 0-byte budget on every run for the allow and deny paths, a parameterized policy and an any-of group.
- **A typo is a build error, not a 403 in production.** Requiring a policy name that no `[Policy]` declares fails the build (`ZAUTH001`); eight generator diagnostics in total catch wiring mistakes at compile time.
- **No reflection, works under NativeAOT and trimming.** The bundled generator emits one dispatcher per request type plus the DI registrations. The library builds with `IsAotCompatible`, and CI publishes and runs a `PublishAot=true` sample on every run.
- **Not tied to ASP.NET Core.** Policies see a small `ISecurityContext` (id, roles, claims). Mediators, agent tool calls and your own dispatchers can all evaluate the same policies.

## Install

```bash
dotnet add package ZeroAlloc.Authorization
dotnet add package Microsoft.Extensions.DependencyInjection # the generated registrations target it
```

Targets `net8.0`, `net9.0` and `net10.0`. The source generator ships inside the package. ASP.NET Core
and generic-host apps already reference the DI package.

## Quick start

```csharp
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Authorization;
using ZeroAlloc.Authorization.Generated;
using ZeroAlloc.Results;

var services = new ServiceCollection().AddZeroAllocAuthorization(); // generated
using var scope = services.BuildServiceProvider().CreateScope();
var authorizer = scope.ServiceProvider.GetRequiredService<AuthorizerFor<DeleteUser>>();

var result = await authorizer.EvaluateAsync(AnonymousSecurityContext.Instance);
Console.WriteLine(result.IsFailure ? $"denied: {result.Error.Code}" : "allowed");
// denied: auth.admin_required

[Policy("AdminOnly")]
public sealed class AdminOnly : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(ISecurityContext ctx, CancellationToken ct = default)
        => new(ctx.Roles.Contains("Admin")
            ? UnitResult<AuthorizationFailure>.Success()
            : new AuthorizationFailure("auth.admin_required", "Admin role required"));
}

[RequirePolicy("AdminOnly")]
public sealed record DeleteUser(string UserId);
```

`AddZeroAllocAuthorization()` and `AuthorizerFor<DeleteUser>` come from the generator: it found the
`[Policy]` class and the `[RequirePolicy]` request and wrote the dispatch for you. Rename the policy
without updating the request and the build fails.

## What you get

- **Named policies** with `[Policy("Name")]`, bound to request types with `[RequirePolicy("Name")]`. Stack several and all must allow.
- **Any-of groups** with `[RequireAnyPolicy("A", "B")]`: the first policy that allows short-circuits the group.
- **Parameterized policies** such as `[RequirePolicy("MinAge", 18)]`. Arguments are type-checked against the policy at compile time and passed typed, without boxing.
- **Resource-based authorization** through `IResourceSecurityContext<TResource>`: when the host supplies it, a policy can inspect the resource it guards.
- **Structured denials.** A failure carries a machine-readable code and a reason, returned as a `UnitResult<AuthorizationFailure>` your host maps to HTTP 403, an exception or a refused tool call.
- **Async policies** for checks that need I/O, such as a tenant lookup.
- **Your own context.** Extend `ISecurityContext` with host-specific data; `AnonymousSecurityContext.Instance` covers callers with no identity.
- **Mediator integration:** [ZeroAlloc.Mediator.Authorization](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/tree/main/src/ZeroAlloc.Mediator.Authorization) runs these policies as a pipeline behavior before the handler of any request that carries `[RequirePolicy]`.
- **Used by** [AI.Sentinel](https://github.com/MarcelRoozekrans/AI.Sentinel) for tool-call authorization of `IChatClient`-based agents.

Diagnostics `ZAUTH001`–`ZAUTH008` are listed in [Getting started](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/blob/main/docs/getting-started.md#generator-diagnostics).

## Performance

Allocation per call, asserted by the `AllocationGate` tests in
[`AllocationBudgetTests.cs`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/blob/main/tests/ZeroAlloc.Authorization.Tests/AllocationBudgetTests.cs):

| Path | Allocated |
|---|---:|
| `EvaluateAsync`, allow and deny | 0 B |
| Generated dispatch, parameterized `[RequirePolicy("MinAge", 18)]` | 0 B |
| Generated dispatch, `[RequireAnyPolicy]` with the first policy allowing | 0 B |

No head-to-head benchmark against another authorization library is published yet. Timings, the
benchmark sources and how zero allocation is enforced are in
[docs/performance.md](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/blob/main/docs/performance.md).

## Learn more

- **Documentation:** [authorization.zeroalloc.net](https://authorization.zeroalloc.net) — policies, attributes, host integration and testing guides.
- **Related ZeroAlloc packages:**
  - [ZeroAlloc.Mediator](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator) ([docs](https://mediator.zeroalloc.net)) — source-generated mediator; its `ZeroAlloc.Mediator.Authorization` package enforces these policies before handlers run.
  - [ZeroAlloc.Pipeline](https://github.com/ZeroAlloc-Net/ZeroAlloc.Pipeline) ([docs](https://pipeline.zeroalloc.net)) — the behavior pipeline the Mediator's authorization step runs in.
  - [ZeroAlloc.Results](https://github.com/ZeroAlloc-Net/ZeroAlloc.Results) ([docs](https://results.zeroalloc.net)) — the `UnitResult<AuthorizationFailure>` type every policy returns.
  - [ZeroAlloc.Validation](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation) ([docs](https://validation.zeroalloc.net)) — source-generated validation that plugs into the same Mediator pipeline.
- **Contributing:** [issues](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/issues) and [pull requests](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/pulls) are welcome.
- **License:** [MIT](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/blob/main/LICENSE).
