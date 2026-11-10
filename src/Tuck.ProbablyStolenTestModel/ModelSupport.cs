namespace Tuck.ProbablyStolenTestModel;

/// <summary>
/// Model boundary: fixed captured geometry, tags, ownership, quantity/admission callbacks, transfers,
/// and ported value calculations with fixture-scoped market inputs.
/// No sprite factories, live player/client effects, dynamic parent shape/state modifiers,
/// rendering, combat, machinery simulation or automatic discovery of item rules from identifiers.
/// </summary>
internal static class ModelSupport
{
    internal static void RequireFixedShapes(params GraphNodeStorage[] roots)
    {
        // Descend only through items explicitly participating in the operation.
        // An inventory root is a source/destination, not a request to inspect its other contents.
        var contents = new Queue<GraphNodeStorage>(roots.OfType<GameItem>());
        var seenContents = new HashSet<GraphNodeStorage>();
        while (contents.TryDequeue(out var node))
        {
            if (!seenContents.Add(node)) continue;
            CheckNode(node);
            foreach (var child in node.children) contents.Enqueue(child);
        }
        RequireSupportedAncestors(roots);
    }

    internal static void RequireSupportedAncestors(params GraphNodeStorage[] roots)
    {
        var ancestors = new Queue<GraphNodeStorage>(roots);
        var visited = new HashSet<GraphNodeStorage>();
        while (ancestors.TryDequeue(out var node))
        {
            if (node == null || !visited.Add(node)) continue;
            CheckNode(node);
            foreach (var parent in node.parents) ancestors.Enqueue(parent);
        }
    }

    private static void CheckNode(GraphNodeStorage node)
    {
        if (node is ModelItem fixture)
        {
            if (fixture.UnsupportedBehavior != null)
                throw new NotSupportedException(fixture.UnsupportedBehavior);
            fixture.ValidateQuantity();
        }
        if (node is GameItem { parentItemShapeModifierForItemFunc: not null }
            || node is GameInventory { parentItemShapeModifierForItemFunc: not null })
            throw new NotSupportedException("Dynamic parent shape modifiers are not modeled.");
    }

    internal static void RequireSplitFactory(GameItem source)
    {
        RequireFixedShapes(source);
        if (source is not ModelItem { SplitFactory: not null })
            throw new NotSupportedException("Splitting requires a fixture SplitFactory.");
    }

    internal static GameItem CreateSplitItem(GameItem source)
    {
        RequireSplitFactory(source);
        var result = ((ModelItem)source).SplitFactory!();
        if (result == null || ReferenceEquals(result, source) || result.parentInventory != null
            || result.identifier != source.identifier)
            throw new InvalidOperationException("SplitFactory must return a fresh, detached item with the same identifier.");
        RequireFixedShapes(result);
        return result;
    }
}
