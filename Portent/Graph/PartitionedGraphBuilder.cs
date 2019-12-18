using System;
using System.Collections.Generic;

namespace Portent
{
    public sealed class PartitionedGraphBuilder
    {
        public CompressedSparseRowGraph AsCompressedSparseRows()
        {
            Minimize(0);
            _minimizedNodes.Clear();
            _root.CalculateReachableTerminals();

            int nodeCount, edgeCount;
            (nodeCount, edgeCount) = _root.GatherNodesCountEdges();
            var orderedNodes = EasyTopologicalSort(nodeCount, _root);

            var rootNodeIndex = _root.OrderedId;

            var edgeToNodeIndex = new int[edgeCount];
            var edgeCharacter = new char[edgeCount];
            var firstChildEdgeIndex = new uint[orderedNodes.Length + 1];
            firstChildEdgeIndex[^1] = (uint)edgeToNodeIndex.Length;
            var reachableTerminalNodes = new ushort[orderedNodes.Length];

            var edgeIndex = 0u;
            foreach (var node in orderedNodes)
            {
                var nodeId = node.OrderedId;

                firstChildEdgeIndex[nodeId] = edgeIndex;
                reachableTerminalNodes[nodeId] = (ushort)node.ReachableTerminalNodes;

                foreach (var (key, childNode) in node.SortedChildren)
                {
                    var terminalModifier = childNode.IsTerminal ? -1 : 1;
                    edgeToNodeIndex[edgeIndex] = terminalModifier * childNode.OrderedId;
                    edgeCharacter[edgeIndex] = key;

                    ++edgeIndex;
                }
            }

            return new CompressedSparseRowGraph(rootNodeIndex, firstChildEdgeIndex, edgeToNodeIndex, edgeCharacter, reachableTerminalNodes, _counts);
        }

        private const int MaxWordLength = 100;
        private string _previousWord = string.Empty;
        private readonly GraphNode _root = new GraphNode();
        private readonly Dictionary<GraphNode, GraphNode> _minimizedNodes = new Dictionary<GraphNode, GraphNode>();

        private struct LetterStackItem
        {
            public char Letter;
            public GraphNode Child;
            public GraphNode Parent;
        }

        private readonly LetterStackItem[] _stack = new LetterStackItem[MaxWordLength];
        private int _stackTop = -1;

        private readonly Dictionary<string, ulong> _counts = new Dictionary<string, ulong>();
        internal int WordCount => _counts.Count;

        /// <summary>
        /// Inserts a word into the DAWG.
        /// Words must be provided in (<see cref="StringComparison.Ordinal"/>) order.
        /// </summary>
        public void Insert(string word, ulong count)
        {
            if (string.CompareOrdinal(word, _previousWord) <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(word));
            }

            var commonPrefix = CommonPrefixWithPreviousWord(word);
            Minimize(commonPrefix);

            // 1 to skip _root
            for (var i = 0; i <= commonPrefix && i <= _stackTop; i++)
            {
                _stack[i].Parent.ChildEdges[word[i]].Count += count;
            }

            var node = _stackTop == -1 ? _root : _stack[_stackTop].Child;
            for (var i = commonPrefix; i < word.Length; i++)
            {
                var letter = word[i];
                var nextNode = new GraphNode();
                node.Children[letter] = nextNode;
                node.ChildEdges[letter] = new GraphEdge(nextNode) { Label = letter, Count = count };
                ++_stackTop;
                ref var stackItem = ref _stack[_stackTop];
                stackItem.Letter = letter;
                stackItem.Parent = node;
                stackItem.Child = nextNode;
                node = nextNode;
            }

            node.IsTerminal = true;
            // NOTE: Using log instead of straight value.
            // Trying to smooth it because we go down weird branches to look for edits.
            node.Count = (long)Math.Log2(count);
            _previousWord = word;
            _counts.Add(word, count);
        }

        private int CommonPrefixWithPreviousWord(string word)
        {
            int commonPrefix;
            var minLen = Math.Min(word.Length, _previousWord.Length);
            for (commonPrefix = 0; commonPrefix < minLen; commonPrefix++)
            {
                if (word[commonPrefix] != _previousWord[commonPrefix])
                {
                    break;
                }
            }

            return commonPrefix;
        }

        private static GraphNode[] EasyTopologicalSort(int nodeCount, GraphNode root)
        {
            var results = new GraphNode[nodeCount];
            var index = 0;
            var additionIndex = 0;
            root.OrderedId = additionIndex;
            results[additionIndex++] = root;
            while (index < additionIndex)
            {
                var next = results[index++];
                foreach (var childNode in next.Children.Values)
                {
                    if (childNode.Parents.Count == 0)
                    {
                        continue;
                    }

                    childNode.Parents.Remove(next);

                    if (childNode.Parents.Count == 0)
                    {
                        childNode.OrderedId = additionIndex;
                        results[additionIndex++] = childNode;
                    }
                }
            }

            return results;
        }

        private void Minimize(int downTo)
        {
            while (_stackTop >= downTo)
            {
                var stackItem = _stack[_stackTop];
                var oldChild = stackItem.Child;
                if (_minimizedNodes.TryGetValue(oldChild, out var existingMinimizedNode))
                {
                    var parent = stackItem.Parent;
                    var letter = stackItem.Letter;

                    parent.Children[letter] = existingMinimizedNode;
                    existingMinimizedNode.Count += oldChild.Count;

                    //new node stuff
                    var edge = parent.ChildEdges[letter];
                    edge.Target = existingMinimizedNode;
                    //TODO: Should this be recursive or just 1 level?
                    existingMinimizedNode.RecursiveAddEdgeCount(edge.Count);
                }
                else
                {
                    _minimizedNodes.Add(oldChild, oldChild);
                }

                --_stackTop;
            }
        }
    }
}
