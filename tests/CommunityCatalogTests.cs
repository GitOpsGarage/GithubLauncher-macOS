using System.Net;
using System.Text.Json;
using GithubLauncher.Services;

static class CommunityCatalogTests
{
    public static async Task RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "community-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string Detail(string repository = "team/game", string provider = "github", string extra = "") =>
                "{\"entry\":{\"name\":\"Game\",\"artwork\":\"file:///private/file\",\"launcher\":{" + extra +
                "}},\"project\":{\"provider\":\"" + provider + "\",\"repository\":\"" + repository + "\"}}";
            CommunityCatalogEntry? Parse(string json)
            {
                using var doc = JsonDocument.Parse(json);
                return CommunityCatalog.ParseDetail(doc.RootElement);
            }
            void Check(bool value, string message) { if (!value) throw new Exception(message); }
            var valid = Parse(Detail())!;
            Check(valid.FolderName == "team_game" && valid.AppIconUrl == "", "External catalog paths and non-HTTPS artwork must not be used.");
            Check(Parse(Detail("../game")) == null && Parse(Detail(provider: "gitlab")) == null,
                "Unsafe repositories and unsupported providers must be excluded.");
            Check(Parse(Detail(extra: "\"releaseAssetFilter\":\"game-one\"")) == null &&
                Parse(Detail(extra: "\"filesToAdd\":[\"setup\"]")) == null, "Required install metadata must not be silently dropped.");

            var metadata = Parse("""
                {"entry":{"name":"Original Game","projectName":"Community Port","projectType":"port",
                "tags":["decomp","decompilation",12],"consoles":["n64"],"basedOn":{"console":"n64"},
                "games":[{"title":"Original Game"},{"title":"Other Game"}],
                "supportedOS":["Windows","macOS","LINUX","macos","unknown",12],
                "libraryArt":{"header":"https://example.com/cover.jpg"}},
                "game":{"title":"Original Game","originalSystems":["ps1"]},
                "project":{"provider":"github","repository":"team/game"}}
                """)!;
            Check(metadata.DisplayName == "Original Game" && metadata.ProjectName == "Community Port" &&
                metadata.CoverUrl == "https://example.com/cover.jpg" && metadata.ProjectType == "port" &&
                metadata.SupportedOS.SequenceEqual(new[] { "macos", "windows", "linux" }),
                "Catalog display metadata must survive import, with distinct normalized platforms.");
            Check(metadata.OriginalSystems.SequenceEqual(new[] { "n64", "ps1" }) &&
                metadata.OriginalGames.SequenceEqual(new[] { "Original Game", "Other Game" }) && metadata.Tags.Length == 2,
                "Original game, source console and method tags must survive import and deduplicate independently.");
            Check(CatalogPresentation.ProjectLabel(metadata.ProjectType, metadata.Tags) == "DECOMPILATION" &&
                CatalogPresentation.ProjectLabel("port", ["recomp", "recompilation"]) == "RECOMPILATION" &&
                CatalogPresentation.ProjectLabel("port", ["controller support"]) == "PORT",
                "Explicit method tags must refine generic port types without guessing from unrelated tags.");
            Check(CatalogPresentation.OriginSummary(metadata).Contains("Nintendo 64 · PlayStation (PS1)") &&
                CatalogPresentation.OriginSummary(metadata).Contains("Original Game · Other Game") &&
                CatalogPresentation.OriginSummary(valid) == "Origin not listed" &&
                CatalogPresentation.OriginSummary(null) == "Origin not listed",
                "Origin labels must show recorded consoles/games and explicitly handle missing metadata.");
            Check(!MacCompatibility.MatchesCatalogFilter(null, MacCatalogFilter.Native),
                "A catalog's macOS claim must not become recorded native release evidence.");
            Check(CatalogPresentation.Platforms([], new() { AssetName = "source.tar.gz", Evidence = "release" }).Length == 0,
                "A generic source archive must not claim Linux support.");
            Check(CatalogPresentation.Platforms([], new() { AssetName = "game-macos.dmg", Evidence = "release",
                AvailableAssets = ["game-win64.zip", "game-linux.AppImage"] }).SequenceEqual(new[] { "macos", "windows", "linux" }),
                "Recorded release assets must supply platforms when catalog metadata is absent.");
            Check(CatalogPresentation.MatchesSearch("original port", metadata.DisplayName, metadata.ProjectName) &&
                !CatalogPresentation.MatchesSearch("original missing", metadata.DisplayName, metadata.ProjectName),
                "Search must match every word across game and project names.");
            var desktop = new HashSet<string> { "macos", "windows", "linux" };
            Check(!CatalogPresentation.MatchesPlatforms(["android", "ios"], desktop, true) &&
                CatalogPresentation.MatchesPlatforms(["macos", "android"], desktop, true),
                "Desktop filters must hide mobile-only projects while keeping projects with a desktop target.");
            Check(CatalogPresentation.MatchesPlatforms(["windows"], desktop, false) &&
                CatalogPresentation.MatchesPlatforms(["linux"], desktop, false) &&
                !CatalogPresentation.MatchesPlatforms(["windows"], new HashSet<string>(), false),
                "Multiple platform checkboxes must match any selection; no selections must match no known platforms.");
            Check(CatalogPresentation.MatchesPlatforms([], desktop, true) &&
                !CatalogPresentation.MatchesPlatforms([], desktop, false),
                "Unknown platforms must obey their own checkbox.");
            Check(!CatalogPresentation.ShouldLoadMore(0, 600, 4000, 36, 100) &&
                CatalogPresentation.ShouldLoadMore(3250, 600, 4000, 36, 100),
                "Automatic paging must wait until scrolling near the end.");
            Check(!CatalogPresentation.ShouldLoadMore(3400, 600, 4000, 100, 100) &&
                !CatalogPresentation.ShouldLoadMore(0, 0, 0, 0, 100) &&
                CatalogPresentation.ShouldLoadMore(0, 900, 700, 36, 100),
                "Automatic paging must stop at the last item, ignore unmeasured views and fill a short page.");

