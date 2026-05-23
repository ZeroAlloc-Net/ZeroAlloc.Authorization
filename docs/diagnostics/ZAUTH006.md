---
id: zauth006
title: ZAUTH006
sidebar_position: 6
---

# ZAUTH006 — [RequireAnyPolicy] with a single policy name

## Severity

Warning.

## Trigger

A `[RequireAnyPolicy(...)]` attribute lists exactly one policy name. The OR
group degenerates to that single policy — semantically identical to
`[RequirePolicy(name)]`, but louder at the call site. The diagnostic nudges
the consumer toward the simpler form.

The dispatcher still compiles; ZAUTH006 is a clarity warning, not a hard
error.

## Triggering code

```csharp
[RequireAnyPolicy("AdminOnly")]
public sealed record DeleteUserCommand(string UserId);
//                  ^^^^^^^^^^^^^^^^^^
// ZAUTH006: [RequireAnyPolicy("AdminOnly")] on 'DeleteUserCommand' lists a
// single policy — use [RequirePolicy("AdminOnly")] for clarity.
```

## Fix

Swap `[RequireAnyPolicy]` for `[RequirePolicy]`:

```csharp
[RequirePolicy("AdminOnly")]
public sealed record DeleteUserCommand(string UserId);
```

If you intended an OR group, add the missing name(s):

```csharp
[RequireAnyPolicy("AdminOnly", "Premium")]
public sealed record DeleteUserCommand(string UserId);
```

See [OR composition](../core-concepts/or-composition.md) for the full
attribute reference.
