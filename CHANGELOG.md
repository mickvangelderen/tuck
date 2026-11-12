# Changelog

All notable changes to Tuck will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

<!--
Record notable, user-facing changes under Unreleased as they are made.
We do not make use of entry categories.
All changes are just listed under the version number.

Coding agents are not allowed to release the mod.
Releasing needs user review, approval and initiation.
Agents are allowed to help update the changelog to prepare the release.

When releasing, move the entries into a new section named
## [VERSION] - YYYY-MM-DD, using the version from src/Tuck.Mod/manifest.xml.
Keep the newest release first, retain Unreleased, and add release or comparison
links at the bottom once the corresponding Git tags exist.
-->

## [Unreleased]

- Automatically open and close guides when appraising.

## [1.1.0] - 2026-11-11

- Interactions that have a tooltip and keybinding now display their associated hotkey in the tooltip.
  This feature can be toggled through the options.
- Water filter swaps now try rotations and nearby positions to fit the removed filter back into your inventory.
- Hotkeys now have dedicated text glyphs representing keyboard keys in the style of Probably Stolen.
- Travelling only requires one click. Enabled by default, can be disabled through the options.
- A new packing solver named "Container first" has been implemented and is now the default packing solver.
  Packing solvers can now be selected through the game options.

## [1.0.0] - 2026-11-09

Initial public release.

- Automatic loot packing.
- Bulk dragging, formation preservation, and filter swapping.
- Add hotkeys for many interactions.
- Improved appraisal and chem finisher UI.

[unreleased]: https://github.com/mickvangelderen/tuck/compare/1.1.0...HEAD
[1.1.0]: https://github.com/mickvangelderen/tuck/releases/tag/1.1.0
[1.0.0]: https://github.com/mickvangelderen/tuck/releases/tag/1.0.0
[@mickvangelderen]: https://github.com/mickvangelderen
