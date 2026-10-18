using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GithubLauncher.Services;

public sealed record CommunityCatalogEntry(string Name, string Repository, string FolderName, string AppIconUrl)
{
    public string DisplayName { get; init; } = "";
    public string ProjectName { get; init; } = "";
    public string CoverUrl { get; init; } = "";
    public string ProjectType { get; init; } = "";
    public string[] SupportedOS { get; init; } = [];
    public string[] Tags { get; init; } = [];
    public string[] OriginalSystems { get; init; } = [];
    public string[] OriginalGames { get; init; } = [];
    public int MetadataVersion { get; init; }
}
public sealed record CommunityCatalogResult(List<CommunityCatalogEntry> Entries, string Status);

/// <summary>Discovery only: catalog claims never become local gameplay verification.</summary>
public static class CommunityCatalog
{
    public const string ApiUrl = "https://api.quiverlauncher.com/api/v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<CommunityCatalogResult> LoadAsync(string directory, bool refresh,
        HttpClient? client = null, string? bundledPath = null)
    {
        var path = Path.Combine(directory, "quiver_catalog_cache.json");
        var cached = ReadCache(path);
        var bundled = ReadCache(bundledPath ?? Path.Combine(AppContext.BaseDirectory, "quiver-catalog.json"));
        // Upgrade old snapshots locally; browsing must not require another full API crawl.
        if (cached != null && bundled != null)
        {
            var metadata = bundled.Entries.ToDictionary(e => e.Repository, StringComparer.OrdinalIgnoreCase);
            cached.Entries = cached.Entries.Select(e =>
            {
                if (!metadata.TryGetValue(e.Repository, out var newer)) return e;
                if (string.IsNullOrEmpty(e.DisplayName)) return newer;
                return e.MetadataVersion < newer.MetadataVersion ? e with
                {
                    Tags = newer.Tags, OriginalSystems = newer.OriginalSystems,
                    OriginalGames = newer.OriginalGames, MetadataVersion = newer.MetadataVersion
                } : e;
            }).ToList();
        }
        cached ??= bundled;
        if (!refresh && cached != null && cached.CheckedAt > DateTimeOffset.UtcNow.AddHours(-24))
            return Result(cached, "cached");

        // Never send the player's GitHub credentials to a third-party catalog.
        using var ownedClient = client == null ? new HttpClient { Timeout = TimeSpan.FromSeconds(15) } : null;
        if (ownedClient != null) ownedClient.DefaultRequestHeaders.UserAgent.ParseAdd("GitOpsGarage-GithubLauncher");
        client ??= ownedClient!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var token = timeout.Token;
        try
        {
            var slugs = new HashSet<string>(StringComparer.Ordinal);
            var cursors = new HashSet<string>(StringComparer.Ordinal);
            string? cursor = null;
            for (int page = 0; ; page++)
            {
                if (page >= 50) throw new JsonException("Catalog pagination exceeded its limit.");
                var url = ApiUrl + "/apps?limit=100" + (cursor == null ? "" : "&cursor=" + Uri.EscapeDataString(cursor));
                using var document = JsonDocument.Parse(await client.GetStringAsync(url, token).ConfigureAwait(false));
                var root = document.RootElement;
                foreach (var item in root.GetProperty("items").EnumerateArray())
                {
                    var slug = Text(item, "slug");
                    if (!string.IsNullOrWhiteSpace(slug)) slugs.Add(slug);
                }
                if (root.GetProperty("isDone").GetBoolean()) break;
                cursor = Text(root, "nextCursor");
                if (string.IsNullOrWhiteSpace(cursor) || !cursors.Add(cursor))
                    throw new JsonException("Catalog pagination did not advance.");
            }

            var entries = new System.Collections.Concurrent.ConcurrentBag<CommunityCatalogEntry>();
            int skipped = 0;
            await Parallel.ForEachAsync(slugs, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token },
                async (slug, cancellation) =>
                {
                    using var document = JsonDocument.Parse(await client.GetStringAsync(
                        ApiUrl + "/apps/" + Uri.EscapeDataString(slug), cancellation).ConfigureAwait(false));
                    var entry = ParseDetail(document.RootElement);
                    if (entry == null) Interlocked.Increment(ref skipped);
                    else entries.Add(entry);
                }).ConfigureAwait(false);

            var snapshot = new Snapshot
            {
                CheckedAt = DateTimeOffset.UtcNow,
                Entries = entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                    .DistinctBy(e => e.Repository, StringComparer.OrdinalIgnoreCase).ToList(),
                Skipped = skipped
            };
            if (snapshot.Entries.Count == 0) throw new JsonException("Catalog returned no supported GitHub projects.");
            Directory.CreateDirectory(directory);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(snapshot, Json), token).ConfigureAwait(false);
                File.Move(temporary, path, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return Result(snapshot, "updated");
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException
            or IOException or InvalidOperationException or KeyNotFoundException)
        {
            System.Diagnostics.Debug.WriteLine($"Community catalog unavailable: {ex.Message}");
            return cached != null ? Result(cached, "offline copy; refresh failed")
                : new([], "Quiver unavailable; showing the other catalogs.");
        }
    }

    public static CommunityCatalogEntry? ParseDetail(JsonElement root)
    {
        if (root.TryGetProperty("withdrawn", out var withdrawn) && withdrawn.ValueKind == JsonValueKind.True) return null;
        var project = root.GetProperty("project");
        if (!string.Equals(Text(project, "provider"), "github", StringComparison.OrdinalIgnoreCase)) return null;
        var repository = Text(project, "repository").Trim();
        if (!Regex.IsMatch(repository, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$") || repository.Split('/').Any(p => p is "." or "..")) return null;
        var entry = root.GetProperty("entry");
        var name = Text(entry, "name");
        var projectName = Text(entry, "projectName");
        if (string.IsNullOrWhiteSpace(name)) return null;
        // These entries require capabilities our installer does not yet implement.
        if (entry.TryGetProperty("launcher", out var launcher))
        {
            if (!string.IsNullOrWhiteSpace(Text(launcher, "releaseAssetFilter"))) return null;
            if (launcher.TryGetProperty("filesToAdd", out var files) && files.ValueKind == JsonValueKind.Array && files.GetArrayLength() > 0) return null;
        }
        var icon = HttpsUrl(Text(entry, "artwork"));
        var cover = entry.TryGetProperty("libraryArt", out var art) && art.ValueKind == JsonValueKind.Object
            ? HttpsUrl(Text(art, "header")) : "";
        var title = string.IsNullOrWhiteSpace(projectName) || name.Equals(projectName, StringComparison.OrdinalIgnoreCase)
            ? name : $"{name} ({projectName})";
        var originals = Objects(entry, "games").Concat(Objects(root, "games")).ToList();
        if (root.TryGetProperty("game", out var game) && game.ValueKind == JsonValueKind.Object) originals.Add(game);
        var consoles = Strings(entry, "consoles").Concat(originals.SelectMany(g => Strings(g, "originalSystems")));
        if (entry.TryGetProperty("basedOn", out var basedOn) && basedOn.ValueKind == JsonValueKind.Object)
            consoles = consoles.Append(Text(basedOn, "console"));
        // Derive a portable, unique folder instead of trusting external paths.
        return new(title, repository, repository.Replace('/', '_'), icon)
        {
            DisplayName = name, ProjectName = projectName, CoverUrl = cover,
            ProjectType = Text(entry, "projectType"),
            Tags = Strings(entry, "tags").Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            OriginalSystems = consoles.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            OriginalGames = originals.Select(g => Text(g, "title")).Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            MetadataVersion = 2,
            SupportedOS = entry.TryGetProperty("supportedOS", out var systems) && systems.ValueKind == JsonValueKind.Array
                ? CatalogPresentation.Platforms(systems.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String)
                    .Select(v => v.GetString()!)) : []
        };
    }

    private static string HttpsUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" ? value : "";

    private static IEnumerable<string> Strings(JsonElement element, string property) =>
        element.TryGetProperty(property, out var values) && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String)
                .Select(v => v.GetString()!.Trim()).Where(s => s.Length > 0) : [];

    private static IEnumerable<JsonElement> Objects(JsonElement element, string property) =>
        element.TryGetProperty(property, out var values) && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.Object) : [];

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static Snapshot? ReadCache(string path)
    {
        try
        {
            var snapshot = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(path), Json);
            return snapshot?.Entries is { Count: > 0 } ? snapshot : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    private static CommunityCatalogResult Result(Snapshot snapshot, string state) => new(snapshot.Entries,
        $"Quiver: {snapshot.Entries.Count} GitHub projects ({state}); {snapshot.Skipped} entries require unsupported providers or install features. Catalog listings do not confirm Mac gameplay.");

    public sealed class Snapshot
    {
        public DateTimeOffset CheckedAt { get; set; }
        public List<CommunityCatalogEntry> Entries { get; set; } = [];
        public int Skipped { get; set; }
    }
}
