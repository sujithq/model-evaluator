using System.Reflection;

namespace ModelEvaluator.Core.Util;

/// <summary>Version of the evaluation harness itself, recorded with every attempt.</summary>
public static class EvaluatorVersion
{
    public static string Value { get; } =
        typeof(EvaluatorVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(EvaluatorVersion).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";
}
