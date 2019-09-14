using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using JBP;

namespace portent
{
    public sealed class CompressedSparseRowGraph
    {
        internal CompressedSparseRowGraph(int rootNodeIndex, int[] firstChildEdgeIndex, int[] edgeToNodeIndex, char[] edgeCharacter, ushort[] reachableTerminalNodes, long[] data, Dictionary<string, long> wordCounts)
            : this(rootNodeIndex, firstChildEdgeIndex, edgeToNodeIndex, edgeCharacter, reachableTerminalNodes, data)
        {
            AssignCounts(RootNodeIndex, new char[100], 0, -1, wordCounts);
        }

        private CompressedSparseRowGraph(int rootNodeIndex, int[] firstChildEdgeIndex, int[] edgeToNodeIndex, char[] edgeCharacter, ushort[] reachableTerminalNodes, long[] data)
        {
            RootNodeIndex = rootNodeIndex;
            FirstChildEdgeIndex = firstChildEdgeIndex;
            EdgeToNodeIndex = edgeToNodeIndex;
            EdgeCharacter = edgeCharacter;
            ReachableTerminalNodes = reachableTerminalNodes;
            WordCounts = data;
        }

        internal readonly int RootNodeIndex;
        internal readonly int[] FirstChildEdgeIndex;
        internal readonly int[] EdgeToNodeIndex;
        internal readonly char[] EdgeCharacter;
        internal readonly ushort[] ReachableTerminalNodes;
        internal readonly long[] WordCounts;

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

        public static CompressedSparseRowGraph Read(Stream stream)
        {
            return new CompressedSparseRowGraph(
                stream.Read<int>(),
                stream.ReadCompressedIntArray(),
                stream.ReadArray<int>(),
                stream.ReadCharArray(),
                stream.ReadCompressedUshortArray(),
                stream.ReadCompressedLongArray());
        }

        public void Save(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new InvalidOperationException();
            }

            var fullPath = Path.GetFullPath(path);
            if (!Directory.Exists(Path.GetDirectoryName(fullPath)))
            {
                throw new InvalidOperationException();
            }

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
