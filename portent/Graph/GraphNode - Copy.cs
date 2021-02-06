using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Portent
{
    internal sealed class GraphNode2
    {
        /// <summary>
        /// This contains the children. It is not modified.
        /// </summary>
        public Dictionary<char, GraphNode2> Children { get; } = new Dictionary<char, GraphNode2>();

        /// <summary>
        /// This will be equivalent to Children. It should contain more accurate Count information: tracked at the edge level instead of by node.
        /// </summary>
        public readonly Dictionary<char, GraphEdge2> ChildEdges = new Dictionary<char, GraphEdge2>();

        public void RecursiveAddEdgeCount(ulong count)
        {
            foreach (var childEdge in ChildEdges.Values)
            {
                childEdge.Count += count;
                childEdge.Target.RecursiveAddEdgeCount(count);
            }
        }

        public IEnumerable<KeyValuePair<char, GraphNode2>> SortedChildren => Children.OrderByDescending(x => x.Value.Count);
        public IEnumerable<KeyValuePair<char, GraphEdge2>> SortedChildEdges => ChildEdges.OrderByDescending(x => x.Value.Count);

        /// <summary>
        /// This contains the parents. Elements are removed from it during Topological Ordering.
        /// </summary>
        public HashSet<GraphNode2> Parents { get; } = new HashSet<GraphNode2>();

        public int ReachableTerminalNodes { get; private set; } = -1;
        public ulong Count { get; set; }
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
            foreach (var child in ChildEdges)
            {
                ReachableTerminalNodes += child.Value.TerminalEdge ? 1 : 0;
                ReachableTerminalNodes += child.Value.Target.CalculateReachableTerminals();
                
            }

            return ReachableTerminalNodes;
        }

        private void StringMe(StringBuilder builder)
        {
            builder.Append('(');

            foreach (var (key, edge) in ChildEdges)
            {
                builder.Append(key);
                if (edge.TerminalEdge)
                {
                    builder.Append("-");
                }

                edge.Target.StringMe(builder);
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

            var hash = (int)'(';
            foreach (var (key, edge) in ChildEdges)
            {
                hash = ((hash << 5) + hash) ^ key;
                hash = ((hash << 5) + hash) ^ (edge.TerminalEdge ? 1 : 0);
                hash = ((hash << 5) + hash) ^ edge.Target.PrivateHash();
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
            return obj is GraphNode2 other && ToString() == other.ToString();
        }
    }
}
