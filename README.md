# ZeroAlloc.Authorization

[![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.Authorization.svg)](https://www.nuget.org/packages/ZeroAlloc.Authorization)
[![Build](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/actions/workflows/ci.yml/badge.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![AOT](https://img.shields.io/badge/AOT--Compatible-passing-brightgreen)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![GitHub Sponsors](https://img.shields.io/github/sponsors/MarcelRoozekrans?style=flat&logo=githubsponsors&color=ea4aaa&label=Sponsor)](https://github.com/sponsors/MarcelRoozekrans)

Authorization primitives for .NET — `ISecurityContext`, `IAuthorizationPolicy`, `[Policy]`, `[RequirePolicy]`, `AnonymousSecurityContext`, and an `AuthorizerFor<TRequest>` dispatcher emitted by the bundled source generator.

Used by:
- [AI.Sentinel](https://github.com/MarcelRoozekrans/AI.Sentinel) — tool-call authorization for `IChatClient`-based agents
- `ZeroAlloc.Mediator.Authorization` v5 — request-handler authorization

## Install

```bash
dotnet add package ZeroAlloc.Authorization
```

Targets `net8.0`, `net9.0`, `net10.0`. The package bundles a Roslyn source generator — no separate `*.Generator` install.

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
    ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default);
}
```

## Writing a policy

```csharp
[Policy("AdminOnly")]
public sealed class AdminOnlyPolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(ctx.Roles.Contains("Admin")
            ? UnitResult<AuthorizationFailure>.Success()
            : new AuthorizationFailure(AuthorizationFailure.DefaultDenyCode, "Admin role required"));
}
```

Bind it on a request type — `[RequirePolicy]` is class/struct-level only, and stacks:

```csharp
[RequirePolicy("AdminOnly")]
public sealed record DeleteUserCommand(string UserId);

[RequirePolicy("ActiveTenant")]
[RequirePolicy("AdminOnly")]
public sealed record PurgeTenantCommand(string TenantId);
```

## Host wiring

The bundled generator emits one `AuthorizerFor<TRequest>` subclass per `[RequirePolicy]`-decorated type plus an `AddZeroAllocAuthorization()` extension on `IServiceCollection`. Hosts call the extension at startup and resolve `AuthorizerFor<T>` per request:

```csharp
using ZeroAlloc.Authorization.Generated;

builder.Services.AddZeroAllocAuthorization();
```

```csharp
var authorizer = sp.GetService<AuthorizerFor<DeleteUserCommand>>();
if (authorizer is not null)
{
    var result = await authorizer.EvaluateAsync(securityContext, ct);
    if (result.IsFailure)
        return Forbid(result.Error); // host maps Code / Reason to its outcome shape
}
```

## Hosts can extend `ISecurityContext`

Hosts define their own subinterface for richer payloads. AI.Sentinel adds `IToolCallSecurityContext : ISecurityContext` with `ToolName` + `Args`. `ZeroAlloc.Mediator.Authorization` v5 adds `IRequestSecurityContext<TRequest>`. Inside the policy body, downcast:

```csharp
public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
    ISecurityContext ctx, CancellationToken ct = default)
    => new(ctx is IToolCallSecurityContext tc && tc.ToolName == "delete_database"
        ? new AuthorizationFailure("tool.destructive", "destructive tool blocked")
        : UnitResult<AuthorizationFailure>.Success());
```

## I/O-bound policies

For checks that need to await something (tenant lookup, external claims validation), mark `EvaluateAsync` as `async`:

```csharp
[Policy("ActiveTenant")]
public sealed class ActiveTenantPolicy(ITenantService tenants) : IAuthorizationPolicy
{
    public async ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
    {
        var active = await tenants.IsActiveAsync(ctx.Id, ct).ConfigureAwait(false);
        return active
            ? UnitResult<AuthorizationFailure>.Success()
            : new AuthorizationFailure("tenant.inactive", "tenant is suspended");
    }
}
```

## Generator diagnostics

The bundled generator emits five compile-time diagnostics:

| ID | Fires when |
|---|---|
| `ZAUTH001` | `[RequirePolicy("X")]` references a policy name with no matching `[Policy("X")]`. |
| `ZAUTH002` | Two `[Policy("X")]` declarations share the same name. |
| `ZAUTH003` | `[Policy]` class doesn't implement `IAuthorizationPolicy`. |
| `ZAUTH004` | `[Policy]` class is abstract or static. |
| `ZAUTH005` | `[RequirePolicy]` placed on a non-class/non-struct target. |

## Performance

BenchmarkDotNet (BDN ShortRun, .NET 10 release build, x64) — happy path on a simple role-check policy:

| Method | Mean | Allocated |
|---|---:|---:|
| `EvaluateAsync` | ~99 ns | 0 B |

Source: [`benchmarks/ZeroAlloc.Authorization.Benchmarks/PolicyEvaluationBenchmarks.cs`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/blob/main/benchmarks/ZeroAlloc.Authorization.Benchmarks/PolicyEvaluationBenchmarks.cs).

The contract is enforced as zero-allocation by:
1. `<IsAotCompatible>true</IsAotCompatible>` on the main library (build-time IL2026/IL3050 analyzers fire on any reflection regression).
2. The `samples/ZeroAlloc.Authorization.AotSmoke/` console app, exercised on each CI run with `PublishAot=true`.
3. The benchmark project above.

## License

MIT.
