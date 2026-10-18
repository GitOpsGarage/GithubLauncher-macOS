using GithubLauncher.Services;
using GitHubLauncher.Core.Models;

if (args is ["--refresh-community-catalog", var outputDirectory])
{
    var result = await CommunityCatalog.LoadAsync(outputDirectory, true);
    Console.WriteLine(result.Status);
    if (!result.Status.Contains("updated")) throw new Exception("Live catalog refresh failed.");
    return;
}
await CommunityCatalogTests.RunAsync();

int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
}
GitHubRelease Release(params string[] names) => new()
{
    tag_name = "v1", assets = names.Select(n => new GitHubAsset { name = n }).ToArray()
};
Check(MacCompatibility.FilterAssets(Release("game-Windows.zip", "game-Linux.zip"), null).Count == 0,
    "Unverified Windows/Linux assets must not be offered on Mac.");
Check(MacCompatibility.FilterAssets(Release("game-macOS.zip", "game-Windows.zip", "game-macOS.dmg"), null)
    .Select(a => a.name).SequenceEqual(new[] { "game-macOS.zip", "game-macOS.dmg" }), "ZIP is preferred but native DMG remains available.");
var approved = new MacCompatibilityEntry { Mode = "crossover", Release = "v1", AssetName = "game-Windows.zip",
    Verified = true, Evidence = "Local gameplay test with CrossOver 26.3", Reports = [new() {
        Runner = "crossover", Release = "v1", AssetName = "game-Windows.zip", Result = "playable",
        Environment = "M5, macOS 26.6.2, CrossOver 26.3, D3DMetal", Evidence = "Gameplay fixture report", TestedAt = DateTimeOffset.UtcNow, PlatformFingerprint = MacGameplay.PlatformFingerprint, RuntimeId = MacGameplay.RuntimeIdentity("crossover") }] };
Check(MacCompatibility.FilterAssets(Release("game-Windows.zip", "other-Windows.exe"), approved).Count == 1,
    "CrossOver approval must match the exact asset.");
Check(!MacCompatibility.IsVerifiedCrossOver(approved, "v2"), "New versions require fresh verification.");
approved.Reports[0].Evidence = "";
Check(MacCompatibility.FilterAssets(Release("game-Windows.zip"), approved).Count == 0,
    "An unverified record must not enable Windows downloads.");
approved.Reports[0].Evidence = "Gameplay fixture report";
Check(MacCompatibility.CatalogCategory(approved) == "tested", "Verified records are classified as tested.");
var candidate = new MacCompatibilityEntry { Mode = "candidate", Release = "v1", AssetName = "game-Windows.zip", Evidence = "official release" };
Check(MacCompatibility.CatalogCategory(candidate) == "candidate", "Published Windows builds are candidates, not tested.");
Check(MacCompatibility.MatchesCatalogFilter(null, MacCatalogFilter.All) &&
    MacCompatibility.MatchesCatalogFilter(candidate, MacCatalogFilter.All),
    "All games must include both unassessed projects and Windows candidates.");
Check(MacCompatibility.BrowseCategory(candidate) == MacCatalogFilter.Other &&
    !MacCompatibility.MatchesCatalogFilter(candidate, MacCatalogFilter.WindowsPlayable),
    "An untested Windows release must remain selectable without appearing as confirmed.");
Check(MacCompatibility.BrowseCategory(approved) == MacCatalogFilter.WindowsPlayable,
    "A current playable Windows report belongs in the confirmed Windows group.");
var nativeWithWindowsReport = new MacCompatibilityEntry { Mode = "native", Release = "v1",
    AssetName = "game-macOS.zip", Evidence = "official release", Reports = approved.Reports };
Check(MacCompatibility.BrowseCategory(nativeWithWindowsReport) == MacCatalogFilter.Native,
    "A native project must stay in the native group even when its Windows build also works.");
Check(MacCompatibility.BrowseCategory(new() { Mode = "unknown", Release = "v2", Reports = approved.Reports }) == MacCatalogFilter.Other,
    "Reports for an old release must not put a project in the confirmed Windows group.");
var nativeOnlyReport = new MacCompatibilityEntry { Mode = "unknown", Release = "v1", Reports = [new() {
    Runner = "native", Release = "v1", AssetName = "game-macOS.zip", Result = "playable",
    Environment = "fixture Mac", Evidence = "fixture gameplay report", TestedAt = DateTimeOffset.UtcNow,
    PlatformFingerprint = MacGameplay.PlatformFingerprint, RuntimeId = "native" }] };
