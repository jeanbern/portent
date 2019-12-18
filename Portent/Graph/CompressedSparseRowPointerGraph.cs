#if DEBUG
using System;
using System.Diagnostics;
#endif
using System.IO;

namespace Portent
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

#if DEBUG
            for (var i = 0; i < firstChildEdgeIndexCount; i++)
            {
                Debug.Assert(FirstChildEdgeIndex[i] <= edgeToNodeIndexCount, $"{nameof(FirstChildEdgeIndex)} should not point past {edgeToNodeIndexCount}, but it's value at index {i} was {FirstChildEdgeIndex[i]}");
            }
#endif

            EdgeToNodeIndex = MemoryChunk.GetArrayAligned<int>(edgeToNodeIndexCount);
            stream.ReadCompressed(EdgeToNodeIndex, edgeToNodeIndexCount);

#if DEBUG
            for (var i = 0; i < edgeToNodeIndexCount; i++)
            {
                Debug.Assert(Math.Abs(EdgeToNodeIndex[i]) <= firstChildEdgeIndexCount, $"{nameof(EdgeToNodeIndex)} should not point past {firstChildEdgeIndexCount}, but it's value at index {i} was {EdgeToNodeIndex[i]}");
            }

#endif
            var byteLength = stream.ReadCompressedUInt();
            var charLength = stream.ReadCompressedUInt();
            EdgeCharacter = MemoryChunk.GetArrayAligned<char>(charLength);
            stream.ReadUtf8(EdgeCharacter, byteLength, charLength);

            var reachableTerminalNodesCount = stream.ReadCompressedUInt();
            ReachableTerminalNodes = MemoryChunk.GetArrayAligned<ushort>(reachableTerminalNodesCount);
            stream.ReadCompressed(ReachableTerminalNodes, reachableTerminalNodesCount);

            WordCount = stream.ReadCompressedUInt();
            WordCounts = MemoryChunk.GetArrayAligned<ulong>(WordCount);
            stream.ReadCompressed(WordCounts, WordCount);

            MemoryChunk.Lock();
        }
    }
}