using System.Runtime.Loader;

namespace Test.DepLib;

/// <summary>A dependency of the test plugin: reports which load context it was loaded into.</summary>
public static class Greeter
{
    public static string Greet(string name) => $"Hello, {name}!";

    public static string LoadContextName() => AssemblyLoadContext.GetLoadContext(typeof(Greeter).Assembly)?.Name ?? "?";
}
