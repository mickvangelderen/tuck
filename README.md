# Tuck

Tuck is a [Probably Stolen](https://store.steampowered.com/app/4348910/Probably_Stolen_Cyberpunk_Shopkeeper_Sim/) mod which helps you pack up and leave.
The author wants to keep playing Probably Stolen but avoid Repetitive Strain Injury (RSI) from night activities, like scavenging.

![Tuck](./media/tuck.png)

## Features

- Tuck the rat helps you pack the most value into your bags before leaving.
- Adds configurable keybindings for
    - Scavenge (Space)
    - Pack (G)
    - Return from night activities (R)
    - Open/close the shutters (C)
    - Open night activities (Q)
    - Drag all matching items (Shift)
    - Place dragged items in formation (Alt)
- Adds configurable modifiers for dragging:
    - holding Shift while dragging one item selects all items of that kind
    - holding Shift while dragging multiple items selects all items of those kinds
    - holding Shift while dragging from empty inventory space selects all draggable items in that inventory
    - releasing Shift restores the original drag or resumes box selection
    - the original behavior (preserve placement) is now toggled by Alt instead
- Adds an option to specify a minimum amount of credits before the "Items left behind will be lost" dialog shows up when returning from scavenging.
  The mod default is 50 credits.
  Set it to 0 credits to retain the vanilla behavior.
  Set it to 1000 credits to disable the warning entirely.
- Adds an option to specify how much of the workspace can be occupied before the "keep your counter clear" warning shows up when going outside.
  The mod default is 40%.
  Set it to 0% to retain the vanilla behavior.
  Set it to 100% to disable the warning entirely.
- Adds separate hold times for returning from night time activities and ringing the bell.
  Both default to 0.4 seconds and can be set from 0.0 to 1.0 seconds.
  Set the time to 0.0 for no hold. The bell still respects the vanilla instant next customer setting.
- Adds an option to center the backpack selection menu, off by default.
- Adds an option to automatically move newly received appraisal slips to the trash, off by default.
- Adds an option to automatically move newly received event receipts to the trash, off by default.
- Adds an option to swap filters by dragging a new filter onto the installed filter in an open filter slot, on by default.
- Adds optional 1, 5 and 20 batch buttons to the chem finisher, off by default.
  Avoids a bug in vanilla where printing consumes ingredients even if it fails to place the output.

## Demos

### Appraisal

![Appraisal](./media/appraisal.png)

### Chem finisher

![Chem finisher](./media/chem-finisher.gif)

### Item dragging improvements

![Item dragging improvements](./media/shift-drag.gif)

## Keybindings

Probably Stolen doesn't allow conflicting keybindings, even when they are used in different contexts.
To work around this, go to the mod's settings from the main menu, and type in the keybinding as a string.

## Limitations

Probably Stolen's data model is very flexible.
The game and mod authors can run run *any* code when interacting with items.

For example, the screwdriver opens a ruined module and salvages some items.
This interaction is not an explicitly modeled behavior that is re-used.
This makes it difficult for Tuck to predict accurately what will happen for items that mods might add.

In our implementation, the ruined module is valued after the value of it's would-be salvaged components.
However, we can not do this generically for any item and interaction that may be added.
We can maintain these overrides for the vanilla game and popular mods if needed.

Without the right overrides, Tuck may not pack "optimally", but he will still help and save you work!

## Alternatives

Others had similar ideas, here are some:

- [Quick Pickup](https://steamcommunity.com/sharedfiles/filedetails/?id=3832375203)
- [Night Haul](https://steamcommunity.com/sharedfiles/filedetails/?id=3828895201)

## Contributions

Feel free to contribute ideas and improvements.
The source code is available at.

## Principles

We want this mod to be highly compatible with other mods. 
To achieve this we try to implement optional features in a way that allows the user to completely disable their effects and avoid incompatibilities.

We also want users to be able to see the source code to scan it for security checks.
Feel free to decompile the mod and look around.

## Licensing

Tuck is licensed under the [MIT License](LICENSE).
You may use, modify, and share it—including commercially—provided you retain the copyright and license notice.
Contributions and improvements are welcome.
