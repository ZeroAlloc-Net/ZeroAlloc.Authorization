using ZeroAlloc.Authorization;

namespace ZeroAlloc.Authorization.Tests;

public class AttributeTests
{
    [Fact]
    public void AuthorizeAttribute_StoresPolicyName()
    {
        var attr = new AuthorizeAttribute("DBA");
        Assert.Equal("DBA", attr.PolicyName);
    }

    [Fact]
    public void AuthorizeAttribute_AttributeUsage_IsMethodOnly()
    {
        var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(
            typeof(AuthorizeAttribute), typeof(AttributeUsageAttribute))!;
        Assert.Equal(AttributeTargets.Method, usage.ValidOn);
    }

    [Fact]
    public void AuthorizationPolicyAttribute_StoresName()
    {
        var attr = new AuthorizationPolicyAttribute("AdminOnly");
        Assert.Equal("AdminOnly", attr.Name);
    }

    [Fact]
    public void AuthorizationPolicyAttribute_AttributeUsage_IsClassOnly()
    {
        var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(
            typeof(AuthorizationPolicyAttribute), typeof(AttributeUsageAttribute))!;
        Assert.Equal(AttributeTargets.Class, usage.ValidOn);
    }
}
