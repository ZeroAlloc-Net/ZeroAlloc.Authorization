using ZeroAlloc.Authorization;

namespace ZeroAlloc.Authorization.Tests;

public class AnonymousSecurityContextTests
{
    [Fact]
    public void Instance_Singleton_HasAnonymousId()
    {
        Assert.Equal("anonymous", AnonymousSecurityContext.Instance.Id);
    }

    [Fact]
    public void Instance_Singleton_HasEmptyRoles()
    {
        Assert.Empty(AnonymousSecurityContext.Instance.Roles);
    }

    [Fact]
    public void Instance_Singleton_HasEmptyClaims()
    {
        Assert.Empty(AnonymousSecurityContext.Instance.Claims);
    }

    [Fact]
    public void Instance_Singleton_ReturnsSameReference()
    {
        Assert.Same(AnonymousSecurityContext.Instance, AnonymousSecurityContext.Instance);
    }
}
