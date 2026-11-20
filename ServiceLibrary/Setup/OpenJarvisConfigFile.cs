namespace ServiceLibrary.Setup;

/// <summary>
/// Leitura mínima do config.toml do OpenJarvis (o que o "jarvis init" gera).
/// Local: $OPENJARVIS_CONFIG, senão $OPENJARVIS_HOME\config.toml, senão %USERPROFILE%\.openjarvis\config.toml.
/// </summary>
public static class OpenJarvisConfigFile
{
    public static string Path
    {
        get
        {
            if (Environment.GetEnvironmentVariable("OPENJARVIS_CONFIG") is { Length: > 0 } explicitFile) return explicitFile;
            if (Environment.GetEnvironmentVariable("OPENJARVIS_HOME") is { Length: > 0 } home) return System.IO.Path.Combine(home, "config.toml");
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".openjarvis", "config.toml");
        }
    }

    public static bool Exists => File.Exists(Path);

    /// <summary>[intelligence] default_model — o modelo que o OpenJarvis usa quando nenhum é pedido.</summary>
    public static string? DefaultModel => Read("intelligence", "default_model");

    /// <summary>
    /// Grava uma chave string numa seção (cria a seção no fim se não existir).
    /// Preserva o resto do arquivo, inclusive comentários.
    /// </summary>
    public static void Write(string section, string key, string value)
    {
        var lines = Exists ? File.ReadAllLines(Path).ToList() : [];
        var entry = $"{key} = \"{value}\"";

        var start = lines.FindIndex(l => l.Trim().Equals($"[{section}]", StringComparison.OrdinalIgnoreCase));
        if (start < 0)
        {
            if (lines.Count > 0 && lines[^1].Trim().Length > 0) lines.Add("");
            lines.Add($"[{section}]");
            lines.Add(entry);
        }
        else
        {
            var end = lines.FindIndex(start + 1, l => l.TrimStart().StartsWith('['));
            if (end < 0) end = lines.Count;

            var existing = lines.FindIndex(start + 1, end - start - 1, l =>
            {
                var t = l.Trim();
                var eq = t.IndexOf('=');
                return !t.StartsWith('#') && eq > 0 && t[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase);
            });

            if (existing >= 0) lines[existing] = entry;
            else lines.Insert(start + 1, entry);
        }

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllLines(Path, lines);
    }

    /// <summary>Lê uma chave simples (string) de uma seção. Ignora comentários.</summary>
    public static string? Read(string section, string key)
    {
        if (!Exists) return null;

        string? current = null;
        foreach (var raw in File.ReadLines(Path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                current = line.Trim('[', ']').Trim();
                continue;
            }

            if (!string.Equals(current, section, StringComparison.OrdinalIgnoreCase)) continue;

            var eq = line.IndexOf('=');
            if (eq < 0 || !line[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) continue;

            var value = line[(eq + 1)..].Trim();
            var hash = value.StartsWith('"') ? value.IndexOf('"', 1) + 1 : value.IndexOf('#');
            if (hash > 0) value = value[..hash];
            value = value.Trim().Trim('"');
            return value.Length > 0 ? value : null;
        }
        return null;
    }
}
