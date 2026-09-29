using System.Reflection;
using Xunit;

namespace Smoke.Acceptance;

public sealed class AdditionTests
{
    [Theory]
    [InlineData(2, 3, 5)]
    [InlineData(0, 0, 0)]
    [InlineData(-7, -2, -9)]
    [InlineData(-5, 5, 0)]
    [InlineData(int.MaxValue, 1, int.MinValue)]
    [InlineData(int.MinValue, -1, int.MaxValue)]
    public void Add_MatchesContract(int left, int right, int expected)
    {
        var workspace = Environment.GetEnvironmentVariable("EVAL_WORKSPACE")
            ?? throw new InvalidOperationException("EVAL_WORKSPACE is required.");
        var assembly = Assembly.LoadFrom(Path.Combine(workspace, "src", "Smoke", "bin", "Release", "net10.0", "Smoke.dll"));
        var type = assembly.GetType("Smoke.Calculator", throwOnError: true)!;
        var method = type.GetMethod("Add", BindingFlags.Public | BindingFlags.Static, [typeof(int), typeof(int)]);
        Assert.NotNull(method);
        Assert.Equal(typeof(int), method.ReturnType);
        Assert.Equal(expected, method.Invoke(null, [left, right]));
    }
}