Check(MacCompatibility.BrowseCategory(nativeOnlyReport) == MacCatalogFilter.Other,
    "A native gameplay report must never be treated as confirmed Windows compatibility.");
Check(MacCompatibility.FilterAssets(Release("game-Windows.zip", "game-Linux.zip"), candidate).Count == 0,
    "Candidate downloads require explicit per-game testing opt-in.");
Check(MacCompatibility.FilterAssets(Release("game-Windows.zip", "game-Linux.zip", "game-Windows.msi"), candidate, true)
    .Select(a => a.name).SequenceEqual(new[] { "game-Windows.zip" }), "Opt-in excludes Linux and unsupported installers.");
Check(MacCompatibility.FilterAssets(Release("game-macOS.zip", "game-Windows.zip"), approved)[0].name == "game-macOS.zip",
    "Native assets take priority over CrossOver.");
Check(GameDataRequirements.Find("Matteo842/CrashBandicoot-Launcher").Details.Contains("SCUS-94900") &&
    GameDataRequirements.Find("Matteo842/CrashBandicoot-Launcher").Console == "PlayStation (PS1)",
    "Crash must identify its original PS1 USA disc, independently of the downloaded port platform.");
Check(GameDataRequirements.Find("unknown/project").DiscId == "Not confirmed",
    "Unknown game data must not be guessed from the title or port platform.");
Check(GameDataRequirements.NoReleaseMessage("Crash", false, false).Contains("No native macOS release") &&
    GameDataRequirements.NoReleaseMessage("Crash", false, false).Contains("Configure Mac launch"),
    "Missing native releases must explain the selected platform and the manual Windows testing path.");
var windowsOnlyPlatforms = MacCompatibility.AvailablePlatforms([Release("CrashBandicoot.exe", "CrashBandicoot-Linux")]);
Check(!windowsOnlyPlatforms.Native && windowsOnlyPlatforms.Windows,
    "A Windows-only Crash release must never offer the native Mac download option.");
var bothPlatforms = MacCompatibility.AvailablePlatforms([Release("game-macOS.zip", "game-Windows.zip")]);
Check(bothPlatforms.Native && bothPlatforms.Windows, "Both platforms remain available when both downloads exist.");
Check(MacCompatibility.AvailablePlatforms([]) == (false, false),
    "No releases must leave both download options unavailable.");
Check(!SteamNotProton.MatchesPreparedExecutable(0, "", "/games/CrashBandicoot.exe", "Crash"),
    "Downloading a game without a registered shortcut must request Steam preparation.");
var crashId = SteamNotProton.GameId("/games/CrashBandicoot.exe", "Crash");
Check(SteamNotProton.MatchesPreparedExecutable(crashId, "/games/CrashBandicoot.exe", "/games/CrashBandicoot.exe", "Crash") &&
    !SteamNotProton.MatchesPreparedExecutable(crashId, "/games/CrashBandicoot.exe", "/games/Other.exe", "Crash") &&
    !SteamNotProton.MatchesPreparedExecutable(crashId, "/games/CrashBandicoot.exe", "/games/CrashBandicoot.exe", "Renamed"),
    "Steam preparation must match the current executable and shortcut identity.");
var root = Path.Combine(Path.GetTempPath(), "github-launcher-tests-" + Guid.NewGuid());
Directory.CreateDirectory(root);
try
{
    File.WriteAllText(Path.Combine(root, "README"), new string('x', 2048));
    File.WriteAllText(Path.Combine(root, "game with spaces.exe"), "fixture");
    var files = MacCompatibility.FindExecutables(root, SearchOption.AllDirectories, out var needsWine);
    Check(files.Count == 1 && needsWine, "Text files must not hide Windows executables.");
    byte[] macho = new byte[16];
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(macho, 0xFEEDFACF);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(macho.AsSpan(12), 2);
    File.WriteAllBytes(Path.Combine(root, "native.game"), macho);
    files = MacCompatibility.FindExecutables(root, SearchOption.AllDirectories, out needsWine);
    Check(files.Count == 1 && !needsWine && files[0].EndsWith("native.game"), "Native Mach-O binaries take priority.");
    File.WriteAllText(Path.Combine(root, "launch.sh"), "#!/bin/sh\nexec java -jar game.jar\n");
    files = MacCompatibility.FindExecutables(root, SearchOption.AllDirectories, out needsWine);
    Check(files[0].EndsWith("launch.sh") && !needsWine, "Launch scripts must precede bundled runtime helpers.");
    Directory.CreateDirectory(Path.Combine(root, "Game.app", "Contents", "MacOS"));
    File.WriteAllBytes(Path.Combine(root, "Game.app", "Contents", "MacOS", "helper"), macho);
    files = MacCompatibility.FindExecutables(root, SearchOption.AllDirectories, out needsWine);
    Check(files.Count == 1 && files[0].EndsWith("Game.app"), "Bundle helpers must not become launch candidates.");
}
finally { Directory.Delete(root, true); }
Check(!MacCompatibility.IsCatalogEligible(new() { Mode = "native", AssetName = "game-Windows.zip", Evidence = "source" }),
    "A native catalog record needs an actual Mac archive.");
