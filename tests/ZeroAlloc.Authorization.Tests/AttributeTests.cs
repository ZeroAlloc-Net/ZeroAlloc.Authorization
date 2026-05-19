using ZeroAlloc.Authorization;

namespace ZeroAlloc.Authorization.Tests;

public class AttributeTests
{
    [Fact]
    public void RequirePolicyAttribute_StoresPolicyName()
    {
        var attr = new RequirePolicyAttribute("DBA");
        Assert.Equal("DBA", attr.PolicyName);
    }

    [Fact]
    public void RequirePolicyAttribute_AttributeUsage_AllowsClassAndStruct()
    {
        var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(
            typeof(RequirePolicyAttribute), typeof(AttributeUsageAttribute))!;
        Assert.Equal(AttributeTargets.Class | AttributeTargets.Struct, usage.ValidOn);
        Assert.True(usage.AllowMultiple);
        Assert.False(usage.Inherited);
    }

    [Fact]
    public void PolicyAttribute_StoresName()
    {
        var attr = new PolicyAttribute("AdminOnly");
        Assert.Equal("AdminOnly", attr.Name);
    }

    [Fact]
    public void PolicyAttribute_AttributeUsage_ClassOnly_NoMultiple()
    {
        var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(
            typeof(PolicyAttribute), typeof(AttributeUsageAttribute))!;
        Assert.Equal(AttributeTargets.Class, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
        Assert.False(usage.Inherited);
    }
}
