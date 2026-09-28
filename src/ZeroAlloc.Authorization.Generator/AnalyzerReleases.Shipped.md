; Shipped analyzer releases.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 2.0.0

### New Rules

Rule ID  | Category                | Severity | Notes
---------|-------------------------|----------|-------------------------------------------------------
ZAUTH001 | ZeroAlloc.Authorization | Error    | Required policy name is not defined
ZAUTH002 | ZeroAlloc.Authorization | Error    | Duplicate policy name
ZAUTH003 | ZeroAlloc.Authorization | Error    | [Policy] class does not implement IAuthorizationPolicy
ZAUTH004 | ZeroAlloc.Authorization | Error    | [Policy] class cannot be instantiated by DI
ZAUTH005 | ZeroAlloc.Authorization | Error    | [RequirePolicy] applied to unsupported type kind

## Release 2.1.0

### New Rules

Rule ID  | Category                | Severity | Notes
---------|-------------------------|----------|-----------------------------------------------------------------
ZAUTH006 | ZeroAlloc.Authorization | Warning  | [RequireAnyPolicy] with a single policy name
ZAUTH007 | ZeroAlloc.Authorization | Error    | [RequirePolicy] argument shape doesn't match policy interface
ZAUTH008 | ZeroAlloc.Authorization | Error    | [Policy] class implements multiple IAuthorizationPolicy variants
