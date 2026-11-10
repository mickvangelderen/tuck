---
name: probably-stolen-modding
description: Knowledge useful when developing mods for Probably Stolen. Use when writing or reviewing mods.
---

# Probably Stolen modding

## When mods load

The game loads mods once, at launch: for each enabled mod, `ModLoader` calls `Init` and then immediately `OnEnable`.
Enabling a mod in the main-menu Mods screen only takes effect after a restart.

## Asset reads during startup

During `Init` and `OnEnable`, the game has not yet associated your mod ID with the directory containing `manifest.xml` and the mod's DLLs.
Consequently, `ModHelper.ReadModFileText` and `ReadModFileBytes` fail with `Unknown mod`, even when the files exist.
Read packaged files after `ModHook.OnModAssetsLoaded` fires for your mod.

## Localization

Name keys after the English phrases in `snake_case` so call sites convey their meaning. Shorten long text to distinctive phrases, and keep keys stable across minor wording edits.

## Exceptions in input callbacks

Exceptions thrown during input handling can leave a button selected or pressed. Let them propagate rather than catching them or adding cleanup to fix the button state: the mod has no meaningful way to recover from arbitrary errors.

## Optional features and mod compatibility

Keep Harmony patches installed regardless of feature settings. When a feature is disabled, its patches should return immediately without changing game behavior or state.

When disabling a feature, remove its owned controls and handlers and undo its changes where practical. Avoid restoring entire objects or layouts from old snapshots.
Compatibility is best effort: preserve unrelated changes when reasonably identifiable, but do not add elaborate tracking to infer ownership of arbitrary shared-state changes.
Harmless transient presentation differences may resolve through the normal UI lifecycle.
Keep cleanup complexity proportional to the concrete behavioral problems it prevents.
