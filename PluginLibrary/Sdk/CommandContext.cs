namespace PluginLibrary.Sdk;

public sealed class CommandContext
{
    public required IReadOnlyDictionary<string, string> Args { get; init; }
    public required Func<string, Task<bool>> Confirm { get; init; }
    public required Action<string> Log { get; init; }

    public string Arg(string name) => Args.TryGetValue(name, out var v) ? v : string.Empty;
}
