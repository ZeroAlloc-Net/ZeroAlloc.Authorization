using System.Runtime.CompilerServices;

namespace ZeroAlloc.Authorization.Generator.Tests;

internal static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Init()
    {
        VerifySourceGenerators.Initialize();
    }
}
