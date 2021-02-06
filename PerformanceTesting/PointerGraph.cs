using Portent;
using System;
using System.Runtime.InteropServices;

namespace PerformanceTesting
{
    public sealed unsafe class PointerGraph : IDisposable
    {
        public readonly int RootNodeIndex;
        public readonly uint* FirstChildEdgeIndex;
        public readonly int* EdgeToNodeIndex;
        public readonly char* EdgeCharacters;
        public readonly ushort[] ReachableTerminalNodes;
        public readonly uint WordCount;
        public readonly ulong[] WordCounts;

        public readonly uint[] FirstChildEdgeIndexArray;
        public readonly int[] EdgeToNodeIndexArray;
        public readonly char[] EdgeCharactersArray;
        private readonly GCHandle FirstChildHandle;
        private readonly GCHandle EdgeToHandle;
        private readonly GCHandle EdgeCharacterHandle;

        public PointerGraph(CompressedSparseRowGraph graph)
        {
            FirstChildEdgeIndexArray = graph.FirstChildEdgeIndex;
            FirstChildHandle = GCHandle.Alloc(FirstChildEdgeIndexArray, GCHandleType.Pinned);
            FirstChildEdgeIndex = (uint*)FirstChildHandle.AddrOfPinnedObject();

            EdgeToNodeIndexArray = graph.EdgeToNodeIndex;
            EdgeToHandle = GCHandle.Alloc(EdgeToNodeIndexArray, GCHandleType.Pinned);
            EdgeToNodeIndex = (int*)EdgeToHandle.AddrOfPinnedObject();

            EdgeCharactersArray = graph.EdgeCharacters;
            EdgeCharacterHandle = GCHandle.Alloc(EdgeCharactersArray, GCHandleType.Pinned);
            EdgeCharacters = (char*)EdgeCharacterHandle.AddrOfPinnedObject();

            RootNodeIndex = graph.RootNodeIndex;
            ReachableTerminalNodes = graph.ReachableTerminalNodes;
            WordCount = (uint)graph.WordCounts.Length;
            WordCounts = graph.WordCounts;
        }

        public void Dispose()
        {
            FirstChildHandle.Free();
            EdgeToHandle.Free();
            EdgeCharacterHandle.Free();
        }
    }
}
