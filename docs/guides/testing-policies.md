---
id: testing-policies
title: Testing Policies
sidebar_position: 3
---

# Testing Policies

Policies are small, side-effect-free classes — easy to unit-test without any host or DI container. This guide shows the patterns the in-repo test suite uses.

See also: [policies](../core-concepts/policies.md), [security-context](../core-concepts/security-context.md), [failure-shape](../core-concepts/failure-shape.md).

---

## Stub `ISecurityContext` with a record

`ISecurityContext` has three properties — a record literal is the cheapest stub:

```csharp
private sealed record TestContext(
    string Id,
    IReadOnlySet<string> Roles,
    IReadOnlyDictionary<string, string> Claims) : ISecurityContext;
```

Construction is a one-liner:

```csharp
private static readonly TestContext Admin = new(
    "alice",
    new HashSet<string> { "Admin" },
    new Dictionary<string, string>());
```

For the anonymous case, use `AnonymousSecurityContext.Instance` directly — it's a singleton and reference equality works.

---

## Test a sync-completing policy

For a CPU-bound policy that returns a completed `ValueTask`, arrange the context, act, assert. `xUnit`'s `[Fact]` + `async Task` covers the await — or call `.GetAwaiter().GetResult()` on a known-completed `ValueTask` if you prefer fully-sync tests.

```csharp
[Fact]
public async Task EvaluateAsync_AdminInRoles_Success()
{
    var policy = new AdminOnlyPolicy();
    var result = await policy.EvaluateAsync(Admin);
    Assert.True(result.IsSuccess);
}

[Fact]
public async Task EvaluateAsync_NoAdminRole_FailsWithDefaultDenyCode()
{
    var ctx = new TestContext("bob", new HashSet<string>(), new Dictionary<string, string>());
    var policy = new AdminOnlyPolicy();

    var result = await policy.EvaluateAsync(ctx);

    Assert.False(result.IsSuccess);
    Assert.Equal(AuthorizationFailure.DefaultDenyCode, result.Error.Code);
}
```

---

## Test an I/O-bound policy

Inject a fake dependency, await the call:

```csharp
[Fact]
public async Task EvaluateAsync_ActiveTenant_Success()
{
    var tenants = new FakeTenantService(active: true);
    var policy = new ActiveTenantPolicy(tenants);

    var result = await policy.EvaluateAsync(Admin);

    Assert.True(result.IsSuccess);
}

[Fact]
public async Task EvaluateAsync_InactiveTenant_EmitsTenantInactive()
{
    var tenants = new FakeTenantService(active: false);
    var policy = new ActiveTenantPolicy(tenants);

    var result = await policy.EvaluateAsync(Admin);

    Assert.False(result.IsSuccess);
    Assert.Equal("tenant.inactive", result.Error.Code);
}
```

Cast to `IAuthorizationPolicy` if you want to exercise the interface dispatch path explicitly — both `(policy).EvaluateAsync(...)` and `((IAuthorizationPolicy)policy).EvaluateAsync(...)` hit the same body, but the interface-typed call confirms the seam a host actually uses.

---

## Testing structured deny

When a policy emits a coded `AuthorizationFailure`, assert all three pieces — success flag, code, reason:

```csharp
[Fact]
public async Task EvaluateAsync_NoAdmin_EmitsRoleDeny()
{
    var ctx = new TestContext("bob", new HashSet<string>(), new Dictionary<string, string>());
    IAuthorizationPolicy policy = new RichDenyPolicy();

    var result = await policy.EvaluateAsync(ctx);

    Assert.False(result.IsSuccess);
    Assert.Equal("policy.deny.role", result.Error.Code);
    Assert.Equal("user is not Admin", result.Error.Reason);
}
```

---

## Testing host subinterfaces

When a policy downcasts to a host-specific subinterface (`IToolCallSecurityContext`, `IRequestSecurityContext<T>`, etc.), stub the subinterface the same way — a record literal:

```csharp
private sealed record ToolCallContext(
    string Id,
    IReadOnlySet<string> Roles,
    IReadOnlyDictionary<string, string> Claims,
    string ToolName,
    IReadOnlyDictionary<string, object?> Args) : IToolCallSecurityContext;

[Fact]
public async Task EvaluateAsync_DestructiveTool_Denies()
{
    var ctx = new ToolCallContext(
        "alice",
        new HashSet<string> { "Admin" },
        new Dictionary<string, string>(),
        ToolName: "delete_database",
        Args: new Dictionary<string, object?>());

    var policy = new NoDestructiveToolsPolicy();

    var result = await policy.EvaluateAsync(ctx);

    Assert.False(result.IsSuccess);
}
```

The downcast inside the policy body sees the subinterface and reads `ToolName`. Test both branches — the subinterface case and the bare `ISecurityContext` fallback — to confirm the policy fails closed when the host context is missing (or, if your policy is meant to be permissive without it, that it allows).

---

## Testing cancellation

Pre-cancel a `CancellationTokenSource` and assert that `EvaluateAsync` throws `OperationCanceledException`. A policy that calls `ct.ThrowIfCancellationRequested()` synchronously before its body short-circuits without dispatching:

```csharp
[Fact]
public async Task EvaluateAsync_PreCancelledToken_Throws()
{
    IAuthorizationPolicy policy = new AdminOnlyPolicy();
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
        async () => await policy.EvaluateAsync(Admin, cts.Token).ConfigureAwait(false));
}
```

For policies whose `EvaluateAsync` does real I/O, also test mid-flight cancellation by cancelling after the call begins; pass `ct` through to the underlying client and trust it to propagate.

---

## Conventions

- **No DI in unit tests.** Construct the policy with `new` and pass dependencies as constructor arguments — it's a sealed class with no magic.
- **Use `IAuthorizationPolicy`-typed variables** when invoking the dispatch entry point so you exercise the same surface a host (and the generator-emitted `AuthorizerFor<T>`) would.
- **One assertion family per `[Fact]`** — code, reason, and `IsSuccess` together count as one family; mixing unrelated checks bloats failure messages.
- **Mirror in-repo test naming** — `Method_Condition_Outcome` (e.g. `EvaluateAsync_NoAdmin_EmitsRoleDeny`). Future readers find tests by grepping the condition.
