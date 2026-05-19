using Xunit;

namespace ZeroAlloc.Authorization.Tests;

public sealed class RequirePolicyAttributeTests
{
    [Fact]
    public void RequirePolicyAttribute_StoresName()
    {
        var attr = new RequirePolicyAttribute("admin");
        Assert.Equal("admin", attr.PolicyName);
    }

    [Fact]
    public void RequirePolicyAttribute_AllowsClassOrStruct_AllowsMultiple()
    {
        var usage = typeof(RequirePolicyAttribute).GetCustomAttributes(typeof(System.AttributeUsageAttribute), false);
        Assert.Single(usage);
        var au = (System.AttributeUsageAttribute)usage[0];
        Assert.Equal(System.AttributeTargets.Class | System.AttributeTargets.Struct, au.ValidOn);
        Assert.True(au.AllowMultiple);
        Assert.False(au.Inherited);
    }
}
