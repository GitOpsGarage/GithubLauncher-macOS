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

    public static string ProjectLabel(string type, IEnumerable<string>? tags = null)
    {
        var methods = (tags ?? []).Select(t => t.Trim().ToLowerInvariant() switch
        {
            "decomp" or "decompilation" => "DECOMPILATION",
            "recomp" or "recompilation" or "static recompilation" => "RECOMPILATION",
            "remake" => "REMAKE", "recreation" => "RECREATION", _ => ""
        }).Where(s => s.Length > 0).Distinct().ToArray();
        return methods.Length > 0 ? string.Join(" · ", methods) : ProjectTypeLabel(type);
    }

    private static string ProjectTypeLabel(string type) => type.ToLowerInvariant() switch
    {
        "port" => "PORT", "recomp" or "recompilation" => "RECOMPILATION",
        "decomp" or "decompilation" => "DECOMPILATION", "remake" => "REMAKE",
        "recreation" => "RECREATION", "engine" => "ENGINE", "tool" => "TOOL",
        "emulator" => "EMULATOR", "game" => "STANDALONE GAME", _ => "PROJECT"
    };

    public static string OriginSummary(CommunityCatalogEntry? entry)
    {
        var systems = (entry?.OriginalSystems ?? []).Select(ConsoleLabel).Distinct().ToArray();
        var games = entry?.OriginalGames ?? [];
        return string.Join("\n", new[]
        {
            systems.Length > 0 ? "Origin: " + string.Join(" · ", systems) : "Origin not listed",
            games.Length > 0 ? "Based on: " + string.Join(" · ", games) : ""
        }.Where(s => s.Length > 0));
    }

    public static string ConsoleLabel(string id) => id.Trim().ToLowerInvariant() switch
    {
        "nes" => "NES", "snes" => "SNES", "n64" => "Nintendo 64", "gcn" => "GameCube",
        "wii" => "Wii", "wiiu" => "Wii U", "switch" => "Nintendo Switch",
        "gb" => "Game Boy", "vb" => "Virtual Boy", "gbc" => "Game Boy Color", "gba" => "Game Boy Advance",
        "nds" => "Nintendo DS", "3ds" => "Nintendo 3DS", "ps1" => "PlayStation (PS1)",
        "ps2" => "PlayStation 2", "ps3" => "PlayStation 3", "ps4" => "PlayStation 4",
        "ps5" => "PlayStation 5", "psp" => "PlayStation Portable", "og xbox" => "Xbox", "x360" => "Xbox 360",
        "sms" => "Sega Master System", "gg" => "Sega Game Gear", "gen" => "Sega Genesis / Mega Drive",
        "segacd" => "Sega CD / Mega-CD", "32x" => "Sega 32X", "saturn" => "Sega Saturn",
        "dreamcast" => "Sega Dreamcast", "arcade" => "Arcade", "pc" => "PC", "mobile" => "Mobile",
        _ => id
    };

    public static bool MatchesSearch(string query, params string[] fields) =>
        query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(word => fields.Any(f => f.Contains(word, StringComparison.OrdinalIgnoreCase)));

    public static bool MatchesPlatforms(string[] available, IReadOnlySet<string> selected, bool includeUnknown) =>
        available.Length == 0 ? includeUnknown : available.Any(selected.Contains);

    public static bool ShouldLoadMore(double offset, double viewport, double extent, int shown, int total) =>
        viewport > 0 && shown < total && offset + viewport >= extent - 200;
}
