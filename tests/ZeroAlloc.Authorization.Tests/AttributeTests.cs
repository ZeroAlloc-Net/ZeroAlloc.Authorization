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
    public void AuthorizeAttribute_AttributeUsage_AllowsMethodAndClass()
    {
        var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(
            typeof(AuthorizeAttribute), typeof(AttributeUsageAttribute))!;
        Assert.Equal(AttributeTargets.Method | AttributeTargets.Class, usage.ValidOn);
        Assert.True(usage.AllowMultiple);
        Assert.True(usage.Inherited);
    }

    [Fact]
    public void AuthorizationPolicyAttribute_StoresName()
    {
        var attr = new AuthorizationPolicyAttribute("AdminOnly");
        Assert.Equal("AdminOnly", attr.Name);
    }

    [Fact]
    public void AuthorizationPolicyAttribute_AttributeUsage_ClassOnly_NoMultiple()
    {
        var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(
            typeof(AuthorizationPolicyAttribute), typeof(AttributeUsageAttribute))!;
        Assert.Equal(AttributeTargets.Class, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
        Assert.False(usage.Inherited);
    }

    [Fact]
    public void AuthorizeAttribute_NullPolicyName_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new AuthorizeAttribute(null!));

    [Fact]
    public void AuthorizeAttribute_EmptyPolicyName_Throws() =>
        Assert.Throws<ArgumentException>(() => new AuthorizeAttribute(""));

    [Fact]
    public void AuthorizationPolicyAttribute_NullName_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new AuthorizationPolicyAttribute(null!));

    [Fact]
    public void AuthorizationPolicyAttribute_EmptyName_Throws() =>
        Assert.Throws<ArgumentException>(() => new AuthorizationPolicyAttribute(""));
}
