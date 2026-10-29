---
id: zauth009
title: ZAUTH009
sidebar_position: 9
---

# ZAUTH009 — Invalid ZeroAllocAuthorizationRegistration value

## Severity

Warning.

## Trigger

The project sets the `ZeroAllocAuthorizationRegistration` MSBuild property to a value other than
`public` or `internal`. The comparison is case-insensitive, and an empty value counts as unset.

The generator ignores the value and emits the registration with its default accessibility: `public`
when the project declares or sees policies, `internal` when it has none. A library that meant to keep
its registration internal therefore still ships a public one, and apps that reference it can hit
`CS0121` on their own `AddZeroAllocAuthorization()` call.

## Triggering code

```xml
<PropertyGroup>
  <ZeroAllocAuthorizationRegistration>private</ZeroAllocAuthorizationRegistration>
</PropertyGroup>
```

```text
warning ZAUTH009: The MSBuild property ZeroAllocAuthorizationRegistration is 'private', which is not
a valid value. Use 'public' or 'internal'; until then the generated registration keeps its default
accessibility.
```

## Fix

Use one of the allowed values, or remove the property to keep the default:

```xml
<PropertyGroup>
  <ZeroAllocAuthorizationRegistration>internal</ZeroAllocAuthorizationRegistration>
</PropertyGroup>
```

See [Libraries that ship policies](../guides/host-integration.md#libraries-that-ship-policies) for
when a project should set it.
