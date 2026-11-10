// Ported from dependencies/probably-stolen/decompiled/GraphUtils.cs.
// Assembly-CSharp MVID: 911508a6-e0eb-4cc0-91e8-e14b16ea28d7.
// Selected graph traversal and transfer methods, unchanged; store/UI routing is outside this model.
#nullable disable
public class GraphUtils
{
    public static bool CanAccept(GraphNodeStorage currentNode, GameItem item)
    {
        if (currentNode.TryFindOneValidInventorySlot(item) == null)
        {
            return false;
        }
        return true;
    }

    public static int TryAccept(GraphNodeStorage currentNode, GameItem item)
    {
        if (item == null)
        {
            return 0;
        }
        return currentNode.TryFindOneValidInventorySlot(item)?.TryAcceptOnce(1) ?? (-1);
    }

    public static int TryAcceptOnce(GraphNodeStorage currentNode, GameItem item, int amount = -1)
    {
        if (item == null)
        {
            return 0;
        }
        if (amount < 0)
        {
            amount = item.unitCount;
        }
        if (amount <= 0)
        {
            return 0;
        }
        int num = 0;
        if (num >= amount)
        {
            return amount - num;
        }
        SlotMarker slotMarker = currentNode.TryFindOneValidInventorySlot(item);
        if (slotMarker == null)
        {
            return amount - num;
        }
        if (slotMarker.numTransfer <= 0)
        {
            return amount - num;
        }
        int num2 = slotMarker.TryAcceptOnce(amount - num);
        if (num2 == 0)
        {
            return amount - num;
        }
        num += num2;
        return amount - num;
    }

    public static int TryAcceptAll(GraphNodeStorage currentNode, GameItem item, int amount = -1)
    {
        if (item == null)
        {
            return 0;
        }
        if (currentNode == null)
        {
            return 0;
        }
        if (amount < 0)
        {
            amount = item.unitCount;
        }
        if (amount <= 0)
        {
            return 0;
        }
        int num = 0;
        for (int i = 0; i < 40; i++)
        {
            if (num >= amount)
            {
                break;
            }
            SlotMarker slotMarker = currentNode.TryFindOneValidInventorySlot(item);
            if (slotMarker == null || slotMarker.numTransfer <= 0)
            {
                break;
            }
            int num2 = slotMarker.TryAcceptOnce(amount - num);
            if (num2 == 0)
            {
                break;
            }
            num += num2;
        }
        return amount - num;
    }

    public static bool FindParentEquals(GraphNodeStorage currentNode, GraphNodeStorage node)
    {
        if (currentNode == null || node == null)
        {
            return false;
        }
        Queue<GraphNodeStorage> queue = new Queue<GraphNodeStorage>();
        queue.Enqueue(currentNode);
        while (queue.Count > 0)
        {
            GraphNodeStorage graphNodeStorage = queue.Dequeue();
            if (graphNodeStorage != currentNode && graphNodeStorage == node)
            {
                return true;
            }
            foreach (GraphNodeStorage parent in graphNodeStorage.parents)
            {
                queue.Enqueue(parent);
            }
        }
        return false;
    }

    public static bool FindChildEquals(GraphNodeStorage currentNode, GraphNodeStorage node)
    {
        if (currentNode == null || node == null)
        {
            return false;
        }
        Queue<GraphNodeStorage> queue = new Queue<GraphNodeStorage>();
        queue.Enqueue(currentNode);
        while (queue.Count > 0)
        {
            GraphNodeStorage graphNodeStorage = queue.Dequeue();
            if (graphNodeStorage != currentNode && graphNodeStorage == node)
            {
                return true;
            }
            foreach (GraphNodeStorage child in graphNodeStorage.children)
            {
                queue.Enqueue(child);
            }
        }
        return false;
    }

    public static T FindParentType<T>(GraphNodeStorage currentNode, Func<T, bool> filter = null, Func<T, bool> passthrough = null)
    {
        if (currentNode == null)
        {
            return default;
        }
        if (filter == null)
        {
            filter = (T c) => true;
        }
        if (passthrough == null)
        {
            passthrough = (T c) => false;
        }
        new HashSet<T>();
        Queue<GraphNodeStorage> queue = new Queue<GraphNodeStorage>();
        queue.Enqueue(currentNode);
        while (queue.Count > 0)
        {
            GraphNodeStorage graphNodeStorage = queue.Dequeue();
            if (graphNodeStorage != currentNode && graphNodeStorage is T val && filter(val))
            {
                return val;
            }
            if (graphNodeStorage != currentNode && graphNodeStorage is T && (!(graphNodeStorage is T arg) || !passthrough(arg)))
            {
                continue;
            }
            foreach (GraphNodeStorage parent in graphNodeStorage.parents)
            {
                queue.Enqueue(parent);
            }
        }
        return default;
    }

