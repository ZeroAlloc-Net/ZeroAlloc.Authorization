namespace ZeroAlloc.Authorization.Generator.Discovery;

internal sealed record RequireInfo(
    string FullyQualifiedTypeName, // "global::MyApp.DeleteUser"
    string SafeIdentifier,         // "MyApp_DeleteUser" — for generated class name
    System.Collections.Generic.IReadOnlyList<string> PolicyNames); // ["admin", "two-factor"]
