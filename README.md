# Enhanced Context Menu

[日本語](README.ja.md)

![Overlay panel beside the game context menu](docs/screenshots/hero-1280x720.png)

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

- Extract context menu entries added by plugins into an overlay panel
- Gamepad controls

## Commands

| Command | Description |
| --- | --- |
| `/enhancedcontextmenu` | Toggle the plugin settings window |

## For developers

1. Build: `dotnet build EnhancedContextMenu.sln -c Release -p:Platform=x64`
2. Point Dalamud’s **dev plugin** path at `EnhancedContextMenu/bin/Release/`
3. Enable **Enhanced Context Menu** in the plugin installer (dev)

[MirageUI](https://github.com/exatrines/MirageUI) is included as a git submodule for the shared UI kit.

## License

[AGPL-3.0-or-later](LICENSE)
