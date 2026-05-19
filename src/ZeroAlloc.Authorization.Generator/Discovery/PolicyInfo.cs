namespace ZeroAlloc.Authorization.Generator.Discovery;

internal sealed record PolicyInfo(
    string Name,                   // "admin"
    string FullyQualifiedTypeName, // "global::MyApp.AdminPolicy"
    bool IsInstantiable);          // false if abstract/static (drives ZAUTH004)