            using var client = new HttpClient(new Handler(request => request.RequestUri!.AbsolutePath.EndsWith("/apps")
                ? request.RequestUri.Query.Contains("cursor=")
                    ? "{\"items\":[{\"slug\":\"other\"}],\"isDone\":true}"
                    : "{\"items\":[{\"slug\":\"game\"},{\"slug\":\"duplicate\"}],\"nextCursor\":\"page two\",\"isDone\":false}"
                : Detail(request.RequestUri.AbsolutePath.EndsWith("/other") ? "team/other" : "team/game")));
            var result = await CommunityCatalog.LoadAsync(directory, true, client);
            Check(result.Entries.Count == 2 && result.Status.Contains("updated"), "Paginated entries must merge by repository.");
            var cache = File.ReadAllText(Path.Combine(directory, "quiver_catalog_cache.json"));
            using var offline = new HttpClient(new Handler(_ => throw new HttpRequestException("offline")));
            result = await CommunityCatalog.LoadAsync(directory, true, offline);
            Check(result.Entries.Count == 2 && result.Status.Contains("offline") &&
                File.ReadAllText(Path.Combine(directory, "quiver_catalog_cache.json")) == cache,
                "Offline refresh must preserve the previous complete snapshot.");
            using var broken = new HttpClient(new Handler(_ => "{\"items\":[],\"isDone\":false,\"nextCursor\":\"repeat\"}"));
            result = await CommunityCatalog.LoadAsync(directory, true, broken);
            Check(result.Entries.Count == 2 && result.Status.Contains("offline"), "Repeated cursors must fall back instead of looping.");
            File.Delete(Path.Combine(directory, "quiver_catalog_cache.json"));
            var bundle = Path.Combine(directory, "bundle.json");
            File.WriteAllText(bundle, cache);
            result = await CommunityCatalog.LoadAsync(directory, true, offline, bundle);
            Check(result.Entries.Count == 2, "First offline launch must use the bundled snapshot.");

            var legacyCache = JsonSerializer.Deserialize<CommunityCatalog.Snapshot>(cache,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            legacyCache.Entries = legacyCache.Entries.Select(e => e with { DisplayName = "" }).ToList();
            File.WriteAllText(Path.Combine(directory, "quiver_catalog_cache.json"), JsonSerializer.Serialize(legacyCache,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            File.WriteAllText(bundle, JsonSerializer.Serialize(new CommunityCatalog.Snapshot
            {
                CheckedAt = DateTimeOffset.UtcNow, Entries = [metadata]
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            result = await CommunityCatalog.LoadAsync(directory, false, offline, bundle);
            Check(result.Entries.Count == 2 && result.Entries.Single(e => e.Repository == "team/game").SupportedOS.Length == 3,
                "Old fresh caches must gain bundled display metadata offline without losing cached projects.");
            legacyCache.Entries = legacyCache.Entries.Select(e => e with { DisplayName = "Cached title", MetadataVersion = 0 }).ToList();
            File.WriteAllText(Path.Combine(directory, "quiver_catalog_cache.json"), JsonSerializer.Serialize(legacyCache,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            result = await CommunityCatalog.LoadAsync(directory, false, offline, bundle);
            var upgraded = result.Entries.Single(e => e.Repository == "team/game");
            Check(upgraded.DisplayName == "Cached title" && upgraded.MetadataVersion == 2 &&
                upgraded.OriginalGames.Length == 2 && upgraded.Tags.Contains("decomp"),
                "Existing display metadata must be retained while old caches gain method and origin fields offline.");
            Console.WriteLine("Community catalog pagination, mapping, deduplication, offline and failure checks passed.");
        }
        finally { Directory.Delete(directory, true); }
    }

    sealed class Handler(Func<HttpRequestMessage, string> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response(request)) });
    }
}
