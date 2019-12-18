using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Portent
{
    internal sealed class GraphNode
    {
        /// <summary>
        /// This contains the children. It is not modified.
        /// </summary>
        public Dictionary<char, GraphNode> Children { get; } = new Dictionary<char, GraphNode>();

        /// <summary>
        /// This will be equivalent to Children. It should contain more accurate Count information: tracked at the edge level instead of by node.
        /// </summary>
        public readonly Dictionary<char, GraphEdge> ChildEdges = new Dictionary<char, GraphEdge>();

        public void RecursiveAddEdgeCount(ulong count)
        {
            foreach (var childEdge in ChildEdges.Values)
            {
                childEdge.Count += count;
                childEdge.Target.RecursiveAddEdgeCount(count);
            }
        }

        public IEnumerable<KeyValuePair<char, GraphNode>> SortedChildren => Children.OrderByDescending(x => x.Value.Count);

        /// <summary>
        /// This contains the parents. Elements are removed from it during Topological Ordering.
        /// </summary>
        public HashSet<GraphNode> Parents { get; } = new HashSet<GraphNode>();

        public bool IsTerminal { get; set; }
        public int ReachableTerminalNodes { get; private set; } = -1;
        public long Count { get; set; }
        public bool Visited { get; set; }
        public int OrderedId { get; set; }

        public (int nodeCount, int edgeCount) GatherNodesCountEdges()
        {
            if (Visited)
            {
                return (0, 0);
            }

            Visited = true;

            var totalEdges = 0;
            var totalNodes = 1;
            foreach (var (_, node) in Children)
            {
                node.Parents.Add(this);
                // ReSharper disable once PossibleNullReferenceException
                var (childCount, childEdges) = node.GatherNodesCountEdges();
                Count += node.Count;
                //Add 1 to represent the edge to the child itself.
                totalEdges += childEdges + 1;
                totalNodes += childCount;
            }

            return (totalNodes, totalEdges);
        }

        public int CalculateReachableTerminals()
        {
            if (ReachableTerminalNodes >= 0)
            {
                return ReachableTerminalNodes;
            }

            ReachableTerminalNodes = 0;
            foreach (var child in Children)
            {
                ReachableTerminalNodes += child.Value.CalculateReachableTerminals();
            }

            ReachableTerminalNodes += IsTerminal ? 1 : 0;
            return ReachableTerminalNodes;
        }

        private void StringMe(StringBuilder builder)
        {
            builder.Append('(');
            if (IsTerminal)
            {
                builder.Append('-');
            }

            foreach (var (key, node) in Children)
            {
                builder.Append(key);
                node.StringMe(builder);
            }

            builder.Append(')');
        }

        public override string ToString()
        {
            var builder = new StringBuilder(50);
            StringMe(builder);
            return builder.ToString();
        }

        private int _cachedHash;

        private int PrivateHash()
        {
            if (_cachedHash != 0)
            {
                return _cachedHash;
            }

            var hash = IsTerminal ? 1 : 0;
            hash = ((hash << 5) + hash) ^ '(';
            foreach (var (key, node) in Children)
            {
                hash = ((hash << 5) + hash) ^ key;
                hash = ((hash << 5) + hash) ^ node.PrivateHash();
            }

            hash = ((hash << 5) + hash) ^ ')';
            _cachedHash = hash;
            return hash;
        }

        public override int GetHashCode()
        {
            return PrivateHash().GetHashCode();
        }

        public override bool Equals(object? obj)
        {
            return obj is GraphNode other && ToString() == other.ToString();
        }
    }
}