Check(MacCompatibility.CatalogCategory(MacCompatibility.FromRelease("owner/game", Release("game-macOS.dmg"))) == "native",
    "Native DMG releases must be discovered without an allowlist.");
Check(MacCompatibility.CatalogCategory(MacCompatibility.FromRelease("owner/game", Release("game-Windows.zip"))) == "candidate",
    "Published Windows releases must not be classified as tested.");
Check(MacCompatibility.CatalogCategory(MacCompatibility.FromRelease("owner/game", Release())) == "unknown",
    "Missing assets mean unknown, not incompatible.");
Check(MacCompatibility.FilterAssets(Release("game-macOS-universal.zip", "game-macOS-arm64.zip"), null).Count > 0,
    "Universal builds must be offered on the host architecture.");
// Compatibility evidence is bound to the exact runner, release, archive, and observed result.
Check(!MacGameplay.IsVerified(approved, "v1", "other-Windows.zip", "crossover"), "Another asset must require a fresh gameplay test.");
Check(!MacGameplay.IsVerified(approved, "v1", "game-Windows.zip", "notproton"), "CrossOver testing must not certify NotProton.");
approved.Reports.Add(new() { Runner = "notproton", Release = "v1", AssetName = "game-Windows.zip", Result = "adjustments",
    Environment = "M5, macOS 26.6.2, NotProton/CrossOver 26.3 Rosetta D3DMetal", Evidence = "Gameplay fixture: controller mapping required", TestedAt = DateTimeOffset.UtcNow, PlatformFingerprint = MacGameplay.PlatformFingerprint, RuntimeId = MacGameplay.RuntimeIdentity("notproton", "notproton-26.3") });
Check(MacCompatibility.FilterAssets(Release("game-Windows.zip"), approved, false, "notproton").Count == 1,
    "NotProton downloads need their own complete playable report.");
Check(MacGameplay.Current(approved).Count == 2, "Independent runner reports must coexist.");
Check(MacGameplay.Current(new() { Release = "v2", AssetName = approved.AssetName, Reports = approved.Reports }).Count == 0,
    "New releases must not inherit current gameplay status.");
approved.Reports.Add(new() { Runner = "notproton", Release = "v1", AssetName = "game-Windows.zip", Result = "unsupported",
    Environment = "M5 macOS 26.6.2 CrossOver 26.3 Rosetta", Evidence = "Gameplay fixture: regression", TestedAt = DateTimeOffset.UtcNow.AddMinutes(1), PlatformFingerprint = MacGameplay.PlatformFingerprint, RuntimeId = MacGameplay.RuntimeIdentity("notproton", "notproton-26.3") });
Check(!MacGameplay.IsVerified(approved, "v1", "game-Windows.zip", "notproton"), "A newer failure supersedes earlier playable results.");
Check(!new MacGameplayReport { Result = "playable" }.IsComplete, "A result without version/environment/evidence is unassessed.");
Check(!MacGameplay.IsVerified(approved, "v1", "game-Windows.zip", "crossover", "changed-runtime"), "A changed runtime must require a new gameplay report.");
var previousRuntime = approved.Reports[0].RuntimeId;
approved.Reports[0].RuntimeId = "crossover|/missing-fixture-app|previous-version";
Check(!MacGameplay.IsVerified(approved, "v1", "game-Windows.zip", "crossover"), "Runtime updates must invalidate evidence even without an explicit caller identity.");
approved.Reports[0].RuntimeId = previousRuntime;
var fingerprint = approved.Reports[0].PlatformFingerprint;
approved.Reports[0].PlatformFingerprint = "another-platform";
Check(!MacGameplay.IsVerified(approved, "v1", "game-Windows.zip", "crossover"), "Another architecture or OS must not inherit verified gameplay.");
approved.Reports[0].PlatformFingerprint = fingerprint;
Check(MacCompatibility.FilterAssets(Release("game-macOS.zip", "game-Windows.zip"), approved, true, "notproton", preferWindows: true)
    .Select(a => a.name).SequenceEqual(new[] { "game-Windows.zip" }), "Explicit Windows selection must override native priority without selecting Linux.");
