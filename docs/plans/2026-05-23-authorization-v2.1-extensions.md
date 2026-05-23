# ZeroAlloc.Authorization v2.1 Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Promote three backlog items (#1 OR composition via `[RequireAnyPolicy]`, #2 parameterized policies via generic `IAuthorizationPolicy<...>`, #3 resource-based authz contract `IResourceSecurityContext<TResource>`) from `docs/backlog.md` into a v2.1 release. All three opt-in/additive on top of v2.0; no contract break.

**Architecture:** Runtime adds one new attribute (`[RequireAnyPolicy]`), three new generic interfaces (`IAuthorizationPolicy<T1>` / `<T1,T2>` / `<T1,T2,T3>`), one new context interface (`IResourceSecurityContext<TResource>`), and a `params object?[]` overload on `[RequirePolicy]`. Generator extends `Discovery/RequireSymbolWalker` to read constant args, extends `Discovery/PolicySymbolWalker` to recognise generic-arity policy interfaces, and extends `Emit/AuthorizerForEmitter` with two new shapes: OR-group eval + combined-failure synthesis, and typed-arg dispatch with literal constants. Three new diagnostics (`ZAUTH006`, `ZAUTH007`, `ZAUTH008`) gate the new surface at compile time.

**Tech Stack:** .NET 8/9/10 multi-target. Roslyn `Microsoft.CodeAnalysis.CSharp` 4.14, xUnit 2, Verify 28. `Microsoft.CodeAnalysis.PublicApiAnalyzers` (RS0016/RS0017) enforces additive PublicAPI.

**Design doc:** [`2026-05-23-authorization-v2.1-extensions-design.md`](2026-05-23-authorization-v2.1-extensions-design.md)
**Decisions log:** [`2026-05-23-authorization-v2.1-decisions-log.md`](2026-05-23-authorization-v2.1-decisions-log.md) (considered-and-rejected rationale per sub-decision)

**Working repo:** `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Authorization/` (already on `feat/authorization-v2.1-extensions` branched off post-v2.0.2 main; design + log commit `fbbd046` is the current HEAD).

**Key context for the implementer:**
- v2.0 source layout:
  - Runtime: `src/ZeroAlloc.Authorization/` — `RequirePolicyAttribute.cs`, `PolicyAttribute.cs`, `IAuthorizationPolicy.cs`, `ISecurityContext.cs`, `AuthorizationFailure.cs`, `AuthorizerFor.cs`, `AnonymousSecurityContext.cs`.
  - Generator: `src/ZeroAlloc.Authorization.Generator/` with sub-folders `Discovery/`, `Emit/`, `Diagnostics/`. Entry point `PolicyRegistryGenerator.cs`.
