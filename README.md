# ZeroAlloc.Authorization

[![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.Authorization.svg)](https://www.nuget.org/packages/ZeroAlloc.Authorization)
[![Build](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/actions/workflows/ci.yml/badge.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![AOT](https://img.shields.io/badge/AOT--Compatible-passing-brightgreen)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![GitHub Sponsors](https://img.shields.io/github/sponsors/MarcelRoozekrans?style=flat&logo=githubsponsors&color=ea4aaa&label=Sponsor)](https://github.com/sponsors/MarcelRoozekrans)

Authorization primitives for .NET. Five types — `ISecurityContext`, `IAuthorizationPolicy`, `[Authorize]`, `[AuthorizationPolicy]`, `AnonymousSecurityContext` — designed to be shared across hosts that need a unified policy contract.

Used by:
- [AI.Sentinel](https://github.com/MarcelRoozekrans/AI.Sentinel) — tool-call authorization for `IChatClient`-based agents
- ZeroAlloc.Mediator.Authorization (planned) — request-handler authorization

## Install

```bash
dotnet add package ZeroAlloc.Authorization
```

Targets `net8.0`, `net9.0`, `net10.0`.

> **Host required.** This package only ships the contract types. A host (AI.Sentinel, ZeroAlloc.Mediator.Authorization, your own dispatcher) must match `[Authorize]` to a registered `[AuthorizationPolicy]` and invoke `IsAuthorized` / `IsAuthorizedAsync` before dispatch.

> **Note:** if you're in an ASP.NET Core project, `using ZeroAlloc.Authorization;` will collide with `using Microsoft.AspNetCore.Authorization;` over the `[Authorize]` name. Use a `using` alias (`using ZAuthorize = ZeroAlloc.Authorization;`) or fully-qualify one side at the call site.

## The contract

```csharp
public interface ISecurityContext
{
    string Id { get; }
    IReadOnlySet<string> Roles { get; }
    IReadOnlyDictionary<string, string> Claims { get; }
}

public interface IAuthorizationPolicy
{
    bool IsAuthorized(ISecurityContext ctx);
    ValueTask<bool> IsAuthorizedAsync(ISecurityContext ctx, CancellationToken ct = default)
        => ValueTask.FromResult(IsAuthorized(ctx));
}
```

## Writing a policy

```csharp
[AuthorizationPolicy("AdminOnly")]
public sealed class AdminOnlyPolicy : IAuthorizationPolicy
{
    public bool IsAuthorized(ISecurityContext ctx) => ctx.Roles.Contains("Admin");
}
```

Bind it on a method:

```csharp
public sealed class UserService
{
    [Authorize("AdminOnly")]
    public Task DeleteUserAsync(string userId) { ... }
}
```

## Hosts can extend `ISecurityContext`

Hosts define their own subinterface for richer payloads. AI.Sentinel adds `IToolCallSecurityContext : ISecurityContext` with `ToolName` + `Args`. Mediator.Authorization will add `IRequestSecurityContext<TRequest>`. Inside the policy body, downcast:

```csharp
public bool IsAuthorized(ISecurityContext ctx)
    => ctx is IToolCallSecurityContext tc && tc.ToolName != "delete_database";
```

## Async overrides

For I/O-bound checks (tenant lookup, external claims validation), override `IsAuthorizedAsync`:

```csharp
public sealed class TenantPolicy(ITenantService tenants) : IAuthorizationPolicy
{
    public bool IsAuthorized(ISecurityContext ctx) =>
        throw new InvalidOperationException("Use async — tenant lookup is I/O-bound.");

    public async ValueTask<bool> IsAuthorizedAsync(ISecurityContext ctx, CancellationToken ct = default)
        => await tenants.IsActiveAsync(ctx.Id, ct).ConfigureAwait(false);
}
```

The host is responsible for calling the async overload.

## Performance

BenchmarkDotNet (BDN ShortRun, .NET 10 release build, x64) — happy path on a simple role-check policy. The full release-time benchmark uses BDN's default job for tight confidence intervals; the indicative numbers below are from a development-time short run.

| Method | Mean | Allocated |
|---|---:|---:|
| `IsAuthorized` | ~9 ns | 0 B |
| `IsAuthorizedAsync` | ~31 ns | 0 B |
| `Evaluate` | ~7 ns | 0 B |
| `EvaluateAsync` | ~99 ns | 0 B |

Source: [`benchmarks/ZeroAlloc.Authorization.Benchmarks/PolicyEvaluationBenchmarks.cs`](benchmarks/ZeroAlloc.Authorization.Benchmarks/PolicyEvaluationBenchmarks.cs).

The contract is enforced as zero-allocation by:
1. `<IsAotCompatible>true</IsAotCompatible>` on the main library (build-time IL2026/IL3050 analyzers fire on any reflection regression).
2. The `samples/ZeroAlloc.Authorization.AotSmoke/` console app, exercised on each CI run with `PublishAot=true`.
3. The benchmark project above.

## License

MIT.
