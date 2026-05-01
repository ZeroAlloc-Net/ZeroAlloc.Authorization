---
id: testing-policies
title: Testing Policies
sidebar_position: 3
---

# Testing Policies

Policies are small, side-effect-free classes — easy to unit-test without any host or DI container. This guide shows the patterns the in-repo test suite uses (see `tests/ZeroAlloc.Authorization.Tests/AuthorizationPolicyEvaluateTests.cs` for tone and conventions).

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

## Test the sync override

For a pure-CPU policy, arrange the context, act, assert:

```csharp
[Fact]
public void IsAuthorized_AdminInRoles_ReturnsTrue()
{
    var policy = new AdminOnlyPolicy();
    Assert.True(policy.IsAuthorized(Admin));
}

[Fact]
public void IsAuthorized_NoAdminRole_ReturnsFalse()
{
    var ctx = new TestContext("bob", new HashSet<string>(), new Dictionary<string, string>());
    var policy = new AdminOnlyPolicy();
    Assert.False(policy.IsAuthorized(ctx));
}
```

Same pattern for `Evaluate` — assert on `result.IsSuccess`:

```csharp
[Fact]
public void Evaluate_AdminInRoles_Success()
{
    var policy = new AdminOnlyPolicy();
    var result = policy.Evaluate(Admin);
    Assert.True(result.IsSuccess);
}
```

---

## Test the async override

`xUnit`'s `[Fact]` + `async Task` covers `IsAuthorizedAsync` and `EvaluateAsync`:

```csharp
[Fact]
public async Task IsAuthorizedAsync_ActiveTenant_ReturnsTrue()
{
    var tenants = new FakeTenantService(active: true);
    var policy = new TenantPolicy(tenants);
    Assert.True(await ((IAuthorizationPolicy)policy).IsAuthorizedAsync(Admin));
}
```

Cast to `IAuthorizationPolicy` to invoke the interface entry point — that confirms the dispatch path your host actually uses.

---

## Testing structured deny

When a policy overrides `Evaluate` to emit a coded `AuthorizationFailure`, assert all three pieces — success flag, code, reason:

```csharp
[Fact]
public void Evaluate_NoAdmin_EmitsRoleDeny()
{
    var ctx = new TestContext("bob", new HashSet<string>(), new Dictionary<string, string>());
    IAuthorizationPolicy policy = new RichDenyPolicy();

    var result = policy.Evaluate(ctx);

    Assert.False(result.IsSuccess);
    Assert.Equal("policy.deny.role", result.Error.Code);
    Assert.Equal("user is not Admin", result.Error.Reason);
}
```

This mirrors `AuthorizationPolicyEvaluateTests.Evaluate_OverrideEmitsCustomCode` in the in-repo test suite.

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
public void IsAuthorized_DestructiveTool_Denies()
{
    var ctx = new ToolCallContext(
        "alice",
        new HashSet<string> { "Admin" },
        new Dictionary<string, string>(),
        ToolName: "delete_database",
        Args: new Dictionary<string, object?>());

    var policy = new NoDestructiveToolsPolicy();

    Assert.False(policy.IsAuthorized(ctx));
}
```

The downcast inside the policy body sees the subinterface and reads `ToolName`. Test both branches — the subinterface case and the bare `ISecurityContext` fallback — to confirm the policy fails closed when the host context is missing (or, if your policy is meant to be permissive without it, that it allows).

---

## Testing cancellation

Pre-cancel a `CancellationTokenSource` and assert that `IsAuthorizedAsync` / `EvaluateAsync` throw `OperationCanceledException`. The default async wrapper calls `ct.ThrowIfCancellationRequested()` synchronously before dispatch, so a pre-cancelled token short-circuits before the await:

```csharp
[Fact]
public async Task IsAuthorizedAsync_PreCancelledToken_Throws()
{
    IAuthorizationPolicy policy = new AdminOnlyPolicy();
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
        async () => await policy.IsAuthorizedAsync(Admin, cts.Token).ConfigureAwait(false));
}
```

This mirrors `AuthorizationPolicyAsyncTests.AsyncDefault_PreCancelledToken_ThrowsOperationCanceled` — same shape, same expectation. For policies whose async override does real I/O, also test mid-flight cancellation by cancelling after the call begins; pass `ct` through to the underlying client and trust it to propagate.

---

## Conventions

- **No DI in unit tests.** Construct the policy with `new` and pass dependencies as constructor arguments — it's a sealed class with no magic.
- **Use `IAuthorizationPolicy`-typed variables** when invoking dispatch entry points so you exercise the same surface a host would.
- **One assertion family per `[Fact]`** — code, reason, and `IsSuccess` together count as one family; mixing unrelated checks bloats failure messages.
- **Mirror in-repo test naming** — `Method_Condition_Outcome` (e.g. `Evaluate_NoAdmin_EmitsRoleDeny`). Future readers find tests by grepping the condition.
