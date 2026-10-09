<p align="center">
  <img src="EnhancedContextMenu/Data/plugin-icon.png" alt="Enhanced Context Menu" width="128">
</p>

<h1 align="center">Enhanced Context Menu</h1>

<p align="center">
  English | <a href="docs/README.ja.md">日本語</a>
</p>

<p align="center">
  <a href="https://github.com/exatrines/EnhancedContextMenu/releases/latest"><img src="https://img.shields.io/github/v/release/exatrines/EnhancedContextMenu?label=Release&amp;labelColor=F280B6&amp;color=FFFFFF&amp;style=flat&amp;sort=date&amp;display_name=tag" alt="Release"></a>
  <a href="CHANGELOG.md"><img src="https://img.shields.io/badge/Changelog-view-FFFFFF?labelColor=F280B6&amp;style=flat" alt="Changelog"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-AGPL--3.0--or--later-FFFFFF?labelColor=F280B6&amp;style=flat" alt="License: AGPL-3.0-or-later"></a>
</p>

<p align="center">
  <img src="docs/screenshots/hero-1280x720.png" alt="Overlay panel beside the game context menu">
</p>

Enhanced Context Menu moves entries that other plugins add to the context menu into an overlay panel. Opening the context menu shows the overlay panel automatically.

Entries added by editing the native UI directly stay on the native context menu, so care is needed.

## Install

1. Run `/xlsettings` and open the **Experimental** tab
2. Add this URL under **Custom Plugin Repositories**:

```
https://raw.githubusercontent.com/exatrines/DalamudPlugins/refs/heads/main/pluginmaster.json
```

3. Run `/xlplugins` and install **Enhanced Context Menu**

## Features

- **Overlay panel** — Moves context menu entries added by other plugins into a panel beside the game menu
- **Gamepad** — Operate the panel with a gamepad

## Commands

| Command | Description |
| --- | --- |
| `/enhancedcontextmenu` | Toggle the plugin settings window |

## For developers

1. `git submodule update --init --recursive`
2. `dotnet build EnhancedContextMenu.sln -c Release -p:Platform=x64`
3. Point Dalamud’s **dev plugin** path at `EnhancedContextMenu/bin/Release/`
4. Enable **Enhanced Context Menu** in the plugin installer (dev)

[MirageUI](https://github.com/exatrines/MirageUI) is included as a git submodule for the shared UI kit.

## Contributing

Contributions are always welcome! Please see the [contribution guide](CONTRIBUTING.md).
