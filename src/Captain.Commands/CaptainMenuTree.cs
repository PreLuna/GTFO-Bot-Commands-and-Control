using System;
using System.Collections.Generic;
using Player;
namespace PreLuna.Captain.Tasks
{
    internal static class CaptainMenuTree
    {
        public const uint RootId = 0xDCE01000, OriginalId = 0xDCE01100, PageStart = 0xDCE02000, PageEnd = 0xDCE04000;
        public const uint NodeStart = 0xDCE04000, NodeEnd = 0xDCE06000;
        public static bool Owned(uint id) => id == RootId || id >= RootId + 16 && id < RootId + 80 || Page(id) || id >= NodeStart && id < NodeEnd;
        public static bool Page(uint id) => id >= PageStart && id < PageEnd;
        private static readonly Dictionary<uint, CommunicationNode> navigation = new();
        public static void Reset() => navigation.Clear();
        public static void Link(uint id, CommunicationNode target) => navigation.Add(id, target);
        public static bool Navigation(uint id) => navigation.ContainsKey(id);
        public static CommunicationNode FindNativeTools(CommunicationNode root)
        {
            var stack = new Stack<CommunicationNode>(); var visited = new HashSet<IntPtr>();
            if (root != null) stack.Push(root);
            int remaining = 1024;
            while (stack.Count > 0 && remaining-- > 0) {
                var node = stack.Pop();
                if (node == null) continue;
                if (!visited.Add(node.Pointer) || node.m_ChildNodes == null) continue;
                // UseTool is a child template expanded by UpdateButtonsForNode, not a destination page.
                // Entering the template itself produces zero buttons and hides the native command list.
                if (!Owned(node.TextId) && !node.IsLastNode)
                    foreach (var child in node.m_ChildNodes)
                        if (child != null && !Owned(child.TextId) && child.Script == CommunicationNode.ScriptType.UseTool) return node;
                for (int i = node.m_ChildNodes.Count - 1; i >= 0; i--) stack.Push(node.m_ChildNodes[i]);
            }
            return null;
        }
        public static CommunicationNode Target(CommunicationNode node)
            => navigation.TryGetValue(node.TextId, out var target) ? target : node;
        // Only detached Captain nodes are paginated. The original/Chatter tree is never mutated.
        public static void Paginate(CommunicationNode node, int capacity, Func<uint, string, CommunicationNode> create, ref uint pageId)
        {
            if (capacity < 2) throw new InvalidOperationException("原版通讯按钮容量不足，保留原版菜单。");
            if (!Owned(node.TextId)) return;
            var children = node.m_ChildNodes;
            foreach (var child in children) Paginate(child, capacity, create, ref pageId);
            if (children.Count <= capacity) return;
            int take = capacity - 1, count = (children.Count + take - 1) / take;
            var pages = new CommunicationNode[count]; pages[0] = node;
            for (int i = 1; i < count; i++)
            {
                if (pageId >= PageEnd) throw new InvalidOperationException("队长菜单分页数量异常。");
                pages[i] = create(pageId++, "下一页");
            }
            for (int p = 0; p < count; p++)
            {
                var values = new Il2CppSystem.Collections.Generic.List<CommunicationNode>();
                for (int i = p * take; i < Math.Min(children.Count, (p + 1) * take); i++) values.Add(children[i]);
                // The back link is a separate lookup, never a cycle in the native node graph.
                if (p + 1 < count) values.Add(pages[p + 1]);
                else
                {
                    if (pageId >= PageEnd) throw new InvalidOperationException("队长菜单分页数量异常。");
                    var first = create(pageId++, "返回第一页");
                    navigation[first.TextId] = pages[0];
                    values.Add(first);
                }
                pages[p].m_ChildNodes = values;
            }
        }
    }
}
