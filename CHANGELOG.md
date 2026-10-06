# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.3.1] - 2026-10-06

### Fixed

- Plugin entries on context menus other than inventory are moved to the overlay. This includes chat item links and Wondrous Tails. The target no longer has to be a player

## [0.3.0] - 2026-10-06

### Added

- Overlay colors for the background, background (active), text, text (active), header text, and border. Each color can be reset
- Option to override text colors set by other plugins. Those colors are used unless the option is on
- Gamepad option to move through nested menus with left and right. Confirm still opens a submenu
- The back row can be selected with the gamepad. A nested menu starts on that row

### Changed

- X and Y offsets share one row and one reset

## [0.2.0] - 2026-10-06

### Added

- Choose the overlay side: right, left, up, or down. If it does not fit, the panel flips to the opposite side
- X and Y offsets for the overlay
- Font size and font scale for the overlay

### Changed

- Overlay text size comes from the font settings. It no longer follows the context menu font

## [0.1.1] - 2026-10-05

### Fixed

- Align the panel with the drawn context menu
- Scale panel text to the context menu's font size

## [0.1.0] - 2026-10-05

### Added

- Overlay panel for context menu entries added by other plugins. It opens automatically when the context menu opens
- Hide an entry from both the panel and the game menu
- Gamepad controls
- Settings for enabling the plugin, UI language, and the panel header
- `/enhancedcontextmenu` toggles the settings window

[Unreleased]: https://github.com/exatrines/EnhancedContextMenu/compare/v0.3.1...HEAD
[0.3.1]: https://github.com/exatrines/EnhancedContextMenu/compare/v0.3.0...v0.3.1
[0.3.0]: https://github.com/exatrines/EnhancedContextMenu/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/exatrines/EnhancedContextMenu/compare/v0.1.1...v0.2.0
[0.1.1]: https://github.com/exatrines/EnhancedContextMenu/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/exatrines/EnhancedContextMenu/releases/tag/v0.1.0
