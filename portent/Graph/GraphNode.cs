using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace portent
{
    public class GraphNode
    {
        /// <summary>
        /// This contains the children. It is not modified.
        /// </summary>
        public Dictionary<char, GraphNode> Children { get; } = new Dictionary<char, GraphNode>();
        /// <summary>
        /// This can contain a copy of Children. It is assigned during Topological Ordering and the elements are removed one by one.
        /// </summary>
        public HashSet<GraphNode>? ChildrenCopy;
        /// <summary>
        /// This will be equivalent to Children. It should contain more accurate Count information: tracked at the edge level instead of by node.
        /// </summary>
        public Dictionary<char, GraphEdge> ChildEdges = new Dictionary<char, GraphEdge>();

        public void RecursiveAddEdgeCount(long count)
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
        /// <summary>
        /// This is a snapshot of the Parents. Assigned during Topological Ordering and not modified.
        /// </summary>
        public GraphNode[]? ParentCopy;

        public bool IsTerminal { get; set; }
        public int ReachableTerminalNodes { get; protected set; } = -1;
        public long Count { get; set; }
        public bool Visited { get; set; }
        public int OrderedId { get; set; }

        public void TopologicalSortChildren(List<GraphNode> results)
        {
            if (Visited)
            {
                //TODO: will this even be hit?
                return;
            }

            Visited = true;
            foreach (var child in SortedChildren.Reverse())
            {
                if (!child.Value.Visited)
                {
                    child.Value.TopologicalSortChildren(results);
                }
            }

            results.Add(this);
        }

        public int GatherNodesCountEdges(ICollection<GraphNode> nodes)
        {
            if (Visited)
            {
                return 0;
            }

            Visited = true;

            var totalEdges = 0;

            nodes.Add(this);

            foreach (var child in Children)
            {
                child.Value.Parents.Add(this);
                // ReSharper disable once PossibleNullReferenceException
                var childEdges = child.Value.GatherNodesCountEdges(nodes);
                Count += child.Value.Count;
                //Add 1 to represent the edge to the child itself.
                totalEdges += childEdges + 1;
            }

            return totalEdges;
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

            foreach (var edge in Children)
            {
                builder.Append(edge.Key);
                edge.Value.StringMe(builder);
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
            foreach (var dawgNode in Children)
            {
                hash = ((hash << 5) + hash) ^ dawgNode.Key;
                hash = ((hash << 5) + hash) ^ dawgNode.Value.PrivateHash();
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
