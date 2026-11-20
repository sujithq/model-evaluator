using System.Diagnostics;
using System.Reflection;

namespace ModelEvaluator.ApiComparison.Core.Infrastructure;

public static class AssemblyMetadata
{
    public static string ApplicationVersion { get; } =
        GetInformationalVersion(typeof(AssemblyMetadata).Assembly);

    public static string VersionFor(Type type) => GetInformationalVersion(type.Assembly);

    public static string NameFor(Type type) => type.Assembly.GetName().Name ?? type.Namespace ?? "unknown";

    private static string GetInformationalVersion(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? FileVersionInfo.GetVersionInfo(assembly.Location).FileVersion
        ?? assembly.GetName().Version?.ToString()
        ?? "unknown";
}
