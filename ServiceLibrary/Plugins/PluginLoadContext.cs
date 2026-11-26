using PluginLibrary.Sdk;
using System.Reflection;
using System.Runtime.Loader;

namespace ServiceLibrary.Plugins;

/// <summary>Cada plugin num contexto isolado, com as próprias dependências.</summary>
internal sealed class PluginLoadContext(string mainDll) : AssemblyLoadContext(Path.GetFileNameWithoutExtension(mainDll))
{
    private static readonly string SdkName = typeof(IJarvisCommand).Assembly.GetName().Name!;
    private readonly AssemblyDependencyResolver _resolver = new(mainDll);

    protected override Assembly? Load(AssemblyName name)
    {
        // O Sdk TEM que vir do app. Se o plugin carregar a cópia dele, o IJarvisCommand
        // do plugin vira "outro tipo" e nenhum comando é reconhecido.
        if (name.Name == SdkName) return null;

        var path = _resolver.ResolveAssemblyToPath(name);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override nint LoadUnmanagedDll(string name)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(name);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }
}
