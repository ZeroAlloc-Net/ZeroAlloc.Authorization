---
id: failure-shape
title: Failure Shape
sidebar_position: 5
---

# Failure Shape

`bool` says "deny." It does not say *why*. Hosts that need to map deny reasons to API responses, log telemetry, or distinguish "wrong role" from "tenant inactive" use the structured-result pair: `AuthorizationFailure` plus `UnitResult<AuthorizationFailure>`. This is the only return shape — every policy emits it through `EvaluateAsync`.

## AuthorizationFailure

```csharp
public readonly struct AuthorizationFailure
{
    public const string DefaultDenyCode = "policy.deny";

    public string Code { get; }      // non-null, machine-readable
    public string? Reason { get; }   // nullable, human-readable

    public AuthorizationFailure(string code, string? reason = null);
}
```

| Member | Convention |
|---|---|
| `Code` | Non-null, non-empty, machine-readable. Examples: `"policy.deny.role"`, `"tenant.inactive"`, `"scope.missing"`. Hosts switch on this value. |
| `Reason` | Nullable, human-readable. Treat as untrusted-for-display unless the policy author guarantees it — surfacing arbitrary reason strings in API responses can leak internal detail. |
| `DefaultDenyCode` | The constant `"policy.deny"`. Emit it when the policy denies without a more specific code. Lets hosts distinguish "policy denied without explanation" from an explicitly-coded deny. |

A `default(AuthorizationFailure)` returns `DefaultDenyCode` from `Code` (not null) — consumers do not need to guard against null `Code`.

```csharp
var f = default(AuthorizationFailure);
Assert.Equal("policy.deny", f.Code);
Assert.Null(f.Reason);
```

---

## UnitResult&lt;AuthorizationFailure&gt;

`EvaluateAsync` returns `ValueTask<UnitResult<AuthorizationFailure>>` from [`ZeroAlloc.Results`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Results). `UnitResult<TError>` is a value type with two states:

```csharp
// Success: zero allocation, no payload.
return UnitResult<AuthorizationFailure>.Success();

// Failure: implicit conversion from AuthorizationFailure.
return new AuthorizationFailure("tenant.inactive", "tenant is suspended");
```

The implicit conversion is the convenient form — `return new AuthorizationFailure(...)` is enough; you do not need to wrap it in a factory call.

For sync-completing policies, wrap the result in `new ValueTask<...>(syncResult)` — both `UnitResult<AuthorizationFailure>` and `AuthorizationFailure` are value types, so the entire happy path stays on the stack.

---

## Worked example: RichDenyPolicy

```csharp
[Policy("AdminOnly")]
public sealed class AdminOnlyPolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(ctx.Roles.Contains("Admin")
            ? UnitResult<AuthorizationFailure>.Success()
            : new AuthorizationFailure("policy.deny.role", "user is not Admin"));
}
```

A host that resolves `AuthorizerFor<TRequest>` and `await`s `EvaluateAsync(ctx, ct)` reads `result.Error.Code == "policy.deny.role"` and maps it to a 403.

---

## What hosts do with the code

The `Code` is the seam between policies and host responses. Typical uses:

**HTTP status mapping** — switch the code to a status:

```csharp
var result = await authorizer.EvaluateAsync(ctx, ct);
if (result.IsSuccess) return Results.Ok(payload);

return result.Error.Code switch
{
    "tenant.inactive"     => Results.StatusCode(423),  // Locked
    "policy.deny.role"    => Results.Forbid(),          // 403
    "scope.missing"       => Results.StatusCode(403),
    _                     => Results.Unauthorized(),    // 401
};
```

**Structured logs / metrics** — emit the code as a tag:

```csharp
_logger.LogWarning("authorization denied: {Code} — {Reason}",
    result.Error.Code, result.Error.Reason);
_metrics.IncrementCounter("authz.deny", tags: [("code", result.Error.Code)]);
```

**Surfacing reason** — only when the policy author guarantees the text is safe:

```csharp
return Results.Problem(
    title: "Authorization failed",
    detail: result.Error.Reason ?? "Access denied.",
    statusCode: 403);
```

When in doubt, drop `Reason` and rely on `Code` plus a host-controlled message.

---

## See also

- [Policies](policies.md) — implementing `EvaluateAsync`.
- [Sync vs async](sync-vs-async.md) — async-only contract notes and the sync-wrap idiom.
- [`ZeroAlloc.Results`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Results) — the `UnitResult<TError>` type.
