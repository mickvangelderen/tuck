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

- Tuck now properly Packs machines into machine bays.
- The drag modifier keys are now work independently.
- Clarified that the "keep formation" key binding just allows you to assign a custom keybinding to that behavior.

## [1.3.0] - 2026-11-15

- Configurable machine warning icons:
  - stolen (only shown during inspections, can be configured)
  - battery low
  - missing ingredients (it's supposed to resemble a gear)
  - insufficient water
  - depleted filter
  - machine output overflow
- Labelers now apply their last applied setting to items on right click. 
- The yeast bioreactor can be used as a tool to start fermentation.
- New hotkeys:
  - E to go to sleep,
  - T to open/close the tablet.
  - A to accept/confirm (generic positive actions).
  - D to decline/reject (generic negative actions).
- Hotkeys for actions that can not be undone generally require holding down the action.
- Mouse/keyboard holds time out after 1.5s.
- The in game options now allow binding the same hotkey to multiple actions. 
- Changelog is now shown when the mod is updated
- Fixed an issue where Tuck wasn't considering items on Dr. Jackson's counter.

## [1.2.0] - 2026-11-12

- Dragging all matching items now binds to Alt+drag instead of Shift+drag, so Shift+drag preserves vanilla formation placement.
- The relevant appraisal guides and color swatches now open automatically when appraising an item.
- All features are now opt-out instead of opt-in.

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

[Unreleased]: https://github.com/mickvangelderen/tuck/compare/1.3.0...HEAD
[1.3.0]: https://github.com/mickvangelderen/tuck/releases/tag/1.2.0
[1.2.0]: https://github.com/mickvangelderen/tuck/releases/tag/1.2.0
[1.1.0]: https://github.com/mickvangelderen/tuck/releases/tag/1.1.0
[1.0.0]: https://github.com/mickvangelderen/tuck/releases/tag/1.0.0
[@mickvangelderen]: https://github.com/mickvangelderen