- Existing diagnostics: ZAUTH001 (unknown policy name), ZAUTH002 (duplicate name), ZAUTH003 (`[Policy]` doesn't implement interface), ZAUTH004 (abstract/static `[Policy]`), ZAUTH005 (`[RequirePolicy]` on invalid target).
- Existing `RequireInfo` shape: `(FullyQualifiedTypeName, SafeIdentifier, IReadOnlyList<string> PolicyNames)`. Extends for v2.1: needs per-policy args + OR-group grouping.
- Existing `PolicyInfo` shape: `(FullyQualifiedTypeName, PolicyName)`. Extends for v2.1: needs arity + generic type parameters.
- Existing emit at `Emit/AuthorizerForEmitter.cs:26-57` iterates `req.PolicyNames`, emits sequential `await ... if failure return failure`. Extend for OR groups + typed args.
- `TreatWarningsAsErrors=true` repo-wide. PublicAPI delta is enforced; new lines must land in `PublicAPI.Unshipped.txt`.
- Repo conventions:
  - MA0051 (Meziantou): methods max 60 lines.
  - RS1032 (CodeAnalysis): diagnostic `messageFormat` must be a single sentence (no trailing period; or with a trailing period if there's an interior `.`).
  - MA0006 (string `==`): prefer `string.Equals(a, b, StringComparison.Ordinal)`.
- Test infrastructure:
  - `tests/ZeroAlloc.Authorization.Generator.Tests/` uses xUnit + Verify (snapshot tests via `TestHelper.Verify<Generator>(source)`).
  - `tests/ZeroAlloc.Authorization.Tests/` is runtime tests.
  - `tests/ZeroAlloc.Authorization.PackSmoke/` is the AOT smoke binary.

---

## Implementation order — rationale

The three features are roughly independent at the contract level but share the generator pipeline. Implementation order minimises churn:

- **Phase A (Tasks 1-3):** runtime types only. Three new attributes/interfaces + one extended attribute. No generator changes; existing v2.0 emit stays byte-identical. Build green after each.
- **Phase B (Tasks 4-5):** diagnostic descriptors + Discovery extensions. ZAUTH006/007/008 + extended `RequireInfo` + extended `PolicyInfo`. Still no emit changes — the generator collects new metadata but emits the v2.0 shape.
- **Phase C (Tasks 6-8):** emit changes. `AuthorizerForEmitter` gains typed-arg dispatch (Task 6), OR-group eval + combined-failure (Task 7). Snapshot tests get re-promoted as the emit shape changes for v2.1 fixtures.
- **Phase D (Tasks 9-11):** diagnostic tests + runtime tests.
- **Phase E (Tasks 12-14):** AOT smoke + docs + backlog cleanup + push + PR.

Total: 14 tasks.

---

## Phase A — Runtime types (Tasks 1-3)

### Task 1: Add `[RequireAnyPolicy(params string[])]` attribute

**Files:**
- Create: `src/ZeroAlloc.Authorization/RequireAnyPolicyAttribute.cs`
- Modify: `src/ZeroAlloc.Authorization/PublicAPI.Unshipped.txt`

**Step 1.1: Create the attribute**

```csharp
using System;

namespace ZeroAlloc.Authorization;

/// <summary>
/// Marks a request type as requiring at least ONE of the named authorization policies to pass.
/// Stacks with <see cref="RequirePolicyAttribute"/> and other <see cref="RequireAnyPolicyAttribute"/>
/// declarations (cross-attribute stacking is AND; within-attribute names form an OR group).
/// </summary>
/// <example>
/// <code>
/// [RequirePolicy("Admin")]
/// [RequireAnyPolicy("Premium", "Trusted")]
/// public sealed record ViewBillingQuery();
/// // Effective: Admin AND (Premium OR Trusted)
/// </code>
/// </example>
/// <remarks>
/// All listed policy names must be defined via <see cref="PolicyAttribute"/> in the consumer's
/// compilation or referenced assemblies. When all candidates fail at runtime, the generator
/// synthesises a combined <see cref="AuthorizationFailure"/> with <c>Code = "any.all_failed"</c>
/// and <c>Reason</c> listing each policy's individual failure (declaration order).
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true, Inherited = false)]
public sealed class RequireAnyPolicyAttribute : Attribute
{
    public RequireAnyPolicyAttribute(params string[] policyNames) => PolicyNames = policyNames;
    public string[] PolicyNames { get; }
}
```

**Step 1.2: Update `PublicAPI.Unshipped.txt`**

Append:

```
ZeroAlloc.Authorization.RequireAnyPolicyAttribute
ZeroAlloc.Authorization.RequireAnyPolicyAttribute.PolicyNames.get -> string![]!
ZeroAlloc.Authorization.RequireAnyPolicyAttribute.RequireAnyPolicyAttribute(params string![]! policyNames) -> void
```

If RS0016/RS0017 suggests a different exact form, accept the suggestion verbatim.

**Step 1.3: Verify build**

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Authorization
dotnet build src/ZeroAlloc.Authorization/ZeroAlloc.Authorization.csproj -c Release
```

Expected: 0 warnings, 0 errors.

**Step 1.4: Commit**

```bash
git add src/ZeroAlloc.Authorization/RequireAnyPolicyAttribute.cs src/ZeroAlloc.Authorization/PublicAPI.Unshipped.txt
git commit -m "$(cat <<'EOF'
feat: add [RequireAnyPolicy(params string[])] for OR composition (v2.1)

Sibling to [RequirePolicy]. Stacked attributes (across both types) AND
together; names within a single [RequireAnyPolicy] form an OR group.
Generator wiring lands in subsequent tasks.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: Add three generic `IAuthorizationPolicy<...>` interfaces + extend `[RequirePolicy]` with `params object?[]` overload

**Files:**
- Create: `src/ZeroAlloc.Authorization/IAuthorizationPolicy.Generic.cs`
- Modify: `src/ZeroAlloc.Authorization/RequirePolicyAttribute.cs`
- Modify: `src/ZeroAlloc.Authorization/PublicAPI.Unshipped.txt`

**Step 2.1: Create the generic-interfaces file**

```csharp
using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Results;

namespace ZeroAlloc.Authorization;

/// <summary>
/// Parameterized authorization policy with one compile-time-constant argument.
/// The argument value is supplied at the call site via
/// <see cref="RequirePolicyAttribute(string, object?[])"/>.
/// </summary>
public interface IAuthorizationPolicy<in T1>
{
    ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, T1 arg1, CancellationToken ct = default);
}

/// <summary>
/// Parameterized authorization policy with two compile-time-constant arguments.
/// </summary>
public interface IAuthorizationPolicy<in T1, in T2>
{
    ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, T1 arg1, T2 arg2, CancellationToken ct = default);
}

/// <summary>
/// Parameterized authorization policy with three compile-time-constant arguments.
/// Beyond three, encode args as a single string or surface them via
/// <see cref="ISecurityContext.Claims"/>.
/// </summary>
public interface IAuthorizationPolicy<in T1, in T2, in T3>
{
    ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, T1 arg1, T2 arg2, T3 arg3, CancellationToken ct = default);
}
```

> Each `T` is `in` (contravariant input). The `EvaluateAsync` shape mirrors the parameterless `IAuthorizationPolicy`'s.

**Step 2.2: Extend `RequirePolicyAttribute`**

Replace `src/ZeroAlloc.Authorization/RequirePolicyAttribute.cs`:

```csharp
using System;

namespace ZeroAlloc.Authorization;

/// <summary>
/// Marks a request type as requiring one or more authorization policies. The
/// named policy must be defined via <see cref="PolicyAttribute"/> somewhere
/// in the consumer's compilation or referenced assemblies. Stack the attribute
/// to require multiple policies (all must pass).
/// </summary>
/// <remarks>
/// Pass compile-time-constant args via the <c>params object?[]</c> overload to invoke a
/// generic <see cref="IAuthorizationPolicy{T1}"/> / <see cref="IAuthorizationPolicy{T1, T2}"/> /
/// <see cref="IAuthorizationPolicy{T1, T2, T3}"/> policy. The generator validates the arg
/// shape against the policy's declared interface at compile time (see <c>ZAUTH007</c>).
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true, Inherited = false)]
public sealed class RequirePolicyAttribute : Attribute
{
    /// <summary>Parameterless overload — selects the parameterless <see cref="IAuthorizationPolicy"/>.</summary>
    public RequirePolicyAttribute(string policyName) => (PolicyName, PolicyArgs) = (policyName, null);

    /// <summary>Parameterized overload — selects a generic <see cref="IAuthorizationPolicy{T1}"/>-family policy.</summary>
    /// <param name="args">Compile-time-constant arguments forwarded to the policy's <c>EvaluateAsync</c>.</param>
    public RequirePolicyAttribute(string policyName, params object?[] args)
        => (PolicyName, PolicyArgs) = (policyName, args);

    public string PolicyName { get; }
    public object?[]? PolicyArgs { get; }
}
```

> **Important:** the existing `RequirePolicyAttribute(string policyName)` ctor is PRESERVED — existing `[RequirePolicy("Admin")]` declarations stay byte-identical (overload resolution prefers the more-specific single-string ctor over `params object?[]`).

**Step 2.3: Update `PublicAPI.Unshipped.txt`**

Append:

```
ZeroAlloc.Authorization.IAuthorizationPolicy<T1>
ZeroAlloc.Authorization.IAuthorizationPolicy<T1>.EvaluateAsync(ZeroAlloc.Authorization.ISecurityContext! ctx, T1 arg1, System.Threading.CancellationToken ct = default) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.UnitResult<ZeroAlloc.Authorization.AuthorizationFailure>>
ZeroAlloc.Authorization.IAuthorizationPolicy<T1, T2>
ZeroAlloc.Authorization.IAuthorizationPolicy<T1, T2>.EvaluateAsync(ZeroAlloc.Authorization.ISecurityContext! ctx, T1 arg1, T2 arg2, System.Threading.CancellationToken ct = default) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.UnitResult<ZeroAlloc.Authorization.AuthorizationFailure>>
ZeroAlloc.Authorization.IAuthorizationPolicy<T1, T2, T3>
ZeroAlloc.Authorization.IAuthorizationPolicy<T1, T2, T3>.EvaluateAsync(ZeroAlloc.Authorization.ISecurityContext! ctx, T1 arg1, T2 arg2, T3 arg3, System.Threading.CancellationToken ct = default) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.UnitResult<ZeroAlloc.Authorization.AuthorizationFailure>>
ZeroAlloc.Authorization.RequirePolicyAttribute.PolicyArgs.get -> object?[]?
ZeroAlloc.Authorization.RequirePolicyAttribute.RequirePolicyAttribute(string! policyName, params object?[]! args) -> void
```

(The existing single-string ctor stays in PublicAPI.Shipped.txt — no entry needed for it here.)

If RS0016/RS0017 suggests different formatting, accept the suggestion verbatim.

**Step 2.4: Verify build**

```bash
dotnet build src/ZeroAlloc.Authorization/ZeroAlloc.Authorization.csproj -c Release
```

Expected: 0/0.

**Step 2.5: Commit**

```bash
git add src/ZeroAlloc.Authorization/IAuthorizationPolicy.Generic.cs src/ZeroAlloc.Authorization/RequirePolicyAttribute.cs src/ZeroAlloc.Authorization/PublicAPI.Unshipped.txt
git commit -m "$(cat <<'EOF'
feat: add generic IAuthorizationPolicy<T1..T3> + params overload on [RequirePolicy] (v2.1)

Three new generic policy interfaces (arities 1, 2, 3) for typed-arg
policies. [RequirePolicy] gains a params object?[] ctor overload for
the call-site. Generator wiring + ZAUTH007 validation land in
subsequent tasks.

Existing [RequirePolicy("Admin")] declarations stay byte-identical
(overload resolution prefers the single-string ctor).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Add `IResourceSecurityContext<TResource>` contract

**Files:**
- Create: `src/ZeroAlloc.Authorization/IResourceSecurityContext.cs`
- Modify: `src/ZeroAlloc.Authorization/PublicAPI.Unshipped.txt`

**Step 3.1: Create the interface**

```csharp
namespace ZeroAlloc.Authorization;

/// <summary>
/// A security context that ALSO carries a typed resource — the thing being acted upon.
/// Hosts may implement this on top of <see cref="ISecurityContext"/> to expose the
/// dispatched request (or any other resource shape) to authorization policies that
/// need resource-typed checks.
/// </summary>
/// <typeparam name="TResource">
/// The resource type the host populates. Policies type-check via
/// <c>ctx is IResourceSecurityContext&lt;TPost&gt; rc</c> to access it.
/// </typeparam>
/// <remarks>
/// <para>v2.1 ships this contract; host packages (<c>ZeroAlloc.Mediator.Authorization</c>,
/// <c>AI.Sentinel</c>) adopt by populating the typed-resource context in their dispatch
/// behaviour as a follow-up. Until then, <c>ctx is IResourceSecurityContext&lt;T&gt;</c>
/// falls through to <c>false</c>.</para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class OwnerOnlyPolicy : IAuthorizationPolicy
/// {
///     public ValueTask&lt;UnitResult&lt;AuthorizationFailure&gt;&gt; EvaluateAsync(
///         ISecurityContext ctx, CancellationToken ct = default)
///         => new(ctx is IResourceSecurityContext&lt;Post&gt; rc &amp;&amp; rc.Resource.OwnerId == ctx.Id
///             ? UnitResult&lt;AuthorizationFailure&gt;.Success()
///             : new AuthorizationFailure("resource.not_owner"));
/// }
/// </code>
/// </example>
public interface IResourceSecurityContext<out TResource> : ISecurityContext
{
    /// <summary>The resource the request is acting upon.</summary>
    TResource Resource { get; }
}
```

> `TResource` is `out` (covariant) so a host populating `IResourceSecurityContext<Post>` is assignable to `IResourceSecurityContext<object>` if a generic policy ever needs it.

**Step 3.2: Update `PublicAPI.Unshipped.txt`**

Append:

```
ZeroAlloc.Authorization.IResourceSecurityContext<TResource>
ZeroAlloc.Authorization.IResourceSecurityContext<TResource>.Resource.get -> TResource
```

**Step 3.3: Verify build**

```bash
dotnet build src/ZeroAlloc.Authorization/ZeroAlloc.Authorization.csproj -c Release
```

Expected: 0/0.

**Step 3.4: Commit**

```bash
git add src/ZeroAlloc.Authorization/IResourceSecurityContext.cs src/ZeroAlloc.Authorization/PublicAPI.Unshipped.txt
git commit -m "$(cat <<'EOF'
feat: add IResourceSecurityContext<TResource> dormant contract (v2.1)

Contract only — host adoption (Mediator.Authorization, AI.Sentinel)
is tracked as a follow-up. Until hosts populate the typed-resource
context, `ctx is IResourceSecurityContext<T>` falls through to false
and consumer policies see "no resource available."

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Phase B — Diagnostics + Discovery (Tasks 4-5)

### Task 4: Add ZAUTH006, ZAUTH007, ZAUTH008 descriptors

**Files:**
- Modify: `src/ZeroAlloc.Authorization.Generator/Diagnostics/Descriptors.cs`

**Step 4.1: Append the three descriptors**

Append at the end of the `Descriptors` static class (after the v2.0 ZAUTH001-005 set):

```csharp
    public static readonly DiagnosticDescriptor RequireAnyPolicySingleName = new(
        id: "ZAUTH006",
        title: "[RequireAnyPolicy] with a single policy name",
        messageFormat: "[RequireAnyPolicy(\"{0}\")] on '{1}' lists a single policy — use [RequirePolicy(\"{0}\")] for clarity",
        category: "ZeroAlloc.Authorization",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RequirePolicyArgShapeMismatch = new(
        id: "ZAUTH007",
        title: "[RequirePolicy] argument shape doesn't match policy interface",
        messageFormat: "[RequirePolicy(\"{0}\", ...)] on '{1}': {2}",
        category: "ZeroAlloc.Authorization",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor PolicyImplementsMultipleVariants = new(
        id: "ZAUTH008",
        title: "[Policy] class implements multiple IAuthorizationPolicy variants",
        messageFormat: "[Policy(\"{0}\")] class '{1}' implements multiple IAuthorizationPolicy variants ({2}); pick one or split into separately-named policies",
        category: "ZeroAlloc.Authorization",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
```

> The ZAUTH007 messageFormat uses `{2}` as a free-form "what's wrong" placeholder filled at the call site (e.g., `"expected 1 argument, got 0"` or `"argument at position 0: expected int, got string"`). This keeps the descriptor count down without sacrificing message specificity.

> **RS1032 note:** existing v2.0 descriptors use semicolons and em-dashes as interior punctuation without a trailing period and don't trip RS1032. If it fires on any of the three new ones, append a trailing `.` (mirrors the v1.4 StateMachine precedent).

**Step 4.2: Verify build**

```bash
dotnet build src/ZeroAlloc.Authorization.Generator/ZeroAlloc.Authorization.Generator.csproj -c Release
```

Expected: 0/0.

**Step 4.3: Commit**

```bash
git add src/ZeroAlloc.Authorization.Generator/Diagnostics/Descriptors.cs
git commit -m "$(cat <<'EOF'
feat(generator): add ZAUTH006-008 diagnostic descriptors (v2.1)

  ZAUTH006 (Warning): [RequireAnyPolicy] with single name -> use [RequirePolicy]
  ZAUTH007 (Error):   [RequirePolicy] arg shape doesn't match policy interface
  ZAUTH008 (Error):   [Policy] class implements multiple IAuthorizationPolicy variants

Descriptors only — detection wiring lands in subsequent tasks.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: Extend `Discovery/RequireInfo` + `RequireSymbolWalker` to read args + collect `[RequireAnyPolicy]`; extend `Discovery/PolicyInfo` + `PolicySymbolWalker` for generic-interface arity

**Files:**
- Modify: `src/ZeroAlloc.Authorization.Generator/Discovery/RequireInfo.cs`
- Modify: `src/ZeroAlloc.Authorization.Generator/Discovery/RequireSymbolWalker.cs`
- Modify: `src/ZeroAlloc.Authorization.Generator/Discovery/PolicyInfo.cs`
- Modify: `src/ZeroAlloc.Authorization.Generator/Discovery/PolicySymbolWalker.cs`

This is the largest single task in the plan. It restructures the discovery records to carry the v2.1 metadata. The emit layer consumes these records in Task 6.

**Step 5.1: Restructure `RequireInfo`**

Replace `src/ZeroAlloc.Authorization.Generator/Discovery/RequireInfo.cs`:

```csharp
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Authorization.Generator.Discovery;

/// <summary>
/// One [Require...] group attached to a request type. v2.1 captures group kind
/// (single-AND-element vs OR-group), names, and constant args for parameterized policies.
/// </summary>
internal sealed record RequireGroup(
    RequireGroupKind Kind,
    IReadOnlyList<string> PolicyNames,
    // Args[i] corresponds to PolicyNames[i]. Inner list is the constant args for that one
    // policy invocation, e.g., [18] for [RequirePolicy("MinAge", 18)]. Empty/null means
    // parameterless. For OR groups, every name's args appear in this slot independently.
    IReadOnlyList<IReadOnlyList<TypedConstant>?> Args,
    // The declaring attribute's location — for diagnostic anchoring.
    Location AttributeLocation);

internal enum RequireGroupKind
{
    /// <summary>A single [RequirePolicy] attribute (one name, stacks AND-wise with other groups).</summary>
    All,
    /// <summary>A single [RequireAnyPolicy] attribute (N names within form OR; the group stacks AND-wise).</summary>
    Any,
}

internal sealed record RequireInfo(
    string FullyQualifiedTypeName,           // "global::MyApp.DeleteUser"
    string SafeIdentifier,                   // "MyApp_DeleteUser"
    IReadOnlyList<RequireGroup> Groups);     // each [Require...] attribute on the type, in declaration order
```

> Constants in attribute arguments survive as `TypedConstant` from Roslyn. We carry them verbatim — the emit layer (Task 6) is responsible for formatting them as C# literals.

**Step 5.2: Extend `PolicyInfo`**

Replace `src/ZeroAlloc.Authorization.Generator/Discovery/PolicyInfo.cs`:

```csharp
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Authorization.Generator.Discovery;

/// <summary>
/// A [Policy("Name")] class discovered by the walker. v2.1 captures which
/// IAuthorizationPolicy variant (parameterless or generic arity 1/2/3) the class
/// implements, plus the type-arg list (for arity 1/2/3).
/// </summary>
internal sealed record PolicyInfo(
    string FullyQualifiedTypeName,           // "global::MyApp.MinAgePolicy"
    string PolicyName,                       // "MinAge"
    int Arity,                               // 0 = parameterless IAuthorizationPolicy; 1/2/3 = IAuthorizationPolicy<T..>
    IReadOnlyList<ITypeSymbol> TypeArgs);    // [int] for IAuthorizationPolicy<int>; empty for arity 0
```

**Step 5.3: Update `RequireSymbolWalker`**

In `RequireSymbolWalker.cs`, walk both `[RequirePolicy]` AND `[RequireAnyPolicy]`. For each attribute occurrence on a type, append a `RequireGroup` to that type's `RequireInfo.Groups`.

Implementation outline (replace the relevant portion of `ProcessType`):

```csharp
private const string RequirePolicyAttributeFullName    = "ZeroAlloc.Authorization.RequirePolicyAttribute";
private const string RequireAnyPolicyAttributeFullName = "ZeroAlloc.Authorization.RequireAnyPolicyAttribute";

private static void ProcessType(
    INamedTypeSymbol type,
    INamedTypeSymbol requireAttr,
    INamedTypeSymbol requireAnyAttr,
    List<RequireInfo> sink,
    List<Diagnostic> diagnostics)
{
    List<RequireGroup>? groups = null;

    foreach (var a in type.GetAttributes())
    {
        if (SymbolEqualityComparer.Default.Equals(a.AttributeClass, requireAttr))
        {
            // [RequirePolicy(name)] or [RequirePolicy(name, args...)] — single-name group, Kind=All.
            if (a.ConstructorArguments.Length == 0) continue;
            if (a.ConstructorArguments[0].Value is not string name || string.IsNullOrEmpty(name)) continue;

            IReadOnlyList<TypedConstant>? argList = null;
            if (a.ConstructorArguments.Length >= 2 && a.ConstructorArguments[1].Kind == TypedConstantKind.Array)
            {
                argList = a.ConstructorArguments[1].Values; // params object?[]
            }

            groups ??= new List<RequireGroup>();
            groups.Add(new RequireGroup(
                RequireGroupKind.All,
                new[] { name },
                new[] { argList },
                a.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? Location.None));
        }
        else if (SymbolEqualityComparer.Default.Equals(a.AttributeClass, requireAnyAttr))
        {
            // [RequireAnyPolicy(params string[])] — OR group, Kind=Any. Args are always null per name (no params support).
            if (a.ConstructorArguments.Length == 0) continue;
            if (a.ConstructorArguments[0].Kind != TypedConstantKind.Array) continue;
            var nameValues = a.ConstructorArguments[0].Values;

            var names = new List<string>(nameValues.Length);
            foreach (var v in nameValues)
            {
                if (v.Value is string n && !string.IsNullOrEmpty(n)) names.Add(n);
            }
            if (names.Count == 0) continue;

            // ZAUTH006: single-name [RequireAnyPolicy] -> warning.
            if (names.Count == 1)
            {
                diagnostics.Add(Diagnostic.Create(
                    Descriptors.RequireAnyPolicySingleName,
                    a.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? Location.None,
                    names[0], type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
            }

            var argsPerName = new IReadOnlyList<TypedConstant>?[names.Count]; // all null (no params on RequireAnyPolicy)
            groups ??= new List<RequireGroup>();
            groups.Add(new RequireGroup(
                RequireGroupKind.Any,
                names,
                argsPerName,
                a.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? Location.None));
        }
    }

    if (groups is null || groups.Count == 0) return;

    var fqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    // existing ZAUTH005 check (TypeKind != Class && != Struct) — preserve as-is

    sink.Add(new RequireInfo(
        fqn,
        SanitizeFqn(fqn),
        groups));
}
```

The top-level `Find(Compilation)` method gets the second attribute symbol:

```csharp
public static RequireWalkResult Find(Compilation compilation)
{
    var requireAttr    = compilation.GetTypeByMetadataName(RequirePolicyAttributeFullName);
    var requireAnyAttr = compilation.GetTypeByMetadataName(RequireAnyPolicyAttributeFullName);
    if (requireAttr is null) return new RequireWalkResult(System.Array.Empty<RequireInfo>(), System.Array.Empty<Diagnostic>());
    // If requireAnyAttr is null (e.g., consumer references an older v2.0 package), treat OR-walk as no-op.

    var results = new List<RequireInfo>();
    var diagnostics = new List<Diagnostic>();
    WalkNamespace(compilation.SourceModule.GlobalNamespace, requireAttr, requireAnyAttr, results, diagnostics);
    foreach (var refAsm in compilation.SourceModule.ReferencedAssemblySymbols)
        WalkNamespace(refAsm.GlobalNamespace, requireAttr, requireAnyAttr, results, diagnostics);
    return new RequireWalkResult(results, diagnostics);
}
```

(`requireAnyAttr` may be `null` if consumer references a v2.0 package without the new attribute. The walk should tolerate this gracefully — just don't look for the new attribute.)

**Step 5.4: Update `PolicySymbolWalker`**

In `PolicySymbolWalker.cs`, when a `[Policy]` class is found, inspect its declared interfaces to determine arity + type args. Fire ZAUTH008 if it implements multiple variants.

Logic outline:

```csharp
private const string IAuthorizationPolicyFullName     = "ZeroAlloc.Authorization.IAuthorizationPolicy";
private const string IAuthorizationPolicy1FullName    = "ZeroAlloc.Authorization.IAuthorizationPolicy`1";
private const string IAuthorizationPolicy2FullName    = "ZeroAlloc.Authorization.IAuthorizationPolicy`2";
private const string IAuthorizationPolicy3FullName    = "ZeroAlloc.Authorization.IAuthorizationPolicy`3";

private static void ProcessType(
    INamedTypeSymbol type,
    INamedTypeSymbol policyAttr,
    INamedTypeSymbol[] policyInterfaces, // indexed by arity (0..3)
    List<PolicyInfo> sink,
    List<Diagnostic> diagnostics)
{
    // existing [Policy] discovery — find the attribute, extract Name
    string? name = ... existing logic ...;
    if (name is null) return;

    // Inspect AllInterfaces for IAuthorizationPolicy* implementations.
    INamedTypeSymbol? implParameterless = null;
    INamedTypeSymbol? implGeneric = null;
    int implGenericArity = 0;
    var implVariantsCount = 0;
    var implVariantsLabel = new System.Text.StringBuilder();

    foreach (var i in type.AllInterfaces)
    {
        var iOrig = i.OriginalDefinition;
        for (int arity = 0; arity <= 3; arity++)
        {
            if (policyInterfaces[arity] is null) continue;
            if (SymbolEqualityComparer.Default.Equals(iOrig, policyInterfaces[arity]))
            {
                if (arity == 0) implParameterless = i;
                else { implGeneric = i; implGenericArity = arity; }
                implVariantsCount++;
                if (implVariantsLabel.Length > 0) implVariantsLabel.Append(", ");
                implVariantsLabel.Append(arity == 0 ? "IAuthorizationPolicy" : $"IAuthorizationPolicy`{arity}");
                break;
            }
        }
    }

    if (implVariantsCount == 0)
    {
        // existing ZAUTH003 (Policy class doesn't implement IAuthorizationPolicy) — preserve
        return;
    }

    if (implVariantsCount > 1)
    {
        // ZAUTH008
        diagnostics.Add(Diagnostic.Create(
            Descriptors.PolicyImplementsMultipleVariants,
            type.Locations.Length > 0 ? type.Locations[0] : Location.None,
            name,
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            implVariantsLabel.ToString()));
        return;
    }

    // Build PolicyInfo with the resolved interface's type args.
    var resolved = implParameterless ?? implGeneric!;
    var typeArgs = resolved.TypeArguments;
    sink.Add(new PolicyInfo(
        type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
        name,
        implParameterless is not null ? 0 : implGenericArity,
        typeArgs.ToArray()));
}
```

The top-level `Find(Compilation)` collects the four candidate interface symbols (parameterless + 3 generic arities) into an array indexed by arity.

**Step 5.5: Verify build + existing tests still pass**

```bash
dotnet build src/ZeroAlloc.Authorization.Generator/ -c Release
dotnet test tests/ZeroAlloc.Authorization.Generator.Tests/ -c Release
dotnet test tests/ZeroAlloc.Authorization.Tests/ -c Release
```

Expected: 0 errors; v2.0 tests still pass (the discovery layer collects new metadata but the emit layer is unchanged — output is byte-identical for v2.0 fixtures).

**Step 5.6: Commit**

```bash
git add src/ZeroAlloc.Authorization.Generator/Discovery/
git commit -m "$(cat <<'EOF'
feat(generator): extend Discovery for [RequireAnyPolicy] groups + policy arity

RequireInfo now carries a list of RequireGroup entries (Kind=All for
[RequirePolicy], Kind=Any for [RequireAnyPolicy]). Each group records
its names + per-name constant args (from the params object?[] overload).

PolicyInfo gains Arity (0 = parameterless, 1/2/3 = generic) + TypeArgs.
PolicySymbolWalker fires ZAUTH008 when a [Policy] class implements
multiple IAuthorizationPolicy variants.

Emit layer (Task 6) still consumes the v2.0 single-name shape via
backward-compat accessors; v2.1 emit lands next.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

> **Snapshot regen warning:** existing generator snapshot tests may fail if the discovery model serialised differently. If they do, regenerate after visually verifying no semantic change for v2.0 fixtures.

---

## Phase C — Emit (Tasks 6-8)

### Task 6: Emit parameterized-policy typed-arg dispatch

**Files:**
- Modify: `src/ZeroAlloc.Authorization.Generator/Emit/AuthorizerForEmitter.cs`

This task extends the emitter to produce typed-arg dispatch when a `RequireGroup.Args[i]` carries values. Also validates the arg shape against the policy's `PolicyInfo.Arity` + `TypeArgs`; fires ZAUTH007 on mismatch.

**Step 6.1: Refactor `EmitOne` to dispatch per group**

Current shape iterates `req.PolicyNames` flat. v2.1 iterates `req.Groups`, emitting one block per group based on `Kind`. For Task 6 we ONLY handle Kind=All (preserves v2.0 emit for non-grouped) and the new typed-arg variant.

```csharp
private static void EmitOne(
    StringBuilder sb,
    RequireInfo req,
    IReadOnlyDictionary<string, PolicyInfo> policiesByName,
    List<Diagnostic> diagnostics)
{
    sb.AppendLine($"    internal sealed class GeneratedAuthorizerFor_{req.SafeIdentifier}");
    sb.AppendLine($"        : global::ZeroAlloc.Authorization.AuthorizerFor<{req.FullyQualifiedTypeName}>");
    sb.AppendLine("    {");
    sb.AppendLine("        private readonly global::System.IServiceProvider _sp;");
    sb.AppendLine($"        public GeneratedAuthorizerFor_{req.SafeIdentifier}(global::System.IServiceProvider sp) => _sp = sp;");
    sb.AppendLine();
    sb.AppendLine("        public override async global::System.Threading.Tasks.ValueTask<global::ZeroAlloc.Results.UnitResult<global::ZeroAlloc.Authorization.AuthorizationFailure>> EvaluateAsync(");
    sb.AppendLine("            global::ZeroAlloc.Authorization.ISecurityContext ctx,");
    sb.AppendLine("            global::System.Threading.CancellationToken ct = default)");
    sb.AppendLine("        {");

    foreach (var group in req.Groups)
    {
        if (group.Kind == RequireGroupKind.All)
        {
            EmitAllGroup(sb, req, group, policiesByName, diagnostics);
        }
        else // Any
        {
            EmitAnyGroup(sb, req, group, policiesByName, diagnostics);
        }
    }

    sb.AppendLine("            return global::ZeroAlloc.Results.UnitResult<global::ZeroAlloc.Authorization.AuthorizationFailure>.Success();");
    sb.AppendLine("        }");
    sb.AppendLine("    }");
    sb.AppendLine();
}

private static void EmitAllGroup(StringBuilder sb, RequireInfo req, RequireGroup group, IReadOnlyDictionary<string, PolicyInfo> policiesByName, List<Diagnostic> diagnostics)
{
    // For Kind=All, there's always exactly one name + at most one args list.
    var name = group.PolicyNames[0];
    if (!policiesByName.TryGetValue(name, out var policy)) return; // ZAUTH001 still applies elsewhere
    var args = group.Args[0]; // may be null (parameterless) or list of TypedConstants

    var argShapeError = ValidateArgShape(args, policy);
    if (argShapeError is not null)
    {
        diagnostics.Add(Diagnostic.Create(
            Descriptors.RequirePolicyArgShapeMismatch,
            group.AttributeLocation,
            name, req.FullyQualifiedTypeName, argShapeError));
        return;
    }

    var local = Sanitize(name);
    var argList = FormatArgsForDispatch(args); // "" if no args, ", 18" if args=[18], etc.

    if (policy.Arity == 0)
    {
        sb.AppendLine($"            global::ZeroAlloc.Authorization.IAuthorizationPolicy __p_{local} = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<{policy.FullyQualifiedTypeName}>(_sp);");
    }
    else
    {
        var genericParamList = string.Join(", ", policy.TypeArgs.Select(ta => ta.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
        sb.AppendLine($"            global::ZeroAlloc.Authorization.IAuthorizationPolicy<{genericParamList}> __p_{local} = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<{policy.FullyQualifiedTypeName}>(_sp);");
    }

    sb.AppendLine($"            var __r_{local} = await __p_{local}.EvaluateAsync(ctx{argList}, ct).ConfigureAwait(false);");
    sb.AppendLine($"            if (__r_{local}.IsFailure) return __r_{local};");
}
```

**Step 6.2: Add `ValidateArgShape` + `FormatArgsForDispatch` helpers**

```csharp
/// <summary>Returns null when args match policy.Arity/TypeArgs; otherwise returns a human-readable shape-error string for ZAUTH007.</summary>
private static string? ValidateArgShape(IReadOnlyList<TypedConstant>? args, PolicyInfo policy)
{
    var actualCount = args?.Count ?? 0;
    if (policy.Arity != actualCount)
    {
        return $"expected {policy.Arity} argument(s) for IAuthorizationPolicy<{(policy.Arity == 0 ? "" : string.Join(", ", policy.TypeArgs.Select(ta => ta.ToDisplayString())))}>, got {actualCount}";
    }
    if (args is null) return null;
    for (int i = 0; i < actualCount; i++)
    {
        var expected = policy.TypeArgs[i];
        var actualType = args[i].Type;
        if (actualType is null) return $"argument at position {i}: could not infer type";
        if (!SymbolEqualityComparer.Default.Equals(actualType, expected))
        {
            // Allow implicit numeric/string conversions? For v2.1 — strict match.
            return $"argument at position {i}: expected {expected.ToDisplayString()}, got {actualType.ToDisplayString()}";
        }
    }
    return null;
}

/// <summary>Formats args as a leading-comma C# literal list for the dispatch call, e.g. ", 18, \"x\"" or "" for empty.</summary>
private static string FormatArgsForDispatch(IReadOnlyList<TypedConstant>? args)
{
    if (args is null || args.Count == 0) return string.Empty;
    var sb = new System.Text.StringBuilder();
    foreach (var c in args)
    {
        sb.Append(", ").Append(FormatConstant(c));
    }
    return sb.ToString();
}

private static string FormatConstant(TypedConstant c)
{
    return c.Kind switch
    {
        TypedConstantKind.Primitive => c.Value switch
        {
            string s   => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"",
            char ch    => "'" + ch + "'",
            bool b     => b ? "true" : "false",
            null       => "null",
            _          => System.FormattableString.Invariant($"{c.Value!}"),
        },
        TypedConstantKind.Enum => $"({c.Type!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}){c.Value}",
        _ => "default",
    };
}
```

> The `FormatConstant` helper handles the four common primitive cases (string with escaping, char, bool, null) plus a generic invariant fallback for numerics. Enums get cast to their underlying type.

**Step 6.3: Verify build + existing tests still pass**

```bash
dotnet build src/ZeroAlloc.Authorization.Generator/ -c Release
dotnet test tests/ZeroAlloc.Authorization.Generator.Tests/ -c Release
```

Expected: 0 errors. v2.0 snapshot tests may need regeneration if the emit shape changed (the new `IAuthorizationPolicy<...>` cast for parameterless arity is the same as v2.0 — should be byte-identical).

**Step 6.4: Commit**

```bash
git add src/ZeroAlloc.Authorization.Generator/Emit/AuthorizerForEmitter.cs
git commit -m "$(cat <<'EOF'
feat(generator): emit typed-arg dispatch for parameterized policies (v2.1)

EmitOne now iterates RequireInfo.Groups instead of flat PolicyNames.
Kind=All (single [RequirePolicy]) preserves v2.0 semantics; parameterized
variants emit literal constants in the EvaluateAsync call (no boxing,
no object[] allocation). ZAUTH007 fires on arg shape mismatch.

OR group (Kind=Any) emit lands in Task 7.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 7: Emit OR-group eval + combined failure synthesis

**Files:**
- Modify: `src/ZeroAlloc.Authorization.Generator/Emit/AuthorizerForEmitter.cs`

**Step 7.1: Implement `EmitAnyGroup`**

```csharp
private static void EmitAnyGroup(
    StringBuilder sb,
    RequireInfo req,
    RequireGroup group,
    IReadOnlyDictionary<string, PolicyInfo> policiesByName,
    List<Diagnostic> diagnostics)
{
    // For each name in the OR group, evaluate the policy. On first success, short-circuit.
    // If all fail, synthesize a combined AuthorizationFailure with code "any.all_failed".
    var groupId = System.Guid.NewGuid().ToString("N").Substring(0, 8); // unique label per group
    sb.AppendLine();
    sb.AppendLine($"            // OR group {groupId}");

    var failureLocalNames = new List<string>();
    foreach (var name in group.PolicyNames)
    {
        if (!policiesByName.TryGetValue(name, out var policy)) continue; // ZAUTH001 elsewhere
        // [RequireAnyPolicy] does not accept args (Task 1 surface), so policy must be parameterless.
        if (policy.Arity != 0)
        {
            diagnostics.Add(Diagnostic.Create(
                Descriptors.RequirePolicyArgShapeMismatch,
                group.AttributeLocation,
                name, req.FullyQualifiedTypeName,
                $"[RequireAnyPolicy] only accepts parameterless policies; '{name}' implements IAuthorizationPolicy<...>"));
            continue;
        }
        var local = Sanitize(name) + "_" + groupId;
        failureLocalNames.Add(local);
        sb.AppendLine($"            global::ZeroAlloc.Authorization.IAuthorizationPolicy __p_{local} = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<{policy.FullyQualifiedTypeName}>(_sp);");
        sb.AppendLine($"            var __r_{local} = await __p_{local}.EvaluateAsync(ctx, ct).ConfigureAwait(false);");
        sb.AppendLine($"            if (__r_{local}.IsSuccess) goto __or_pass_{groupId};");
    }

    if (failureLocalNames.Count == 0)
    {
        // No valid policies in the group — let ZAUTH001/ZAUTH007 surface the actual problem
        // (already diagnosed above). Emit a labeled pass to satisfy goto syntax.
        sb.AppendLine($"            __or_pass_{groupId}: ;");
        return;
    }

    // All-fail synthesis.
    sb.Append("            return global::ZeroAlloc.Results.UnitResult<global::ZeroAlloc.Authorization.AuthorizationFailure>.Failure(\n");
    sb.Append("                new global::ZeroAlloc.Authorization.AuthorizationFailure(\n");
    sb.Append("                    \"any.all_failed\",\n");
    sb.Append("                    $\"");
    for (int i = 0; i < failureLocalNames.Count; i++)
    {
        var name = group.PolicyNames[i];
        var local = failureLocalNames[i];
        if (i > 0) sb.Append(" OR ");
        // Embed expression interpolation that picks Reason ?? Code at runtime.
        sb.Append("[").Append(name).Append(": {__r_").Append(local).Append(".Error.Reason ?? __r_").Append(local).Append(".Error.Code}]");
    }
    sb.Append("\"));\n");
    sb.AppendLine($"            __or_pass_{groupId}: ;");
}
```

> The `__or_pass_{groupId}` label is a `goto` target — the simplest way to express "short-circuit out of N awaits without nesting." Each group gets a unique label via a short GUID slice.

**Step 7.2: Verify build + existing tests**

```bash
dotnet build src/ZeroAlloc.Authorization.Generator/ -c Release
dotnet test tests/ZeroAlloc.Authorization.Generator.Tests/ -c Release
```

Expected: 0 errors. Existing v2.0 snapshots should be byte-identical (no fixtures use `[RequireAnyPolicy]` yet).

**Step 7.3: Commit**

```bash
git add src/ZeroAlloc.Authorization.Generator/Emit/AuthorizerForEmitter.cs
git commit -m "$(cat <<'EOF'
feat(generator): emit OR-group eval + combined failure for [RequireAnyPolicy] (v2.1)

Each [RequireAnyPolicy(...)] becomes a sequential evaluation that
short-circuits on first success (goto __or_pass_<id>); on all-fail,
synthesises an AuthorizationFailure with Code "any.all_failed" and
Reason "[A: ...] OR [B: ...] OR [C: ...]" in declaration order.

[RequireAnyPolicy] only accepts parameterless policies in v2.1.
Parameterized variants in an OR group fire ZAUTH007 with a
clarifying message.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 8: Snapshot tests for Task 6+7 emit shapes

**Files:**
- Create: `tests/ZeroAlloc.Authorization.Generator.Tests/RequireAnyPolicyGeneratorTests.cs`
- Create: `tests/ZeroAlloc.Authorization.Generator.Tests/ParameterizedPolicyGeneratorTests.cs`
- Create (via Verify): 4 `.verified.txt` snapshots.

**Step 8.1: Add snapshot tests**

```csharp
namespace ZeroAlloc.Authorization.Generator.Tests;

using System.Threading.Tasks;
using VerifyXunit;
using Xunit;

public class RequireAnyPolicyGeneratorTests
{
    [Fact]
    public Task OrGroup_TwoCandidates_GeneratesShortCircuitEval()
    {
        var source = """
            using ZeroAlloc.Authorization;
            namespace MyApp;
            [Policy("Premium")] public sealed class PremiumPolicy : IAuthorizationPolicy { /* impl */ }
            [Policy("Trusted")] public sealed class TrustedPolicy : IAuthorizationPolicy { /* impl */ }
            [RequireAnyPolicy("Premium", "Trusted")]
            public sealed record ViewBillingQuery();
            """;
        return TestHarness.VerifyGenerator(source);
    }

    [Fact]
    public Task MixedAndOr_AdminPlusAnyOfPremiumOrTrusted()
    {
        var source = """
            using ZeroAlloc.Authorization;
            namespace MyApp;
            [Policy("Admin")]   public sealed class AdminPolicy   : IAuthorizationPolicy { /* impl */ }
            [Policy("Premium")] public sealed class PremiumPolicy : IAuthorizationPolicy { /* impl */ }
            [Policy("Trusted")] public sealed class TrustedPolicy : IAuthorizationPolicy { /* impl */ }
            [RequirePolicy("Admin")]
            [RequireAnyPolicy("Premium", "Trusted")]
            public sealed record ViewBillingQuery();
            """;
        return TestHarness.VerifyGenerator(source);
    }
}

public class ParameterizedPolicyGeneratorTests
{
    [Fact]
    public Task SingleIntArg_GeneratesTypedDispatch()
    {
        var source = """
            using ZeroAlloc.Authorization;
            namespace MyApp;
            [Policy("MinAge")] public sealed class MinAgePolicy : IAuthorizationPolicy<int> { /* impl */ }
            [RequirePolicy("MinAge", 18)]
            public sealed record ApplyForLicenseCommand();
            """;
        return TestHarness.VerifyGenerator(source);
    }

    [Fact]
    public Task TwoArgs_StringAndInt_GeneratesTypedDispatch()
    {
        var source = """
            using ZeroAlloc.Authorization;
            namespace MyApp;
            [Policy("Permission")] public sealed class PermissionPolicy : IAuthorizationPolicy<string, int> { /* impl */ }
            [RequirePolicy("Permission", "read", 42)]
            public sealed record ReadDocCommand();
            """;
        return TestHarness.VerifyGenerator(source);
    }
}
```

> Use the existing `TestHarness.VerifyGenerator` helper (mirrors the pattern in existing `PolicyRegistryGeneratorTests.cs`). The placeholder `/* impl */` will compile as stubs — the generator only walks attributes, not bodies.

**Step 8.2: Run, expect FAIL (received vs verified)**

```bash
dotnet test tests/ZeroAlloc.Authorization.Generator.Tests/ -c Release --filter "FullyQualifiedName~RequireAnyPolicyGeneratorTests|FullyQualifiedName~ParameterizedPolicyGeneratorTests"
```

Expected: 4 tests FAIL with "received but no verified" diff.

**Step 8.3: Inspect each `.received.txt` then rename to `.verified.txt`**

Verify:
- `OrGroup_TwoCandidates`: emits the `__p_premium_<id>` / `__p_trusted_<id>` pair with `goto __or_pass_<id>` short-circuit + all-fail synthesis.
- `MixedAndOr`: emits Admin sequentially first, then the OR group.
- `SingleIntArg`: emits `__p_minage.EvaluateAsync(ctx, 18, ct)` — literal `18`.
- `TwoArgs_StringAndInt`: emits `__p_permission.EvaluateAsync(ctx, "read", 42, ct)`.

Rename `.received.txt` → `.verified.txt`.

**Step 8.4: Re-run, expect PASS + commit**

```bash
dotnet test tests/ZeroAlloc.Authorization.Generator.Tests/ -c Release --filter "FullyQualifiedName~RequireAnyPolicyGeneratorTests|FullyQualifiedName~ParameterizedPolicyGeneratorTests"
git add tests/ZeroAlloc.Authorization.Generator.Tests/RequireAnyPolicyGeneratorTests.cs tests/ZeroAlloc.Authorization.Generator.Tests/ParameterizedPolicyGeneratorTests.cs tests/ZeroAlloc.Authorization.Generator.Tests/Snapshots/
git commit -m "$(cat <<'EOF'
test(generator): snapshot tests for OR-group emit + parameterized dispatch (v2.1)

Four scenarios:
  - OR group with two candidates (short-circuit on first success).
  - Mixed AND/OR (Admin + ANY of Premium/Trusted).
  - Single int arg (typed dispatch with literal constant).
  - Two args (string + int) typed dispatch.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Phase D — Diagnostic + runtime tests (Tasks 9-11)

### Task 9: Diagnostic tests for ZAUTH006, ZAUTH007, ZAUTH008

**Files:**
- Modify (or create if absent): `tests/ZeroAlloc.Authorization.Generator.Tests/DiagnosticTests.cs`

**Step 9.1: Append the new tests**

Locate the existing diagnostic tests (search for `ZAUTH005`). Append:

```csharp
[Fact]
public async Task ZAUTH006_FiresWhen_RequireAnyPolicy_HasSingleName()
{
    var source = """
        using ZeroAlloc.Authorization;
        [Policy("Admin")] public sealed class AdminPolicy : IAuthorizationPolicy { /* impl */ }
        [RequireAnyPolicy("Admin")]
        public sealed record Cmd();
        """;
    var diags = await TestHarness.GetDiagnostics(source);
    Assert.Contains(diags, d => string.Equals(d.Id, "ZAUTH006", StringComparison.Ordinal));
}

[Fact]
public async Task ZAUTH007_FiresWhen_RequirePolicyArgs_DontMatchPolicyArity()
{
    var source = """
        using ZeroAlloc.Authorization;
        [Policy("MinAge")] public sealed class MinAgePolicy : IAuthorizationPolicy<int> { /* impl */ }
        [RequirePolicy("MinAge")]  // missing the int arg
        public sealed record Cmd();
        """;
    var diags = await TestHarness.GetDiagnostics(source);
    Assert.Contains(diags, d => string.Equals(d.Id, "ZAUTH007", StringComparison.Ordinal));
}

[Fact]
public async Task ZAUTH007_FiresWhen_RequirePolicyArg_TypeMismatch()
{
    var source = """
        using ZeroAlloc.Authorization;
        [Policy("MinAge")] public sealed class MinAgePolicy : IAuthorizationPolicy<int> { /* impl */ }
        [RequirePolicy("MinAge", "eighteen")]  // string instead of int
        public sealed record Cmd();
        """;
    var diags = await TestHarness.GetDiagnostics(source);
    Assert.Contains(diags, d => string.Equals(d.Id, "ZAUTH007", StringComparison.Ordinal));
}

[Fact]
public async Task ZAUTH008_FiresWhen_PolicyImplements_MultipleVariants()
{
    var source = """
        using ZeroAlloc.Authorization;
        [Policy("MinAge")]
        public sealed class MinAgePolicy
            : IAuthorizationPolicy, IAuthorizationPolicy<int> { /* impl */ }
        """;
    var diags = await TestHarness.GetDiagnostics(source);
    Assert.Contains(diags, d => string.Equals(d.Id, "ZAUTH008", StringComparison.Ordinal));
}
```

> Use `string.Equals(..., StringComparison.Ordinal)` per MA0006 convention.

**Step 9.2: Run + commit**

```bash
dotnet test tests/ZeroAlloc.Authorization.Generator.Tests/ -c Release --filter "FullyQualifiedName~DiagnosticTests"
git add tests/ZeroAlloc.Authorization.Generator.Tests/DiagnosticTests.cs
git commit -m "$(cat <<'EOF'
test(generator): diagnostic tests for ZAUTH006-008

  ZAUTH006: [RequireAnyPolicy] with a single policy name -> warning.
  ZAUTH007: arg arity mismatch + arg type mismatch (two positive cases).
  ZAUTH008: policy class implements both IAuthorizationPolicy + IAuthorizationPolicy<int>.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 10: Runtime tests for OR composition + parameterized + resource

**Files:**
- Create: `tests/ZeroAlloc.Authorization.Tests/OrCompositionTests.cs`
- Create: `tests/ZeroAlloc.Authorization.Tests/ParameterizedPolicyTests.cs`
- Create: `tests/ZeroAlloc.Authorization.Tests/ResourceSecurityContextTests.cs`

**Step 10.1: OR composition tests**

```csharp
namespace ZeroAlloc.Authorization.Tests;

using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ZeroAlloc.Authorization;

public class OrCompositionTests
{
    [Fact]
    public async Task OrGroup_FirstPolicyPasses_Succeeds()
    {
        var sp = BuildServiceProvider(passing: ["Premium"]);
        var auth = sp.GetRequiredService<global::ZeroAlloc.Authorization.Generated.GeneratedAuthorizerFor_OrCompositionTests_ViewBillingQuery>();
        var result = await auth.EvaluateAsync(new AnonymousSecurityContext());
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task OrGroup_AllPoliciesFail_ReturnsCombinedFailure()
    {
        var sp = BuildServiceProvider(passing: []); // none pass
        var auth = sp.GetRequiredService<global::ZeroAlloc.Authorization.Generated.GeneratedAuthorizerFor_OrCompositionTests_ViewBillingQuery>();
        var result = await auth.EvaluateAsync(new AnonymousSecurityContext());
        Assert.True(result.IsFailure);
        Assert.Equal("any.all_failed", result.Error.Code);
        Assert.Contains("[Premium:", result.Error.Reason);
        Assert.Contains("OR", result.Error.Reason);
        Assert.Contains("[Trusted:", result.Error.Reason);
    }

    [RequireAnyPolicy("Premium", "Trusted")]
    public sealed record ViewBillingQuery();

    [Policy("Premium")]
    public sealed class PremiumPolicy : IAuthorizationPolicy { /* impl using a flag from DI */ }

    [Policy("Trusted")]
    public sealed class TrustedPolicy : IAuthorizationPolicy { /* impl using a flag from DI */ }

    // Helper to build a DI container with configurable per-policy pass/fail behaviour.
    private static IServiceProvider BuildServiceProvider(string[] passing) { /* ... */ }
}
```

> The generated authorizer type lives in the `ZeroAlloc.Authorization.Generated` namespace under a `GeneratedAuthorizerFor_<safe-id>` pattern. The fixture types (`PremiumPolicy`, etc.) need to be declared at file scope so the generator picks them up; you may need to factor them into a `TestFixtures.cs` if the namespace collisions get awkward. Mirror the convention used by existing v2.0 runtime tests.

**Step 10.2: Parameterized policy tests**

Similar shape but with `[Policy("MinAge")] class : IAuthorizationPolicy<int>` and `[RequirePolicy("MinAge", 18)]`. Verify the int 18 reaches the policy at dispatch time.

**Step 10.3: Resource security context test**

```csharp
public class ResourceSecurityContextTests
{
    [Fact]
    public async Task IResourceSecurityContext_PolicyCanTypeCheck()
    {
        // Build a context that ALSO implements IResourceSecurityContext<Post>.
        var ctx = new TestPostContext(new Post(Id: 1, OwnerId: "alice"), "alice");
        var policy = new OwnerOnlyPolicy();
        var result = await policy.EvaluateAsync(ctx);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task IResourceSecurityContext_NoResource_FallsThrough()
    {
        var ctx = new AnonymousSecurityContext(); // does NOT implement IResourceSecurityContext<Post>
        var policy = new OwnerOnlyPolicy();
        var result = await policy.EvaluateAsync(ctx);
        Assert.True(result.IsFailure);
        Assert.Equal("resource.not_owner", result.Error.Code);
    }

    public sealed record Post(int Id, string OwnerId);
    public sealed class TestPostContext : IResourceSecurityContext<Post>
    {
        public TestPostContext(Post resource, string id) { Resource = resource; Id = id; }
        public Post Resource { get; }
        public string Id { get; }
        public IReadOnlyDictionary<string, string> Claims => new Dictionary<string, string>();
    }
    public sealed class OwnerOnlyPolicy : IAuthorizationPolicy
    {
        public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
            ISecurityContext ctx, CancellationToken ct = default)
            => new(ctx is IResourceSecurityContext<Post> rc && rc.Resource.OwnerId == ctx.Id
                ? UnitResult<AuthorizationFailure>.Success()
                : new AuthorizationFailure("resource.not_owner"));
    }
}
```

> Check the real `ISecurityContext` shape in `src/ZeroAlloc.Authorization/ISecurityContext.cs` and adapt the test fixture to match.

**Step 10.4: Run + commit**

```bash
dotnet test tests/ZeroAlloc.Authorization.Tests/ -c Release
git add tests/ZeroAlloc.Authorization.Tests/
git commit -m "$(cat <<'EOF'
test: runtime tests for v2.1 features (OR, parameterized, resource)

  OrCompositionTests:        first-success short-circuit + all-fail combined failure.
  ParameterizedPolicyTests:  typed-arg dispatch via [RequirePolicy("Name", 18)].
  ResourceSecurityContextTests: type-check via `ctx is IResourceSecurityContext<Post>`.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 11: Allocation budget for parameterized dispatch

**Files:**
- Modify (or create if absent): `tests/ZeroAlloc.Authorization.Tests/AllocationBudgetTests.cs`

**Step 11.1: Add a budget gate**

```csharp
[Fact]
public async Task Parameterized_Map_Allocates_Within_Budget()
{
    // [RequirePolicy("MinAge", 18)] dispatch — the int arg becomes a literal in the emit,
    // no boxing. Budget: same as the parameterless dispatch path (0 B for the leaf call,
    // ~64 B for the DI resolution + ValueTask state machine).
    var sp = BuildServiceProviderWith_MinAge_18();
    var auth = sp.GetRequiredService<global::ZeroAlloc.Authorization.Generated.GeneratedAuthorizerFor_X>();
    await AllocationGate.AssertBudgetAsync(128, 100,
        async () => _ = await auth.EvaluateAsync(new AnonymousSecurityContext()),
        "[Parameterized] EvaluateAsync(ctx, 18)");
}
```

> Use the existing `AllocationGate.AssertBudgetAsync` helper from `ZeroAlloc.TestHelpers`. Adjust the budget number after measuring; 128 B is a conservative starting point covering DI resolution + ValueTask wrapping.

**Step 11.2: Run + commit**

```bash
dotnet test tests/ZeroAlloc.Authorization.Tests/ -c Release --filter "FullyQualifiedName~AllocationBudgetTests"
git add tests/ZeroAlloc.Authorization.Tests/AllocationBudgetTests.cs
git commit -m "$(cat <<'EOF'
test(certify): allocation budget for parameterized [RequirePolicy] dispatch (v2.1)

Verifies the typed-arg dispatch path stays within the same budget as
parameterless [RequirePolicy] — confirms no boxing, no object[].

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Phase E — AOT smoke + docs + push + PR (Tasks 12-14)

### Task 12: Extend AOT smoke (PackSmoke) binary

**Files:**
- Modify: `tests/ZeroAlloc.Authorization.PackSmoke/Program.cs` (or its equivalent — locate via `git grep AddZeroAllocAuthorization` to find the existing entry point).

**Step 12.1: Add three exercises**

```csharp
// v2.1 — [RequireAnyPolicy] short-circuit-on-success
var orAuth = sp.GetRequiredService<global::ZeroAlloc.Authorization.Generated.GeneratedAuthorizerFor_X_OrGroup>();
var orResult = await orAuth.EvaluateAsync(new AnonymousSecurityContext());
if (orResult.IsFailure) throw new InvalidOperationException("[RequireAnyPolicy] AOT smoke: expected success");

// v2.1 — parameterized [RequirePolicy("MinAge", 18)] dispatch
var paramAuth = sp.GetRequiredService<global::ZeroAlloc.Authorization.Generated.GeneratedAuthorizerFor_X_Parameterized>();
var paramResult = await paramAuth.EvaluateAsync(new AnonymousSecurityContext());
if (paramResult.IsFailure) throw new InvalidOperationException("[Parameterized] AOT smoke: expected success");

// v2.1 — IResourceSecurityContext<T> compiles in AOT (just verifies the contract surfaces).
// Policy doesn't need to be exercised through DI — the type-check happens at the call site.
IResourceSecurityContext<string>? probe = null;
Console.WriteLine($"[IResourceSecurityContext] probe is {(probe is null ? "null" : "set")}");
```

**Step 12.2: Run AOT smoke**

```bash
dotnet run --project tests/ZeroAlloc.Authorization.PackSmoke -c Release
```

Expected: exit 0.

**Step 12.3: Commit**

```bash
git add tests/ZeroAlloc.Authorization.PackSmoke/Program.cs
git commit -m "$(cat <<'EOF'
test(aot): exercise v2.1 features in AOT smoke (PackSmoke)

[RequireAnyPolicy], [RequirePolicy(args...)], and the
IResourceSecurityContext<T> contract all survive the AOT publish path.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 13: Documentation

**Files:**
- Discover existing convention in `docs/` (likely flat layout based on Mapping's pattern, but verify).
- Create: 3 core-concepts pages for #1, #2, #3.
- Modify: existing attribute/diagnostics docs to add the new attributes + ZAUTH006-008.
- Modify: `docs/README.md` (or equivalent index).

**Step 13.1: Discover the convention**

```bash
ls docs/
cat docs/README.md  # or docs/index.md
ls docs/diagnostics 2>/dev/null  # may or may not exist
```

Match the structure verbatim. Don't invent new directory layouts.

**Step 13.2: Author the 3 core-concepts pages**

Each ≤ 200 lines. Cover Why / How / Constraints / Generator emit / Related.

For `or-composition`:
- Why: avoid hand-written `AdminAndPremiumPolicy` aggregates.
- How: `[RequireAnyPolicy(params string[])]` + `[RequirePolicy]` compose naturally.
- Constraint: `[RequireAnyPolicy]` only accepts parameterless policies in v2.1 (parameterized OR is a follow-up).
- Combined-failure shape: code `"any.all_failed"`, reason format `[name: reason] OR [name: reason]`.
- Reference ZAUTH006.

For `parameterized-policies`:
- Why: kill the `MinAge18Policy` / `MinAge21Policy` explosion.
- How: `IAuthorizationPolicy<T1>` / `<T1,T2>` / `<T1,T2,T3>` + `[RequirePolicy("Name", args...)]`.
- Constraint: args must be C# attribute constants (no `DateTime.Today`, no `new Foo()`).
- Arity cap: 3. Beyond, encode as string or claims.
- Reference ZAUTH007, ZAUTH008.

For `resource-based-authorization`:
- Why: shared policy classes across hosts when the resource type matches.
- How: `IResourceSecurityContext<TResource>` + `ctx is IResourceSecurityContext<Post> rc`.
- **Dormant contract:** v2.1 ships the interface; hosts (`Mediator.Authorization`, `AI.Sentinel`) adopt as follow-up.
- Until hosts populate, `ctx is IResourceSecurityContext<T>` falls through to `false`.

**Step 13.3: Update attribute + diagnostic docs + index**

Add entries for `[RequireAnyPolicy]`, the new `[RequirePolicy(args...)]` overload, the 3 new generic interfaces, the resource interface, and ZAUTH006-008.

**Step 13.4: Commit**

```bash
git add docs/
git commit -m "$(cat <<'EOF'
docs: v2.1 — OR composition, parameterized policies, resource-based authz

Three new core-concepts pages + diagnostic entries for ZAUTH006-008.
Attribute reference gains [RequireAnyPolicy] and the params overload
on [RequirePolicy]. Resource-based-authz page explicitly documents
the dormant-contract semantic + host adoption follow-up.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 14: Backlog prune + push + open PR + admin-merge

**Files:**
- Modify: `docs/backlog.md` — remove #1, #2, #3; add a v2.1 graduation note.

**Step 14.1: Prune `docs/backlog.md`**

Remove sections for items #1, #2, #3 (now shipped). Add at the top:

```markdown
> **Update 2026-05-23:** Items #1 (OR composition), #2 (parameterized policies),
> and #3 (resource-based authorization) graduated into v2.1 — see
> [`plans/2026-05-23-authorization-v2.1-extensions-design.md`](plans/2026-05-23-authorization-v2.1-extensions-design.md).
> Host adoption of #3 (populating IResourceSecurityContext<TResource>
> in Mediator.Authorization + AI.Sentinel) is a separate follow-up.
```

**Step 14.2: Final build + test sweep**

```bash
dotnet build ZeroAlloc.Authorization.slnx -c Release
dotnet test ZeroAlloc.Authorization.slnx -c Release --no-build
```

Expected: 0 errors, all tests pass.

**Step 14.3: Commit backlog**

```bash
git add docs/backlog.md
git commit -m "$(cat <<'EOF'
docs(backlog): prune items graduated into v2.1 (#1, #2, #3)

Host adoption of #3 (Mediator.Authorization, AI.Sentinel populating
IResourceSecurityContext<TResource>) is tracked as a follow-up.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

**Step 14.4: Push**

```bash
git push -u origin feat/authorization-v2.1-extensions
```

**Step 14.5: Open the PR**

```bash
gh pr create --base main --head feat/authorization-v2.1-extensions --title "feat: v2.1 — OR composition + parameterized policies + resource-based authz (#1+#2+#3)" --body "$(cat <<'EOF'
## Summary

v2.1 promotes three backlog items, all opt-in/additive on top of v2.0.

| Item | Surface |
|---|---|
| #1 | New attribute `[RequireAnyPolicy(params string[])]`. Stacks with `[RequirePolicy]`; within-attribute names form an OR group; all-fail synthesises a combined `AuthorizationFailure` with `Code = "any.all_failed"`. |
| #2 | Three new generic interfaces `IAuthorizationPolicy<T1>` / `<T1,T2>` / `<T1,T2,T3>` + `params object?[]` overload on `[RequirePolicy]`. Generator emits typed dispatch with literal constants — no boxing. |
| #3 | New `IResourceSecurityContext<TResource>` contract. Dormant — host adoption (Mediator.Authorization, AI.Sentinel) is a follow-up. |

Three new diagnostics: `ZAUTH006` (`[RequireAnyPolicy]` single name → warning), `ZAUTH007` (arg shape mismatch → error), `ZAUTH008` (policy class implements multiple `IAuthorizationPolicy` variants → error).

## Architecture

- **Discovery:** `RequireInfo` restructured to carry `RequireGroup[]` (Kind=All/Any, names, per-name constant args). `PolicyInfo` gains `Arity` + `TypeArgs` from the implemented interface.
- **Emit:** `AuthorizerForEmitter.EmitOne` iterates groups instead of flat names. `EmitAllGroup` handles `[RequirePolicy]` (with typed args); `EmitAnyGroup` emits short-circuit-on-success + combined failure for `[RequireAnyPolicy]`.
- **Diagnostics:** ZAUTH007 runs at emit time against the resolved `PolicyInfo`; ZAUTH008 runs at discovery time when a class implements multiple variants.

## Test results — local pre-push

- Generator tests: 4 new snapshot tests + 4 new diagnostic tests.
- Runtime tests: 3 new (OR composition, parameterized, resource context) + 1 new allocation budget.
- AOT smoke: exercises all three features.
- Build: 0 W / 0 E across net8/9/10.

## Backlog

After v2.1: 0 surviving items in the active backlog. Host adoption of #3 (Mediator.Authorization, AI.Sentinel populating `IResourceSecurityContext<TResource>`) tracked separately.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

**Step 14.6: Watch CI**

```bash
gh pr checks --watch
```

Once all non-lint checks are green (`lint-commits` may fail on per-commit subject style — squash-merge discards them):

```bash
gh pr merge --squash --delete-branch --admin
git checkout main && git fetch origin main && git reset --hard origin/main
```

Admin-merge is the established pattern for this org when commitlint flags per-commit subjects on a squash-merge — see ZeroAlloc.Mapping PR #17 / ZeroAlloc.StateMachine PR #27 / #29.

---

## Done

After Tasks 1-14:

- 3 backlog items graduated (#1, #2, #3) — v2.1 ships.
- 1 new public attribute (`RequireAnyPolicyAttribute`).
- 1 extended public attribute (`RequirePolicyAttribute` — new `params object?[]` overload).
- 4 new public interfaces (`IAuthorizationPolicy<T1>`, `<T1,T2>`, `<T1,T2,T3>`, `IResourceSecurityContext<TResource>`).
- 3 new diagnostics (ZAUTH006, ZAUTH007, ZAUTH008).
- ~8 new generator tests + 3 new runtime test files + 1 new budget + AOT smoke extension.
- `docs/backlog.md` pruned to 0 active items (host follow-up tracked separately).
- PR open against main; CI green; admin-merged.

---

## Notes for the executor

- **MA0051 (60-line method limit).** Most new emit methods are <40 lines. If `EmitAnyGroup` pushes 60 (StringBuilder calls add up fast), extract the failure-synthesis block into a separate helper.
- **RS1032 (single-sentence messageFormat).** Two of the three new descriptors have interior punctuation. Watch for trailing-period suggestions; if RS1032 fires, append `.` to match the v1.4 StateMachine precedent.
- **MA0006 (`string.Equals` over `==`).** Already used consistently in diagnostic tests; carry that pattern forward.
- **PublicAPI tracking.** ~15 new lines across the 6 new types/members. RS0016/RS0017 catches missing entries.
- **VerifyXunit snapshots.** Existing v2.0 snapshots may regenerate if the discovery-record serialisation changed (it shouldn't if `RequireInfo.Groups` defaults sensibly for single-name v2.0 fixtures). Inspect any diff before promoting.
- **Cross-class transitive validation.** Same caveat as Mapping v1.4: ZAUTH007 validates arg-shape against same-compilation policies via the existing cross-assembly walk; v2.1 inherits that scope automatically.
- **`[RequireAnyPolicy]` only accepts parameterless policies in v2.1.** ZAUTH007 fires if a parameterized policy is named inside an OR group. Documented in `docs/core-concepts/or-composition.md` as a known limitation; future work can lift this.
