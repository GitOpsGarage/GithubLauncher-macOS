# GitHub Launcher Mac — GitOpsGarage

A maintained macOS fork of [SirDiabo/GithubLauncher](https://github.com/SirDiabo/GithubLauncher).
This fork is maintained by GitOpsGarage; it is not an upstream-endorsed release.
The original MIT license and project attribution are preserved.

## macOS support

- Self-contained Apple Silicon and Intel builds using Avalonia/.NET 9.
- Native macOS releases take priority; each game can explicitly select its Windows build.
- A release audit covers the full bundled catalog, including Twilight Princess
  (Dusklight), Wind Waker HD, and Spider-Man: Edge of Time Recompiled.
- One catalog selector offers **All games** (default), **Native Mac builds**,
  **Windows — confirmed playable on Mac**, and **Other games — try on this Mac**.
  Windows confirmation requires a current gameplay report; unverified projects remain selectable.
- Native ZIP, TAR.GZ, TAR.XZ, TGZ, and DMG installation preserves complete payloads,
  including tools/SDKs stored beside an app bundle.
- New Windows games default to direct CrossOver; each game can switch to Steam / NotProton.
  Existing saved launch methods are preserved. Native Mac builds still take priority.
  Steam receives the actual Windows executable and selected installed NotProton tool.
  Unverified Windows builds require explicit per-game testing opt-in.
- Record **Playable**, **Playable with adjustments**, or **Unsupported** separately
  for native Mac, direct CrossOver, and Steam / NotProton. Updates require fresh tests.
- Settings, library, cache, and default installs live outside the app bundle.

See [MACOS.md](MACOS.md) for builds, game setup, data locations, and limitations.
The launcher downloads and manages projects; it does not supply original game data.

[NotProton](https://github.com/NotProtonNot/NotProton) enables Steam Play in the macOS
Steam client using CrossOver. Install and configure it separately, then use
**Configure Mac launch… → Steam / NotProton → Prepare in Steam** with Steam closed.
The launcher backs up Steam configuration before registering the shortcut and tool.
Gameplay reports stay local; this fork does not claim Valve certification or supply
pre-certified Windows games. Submit reproducible reports through the fork's
[compatibility report form](https://github.com/GitOpsGarage/GithubLauncher-macOS/issues/new?template=mac-compatibility.yml).

## Community catalog

The catalog combines the configured GitHub app list, bundled projects, and the public
[Quiver community catalog](https://quiverlauncher.com/). Quiver is an independent
project; this fork does not claim its endorsement. Settings → Advanced can disable
that source. Community discovery refreshes at most daily, or with Refresh Catalog,
with a local cache and a bundled offline snapshot. GitHub tokens are never sent to
Quiver. Existing library entries and gameplay reports are not replaced.

Browse a single alphabetical grid with game covers, separate game/project names,
platform badges, search, a platform dropdown with checkboxes and A–Z / Z–A sorting. macOS, Windows,
Linux and unlisted platforms are selected initially; mobile-only projects are
hidden unless Android or iOS is selected. Any checked platform can match. Scrolling
near the end appends the next 36 cards automatically, without resetting the scroll
position or fetching releases again. Platforms come from Quiver metadata or recorded release assets;
missing information is shown as "Platforms not listed". The Mac compatibility
filter and card status still use release evidence and local gameplay reports,
not the catalog's platform claims. Use **Setup guide** for original game data
requirements and **+ Add** to add a project to the library.

Only GitHub-backed projects supported by this installer are imported. GitLab/manual
projects and entries requiring extra setup files or asset-specific filters are
excluded and counted in the catalog status. Entries sharing a repository are merged.
Catalog listings and Quiver verification do not establish Mac gameplay compatibility:
release discovery and local tests remain separate. Downloads come from the project's
GitHub releases through this launcher's existing installation checks.

To refresh the bundled offline snapshot from the public API, run:

```sh
dotnet run --project tests/MacCompatibility.Tests.csproj -- --refresh-community-catalog /tmp/github-launcher-community
cp /tmp/github-launcher-community/quiver_catalog_cache.json quiver-catalog.json
```

The refresh command fails rather than exporting an incomplete response.

## Development and commits

```sh
git clone --recurse-submodules https://github.com/GitOpsGarage/GithubLauncher-macOS.git
cd GithubLauncher-macOS
bundle install
bundle exec overcommit --install
bundle exec overcommit --sign
bundle exec overcommit --sign pre-commit
bash scripts/check.sh
bash scripts/build-macos.sh osx-arm64
```

Every commit must pass Overcommit. Do not bypass hooks. CI also runs the same checks
before producing Mac artifacts. Run `python3 scripts/audit-mac-catalog.py` with an
authenticated GitHub CLI to refresh bundled release evidence. GitHub API discovery
in the app preserves cached results when offline or rate limited.

## Original project documentation

[![.NET 9](https://img.shields.io/badge/.NET-9-512BD4)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/github/license/SirDiabo/GithubLauncher)](https://github.com/SirDiabo/GithubLauncher/blob/main/LICENSE)

>## Important
>### Migration from N64RecompLauncher to GithubLauncher
>
>This software was previously knows as the "N64Recomp Launcher", but this had to be changed due to continued requests by the N64Recomp Tool Developers. If you are interested in a version of the launcher that is primarily for N64 Recompiled Games, then check out [this version](https://github.com/SirDiabo/GithubLauncher/releases/tag/v1.70).
>You can then go into the `update_check.json` and change the year in `LastCheckTime` to something like `9999` to disable self-updates.
>
>If you have an old version, and wish to update to the newer ones, you will have to update manually by dragging the new files into your launcher folder. You can then delete all files with "N64Recomp" in their names.
>Your settings will remain compatible.

![Github Launcher Screenshot](Assets/LauncherScreenshot.png)
A modern, user-friendly launcher application for managing and running GitHub-hosted applications. This tool streamlines the process of downloading, installing, and launching your favorite projects.

## Features

- **Automated Updates**: Seamlessly download and install the latest releases from GitHub
- **Version Management**: Stay up-to-date with automatic version checking and updates
- **App Management**: Easy-to-use interface for launching GitHub-hosted apps
- **Smart Integration**: Direct integration with GitHub releases for smooth updates

## Getting Started

### Prerequisites

- .NET 9 Runtime (get it [here](https://dotnet.microsoft.com/en-us/))
- Internet connection for updates and downloads

### Installation

1. Download the latest release from the [Releases](https://github.com/GitOpsGarage/GithubLauncher-macOS/releases) page
2. Extract the downloaded archive to your preferred location
3. Run the executable.

## Usage

1. Launch the application
2. The launcher will automatically check for updates on startup
3. Browse your app library through the interface
4. Select an app and click "Download/Launch" to use it

## Configuration

### GitHub API Token
To avoid hitting GitHub's API rate limits, you can provide a personal access token.
Create a token with no special permissions needed and set it in the launcher settings.
You can create a token at ```GitHub Settings -> Developer settings > Personal access tokens > Tokens (classic) > Generate new token```
You don't need to give it any special permissions. Then paste that Token into your Settings field. Do not share your Token!

### apps.json Structure

The launcher uses an `apps.json` file to manage the available apps. Every entry is user-managed and editable; there are no built-in stable, experimental, or custom categories.

#### App Entry Properties

Each app entry requires the following properties:

- **`name`** - The display name of the app as it appears in the launcher
- **`repository`** - The GitHub repository in the format `username/repository`
- **`folderName`** - The folder name where the app will be downloaded and installed
- **`appIconUrl`** - URL of the app's icon image. If null, a default icon will be used.

#### Example Configuration

```json
{
    "apps": [
        {
            "name": "Example App",
            "repository": "username/example-app-repo",
            "folderName": "ExampleApp",
            "appIconUrl": null
        },
        {
            "name": "Another App",
            "repository": "anotheruser/another-app-repo",
            "folderName": "AnotherApp",
            "appIconUrl": "link/to/an/image.png"
        }
    ]
}
```

## Support

If you encounter any issues or have questions:
- [Open an issue](https://github.com/GitOpsGarage/GithubLauncher-macOS/issues)
- Check existing issues for solutions
