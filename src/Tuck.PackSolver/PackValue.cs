using System;

namespace Tuck.PackSolver;

/// <summary>Packing priorities, separate from the game's current item-value calculation.</summary>
/// <remarks>
/// <para>
/// Keep the audited, hardcoded vanilla recovery values and rule mappings for now. Broader mod
/// compatibility is deferred until substantial adoption and demand justify the added machinery.
/// New items that fit the supported rules use their current game value without an inferred recovery
/// bonus. Custom transfer or modifier callbacks can make transfers or the whole scene unsupported.
/// Mods can replace recipes, material values or callbacks while retaining an identifier; that can
/// invalidate these values and GameRuleCapture's mappings. Identifiers and callback counts do not
/// prove vanilla behavior, and refreshing the snapshot does not discover changed callback semantics.
/// </para>
/// <para>
/// Cooperating mods would need a public, reflection-free metadata/provider contract or audited
/// adapters. Recovery descriptions would supply outputs, quantities, conditions and consumed
/// resources. Tool wear can be estimated as tool cost * charges used / max charges; packing
/// value would be the greater of current value and net recovery value. Custom packing behavior
/// also needs independent admission, transfer, stacking and effective-attribute rules, with any
/// additional inputs captured in the state and replay tests checked against the game.
/// </para>
/// <para>
/// Behaviors available only as executable callbacks need an audited integration or a game-supported
/// isolated simulation environment. Callbacks expose no recipe and can inspect other game state,
/// spawn items and mutate inventories or global state. Temporary items/tools alone do not isolate
/// these effects, and one trial does not describe every state-dependent outcome. General simulation
/// would need the relevant environment and containment of all effects; our adapter provides neither.
/// Do not execute speculative interactions or introduce reflection to discover recipes. Pack
/// moves items intact; later dismantling remains the player's choice.
/// </para>
/// </remarks>
internal static class PackValue
{
    internal static long UnitValue(string identifier, long currentValue) => identifier switch
    {
        // ToolDirectory.CreateScrewdriver produces one of each; MaterialDirectory supplies their values.
        // Credit later recovery with a screwdriver, without adding dismantling to the execution plan.
        "system_module_ruined" => Math.Max(currentValue, 33L + 12L + 3L), // common_electronic + printer_plastic + junk
        _ => currentValue
    };
}