Check(MacCompatibility.FilterAssets(Release("game-macOS.zip"), approved, true, "notproton", preferWindows: true).Count == 0,
    "An explicit Windows preference must not silently fall back to another build.");
Check(MacGameplay.Current(new() { Release = "v1", AvailableAssets = ["other-Windows.zip"], Reports = approved.Reports }).Count == 0,
    "A removed archive must no longer be shown as currently tested.");
Check(MacCompatibility.FilterAssets(Release("game-Windows.zip"), approved, true, nativeOnly: true).Count == 0,
    "An explicit Mac selection must not fall back to Windows even when testing is allowed.");
Check(MacCompatibility.FilterAssets(Release("game-macOS-universal.zip", "game-Windows.zip"), approved, true, nativeOnly: true)
    .All(a => a.name.Contains("macOS")), "An explicit Mac selection must offer only native downloads.");
string steamConfig = "// keep comment\n\"InstallConfigStore\" { \"Software\" { \"Valve\" { \"Steam\" { \"Keep\" \"unchanged\" } } } }";
var mapped = SteamNotProton.SetToolMapping(steamConfig, 123, "notproton-26.3");
Check(SteamNotProton.FindMapping(mapped, 123) == "notproton-26.3" && mapped.Contains("// keep comment") && mapped.Contains("\"Keep\" \"unchanged\""),
    "Steam tool insertion must preserve unrelated config/comments.");
var remapped = SteamNotProton.SetToolMapping(mapped, 123, "notproton-preview-fex");
Check(SteamNotProton.FindMapping(remapped, 123) == "notproton-preview-fex" && remapped.Split("\"123\"").Length == 2,
    "Mapping updates must not create duplicate entries.");
var twoMappings = SteamNotProton.SetToolMapping(remapped, 456, "notproton-26.3");
Check(SteamNotProton.FindMapping(twoMappings, 123) == "notproton-preview-fex" && SteamNotProton.FindMapping(twoMappings, 456) == "notproton-26.3",
    "Registering another game must preserve existing tool assignments.");
bool malformedRejected = false;
try { SteamNotProton.SetToolMapping(steamConfig[..^1], 123, "notproton-26.3"); }
catch (InvalidDataException) { malformedRejected = true; }
Check(malformedRejected, "Malformed VDF must be refused before any file is changed.");
var quotedId = SteamNotProton.GameId("/games/Game with spaces.exe", "Game");
Check((quotedId & 0xffffffff) == 0x02000000 && (quotedId >> 63) == 1 && quotedId != SteamNotProton.GameId("/games/Other.exe", "Game"),
    "Steam game IDs must encode shortcut type and executable identity.");
