using PluginLibrary.Sdk;

namespace ServiceLibrary.Plugins;

public static class PluginLoader
{
    public static string Folder => Path.Combine(AppPaths.Roaming, "plugins");

    public static List<IJarvisCommand> LoadAll(ISet<string>? enabled = null)
    {
        List<IJarvisCommand> commands = [];
        Directory.CreateDirectory(Folder);

        foreach (var dir in Directory.GetDirectories(Folder))
        {
            var name = Path.GetFileName(dir);
            if (enabled is not null && !enabled.Contains(name)) continue; // só os liberados no config

            var main = Path.Combine(dir, name + ".dll");
            if (!File.Exists(main)) continue;

            try
            {
                var assembly = new PluginLoadContext(main).LoadFromAssemblyPath(main);
                foreach (var type in assembly.GetExportedTypes())
                {
                    if (type.IsAbstract || !typeof(IJarvisCommand).IsAssignableFrom(type)) continue;
                    if (type.GetConstructor(Type.EmptyTypes) is null) continue;
                    commands.Add((IJarvisCommand)Activator.CreateInstance(type)!);
                }
                Log.Info($"Plugin {name}: carregado");
            }
            catch (Exception ex)
            {
                // Um plugin quebrado não derruba o Jarvis.
                Log.Error($"Plugin {name} falhou ao carregar", ex);
            }
        }

        return commands;
    }
}
