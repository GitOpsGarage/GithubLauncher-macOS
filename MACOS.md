# Local macOS build

This launcher manages GitHub releases. It does not recompile console ROMs itself.
The catalog includes static recompilations, decompilation ports, remakes, and emulators.

## Build and run

Install the .NET 9 SDK, initialize the Core submodule, then run:

```sh
git submodule update --init --recursive
bash scripts/build-macos.sh osx-arm64
open publish/GithubLauncher-Mac.app
```

Use `osx-x64` for an Intel Mac. The published app includes the .NET runtime.
Set `DOTNET_BIN` to an SDK executable path when it is not on PATH.

## Find games for this Mac

The bundled audit covers 129 project repositories. It currently records 57 published
Mac builds, 40 Windows candidates, and 32 unknown/no stable release results. This includes
emulators as well as game ports. These numbers are release metadata, not gameplay tests.
The catalog includes Dusklight (Twilight Princess), Wind Waker HD, and Spider-Man: Edge
of Time Recompiled. Supplementary fork entries are merged with the upstream catalog.

Use the **Browse games** selector to choose **All games** (the default),
**Native Mac builds**, **Windows — confirmed playable on Mac**, or
**Other games — try on this Mac**. The count shows visible and total catalog entries.
The old checkbox filters are replaced by this single selection; existing installations
start with All games unless a selection has been saved in the new selector.
All games includes every project from the configured and bundled catalogs, including
emulators. It does not claim to include every game on GitHub.

Native Mac builds have a published macOS archive; this is not a gameplay rating.
Windows confirmation requires a current playable report for the Windows release,
archive, architecture/macOS and runtime through CrossOver or Steam / NotProton.
Native releases appear in the native group even if a Windows report also exists.
Other games includes unverified Windows candidates and unassessed projects. These
remain selectable so users can configure and try them. An empty confirmed group
means no matching reports exist; it does not mean no Windows games work.
Hover over each card's support label for release evidence and gameplay reports.
Changing the selector filters the loaded catalog locally. Refresh updates stale release
evidence using GitHub's API, caching it for 24 hours and stopping when rate limited.
Offline startup uses the full bundled catalog.

Downloads prefer native macOS archives and ARM builds when available. This installer
supports ZIP, TAR.GZ, TAR.XZ, TGZ, and DMG. PKG and other system installers
require manual installation; locate the installed app afterward. Disk images are mounted
read-only and detached after copying. License prompts are never auto-accepted.
Intel-only Mac binaries may require Rosetta. Each game's original data/ROM revision,
graphics requirements, and first-run import procedure remain project-specific.
Read the project's setup guide before downloading. No ROMs are supplied.

Some native releases depend on shared libraries installed by Homebrew rather than
bundling them. For example, Street Fighter Alpha 3 Recompiled v0.2.16 (macOS ARM64)
links SDL3, FreeType, and HarfBuzz from `/opt/homebrew/opt`. Install those dependencies
with `brew install sdl3 freetype harfbuzz`. Homebrew stores versioned packages under
`/opt/homebrew/Cellar` and exposes stable paths under `/opt/homebrew/opt`; these are
shared Mac libraries, separate from the launcher and CrossOver bottles. The launcher
reports diagnostic output when a Mac process fails during its first two seconds,
including missing-library loader errors. It does not silently install game dependencies.

## Windows builds through CrossOver

Steam / NotProton is the default Windows launch method for new games. Existing saved
runner choices are preserved. Each game can switch to direct CrossOver in
**Configure Mac launch…**. For direct CrossOver, configure the app path and an existing
bottle name in Settings.
Right-click a library game and choose "Configure Mac launch…" to save a per-game bottle
and opt into testing unverified Windows builds. Adding a candidate opens this dialog.
Opt-in does not mark a game as tested. Create and
configure bottles in CrossOver first; choose graphics settings and install dependencies
according to the game's own instructions. A per-game `bottle` overrides the default.
The launcher executes CrossOver's `wine --bottle ... --workdir ... --cx-app ...` using
individual process arguments, without a shell.

### NotProton and Steam

