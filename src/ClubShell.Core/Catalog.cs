using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClubShell.Core;

public sealed class LaunchSpec
{
    /// <summary>До трёх вариантов пути: на разных ПК программа может лежать в разных местах. Берётся первый найденный.</summary>
    public List<string> Paths { get; set; } = new();
    public string? Args { get; set; }
    public string? WorkDir { get; set; }
}

public sealed class CatalogEntry
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Kind { get; set; } = "game"; // game | app
    public string? Group { get; set; }
    public string? Cover { get; set; }
    public bool ClubAccount { get; set; }
    public int? MinAge { get; set; }
    public bool Pinned { get; set; }          // закреплено на панели задач
    public LaunchSpec Launch { get; set; } = new();
}

/// <summary>Запись каталога, для которой на этом ПК найден путь запуска.</summary>
public sealed record ResolvedEntry(CatalogEntry Entry, string Path, string? Args, string? WorkDir);

public static class CatalogLoader
{
    private sealed class CatalogFile { public List<CatalogEntry> Items { get; set; } = new(); }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public static IReadOnlyList<CatalogEntry> Load(string file)
    {
        if (!File.Exists(file)) return Array.Empty<CatalogEntry>();
        var parsed = JsonSerializer.Deserialize<CatalogFile>(File.ReadAllText(file), Options);
        return parsed?.Items.Where(e => e.Id.Length > 0 && e.Title.Length > 0).ToList() ?? new List<CatalogEntry>();
    }

    /// <summary>
    /// Оставляет только то, что реально есть на этом ПК: гость не видит ярлыки, которые не запустятся.
    /// URI-схемы (steam://, uplay:// …) и shell:AppsFolder проверить заранее нельзя, поэтому считаются доступными.
    /// </summary>
    public static IReadOnlyList<ResolvedEntry> Resolve(IEnumerable<CatalogEntry> entries, Func<string, bool>? fileExists = null, Func<string, string?>? finder = null)
    {
        fileExists ??= File.Exists;
        var result = new List<ResolvedEntry>();
        foreach (var e in entries)
        {
            foreach (var raw in e.Launch.Paths.Take(3))
            {
                var path = Environment.ExpandEnvironmentVariables(raw);
                if (path.StartsWith(ExeFinder.Prefix, StringComparison.OrdinalIgnoreCase))
                    path = (finder ?? ExeFinder.Find)(path[ExeFinder.Prefix.Length..]) ?? "";
                if (path.Length > 0 && (IsUri(path) || fileExists(path)))
                {
                    result.Add(new ResolvedEntry(e, path, e.Launch.Args, e.Launch.WorkDir is null ? null : Environment.ExpandEnvironmentVariables(e.Launch.WorkDir)));
                    break;
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Подставляет в аргументы запуска {user} (имя гостя) и {pc} (имя ПК).
    /// Значение берётся от сервера, поэтому из него убираются кавычки и управляющие символы, а результат всегда в кавычках:
    /// гость не может дописать в командную строку лишние ключи.
    /// </summary>
    public static string ExpandArgs(string? args, string? user, string pc)
    {
        if (string.IsNullOrEmpty(args)) return "";
        return args
            .Replace("{user}", Quote(user ?? ""), StringComparison.Ordinal)
            .Replace("{pc}", Quote(pc), StringComparison.Ordinal);
    }

    private static string Quote(string value)
    {
        var clean = new string(value.Where(c => c != '"' && !char.IsControl(c)).ToArray()).TrimEnd('\\');
        return "\"" + clean + "\"";
    }

    public static bool IsUri(string path) =>
        path.Contains("://", StringComparison.Ordinal) || path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase);
}
