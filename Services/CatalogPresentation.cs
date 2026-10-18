using GitHubLauncher.Core.Services;

namespace GithubLauncher.Services;

/// <summary>Catalog platforms describe published targets, never verified gameplay.</summary>
public static class CatalogPresentation
{
    public static readonly string[] PlatformKeys = ["macos", "windows", "linux", "android", "ios"];

    public static string[] Platforms(IEnumerable<string> declared, MacCompatibilityEntry? release = null)
    {
        var known = declared.Select(s => s.Trim().ToLowerInvariant() switch
        {
            "mac" or "macos" or "osx" => "macos",
            "win" or "windows" => "windows",
            "linux" => "linux",
            "android" => "android",
            "ios" => "ios",
            _ => ""
        }).Where(s => s.Length > 0).ToHashSet();
        // Recorded release assets can supplement a catalog that omits platforms.
        if (release != null && !string.IsNullOrWhiteSpace(release.Evidence))
        {
            var assets = (release.AvailableAssets ?? []).Append(release.AssetName);
            if (assets.Any(a => PlatformAssetMatcher.MatchesPlatform(a, "macOS"))) known.Add("macos");
            if (assets.Any(PlatformAssetMatcher.IsWindowsAsset)) known.Add("windows");
            if (assets.Any(a => a.Contains("linux", StringComparison.OrdinalIgnoreCase) ||
                a.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase) ||
                a.EndsWith(".deb", StringComparison.OrdinalIgnoreCase) ||
                a.EndsWith(".rpm", StringComparison.OrdinalIgnoreCase))) known.Add("linux");
        }
        return PlatformKeys.Where(known.Contains).ToArray();
    }

    public static string PlatformLabel(string key) => key switch
    {
        "macos" => "macOS", "windows" => "Windows", "linux" => "Linux",
        "android" => "Android", "ios" => "iOS", _ => "Not listed"
    };

    public static string ProjectLabel(string type) => type.ToLowerInvariant() switch
    {
        "port" => "PORT", "recomp" or "recompilation" => "RECOMPILATION",
        "decomp" or "decompilation" => "DECOMPILATION", "remake" => "REMAKE",
        "recreation" => "RECREATION", "engine" => "ENGINE", _ => "PROJECT"
    };

    public static bool MatchesSearch(string query, params string[] fields) =>
        query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(word => fields.Any(f => f.Contains(word, StringComparison.OrdinalIgnoreCase)));

    public static bool MatchesPlatforms(string[] available, IReadOnlySet<string> selected, bool includeUnknown) =>
        available.Length == 0 ? includeUnknown : available.Any(selected.Contains);

    public static bool ShouldLoadMore(double offset, double viewport, double extent, int shown, int total) =>
        viewport > 0 && shown < total && offset + viewport >= extent - 200;
}
