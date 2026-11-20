using ModelEvaluator.Core.Configuration;

namespace ModelEvaluator.Core.Adapters;

public sealed class ModelAdapterFactory
{
    private readonly Dictionary<string, IModelAdapter> _adapters;

    public ModelAdapterFactory(IEnumerable<IModelAdapter> adapters)
    {
        _adapters = adapters.ToDictionary(adapter => adapter.Id, StringComparer.Ordinal);
    }

    public static ModelAdapterFactory CreateDefault() =>
        new([
            new LocalFixtureAdapter(),
            new DirectRestAdapter(),
            new OpenAiSdkAdapter(),
            new DirectResponsesAdapter(),
            new OpenAiResponsesAdapter(),
            new MicrosoftExtensionsAiAdapter(useResponsesApi: false),
            new MicrosoftExtensionsAiAdapter(useResponsesApi: true),
            new SemanticKernelAdapter(),
            new CopilotCliAdapter(),
        ]);

    public IReadOnlyCollection<IModelAdapter> All => _adapters.Values;

    public IModelAdapter Get(string id) =>
        _adapters.TryGetValue(id, out var adapter)
            ? adapter
            : throw new KeyNotFoundException(
                $"Adapter '{id}' is unknown. Available adapters: {string.Join(", ", _adapters.Keys)}.");
}
