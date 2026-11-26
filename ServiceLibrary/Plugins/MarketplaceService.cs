using PluginLibrary.Sdk;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ServiceLibrary.Plugins;

/// <summary>Um plugin publicado no <c>index.json</c> do marketplace.</summary>
public sealed record MarketplaceEntry(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("author")] string Author,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("sdkVersion")] int SdkVersion,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("sha256")] string Sha256);

public sealed record MarketplaceIndex(
    [property: JsonPropertyName("schema")] int Schema,
    [property: JsonPropertyName("plugins")] List<MarketplaceEntry> Plugins);

/// <summary>
/// Baixa e atualiza os plugins de <see cref="AppConfig.EnabledPlugins"/> a partir do repositório do marketplace
/// (GitHub Releases). O CI do repositório compila o código revisado nos PRs e publica o <c>index.json</c> com o
/// SHA-256 de cada zip. Aqui nada é instalado sem estar na lista do usuário, sem o hash conferir ou fora do repo.
/// </summary>
public sealed partial class MarketplaceService(AppConfig config, HttpClient? http = null)
{
    private const string MarkerName = ".marketplace";
    private const long MaxZipBytes = 100L * 1024 * 1024;

    private readonly HttpClient _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

    [GeneratedRegex("^[a-z0-9_]{1,64}$")]
    private static partial Regex IdPattern();

    /// <summary>"dono/nome" ou a URL completa do GitHub; null se o marketplace não está configurado.</summary>
    private string? Repo
    {
        get
        {
            var r = config.MarketplaceRepo.Trim().TrimEnd('/');
            if (r.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)) r = r["https://github.com/".Length..];
            return Regex.IsMatch(r, @"^[\w.-]+/[\w.-]+$") ? r : null;
        }
    }

    /// <summary>Instala/atualiza os plugins liberados. Nunca lança: falha de rede mantém o que já está instalado.</summary>
    public async Task SyncAsync(CancellationToken ct = default)
    {
        if (config.EnabledPlugins.Count == 0) return;
        if (Repo is not { } repo)
        {
            if (!string.IsNullOrWhiteSpace(config.MarketplaceRepo)) Log.Error($"MarketplaceRepo inválido: {config.MarketplaceRepo}");
            return;
        }

        try
        {
            var index = await _http.GetFromJsonAsync<MarketplaceIndex>(
                $"https://github.com/{repo}/releases/latest/download/index.json", ct);
            if (index is null) return;

            foreach (var id in config.EnabledPlugins)
            {
                var entry = index.Plugins.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                if (entry is null) { Log.Info($"Marketplace: \"{id}\" não existe no índice"); continue; }

                try { await InstallAsync(repo, entry, ct); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { Log.Error($"Marketplace: falha ao instalar {id}", ex); }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log.Error("Marketplace indisponível; usando os plugins já instalados", ex);
        }
    }

    private async Task InstallAsync(string repo, MarketplaceEntry entry, CancellationToken ct)
    {
        if (!IdPattern().IsMatch(entry.Id)) throw new InvalidDataException($"id inválido: {entry.Id}");
        if (entry.SdkVersion != SdkInfo.Version)
        {
            Log.Info($"Marketplace: {entry.Id} usa o SDK {entry.SdkVersion} (o app usa {SdkInfo.Version}); ignorado");
            return;
        }
        // Só aceita arquivos publicados nos releases do próprio repositório do marketplace.
        if (!entry.Url.StartsWith($"https://github.com/{repo}/releases/download/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"URL fora do repositório do marketplace: {entry.Url}");

        var target = Path.Combine(PluginLoader.Folder, entry.Id);
        var marker = Path.Combine(target, MarkerName);
        var stamp = $"{entry.Version}|{entry.Sha256.ToLowerInvariant()}";
        if (File.Exists(marker) && await File.ReadAllTextAsync(marker, ct) == stamp) return;

        Log.Info($"Marketplace: baixando {entry.Id} {entry.Version}");
        var work = Path.Combine(PluginLoader.Folder, ".staging", entry.Id);
        if (Directory.Exists(work)) Directory.Delete(work, true);
        Directory.CreateDirectory(work);
        var zipPath = Path.Combine(work, "pacote.zip");
        var extracted = Path.Combine(work, "conteudo");

        try
        {
            using (var resp = await _http.GetAsync(entry.Url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                if (resp.Content.Headers.ContentLength > MaxZipBytes) throw new InvalidDataException("pacote grande demais");
                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var dst = File.Create(zipPath);
                await src.CopyToAsync(dst, ct);
            }

            await using (var fs = File.OpenRead(zipPath))
            {
                if (fs.Length > MaxZipBytes) throw new InvalidDataException("pacote grande demais");
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct));
                if (!hash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("SHA-256 não confere; pacote descartado");
            }

            ExtractSafely(zipPath, extracted);
            if (!File.Exists(Path.Combine(extracted, entry.Id + ".dll")))
                throw new InvalidDataException($"{entry.Id}.dll não está no pacote");

            await File.WriteAllTextAsync(Path.Combine(extracted, MarkerName), stamp, ct);
            if (Directory.Exists(target)) Directory.Delete(target, true);
            Directory.Move(extracted, target);
            Log.Info($"Marketplace: {entry.Id} {entry.Version} instalado");
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { /* limpeza: não atrapalha */ }
        }
    }

    /// <summary>Extrai recusando entradas que escapem da pasta de destino (zip-slip).</summary>
    private static void ExtractSafely(string zipPath, string dest)
    {
        Directory.CreateDirectory(dest);
        var root = Path.GetFullPath(dest) + Path.DirectorySeparatorChar;
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var e in zip.Entries)
        {
            var path = Path.GetFullPath(Path.Combine(dest, e.FullName));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"entrada suspeita no zip: {e.FullName}");
            if (e.FullName.EndsWith('/')) { Directory.CreateDirectory(path); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            e.ExtractToFile(path, overwrite: true);
        }
    }
}
