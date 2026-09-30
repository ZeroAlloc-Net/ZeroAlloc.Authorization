namespace ZeroAlloc.Authorization.Generator.Discovery;

/// <summary>
/// One argument that a [RequirePolicy] passes to a parameterized policy.
/// </summary>
/// <param name="Literal">The C# expression the dispatcher passes.</param>
/// <param name="Type">The argument's type, or null when the compiler could not infer one.</param>
internal sealed record ConstantArg(string Literal, TypeRef? Type);
