namespace ZeroAlloc.Authorization.Generator;

/// <summary>
/// The accessibility a project asked for with the <c>ZeroAllocAuthorizationRegistration</c>
/// MSBuild property.
/// </summary>
internal enum RegistrationAccessibility
{
    /// <summary>Unset, empty or invalid: public when the compilation has policies or requests, internal otherwise.</summary>
    Default,

    /// <summary>The same as <see cref="Default"/>, asked for explicitly.</summary>
    Public,

    /// <summary>Always internal, so a library's registration cannot clash with the one of an app that references it.</summary>
    Internal,
}
