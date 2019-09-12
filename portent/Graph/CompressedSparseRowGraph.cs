using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using JBP;

namespace portent
{
    public sealed class CompressedSparseRowGraph
    {
        public CompressedSparseRowGraph(int rootNodeIndex, int[] firstChildEdgeIndex, int[] edgeToNodeIndex, char[] edgeCharacter, ushort[] reachableTerminalNodes, long[] data, Dictionary<string, long> wordCounts)
        {
            RootNodeIndex = rootNodeIndex;
            FirstChildEdgeIndex = firstChildEdgeIndex;
            EdgeToNodeIndex = edgeToNodeIndex;
            EdgeCharacter = edgeCharacter;
            ReachableTerminalNodes = reachableTerminalNodes;
            WordCounts = data;
            AssignCounts(RootNodeIndex, new char[100], 0, -1, wordCounts);
        }

        public readonly int RootNodeIndex;
        public readonly int[] FirstChildEdgeIndex;
        public readonly int[] EdgeToNodeIndex;
        public readonly char[] EdgeCharacter;
        public readonly ushort[] ReachableTerminalNodes;
        public readonly long[] WordCounts;

        private int AssignCounts(int node, char[] builder, int builderLength, int reachableCount, Dictionary<string, long> counts)
        {
            if (node < 0)
            {
                ++reachableCount;
                var word = new string(builder, 0, builderLength);
                WordCounts[reachableCount] = counts[word];
                node = -node;
            }

            var i = FirstChildEdgeIndex[node];
            var last = FirstChildEdgeIndex[node + 1];
            for (; i < last; ++i)
            {
                builder[builderLength] = EdgeCharacter[i];
                var nextNode = EdgeToNodeIndex[i];

                var childReachable = AssignCounts(nextNode, builder, builderLength + 1, reachableCount, counts);
                var realReachable = ReachableTerminalNodes[Math.Abs(nextNode)];
                Debug.Assert(realReachable == childReachable - reachableCount);
                reachableCount = childReachable;
            }

            return reachableCount;
        }

        public void Save(string path)
        {
            using var stream = File.OpenWrite(path);
            if (stream == null)
            {
                throw new InvalidOperationException();
            }

            stream.Write(RootNodeIndex);
            stream.WriteCompressed(FirstChildEdgeIndex);
            stream.Write(EdgeToNodeIndex);
            stream.Write(EdgeCharacter);
            stream.WriteCompressed(ReachableTerminalNodes);
            stream.WriteCompressed(WordCounts);
        }
    }
}
