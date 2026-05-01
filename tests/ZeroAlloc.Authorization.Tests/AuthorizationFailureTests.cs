using ZeroAlloc.Authorization;

namespace ZeroAlloc.Authorization.Tests;

public class AuthorizationFailureTests
{
    [Fact]
    public void Constructor_StoresCodeAndReason()
    {
        var f = new AuthorizationFailure("policy.deny.role", "user is not Admin");
        Assert.Equal("policy.deny.role", f.Code);
        Assert.Equal("user is not Admin", f.Reason);
    }

    [Fact]
    public void Constructor_AllowsNullReason()
    {
        var f = new AuthorizationFailure("policy.deny");
        Assert.Equal("policy.deny", f.Code);
        Assert.Null(f.Reason);
    }

    [Fact]
    public void Constructor_RejectsNullCode()
    {
        Assert.Throws<ArgumentNullException>(() => new AuthorizationFailure(null!));
    }

    [Fact]
    public void Constructor_RejectsEmptyCode()
    {
        Assert.Throws<ArgumentException>(() => new AuthorizationFailure(""));
    }

    [Fact]
    public void DefaultDenyCode_IsStableConstant()
    {
        Assert.Equal("policy.deny", AuthorizationFailure.DefaultDenyCode);
    }
}
