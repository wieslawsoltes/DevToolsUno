namespace DevTools.Uno.Diagnostics.Internal;

internal static class HierarchyExpansionState
{
    public static HashSet<TKey> CaptureExpandedKeys<TNode, TKey>(
        IEnumerable<TNode> roots,
        Func<TNode, IEnumerable<TNode>> getChildren,
        Func<TNode, TKey?> getKey,
        Func<TNode, bool> isExpanded,
        IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
    {
        var expandedKeys = new HashSet<TKey>(comparer);

        foreach (var root in roots)
        {
            Capture(root);
        }

        return expandedKeys;

        void Capture(TNode node)
        {
            if (isExpanded(node) && getKey(node) is { } key)
            {
                expandedKeys.Add(key);
            }

            foreach (var child in getChildren(node))
            {
                Capture(child);
            }
        }
    }

    public static void RestoreExpandedKeys<TNode, TKey>(
        IEnumerable<TNode> roots,
        Func<TNode, IEnumerable<TNode>> getChildren,
        Func<TNode, TKey?> getKey,
        Action<TNode, bool> setExpanded,
        ISet<TKey> expandedKeys)
        where TKey : notnull
    {
        foreach (var root in roots)
        {
            Restore(root);
        }

        void Restore(TNode node)
        {
            setExpanded(node, getKey(node) is { } key && expandedKeys.Contains(key));

            foreach (var child in getChildren(node))
            {
                Restore(child);
            }
        }
    }
}
