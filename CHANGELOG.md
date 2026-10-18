# Changelog

Changes in the GitOpsGarage-maintained macOS fork of SirDiabo/GithubLauncher.
Upstream attribution and licenses are preserved; this fork is not endorsed by upstream.

## [0.1.0-macos-preview.2] - 2026-10-17

### Added

- Quiver community catalog integration with 381 supported GitHub projects in the
  bundled offline snapshot. Catalog updates are cached, and GitHub credentials are
  never sent to Quiver.
- Catalog search across game names, projects and repositories, alphabetical sorting,
  and a platform dropdown with independent checkboxes.
- Platform badges sourced from catalog metadata and recorded release assets.
  Platform listings remain separate from native release evidence and Mac gameplay reports.
- Automatic loading of the next 36 cards when scrolling near the end of the catalog.

### Changed

- Reorganized the catalog into a single grid with game covers, separate game and
  project names, setup guides, and library status.
- Desktop platforms and projects with unlisted platforms are selected initially;
  Android-only and iOS-only projects are hidden by default.
- New games default to direct CrossOver for Windows builds. Existing per-game
  selections are preserved; Steam / NotProton remains available.
- Replaced the launcher artwork with a flat Octocat holding a controller on a white
  background, with a thin blue outline and orange buttons.

### Fixed

- Load additional cards without rebuilding the existing grid or resetting its scroll position.
- Use an artwork-specific macOS icon resource name to distinguish updated icons.
- Include the release version in the app, assembly metadata and macOS bundle.

### Release scope

- Apple Silicon download, requiring macOS Tahoe 26 or newer because of the bundled SDL2 library.
- Includes the .NET runtime and an ad-hoc signature; no Apple Developer ID signing or notarization.
- Published builds and catalog listings do not establish gameplay compatibility.
  Original game data, game-specific dependencies and Windows compatibility tools are not included.

## [0.1.0-macos-preview.1] - 2026-10-14

- First Apple Silicon preview of the macOS fork, including native app packaging and icon.
- Native macOS release discovery, complete release payload installation and per-game
  Mac/Windows download selection.
- Separate native release, Windows candidate and locally tested gameplay evidence.
- Direct CrossOver and Steam / NotProton launch configuration, original game data
  guidance and startup diagnostics.

[0.1.0-macos-preview.2]: https://github.com/GitOpsGarage/GithubLauncher-macOS/compare/v0.1.0-macos-preview.1...v0.1.0-macos-preview.2
[0.1.0-macos-preview.1]: https://github.com/GitOpsGarage/GithubLauncher-macOS/releases/tag/v0.1.0-macos-preview.1