var steamFixture = Path.Combine(Path.GetTempPath(), "github-launcher-steam-tests-" + Guid.NewGuid());
try
{
    Directory.CreateDirectory(Path.Combine(steamFixture, "config"));
    Directory.CreateDirectory(Path.Combine(steamFixture, "userdata", "1234", "config"));
    var toolDir = Path.Combine(steamFixture, "compatibilitytools.d", "notproton-26.3");
    Directory.CreateDirectory(toolDir);
    File.WriteAllText(Path.Combine(toolDir, "run"), "fixture");
    File.WriteAllText(Path.Combine(toolDir, "compatibilitytool.vdf"), "fixture");
    var configPath = Path.Combine(steamFixture, "config", "config.vdf");
    File.WriteAllText(configPath, steamConfig);
    var exe = Path.Combine(steamFixture, "Game with spaces.exe");
    File.WriteAllText(exe, "fixture; never executed");
    Check(SteamNotProton.Accounts(steamFixture).SequenceEqual(new[] { "1234" }) && SteamNotProton.Tools(steamFixture).Count == 1,
        "Setup must discover existing accounts and installed tool manifests.");
    Environment.SetEnvironmentVariable("GITHUB_LAUNCHER_DATA_DIR", Path.Combine(steamFixture, "launcher-data"));
    MacGameplay.Save("fixture/game", approved.Reports[0]);
    MacGameplay.Save("fixture/game", approved.Reports[1]);
    Check(MacGameplay.Load()["fixture/game"].Count == 2 && MacCompatibility.Find("fixture/game")!.Reports.Count == 2,
        "Independent gameplay reports must survive persistence and catalog merging without overwriting release metadata.");
    if (!SteamNotProton.IsSteamRunning())
    {
        ulong id = SteamNotProton.Register("Fixture game", exe, "notproton-26.3", "1234", steamFixture);
        var shortcutsPath = Path.Combine(steamFixture, "userdata", "1234", "config", "shortcuts.vdf");
        var shortcutRoot = SteamNotProton.ReadShortcuts(shortcutsPath);
        var shortcuts = (Dictionary<string, object>)shortcutRoot["shortcuts"];
        var entry = (Dictionary<string, object>)shortcuts.Values.Single();
        Check((string)entry["exe"] == $"\"{exe}\"" && (string)entry["LaunchOptions"] == "" && id == SteamNotProton.GameId(exe, "Fixture game"),
            "Steam must run the Windows exe directly, never recurse through the launcher.");
        SteamNotProton.Register("Fixture game", exe, "notproton-26.3", "1234", steamFixture);
        shortcuts = (Dictionary<string, object>)SteamNotProton.ReadShortcuts(shortcutsPath)["shortcuts"];
        Check(shortcuts.Count == 1 && File.Exists(configPath + ".githublauncher.bak") && File.Exists(shortcutsPath + ".githublauncher.bak"),
            "Repeated registration must be idempotent and back up both Steam files.");
        byte[] valid = File.ReadAllBytes(shortcutsPath);
        File.WriteAllBytes(shortcutsPath, [99, 0]);
        string before = File.ReadAllText(configPath);
        bool refused = false;
        try { SteamNotProton.Register("Fixture game", exe, "notproton-26.3", "1234", steamFixture); }
        catch (InvalidDataException) { refused = true; }
        Check(refused && File.ReadAllText(configPath) == before && File.ReadAllBytes(shortcutsPath).SequenceEqual(new byte[] { 99, 0 }),
            "Unknown shortcut fields/types must not corrupt either Steam file.");
        File.WriteAllBytes(shortcutsPath, valid);
    }
    else Console.WriteLine("Steam is running: registration fixtures skipped; parser and evidence tests still run.");
    if (OperatingSystem.IsMacOS() && SteamNotProton.Tools().Count > 0)
    {
        // Read-only validation against the installed user's config; never write or print account data.
        var realConfig = File.ReadAllText(Path.Combine(SteamNotProton.SteamRoot, "config", "config.vdf"));
        Check(SteamNotProton.FindMapping(SteamNotProton.SetToolMapping(realConfig, 4294967294, SteamNotProton.Tools()[0]), 4294967294) == SteamNotProton.Tools()[0],
            "Parser must accept the real macOS Steam config without modifying it.");
    }
}
finally { if (Directory.Exists(steamFixture)) Directory.Delete(steamFixture, true); }
if (!OperatingSystem.IsWindows())
{
    var failure = new System.Diagnostics.ProcessStartInfo("/bin/sh");
    failure.ArgumentList.Add("-c");
    failure.ArgumentList.Add("printf 'Library not loaded: libSDL3.0.dylib\\n' >&2; exit 7");
    bool diagnosed = false;
    try { await MacLaunchDiagnostics.StartAsync(failure); }
    catch (InvalidOperationException ex) { diagnosed = ex.Message.Contains("code 7") && ex.Message.Contains("libSDL3.0.dylib"); }
    Check(diagnosed, "An early startup failure must expose the actual loader error instead of silently returning.");
    using var success = await MacLaunchDiagnostics.StartAsync(new System.Diagnostics.ProcessStartInfo("/usr/bin/true"));
    Check(success.HasExited && success.ExitCode == 0, "Successful launch helpers that exit immediately must remain valid.");
}
Console.WriteLine($"Passed {checks} Mac compatibility checks.");

