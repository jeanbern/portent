using System;
using System.Collections.Generic;
#if DEBUG
using System.Diagnostics;
#endif
using System.IO;
using System.Runtime;

namespace portent
{
    public unsafe class CompressedSparseRowPointerGraph
    {
        internal readonly LargePageMemoryChunk MemoryChunk;
        internal readonly int RootNodeIndex;
        internal readonly uint* FirstChildEdgeIndex;
        internal readonly int* EdgeToNodeIndex;
        internal readonly char* EdgeCharacter;
        internal readonly ushort* ReachableTerminalNodes;
        internal readonly uint WordCount;
        internal readonly ulong* WordCounts;

        public static CompressedSparseRowPointerGraph Read(Stream stream)
        {
            return new CompressedSparseRowPointerGraph(stream);
        }

        public CompressedSparseRowPointerGraph(Stream stream)
        {
            var totalPageSize = stream.ReadCompressedULong();
            MemoryChunk = new LargePageMemoryChunk(totalPageSize);

            RootNodeIndex = stream.ReadCompressedInt();

            var firstChildEdgeIndexCount = stream.ReadCompressedUInt();
            FirstChildEdgeIndex = MemoryChunk.GetArrayAligned<uint>(firstChildEdgeIndexCount);
            stream.ReadSequentialCompressedUshortToUint(FirstChildEdgeIndex, firstChildEdgeIndexCount);

            var edgeToNodeIndexCount = stream.ReadCompressedUInt();
            EdgeToNodeIndex = MemoryChunk.GetArrayAligned<int>(edgeToNodeIndexCount);
            stream.ReadCompressed(EdgeToNodeIndex, edgeToNodeIndexCount);

            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();

            var byteLength = stream.ReadCompressedUInt();
            var charLength = stream.ReadCompressedUInt();
            EdgeCharacter = MemoryChunk.GetArrayAligned<char>(charLength);
            stream.ReadUtf8(EdgeCharacter, byteLength, charLength);

            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();

            var reachableTerminalNodesCount = stream.ReadCompressedUInt();
            ReachableTerminalNodes = MemoryChunk.GetArrayAligned<ushort>(reachableTerminalNodesCount);
            stream.ReadCompressed(ReachableTerminalNodes, reachableTerminalNodesCount);

            WordCount = stream.ReadCompressedUInt();
            WordCounts = MemoryChunk.GetArrayAligned<ulong>(WordCount);
            stream.ReadCompressed(WordCounts, WordCount);

            MemoryChunk.Lock();
        }
    }

    public sealed class CompressedSparseRowGraph
    {
        internal CompressedSparseRowGraph(int rootNodeIndex, uint[] firstChildEdgeIndex, int[] edgeToNodeIndex, char[] edgeCharacter, ushort[] reachableTerminalNodes, ulong[] data, Dictionary<string, ulong> wordCounts)
            : this(rootNodeIndex, firstChildEdgeIndex, edgeToNodeIndex, edgeCharacter, reachableTerminalNodes, data)
        {
            AssignCounts(RootNodeIndex, new char[100], 0, -1, wordCounts);
        }

        private CompressedSparseRowGraph(int rootNodeIndex, uint[] firstChildEdgeIndex, int[] edgeToNodeIndex, char[] edgeCharacter, ushort[] reachableTerminalNodes, ulong[] data)
        {
            RootNodeIndex = rootNodeIndex;
            FirstChildEdgeIndex = firstChildEdgeIndex;
            EdgeToNodeIndex = edgeToNodeIndex;
            EdgeCharacter = edgeCharacter;
            ReachableTerminalNodes = reachableTerminalNodes;
            WordCounts = data;
        }

        internal readonly int RootNodeIndex;
        internal readonly uint[] FirstChildEdgeIndex;
        internal readonly int[] EdgeToNodeIndex;
        internal readonly char[] EdgeCharacter;
        internal readonly ushort[] ReachableTerminalNodes;
        internal readonly ulong[] WordCounts;

        private int AssignCounts(int node, char[] builder, int builderLength, int reachableCount, Dictionary<string, ulong> counts)
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
#if DEBUG
                var realReachable = ReachableTerminalNodes[Math.Abs(nextNode)];
                Debug.Assert(realReachable == childReachable - reachableCount);
#endif
                reachableCount = childReachable;
            }

            return reachableCount;
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

            var requiredLength = LargePageMemoryChunk.Builder()
                .ReserveAligned(FirstChildEdgeIndex)
                .ReserveAligned(EdgeToNodeIndex)
                .ReserveAligned(EdgeCharacter)
                .ReserveAligned(ReachableTerminalNodes)
                .ReserveAligned(WordCounts)
                .AllocationSize;

            using var stream = File.OpenWrite(path);
            if (stream == null)
            {
                throw new InvalidOperationException();
            }

            stream.WriteCompressed(requiredLength);
            stream.WriteCompressed(RootNodeIndex);
            stream.WriteSequentialCompressedToUshort(FirstChildEdgeIndex);
            stream.WriteCompressed(EdgeToNodeIndex);
            stream.WriteUtf8(EdgeCharacter);
            stream.WriteCompressed(ReachableTerminalNodes);
            stream.WriteCompressed(WordCounts);
        }
    }
}