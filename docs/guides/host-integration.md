---
id: host-integration
title: Host Integration
sidebar_position: 2
---

# Host Integration

ZeroAlloc.Authorization ships only the contract. Everything that *runs* — discovery, registration, dispatch, response mapping — lives in the host. This guide describes the responsibilities a host must satisfy and the patterns existing hosts use.

See also: [policies](../core-concepts/policies.md), [security-context](../core-concepts/security-context.md), [attributes](../attributes.md).

---

## Host responsibilities

A host integrates ZeroAlloc.Authorization by doing four things, in order:

1. **Resolve `[Authorize("Name")]`** annotations on dispatch targets (methods, request handlers, tool-call signatures). Whether resolution happens at compile time (source generator) or runtime (reflection on first call) is the host's choice.
2. **Discover `[AuthorizationPolicy("Name")]`-attributed classes** and build a `name → factory` registry so a `[Authorize("AdminOnly")]` reference can be resolved to a callable policy instance. Discovery strategies vary — assembly walks, source generators, hand-registration are all valid.
3. **Construct an `ISecurityContext`** (or a host-specific subinterface) per call from whatever caller-identity material the host has — HTTP request, gRPC metadata, tool-call invocation, mediator request, etc.
4. **Invoke `EvaluateAsync(ctx, ct)`** (or one of the simpler entry points) and translate the resulting `UnitResult<AuthorizationFailure>` into the host's outcome — HTTP response, typed exception, log entry, structured tool-call refusal.

The contract package itself does **none** of this. There is no scanner, no DI extension, no dispatcher. The five contract types simply carry names and define the policy shape.

---

## Host-specific subinterfaces

`ISecurityContext` carries three fields. When a host needs to flow more — a tool name, a request payload, an HTTP path — it defines a subinterface and policies downcast to read it.

**AI.Sentinel — tool-call host:**

```csharp
public interface IToolCallSecurityContext : ISecurityContext
{
    string ToolName { get; }
    IReadOnlyDictionary<string, object?> Args { get; }
}
```

The host populates the subinterface when constructing the per-call context, then passes it as `ISecurityContext` to the policy. The policy downcasts inside the body when it cares:

```csharp
[AuthorizationPolicy("NoDestructiveTools")]
public sealed class NoDestructiveToolsPolicy : IAuthorizationPolicy
{
    public bool IsAuthorized(ISecurityContext ctx)
    {
        if (ctx is IToolCallSecurityContext tc && tc.ToolName == "delete_database")
            return false;
        return true;
    }
}
```

**ZeroAlloc.Mediator.Authorization (planned) — request-scoped host:**

```csharp
public interface IRequestSecurityContext<TRequest> : ISecurityContext
{
    TRequest Request { get; }
}
```

Same pattern — the host wraps the live request in the typed subinterface, the policy downcasts to read it. A policy that *requires* the host context (i.e. it makes no sense outside that host) should fail closed when the downcast misses, rather than silently allow.

---

## Mapping `AuthorizationFailure.Code` to outcomes

The whole point of `Evaluate` returning a coded failure is that hosts switch on `Code` to produce structured responses. Each host translates differently.

**HTTP host — switch to a status:**

```csharp
var result = await policy.EvaluateAsync(ctx, ct);
if (result.IsSuccess) return Results.Ok(payload);

return result.Error.Code switch
{
    "tenant.inactive"   => Results.StatusCode(423),  // Locked
    "policy.deny.role"  => Results.Forbid(),          // 403
    "scope.missing"     => Results.StatusCode(403),
    _                   => Results.Unauthorized(),    // 401
};
```

**Mediator host — convert to a result type:**

```csharp
var result = await policy.EvaluateAsync(ctx, ct);
if (result.IsSuccess) return await next(request, ct);

// Either bubble as a typed Result<T, AuthorizationFailure>...
return Result.Failure<TResponse, AuthorizationFailure>(result.Error);

// ...or throw a typed exception the pipeline knows about:
throw new UnauthorizedException(result.Error.Code, result.Error.Reason);
```

**Tool-call host — refuse and emit a structured rejection:**

```csharp
var result = await policy.EvaluateAsync(ctx, ct);
if (!result.IsSuccess)
{
    return new ToolCallRejection(
        toolName: ctx.ToolName,
        code: result.Error.Code,
        message: result.Error.Reason ?? "Tool call denied.");
}
```

The shape of the response is host-specific; what matters is that the policy emits a *machine-readable code* and the host has the freedom to map it.

For full deny-code conventions and the `AuthorizationFailure` shape, see [failure-shape](../core-concepts/failure-shape.md).

---

## What the contract package does NOT do

This is worth stating explicitly because it shapes every host integration:

- **No scanning.** The package never looks at your assemblies. Hosts walk `[AuthorizationPolicy]`-attributed types themselves.
- **No DI registration.** There is no `AddZeroAllocAuthorization()` extension method. Hosts decide the lifetime (singleton / scoped / transient) and add policies to whichever container they target.
- **No dispatch.** Nothing calls `IsAuthorized` for you. The host's pipeline (request handler, tool-call broker, HTTP middleware) is where invocation lives.
- **No combinator semantics.** When multiple `[Authorize]` attributes stack on a single method, the contract is silent on whether they're AND or OR — every existing host treats them as AND, but that is host policy, not contract.

If you need an integration that does not yet exist, write a host. The contract is small on purpose so the same five types can be reused across HTTP, mediator, tool-call, gRPC, and any other dispatch surface without one host's assumptions leaking into another.
