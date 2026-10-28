using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ZeroAlloc.Authorization;
using ZeroAlloc.Authorization.Generated;

namespace ZeroAlloc.Authorization.Tests;

/// <summary>
/// <c>AddZeroAllocAuthorization()</c> always registers <see cref="ZeroAllocAuthorizationMarker"/>,
/// so hosts and integrations can tell the method ran.
/// </summary>
public sealed class AuthorizationMarkerTests
{
    [Fact]
    public void AddZeroAllocAuthorization_RegistersMarkerAsSingleton()
    {
        var services = new ServiceCollection();

        services.AddZeroAllocAuthorization();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(ZeroAllocAuthorizationMarker));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        using var sp = services.BuildServiceProvider();
        Assert.NotNull(sp.GetService<ZeroAllocAuthorizationMarker>());
    }

    [Fact]
    public void AddZeroAllocAuthorization_CalledTwice_RegistersMarkerOnce()
    {
        var services = new ServiceCollection();

        services.AddZeroAllocAuthorization();
        services.AddZeroAllocAuthorization();

        Assert.Single(services, d => d.ServiceType == typeof(ZeroAllocAuthorizationMarker));
        using var sp = services.BuildServiceProvider();
        Assert.Same(
            sp.GetRequiredService<ZeroAllocAuthorizationMarker>(),
            sp.GetRequiredService<ZeroAllocAuthorizationMarker>());
    }

    [Fact]
    public void WithoutAddZeroAllocAuthorization_MarkerIsAbsent()
    {
        var services = new ServiceCollection();

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(ZeroAllocAuthorizationMarker));
    }
}