[NotProton](https://github.com/NotProtonNot/NotProton) enables Steam Play in the macOS
Steam client using CrossOver and macOS ports of some Proton components. Its current
README lists CrossOver 26.3 and selected Preview versions as supported; consult that
project for the matching Steam client versions.

1. Install NotProton and use its application to prepare a supported CrossOver runner
   and Steam client. Start Steam once so its compatibility tools are registered.
2. Add a game, open **Configure Mac launch…**, and choose **Steam / NotProton**.
   Select an existing Steam account and installed compatibility tool. Enable testing
   only when the Windows build lacks a matching playable report.
3. Select **Windows — Steam / NotProton or CrossOver** in the game version selector
   when you want the Windows edition, then save settings
   and install that build.
4. Close Steam, select the installed Windows `.exe`, and click **Prepare in Steam**.
   The launcher registers that executable directly in the selected account's
   `shortcuts.vdf` and assigns the tool in Steam's `config/config.vdf` using the
   shortcut app ID. It preserves unrelated entries and makes `.githublauncher.bak`
   backups; malformed or unsupported VDF data is refused before writing.
5. Reopen Steam with the selected account. The launcher's **Launch** button uses
   `steam://rungameid/...`. Steam owns the NotProton launch and prefix; direct CrossOver
   bottles are not reused. Prepare again if the executable or tool assignment changes.

The launcher detects installed tools, but does not install NotProton, patch Steam,
accept CrossOver licenses, or change global Steam compatibility defaults. It must be
run outside Steam when preparing shortcuts so Steam can close completely.

## Gameplay compatibility reports

Use **Configure Mac launch… → Record gameplay test…** after playing a game. Results are
**Playable**, **Playable with adjustments**, **Unsupported**, or **Untested** when no
matching complete report exists. Opening a window alone is not a gameplay test.
Record the exact release and archive, Mac model, runtime version, Rosetta/FEX selection,
graphics backend, tested duration/section, performance, controls, audio, and adjustments.
The launcher captures architecture/macOS and a runtime identity automatically.
CrossOver identity includes its bundle metadata hash; NotProton identity includes the
selected tool and deployed NotProton version. Runtime updates invalidate matching reports.

Reports are stored by repository in
`~/Library/Application Support/GithubLauncherMac/mac-gameplay.json`. Reports for different
launch methods remain separate. The newest complete result for the same release, asset,
platform, and runtime determines status; a later failure supersedes earlier success.
Older reports remain visible in details, but do not certify new releases. Native build
availability is tracked separately from gameplay results. Legacy `verified: true` alone
is insufficient without a complete gameplay report. No Windows games are pre-certified.
Reports are local observations, not Valve certification; another Mac model or graphics
configuration can behave differently even on the same architecture/macOS.

To contribute public evidence, use the fork's
[compatibility report form](https://github.com/GitOpsGarage/GithubLauncher-macOS/issues/new?template=mac-compatibility.yml).
The launcher does not publish local reports automatically.

User entries override the bundled entries by repository. A different Windows release or asset requires
new verification. Windows games whose renderer requires unsupported Vulkan features
may fail even when other Windows games work in the same bottle. ProtonDB ratings do not
establish CrossOver compatibility. The compiled console CPU code is only part of a port;
its renderer, audio, input, and runtime also need to work on the target platform.

## Validation and limitations

```sh
dotnet run --project tests/MacCompatibility.Tests.csproj
```

Launcher self-updates default to disabled on Mac to preserve this adaptation. Game updates
remain available. Native Mac release changes are filtered by platform filename; they are
not automatically gameplay-tested. Upstream compiler warnings remain.

Build artifacts are development bundles, without Developer ID signing or notarization.
Maintainer signing/notarization credentials have not been configured.
macOS settings, app lists, cache, and default installs now live under
`~/Library/Application Support/GithubLauncherMac/`. Existing portable configuration is
copied once without overwriting user files. `GITHUB_LAUNCHER_DATA_DIR` overrides the data
folder for isolated testing. Custom game install paths remain supported.

The build script bundles a matching Homebrew SDL2 library when available (override with
`SDL2_LIBRARY`). Without SDL2, the app keeps working with keyboard/mouse; controller
input is unavailable. Selecting an executable is saved by the existing launcher UI.
Gameplay validation with the user’s original game data remains necessary; automated
checks exercise launcher behavior and fixture files, not actual games.

## Choosing a Windows launch method

New games use direct CrossOver by default. Create a suitable bottle in CrossOver,
then select it in Configure Mac launch. The launcher keeps downloaded game payloads
in its game folder and invokes the selected bottle's runtime; it does not copy every
game into the bottle's drive C. Dependencies may still need installation in that
bottle. Existing per-game runner settings are preserved.

Steam / NotProton remains available per game when Steam library integration is
wanted. NotProton also uses CrossOver: it adds Steam Play integration and requires
matching supported Steam and CrossOver versions, a configured NotProton tool, and a
prepared shortcut. It is not a separate compatibility engine. Consult the
[official NotProton requirements](https://github.com/NotProtonNot/NotProton).
Direct launch is the simpler default for standalone ports; neither path is assumed
to improve performance or compatibility without a gameplay test of that game/build.
