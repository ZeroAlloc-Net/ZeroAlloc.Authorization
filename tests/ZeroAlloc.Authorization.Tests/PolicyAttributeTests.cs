using Xunit;

namespace ZeroAlloc.Authorization.Tests;

public sealed class PolicyAttributeTests
{
    [Fact]
    public void PolicyAttribute_StoresName()
    {
        var attr = new PolicyAttribute("admin");
        Assert.Equal("admin", attr.Name);
    }

    [Fact]
    public void PolicyAttribute_AllowsClassTargetsOnly()
    {
        var usage = typeof(PolicyAttribute).GetCustomAttributes(typeof(System.AttributeUsageAttribute), false);
        Assert.Single(usage);
        var au = (System.AttributeUsageAttribute)usage[0];
        Assert.Equal(System.AttributeTargets.Class, au.ValidOn);
        Assert.False(au.AllowMultiple);
        Assert.False(au.Inherited);
    }
}
