namespace ZeroAlloc.Authorization;

/// <summary>
/// Registered as a singleton by the generated <c>AddZeroAllocAuthorization()</c>, whether or not the
/// project declares any <c>[Policy]</c> or <c>[RequirePolicy]</c>. Hosts and integrations probe the
/// service collection for it to know that <c>AddZeroAllocAuthorization()</c> ran. It carries no
/// behaviour and is never resolved for its members.
/// </summary>
public sealed class ZeroAllocAuthorizationMarker
{
}
