using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime;

namespace portent
{
    public class PartitionedGraphBuilder
    {
        public CompressedSparseRowGraph AsCompressedSparseRows()
        {
            var root = Finish();

            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();

            var edges = EdgeCount;
            var allNodes = OrderedNodes;

            var edgeToNodeIndex = new int[edges];
            var edgeCharacter = new char[edges];
            var firstChildEdgeIndex = new int[allNodes.Count + 1];
            firstChildEdgeIndex[^1] = edgeToNodeIndex.Length;
            var rootNodeIndex = root.OrderedId;
            var reachableTerminalNodes = new ushort[allNodes.Count];

            var edgeIndex = 0;
            var nodeCount = 0;
            foreach (var node in allNodes)
            {
                var nodeId = node.OrderedId;
                Debug.Assert(nodeId == nodeCount);
                ++nodeCount;
                firstChildEdgeIndex[nodeId] = edgeIndex;
                reachableTerminalNodes[nodeId] = (ushort)node.ReachableTerminalNodes;

                foreach (var child in node.SortedChildren)
                {
                    var terminalModifier = child.Value.IsTerminal ? -1 : 1;
                    edgeToNodeIndex[edgeIndex] = terminalModifier * child.Value.OrderedId;
                    edgeCharacter[edgeIndex] = child.Key;
                    ++edgeIndex;
                }
            }

            var wordCounts = new long[WordCount];

            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();

            return new CompressedSparseRowGraph(rootNodeIndex, firstChildEdgeIndex, edgeToNodeIndex, edgeCharacter, reachableTerminalNodes, wordCounts, Counts);
        }

        private const int MaxWordLength = 40;
        private string _previousWord = string.Empty;
        private readonly GraphNode _root = new GraphNode();
        private readonly Dictionary<GraphNode, GraphNode> _minimizedNodes = new Dictionary<GraphNode, GraphNode>();
        private readonly char[] _letterStack = new char[MaxWordLength];
        private readonly GraphNode[] _childStack = new GraphNode[MaxWordLength];
        private readonly GraphNode[] _parentStack = new GraphNode[MaxWordLength];

        public List<GraphNode> AllNodes { get; } = new List<GraphNode>();
        public List<GraphNode> OrderedNodes { get; } = new List<GraphNode>();

        public int EdgeCount { get; private set; }

        private int _stackTop = -1;

        public readonly Dictionary<string, long> Counts = new Dictionary<string, long>();
        public int WordCount => Counts.Count;

        /// <summary>
        /// Inserts a word into the DAWG. Words are expected to be provided in a sorted order.
        /// </summary>
        public void Insert(string word, long count)
        {
            if (WordCount % 5000 == 0)
            {
                Debug.WriteLine($"Insert: {WordCount.ToString()}");
            }

            Debug.Assert(string.CompareOrdinal(word, _previousWord) > 0);
            Debug.Assert(word.Length < MaxWordLength);

            var commonPrefix = CommonPrefixWithPreviousWord(word);
            Minimize(commonPrefix);

            // 1 to skip _root
            for (var i = 0; i <= commonPrefix && i <= _stackTop; i++)
            {
                _parentStack[i].ChildEdges[word[i]].Count += count;
            }

            var node = _stackTop == -1 ? _root : _childStack[_stackTop];
            for (var i = commonPrefix; i < word.Length; i++)
            {
                var letter = word[i];
                var nextNode = new GraphNode();
                node.Children[letter] = nextNode;
                node.ChildEdges[letter] = new GraphEdge(nextNode) { Label = letter, Count = count };
                ++_stackTop;
                _parentStack[_stackTop] = node;
                _letterStack[_stackTop] = letter;
                _childStack[_stackTop] = nextNode;
                node = nextNode;
            }

            node.IsTerminal = true;
            // NOTE: Using log instead of straight value.
            // Trying to smooth it because we go down weird branches to look for edits.
            node.Count = (long)Math.Log2(count);
            _previousWord = word;
            Counts.Add(word, count);
        }

        public GraphNode Finish()
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            Debug.WriteLine($"{stopwatch.ElapsedMilliseconds.ToString()} Finishing up {GetType().Name}");
            Minimize(0);
            _minimizedNodes.Clear();
            Debug.WriteLine($"{stopwatch.ElapsedMilliseconds.ToString()} Calculating reachable Terminals.");
            _root.CalculateReachableTerminals();

            Debug.WriteLine($"{stopwatch.ElapsedMilliseconds.ToString()} Collecting Nodes");
            CollectNodes();
            Debug.WriteLine($"{stopwatch.ElapsedMilliseconds.ToString()} Topological Sort");
            TopologicalSort();
            Debug.WriteLine($"{stopwatch.ElapsedMilliseconds.ToString()} Assigning Ids");
            AssignIds();
            Debug.WriteLine($"{stopwatch.ElapsedMilliseconds.ToString()} Graph ready");

            return _root;
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

        private void AssignIds()
        {
            for (var i = 0; i < OrderedNodes.Count; i++)
            {
                OrderedNodes[i].OrderedId = i;
            }
        }