if (OperatingSystem.IsMacOS())
{
    var fixture = Path.Combine(Path.GetTempPath(), "github-launcher-install-tests-" + Guid.NewGuid());
    Directory.CreateDirectory(fixture);
    try
    {
        var payload = Path.Combine(fixture, "payload");
        Directory.CreateDirectory(Path.Combine(payload, "Wind Waker HD.app", "Contents", "MacOS"));
        File.WriteAllText(Path.Combine(payload, "Wind Waker HD.app", "Contents", "MacOS", "game"), "fixture");
        Directory.CreateDirectory(Path.Combine(payload, "tools"));
        File.WriteAllText(Path.Combine(payload, "tools", "setup.py"), "# fixture");
        File.WriteAllText(Path.Combine(payload, "portable.txt"), "fixture");
        string zip = Path.Combine(fixture, "macOS with spaces.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(payload, zip);
        string installed = Path.Combine(fixture, "installed");
        await MacGameInstaller.InstallAsync(zip, installed, Path.GetFileName(zip), "v1");
        Check(File.Exists(Path.Combine(installed, "tools", "setup.py")), "ZIP installation must preserve sibling setup tools.");
        Check(File.Exists(Path.Combine(installed, "portable.txt")), "Portable markers must survive installation.");
        Check(await File.ReadAllTextAsync(Path.Combine(installed, "version.txt")) == "v1", "Version is written after successful installation.");

        var badPayload = Path.Combine(fixture, "bad-payload");
        Directory.CreateDirectory(badPayload);
        File.WriteAllText(Path.Combine(badPayload, "README"), "No app inside.");
        var badZip = Path.Combine(fixture, "bad-macOS.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(badPayload, badZip);
        bool rejected = false;
        try { await MacGameInstaller.InstallAsync(badZip, installed, "bad-macOS.zip", "v2"); }
        catch (InvalidDataException) { rejected = true; }
        Check(rejected && await File.ReadAllTextAsync(Path.Combine(installed, "version.txt")) == "v1",
            "An invalid download must not replace a working installation/version.");

        var tar = Path.Combine(fixture, "game-macOS.tar.xz");
        var tarInfo = new System.Diagnostics.ProcessStartInfo("/usr/bin/tar") { UseShellExecute = false };
        foreach (var arg in new[] { "-cJf", tar, "-C", payload, "." }) tarInfo.ArgumentList.Add(arg);
        using (var process = System.Diagnostics.Process.Start(tarInfo)!)
        { await process.WaitForExitAsync(); Check(process.ExitCode == 0, "TAR.XZ fixture must be created."); }
        var tarInstall = Path.Combine(fixture, "tar-installed");
        await MacGameInstaller.InstallAsync(tar, tarInstall, "game-macOS.tar.xz", "v3");
        Check(File.Exists(Path.Combine(tarInstall, "tools", "setup.py")), "TAR.XZ installation must preserve all payload files.");

        var dmg = Path.Combine(fixture, "game-macOS.dmg");
        var dmgInfo = new System.Diagnostics.ProcessStartInfo("/usr/bin/hdiutil") { UseShellExecute = false, RedirectStandardOutput = true };
        foreach (var arg in new[] { "create", "-srcfolder", payload, "-volname", "Launcher Native Test", "-format", "UDZO", dmg }) dmgInfo.ArgumentList.Add(arg);
        using (var process = System.Diagnostics.Process.Start(dmgInfo)!)
        { var output = process.StandardOutput.ReadToEndAsync(); await process.WaitForExitAsync(); await output;
          Check(process.ExitCode == 0, "DMG fixture must be created."); }
        var dmgInstall = Path.Combine(fixture, "dmg-installed");
        await MacGameInstaller.InstallAsync(dmg, dmgInstall, "game-macOS.dmg", "v4");
        Check(File.Exists(Path.Combine(dmgInstall, "tools", "setup.py")), "DMG installation must preserve sibling files and detach the image.");

        if (args.Length > 0)
        {
            var actualInstall = Path.Combine(fixture, "actual-release");
            await MacGameInstaller.InstallAsync(args[0], actualInstall, Path.GetFileName(args[0]), "fixture-release");
            var actualApps = MacCompatibility.FindExecutables(actualInstall, SearchOption.AllDirectories, out var windows);
            Check(actualApps.Count > 0 && !windows, "The actual macOS release must expose a native launch target.");
            Check(actualApps.Any(p => Directory.Exists(p) && File.Exists(Path.Combine(p, "Contents", "Info.plist"))),
                "The actual app bundle metadata must survive installation.");
        }
    }
    finally { Directory.Delete(fixture, true); }
    Console.WriteLine($"Passed {checks} compatibility and native installation checks.");
}
