using ModelEvaluator.Core.Execution;

namespace ModelEvaluator.Core.Adapters;

/// <summary>Resolves adapters by key so new providers can be plugged in through configuration.</summary>
public sealed class ModelAdapterFactory
{
    private readonly Dictionary<string, IModelAdapter> _adapters = new(StringComparer.OrdinalIgnoreCase);

    public ModelAdapterFactory(IEnumerable<IModelAdapter> adapters)
    {
        foreach (var adapter in adapters)
        {
            _adapters[adapter.Key] = adapter;
        }
    }

    /// <summary>Factory with the adapters that ship with the evaluator.</summary>
    public static ModelAdapterFactory CreateDefault(ProcessRunner? processRunner = null) =>
        new([new LocalSampleAdapter(), new CommandLineAdapter(processRunner ?? new ProcessRunner())]);

    public IReadOnlyCollection<string> Keys => _adapters.Keys;

    public IModelAdapter Get(string key) => _adapters.TryGetValue(key, out var adapter)
        ? adapter
        : throw new KeyNotFoundException($"Unknown adapter '{key}'. Available adapters: {string.Join(", ", _adapters.Keys)}.");
}