        private void Add(GraphNode node, Dictionary<int, HashSet<GraphNode>> parentCounts)
        {
            OrderedNodes.Add(node);

            foreach (var item in node.Parents)
            {
                item.ChildrenCopy?.Remove(node);
            }

            var copyOfList = node.ChildrenCopy.ToList();
#pragma warning disable U2U1203 // Use foreach efficiently - I am modifying the underlying collection
            foreach (var item in copyOfList)
#pragma warning restore U2U1203 // Use foreach efficiently
            {
                var currentCount = item.Parents.Count;
                parentCounts[item.Parents.Count].Remove(item);
                item.Parents.Remove(node);
                parentCounts[item.Parents.Count].Add(item);
                node.ChildrenCopy?.Remove(item);
            }
        }

        private void TopologicalSort()
        {
            Debug.WriteLine("\tBegin Topological sort");
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            var parentMax = 0;
            Debug.WriteLine($"\t{stopwatch.ElapsedMilliseconds.ToString()} Creating Parent + Child Copies");
            foreach (var node in AllNodes)
            {
                if (node.Parents.Count > parentMax)
                {
                    parentMax = node.Parents.Count;
                }

                node.ChildrenCopy = new HashSet<GraphNode>(node.Children.Values);
                node.ParentCopy = node.Parents.ToArray();
            }

            Debug.WriteLine($"\t{stopwatch.ElapsedMilliseconds.ToString()} Populating parentCounts");
            var parentCounts = new Dictionary<int, HashSet<GraphNode>>(parentMax + 1);
            for (var i = 0; i <= parentMax; i++)
            {
                parentCounts[i] = new HashSet<GraphNode>();
            }

            Debug.WriteLine($"\t{stopwatch.ElapsedMilliseconds.ToString()}");
            foreach (var node in AllNodes)
            {
                parentCounts[node.Parents.Count].Add(node);
            }

            var potentials = parentCounts[0];
            var window = new Queue<GraphNode>();
            const int windowSize = 4;
            var added = 0;
            var total = AllNodes.Count;
            OrderedNodes.Capacity = total;
            var decider = new Dictionary<GraphNode, long>();

            Debug.WriteLine($"\t{stopwatch.ElapsedMilliseconds.ToString()} Starting Loop");
            stopwatch.Restart();
            var innerStopwatch = new Stopwatch();
            while (added < total)
            {
                added++;
                if (added % 100 == 0)
                {
                    Debug.WriteLine($"\t{stopwatch.ElapsedMilliseconds.ToString()} Added: {added.ToString()} / {total.ToString()}. Time in inner loops: {innerStopwatch.ElapsedMilliseconds.ToString()}");
                }

                decider.Clear();
                Debug.Assert(potentials.Count > 0);

                innerStopwatch.Start();
                foreach (var current in window)
                {
                    foreach (var childEdge in current.ChildEdges.Values)
                    {
                        if (!potentials.Contains(childEdge.Target))
                        {
                            continue;
                        }

                        if (decider.TryGetValue(childEdge.Target, out long result))
                        {
                            decider[childEdge.Target] += childEdge.Count;
                        }
                        else
                        {
                            decider.Add(childEdge.Target, childEdge.Count);
                        }
                    }

                    if (current.ParentCopy is null)
                    {
                        continue;
                    }

                    foreach (var parent in current.ParentCopy)
                    {
                        foreach (var childEdge in parent.ChildEdges.Values)
                        {
                            if (!potentials.Contains(childEdge.Target))
                            {
                                continue;
                            }

                            if (decider.TryGetValue(childEdge.Target, out long result))
                            {
                                decider[childEdge.Target] += childEdge.Count;
                            }
                            else
                            {
                                decider.Add(childEdge.Target, childEdge.Count);
                            }
                        }
                    }
                }

                innerStopwatch.Stop();

                var node = potentials.First();

                var max = -1L;
                foreach (var item in decider)
                {
                    if (item.Value > max)
                    {
                        max = item.Value;
                        node = item.Key;
                    }
                }

                Add(node, parentCounts);

                if (window.Count >= windowSize)
                {
                    window.Dequeue();
                }

                window.Enqueue(node);

                potentials.Remove(node);
            }

            Debug.WriteLine($"\tTopological Sort complete. Inner loop took {innerStopwatch.ElapsedMilliseconds.ToString()} out of {stopwatch.ElapsedMilliseconds.ToString()} for the outer loop.");
        }

        private void CollectNodes()
        {
            AllNodes.Capacity = Counts.Count;
            EdgeCount = _root.GatherNodesCountEdges(AllNodes);
        }

        private void Minimize(int downTo)
        {
            while (_stackTop >= downTo)
            {
                var oldChild = _childStack[_stackTop];
                if (_minimizedNodes.TryGetValue(oldChild, out var existingMinimizedNode))
                {
                    _parentStack[_stackTop].Children[_letterStack[_stackTop]] = existingMinimizedNode;
                    existingMinimizedNode.Count += oldChild.Count;

                    //new node stuff
                    var edge = _parentStack[_stackTop].ChildEdges[_letterStack[_stackTop]];
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
