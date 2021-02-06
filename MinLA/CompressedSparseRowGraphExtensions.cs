using System;
using System.Collections.Generic;
//#if DEBUG
using System.Diagnostics;
using System.IO;
//#endif
using System.Linq;
using Portent;

namespace MinLA
{
    public static class CompressedSparseRowGraphExtensions
    {
        public static void WriteD3JsonFormat(this CompressedSparseRowGraph graph, string filename)
        {
            using var file = new StreamWriter(File.OpenWrite(filename));
            file.Write(@"
{
    ""nodes"":
    [ ");
            file.Write(
@"
        {
            ""x"":0,
            ""y"":0
        }");
            for (var i = 1; i < graph.FirstChildEdgeIndex.Length - 1; i++)
            {
                file.Write(
@",
        {
            ""x"":"+i+@",
            ""y"":"+i+@"
        }");
            }

            file.Write(
@"
    ],
    ""links"":[  ");
            var useComma = false;
            for (var i = 1; i < graph.FirstChildEdgeIndex.Length - 1; i++)
            {
                var node = i - 1;
                var first = graph.FirstChildEdgeIndex[node];
                var last = graph.FirstChildEdgeIndex[i];
                for (var j = first; j < last; j++)
                {
                    var target = Math.Abs(graph.EdgeToNodeIndex[j]);
                    if (useComma)
                    {
                        file.Write(",");
                    }
                    else
                    {
                        useComma = true;
                    }

                    file.Write(
@"
        {
            ""source"":"+node+@",
            ""target"":"+target+@"
        }");
                }
            }

            file.Write(
@"
    ]
}");
        }

        public static CompressedSparseRowGraph Arrange(this CompressedSparseRowGraph graph, int[] arrangement)
        {
            var nodeCount = graph.FirstChildEdgeIndex.Length - 1;
            if (nodeCount != arrangement.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(arrangement));
            }

            //#if DEBUG
            var set = new HashSet<int>();
            for (var i = 0; i < arrangement.Length; i++)
            {
                Debug.Assert(0 <= arrangement[i], "0 <= arrangement[i]");
                if (!set.Add(arrangement[i]))
                {
                    var errorMessage = "Duplicate in arrangement at index " + i + " with value " + arrangement[i];
                    throw new InvalidOperationException(errorMessage);
                }
            }
            //#endif

            var reverse = new int[arrangement.Length];
            for (var i = 0; i < arrangement.Length; i++)
            {
                reverse[arrangement[i]] = i;
            }

            //#if DEBUG
            set.Clear();
            for (var i = 0; i < reverse.Length; i++)
            {
                Debug.Assert(0 <= reverse[i], "0 <= reverse[i]");
                if (!set.Add(reverse[i]))
                {
                    var errorMessage = "Duplicate in reverse at index " + i + " with value " + reverse[i];
                    throw new InvalidOperationException(errorMessage);
                }
            }
            //#endif

            // We skip node index 0 if it is terminal in the new graph.
            // It would be indistinguishable because there's no negative 0.
            var zeroIndexOffset = reverse[0] < 0 ? 1 : 0;
            var newFirstChildEdgeIndex = new uint[nodeCount + 1 + zeroIndexOffset];
            var newReachableTerminalCounts = new ushort[nodeCount + zeroIndexOffset];

            var newEdgeToNodeIndex = new int[graph.EdgeToNodeIndex.Length];
            var newEdgeCharacters = new char[graph.EdgeCharacters.Length];

            var edgeCount = 0u;
            for (var i = 0; i < nodeCount; i++)
            {
                var oldNode = arrangement[i];

                newFirstChildEdgeIndex[i + zeroIndexOffset] = edgeCount;
                newReachableTerminalCounts[i + zeroIndexOffset] = graph.ReachableTerminalNodes[oldNode];

                var first = graph.FirstChildEdgeIndex[oldNode];
                var last = graph.FirstChildEdgeIndex[oldNode + 1];
                //var edges = graph.EdgeToNodeIndex.Skip((int)first).Take((int)(last - first)).Select((x, index) => new {targetNode = x, index = index}).OrderBy(x => reverse[Math.Abs(x.targetNode)]);
                var edges = graph.EdgeToNodeIndex.Skip((int)first).Take((int)(last - first)).Select((x, index) => new { targetNode = x, index = index });//.OrderBy(x => reverse[Math.Abs(x.targetNode)]);

                foreach (var edge in edges)
                {
                    var targetNodeOldGraph = graph.EdgeToNodeIndex[first + edge.index];
                    Debug.Assert(targetNodeOldGraph == edge.targetNode);
                    if (targetNodeOldGraph != edge.targetNode)
                    {
                        throw new InvalidOperationException("");
                    }
                    var terminal = targetNodeOldGraph < 0;
                    newEdgeToNodeIndex[edgeCount] = (reverse[Math.Abs(targetNodeOldGraph)] + zeroIndexOffset) * (terminal ? -1 : 1);
                    newEdgeCharacters[edgeCount] = graph.EdgeCharacters[first + edge.index];
                    edgeCount++;
                }
                /*
                for (var j = graph.FirstChildEdgeIndex[oldNode]; j < graph.FirstChildEdgeIndex[oldNode + 1]; j++)
                {
                    var targetNodeOldGraph = graph.EdgeToNodeIndex[j];
                    var terminal = targetNodeOldGraph < 0;
                    newEdgeToNodeIndex[edgeCount] = (reverse[Math.Abs(targetNodeOldGraph)] + zeroIndexOffset) * (terminal ? -1 : 1);
                    newEdgeCharacters[edgeCount] = graph.EdgeCharacters[j];
                    edgeCount++;
                }*/
            }

            newFirstChildEdgeIndex[^1] = (uint) newEdgeToNodeIndex.Length;

            return new CompressedSparseRowGraph(reverse[graph.RootNodeIndex] + zeroIndexOffset, newFirstChildEdgeIndex, newEdgeToNodeIndex.Take((int)edgeCount).ToArray(), newEdgeCharacters, newReachableTerminalCounts, graph.DictionaryCounts);
        }

        public static double ArrangementCost(this CompressedSparseRowGraph me)
        {
            var total = 0.0d;
            for (var i = 1; i < me.FirstChildEdgeIndex.Length; i++)
            {
                var nodeId = i - 1;
                for (var j = me.FirstChildEdgeIndex[i - 1]; j < me.FirstChildEdgeIndex[i]; j++)
                {
                    var weight = me.EdgeWeights[j];
                    var targetNode = Math.Abs(me.EdgeToNodeIndex[j]);
                    total += Math.Abs(nodeId - targetNode) * weight;
                }
            }

            return total;
        }

        public static double CacheArrangementCost(this CompressedSparseRowGraph me)
        {
            var total = 0.0d;
            for (var i = 1; i < me.FirstChildEdgeIndex.Length; i++)
            {
                var nodeId = i - 1;
                for (var j = me.FirstChildEdgeIndex[i - 1]; j < me.FirstChildEdgeIndex[i]; j++)
                {
                    var weight = me.EdgeWeights[j];
                    var targetNode = Math.Abs(me.EdgeToNodeIndex[j]);
                    //total += Math.Abs(nodeId - targetNode) < CacheAwareInstance.CacheSize ? 0 : weight;
                    total += nodeId/CacheAwareInstance.CacheSize == targetNode/CacheAwareInstance.CacheSize ? 0 : weight;
                }
            }

            return total;
        }
        /*
        public static CompressedSparseRowGraph ArrangeGreedy(this CompressedSparseRowGraph me)
        {
            var sorts = new List<Tuple<int, int>>(me.FirstChildEdgeIndex.Length - 1);
            for(var i = 0; i < me.FirstChildEdgeIndex.Length -1; i++)
            {
                var firstChildIndex = me.FirstChildEdgeIndex[i];
                var lastChildIndex = me.FirstChildEdgeIndex[i + 1];
                for(var edge = firstChildIndex; edge < lastChildIndex; edge++)
                {
                    var targetNode = me.EdgeToNodeIndex[edge];

                }
            }
        }
        */

        public static List<UndirectedGraphNode> Convert(this CompressedSparseRowGraph me)
        {
            var result = new List<UndirectedGraphNode>();
            var maxNodeId = me.FirstChildEdgeIndex.Length - 2;
            for (var i = 0; i <= maxNodeId; i++)
            {
                result.Add(new UndirectedGraphNode(i));
            }

            for (var nodeIndexAbs = 0; nodeIndexAbs < me.FirstChildEdgeIndex.Length - 1; nodeIndexAbs++)
            {
                var last = me.FirstChildEdgeIndex[nodeIndexAbs + 1];
                var currentNode = result[nodeIndexAbs];
                for (var j = me.FirstChildEdgeIndex[nodeIndexAbs]; j < last; j++)
                {
                    var childNodeIndex = me.EdgeToNodeIndex[j];
                    var childNodeIndexAbs = Math.Abs(childNodeIndex);
                    var weightEdgeToChild = me.EdgeWeights[j];
                    var childNode = result[childNodeIndexAbs];
                    if (childNodeIndex < 0)
                    {
                        childNode.Terminal = true;
                    }

                    currentNode.Children.Add(childNode.Id);
                    AddNeighborEdge(nodeIndexAbs, currentNode, childNodeIndexAbs, childNode, weightEdgeToChild);

                    // Sibling section - very time consuming
                    for (var k = j + 1; k < last; k++)
                    {
                        var siblingNodeIndexAbs = Math.Abs(me.EdgeToNodeIndex[k]);
                        var siblingNode = result[siblingNodeIndexAbs];
                        AddSiblingEdge(childNodeIndexAbs, childNode, siblingNodeIndexAbs, siblingNode, weightEdgeToChild);
                    }
                }
            }

            return result;
        }

        private static void AddNeighborEdge(int node1IndexAbs, UndirectedGraphNode node1, int node2IndexAbs, UndirectedGraphNode node2, double edgeWeight)
        {
            if (node2.Neighbors.ContainsKey(node1IndexAbs))
            {
                node2.Neighbors[node1IndexAbs] += edgeWeight;
            }
            else
            {
                node2.Neighbors[node1IndexAbs] = edgeWeight;
            }

            if (node1.Neighbors.ContainsKey(node2IndexAbs))
            {
                node1.Neighbors[node2IndexAbs] += edgeWeight;
            }
            else
            {
                node1.Neighbors[node2IndexAbs] = edgeWeight;
            }
        }

        private static void AddSiblingEdge(int node1IndexAbs, UndirectedGraphNode node1, int node2IndexAbs, UndirectedGraphNode node2, double edgeWeight)
        {
            if (node2.Siblings.ContainsKey(node1IndexAbs))
            {
                node2.Siblings[node1IndexAbs] += edgeWeight;
            }
            else
            {
                node2.Siblings[node1IndexAbs] = edgeWeight;
            }

            if (node1.Siblings.ContainsKey(node2IndexAbs))
            {
                node1.Siblings[node2IndexAbs] += edgeWeight;
            }
            else
            {
                node1.Siblings[node2IndexAbs] = edgeWeight;
            }
        }
    }
}
