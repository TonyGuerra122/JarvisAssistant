namespace PluginLibrary.Sdk;

public sealed record CommandParameter(
    string Name,
    string Description,
    string Type = "string",                 // "string", "integer", "boolean"
    bool Required = true,
    IReadOnlyList<string>? Options = null // vira um "enum": o modelo só escolhe entre essas opções
);