    public static T FindChildType<T>(GraphNodeStorage currentNode, Func<T, bool> filter = null, Func<T, bool> passthrough = null)
    {
        if (currentNode == null)
        {
            return default;
        }
        if (filter == null)
        {
            filter = (T c) => true;
        }
        if (passthrough == null)
        {
            passthrough = (T c) => false;
        }
        Queue<GraphNodeStorage> queue = new Queue<GraphNodeStorage>();
        queue.Enqueue(currentNode);
        while (queue.Count > 0)
        {
            GraphNodeStorage graphNodeStorage = queue.Dequeue();
            if (graphNodeStorage != currentNode && graphNodeStorage is T val && filter(val))
            {
                return val;
            }
            if (graphNodeStorage != currentNode && graphNodeStorage is T && (!(graphNodeStorage is T arg) || !passthrough(arg)))
            {
                continue;
            }
            foreach (GraphNodeStorage child in graphNodeStorage.children)
            {
                queue.Enqueue(child);
            }
        }
        return default;
    }

    public static List<T> FindChildrenType<T>(GraphNodeStorage currentNode, Func<T, bool> filter = null, Func<T, bool> passthrough = null)
    {
        if (currentNode == null)
        {
            return new List<T>();
        }
        if (filter == null)
        {
            filter = (T c) => true;
        }
        if (passthrough == null)
        {
            passthrough = (T c) => false;
        }
        HashSet<T> hashSet = new HashSet<T>();
        Queue<GraphNodeStorage> queue = new Queue<GraphNodeStorage>();
        queue.Enqueue(currentNode);
        while (queue.Count > 0)
        {
            GraphNodeStorage graphNodeStorage = queue.Dequeue();
            if (graphNodeStorage != currentNode && graphNodeStorage is T val && filter(val))
            {
                hashSet.Add(val);
            }
            if (graphNodeStorage != currentNode && graphNodeStorage is T && (!(graphNodeStorage is T arg) || !passthrough(arg)))
            {
                continue;
            }
            foreach (GraphNodeStorage child in graphNodeStorage.children)
            {
                queue.Enqueue(child);
            }
        }
        return hashSet.ToList();
    }

    public static List<T> FindParentsType<T>(GraphNodeStorage currentNode, Func<T, bool> filter = null, Func<T, bool> passthrough = null)
    {
        if (currentNode == null)
        {
            return new List<T>();
        }
        if (filter == null)
        {
            filter = (T c) => true;
        }
        if (passthrough == null)
        {
            passthrough = (T c) => false;
        }
        HashSet<T> hashSet = new HashSet<T>();
        Queue<GraphNodeStorage> queue = new Queue<GraphNodeStorage>();
        queue.Enqueue(currentNode);
        while (queue.Count > 0)
        {
            GraphNodeStorage graphNodeStorage = queue.Dequeue();
            if (graphNodeStorage != currentNode && graphNodeStorage is T val && filter(val))
            {
                hashSet.Add(val);
            }
            if (graphNodeStorage != currentNode && graphNodeStorage is T && (!(graphNodeStorage is T arg) || !passthrough(arg)))
            {
                continue;
            }
            foreach (GraphNodeStorage parent in graphNodeStorage.parents)
            {
                queue.Enqueue(parent);
            }
        }
        return hashSet.ToList();
    }

    public static List<T> FindAllChildrenType<T>(GraphNodeStorage currentNode, Func<T, bool> filter = null, Func<T, bool> passthrough = null)
    {
        if (currentNode == null)
        {
            return new List<T>();
        }
        if (filter == null)
        {
            filter = (T c) => true;
        }
        if (passthrough == null)
        {
            passthrough = (T c) => true;
        }
        HashSet<T> hashSet = new HashSet<T>();
        Queue<GraphNodeStorage> queue = new Queue<GraphNodeStorage>();
        queue.Enqueue(currentNode);
        while (queue.Count > 0)
        {
            GraphNodeStorage graphNodeStorage = queue.Dequeue();
            if (graphNodeStorage != currentNode && graphNodeStorage is T val && filter(val))
            {
                hashSet.Add(val);
            }
            if (graphNodeStorage != currentNode && (!(graphNodeStorage is T arg) || !passthrough(arg)) && graphNodeStorage is T)
            {
                continue;
            }
            foreach (GraphNodeStorage child in graphNodeStorage.children)
            {
                queue.Enqueue(child);
            }
        }
        return hashSet.ToList();
    }
}
