namespace ServiceLibrary.Commands;

// Ferramentas de arquivos: só dentro das pastas permitidas (AppConfig.AllowedFolders).
public sealed partial class CommandRunner
{
    /// <summary>Extensões que nunca são abertas por voz: executam código.</summary>
    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".com", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf",
        ".msi", ".msp", ".scr", ".lnk", ".reg", ".hta", ".jar", ".dll", ".cpl",
    };

    private List<string> AllowedRoots() =>
    [
        .. _config.AllowedFolders
            .Select(f => Environment.ExpandEnvironmentVariables(f).Trim())
            .Where(f => f.Length > 0 && Directory.Exists(f))
            .Select(f => Path.GetFullPath(f).TrimEnd('\\'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
    ];

    private static bool IsInside(string full, string root) =>
        full.Equals(root, StringComparison.OrdinalIgnoreCase)
        || full.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase);

    /// <summary>Entende apelidos ("downloads", "documentos"...) e caminhos completos.</summary>
    private string? ResolvePath(string input)
    {
        input = input.Trim().Trim('"');
        if (input.Length == 0) return null;

        var roots = AllowedRoots();
        var alias = Normalize(input);
        string? folder = alias switch
        {
            "downloads" or "download" => "Downloads",
            "documentos" or "documents" => "Documents",
            "area de trabalho" or "desktop" => "Desktop",
            "imagens" or "fotos" or "pictures" => "Pictures",
            "musicas" or "musica" or "music" => "Music",
            "videos" or "video" => "Videos",
            _ => null,
        };
        if (folder is not null)
            return roots.FirstOrDefault(r => Path.GetFileName(r).Equals(folder, StringComparison.OrdinalIgnoreCase));

        var expanded = Environment.ExpandEnvironmentVariables(input);
        if (Path.IsPathRooted(expanded)) return Path.GetFullPath(expanded);

        // Relativo: procura dentro de cada pasta permitida.
        return roots.Select(r => Path.GetFullPath(Path.Combine(r, expanded))).FirstOrDefault(p => File.Exists(p) || Directory.Exists(p));
    }

    private string OpenPath(string caminho)
    {
        var full = ResolvePath(caminho);
        if (full is null) return $"Não encontrei \"{caminho}\" nas pastas permitidas.";

        var roots = AllowedRoots();
        if (!roots.Any(r => IsInside(full, r))) return "Esse local não está nas pastas permitidas.";

        if (Directory.Exists(full))
        {
            ShellOpen(full);
            return $"Pasta {Path.GetFileName(full)} aberta.";
        }
        if (!File.Exists(full)) return "Esse arquivo não existe.";
        if (BlockedExtensions.Contains(Path.GetExtension(full)))
            return "Por segurança, não abro programas ou scripts por voz.";

        ShellOpen(full);
        return $"Arquivo {Path.GetFileName(full)} aberto.";
    }

    private async Task<string> FindFilesAsync(string nome, string pasta, CancellationToken ct)
    {
        var term = nome.Replace("*", "").Replace("?", "").Trim();
        if (term.Length < 2) return "Diga parte do nome do arquivo.";

        var roots = AllowedRoots();
        if (!string.IsNullOrWhiteSpace(pasta))
        {
            var one = ResolvePath(pasta);
            if (one is null || !roots.Any(r => IsInside(one, r))) return "Essa pasta não está nas permitidas.";
            roots = [one];
        }
        if (roots.Count == 0) return "Nenhuma pasta permitida existe. Veja AllowedFolders na config.";

        // A busca roda fora da thread principal e tem limite de tempo.
        var found = await Task.Run(() =>
        {
            var results = new List<string>();
            var opts = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                MatchCasing = MatchCasing.CaseInsensitive,
                AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
            };
            var deadline = DateTime.UtcNow.AddSeconds(8);
            foreach (var root in roots)
                foreach (var entry in Directory.EnumerateFileSystemEntries(root, $"*{term}*", opts))
                {
                    ct.ThrowIfCancellationRequested();
                    results.Add(entry);
                    if (results.Count >= 5 || DateTime.UtcNow > deadline) return results;
                }
            return results;
        }, ct);

        if (found.Count == 0) return $"Não achei nada com \"{term}\".";
        return "Encontrei: " + string.Join("; ", found.Select(f => f)) + ". Use abrir_pasta com o caminho completo para abrir.";
    }
}
