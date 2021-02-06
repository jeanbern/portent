using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime;

namespace Portent
{
    public sealed class CompressedSparseRowGraph : IDisposable
    {
        public CompressedSparseRowGraph(int rootNodeIndex, uint[] firstChildEdgeIndex, int[] edgeToNodeIndex, char[] edgeCharacters, ushort[] reachableTerminalNodes, Dictionary<string, ulong> wordCounts)
        {
            RootNodeIndex = rootNodeIndex;
            FirstChildEdgeIndex = firstChildEdgeIndex;
            EdgeToNodeIndex = edgeToNodeIndex;
            EdgeCharacters = edgeCharacters;
            ReachableTerminalNodes = reachableTerminalNodes;
            WordCounts = new ulong[wordCounts.Count];
            EdgeWeights = new float[edgeToNodeIndex.Length];
            DictionaryCounts = wordCounts;

            var reachableCount = AssignWordCounts(RootNodeIndex, new char[100], 0, -1) + 1;
            if (reachableCount != wordCounts.Count)
            {
                throw new ArgumentException($"{nameof(wordCounts)} contained {wordCounts.Count} entries, but there were only {reachableCount} assignments.");
            }

            var first = FirstChildEdgeIndex[rootNodeIndex];
            var last = FirstChildEdgeIndex[rootNodeIndex + 1];

            foreach (var wordCount in wordCounts)
            {
                var word = wordCount.Key;
                var count = wordCount.Value;
                var target = word[0];
                for (var i = first; i < last; i++)
                {
                    if (EdgeCharacters[i] == target)
                    {
                        AssignEdgeWeights(i, word, 1, count);
                        break;
                    }
                }
            }
        }

        public readonly int RootNodeIndex;
        public readonly uint[] FirstChildEdgeIndex;
        public readonly int[] EdgeToNodeIndex;
        public readonly char[] EdgeCharacters;
        public readonly ushort[] ReachableTerminalNodes;
        public readonly ulong[] WordCounts;

        public readonly float[] EdgeWeights;

        public readonly Dictionary<string, ulong> DictionaryCounts;

        private void AssignEdgeWeights(uint edge, string word, int wordIndex, ulong wordCount)
        {
            EdgeWeights[edge] += wordCount;
            if (wordIndex == word.Length)
            {
                return;
            }

            var nextNode = Math.Abs(EdgeToNodeIndex[edge]);
            var nextChar = word[wordIndex];
            var first = FirstChildEdgeIndex[nextNode];
            var last = FirstChildEdgeIndex[nextNode + 1];
            for (var i = first; i < last; i++)
            {
                if (EdgeCharacters[i] == nextChar)
                {
                    AssignEdgeWeights(i, word, wordIndex + 1, wordCount);
                }
            }
        }

        private int AssignWordCounts(int node, char[] builder, int builderLength, int reachableCount)
        {
            if (node < 0)
            {
                node = -node;
                ++reachableCount;

                var word = new string(builder, 0, builderLength);
                WordCounts[reachableCount] = DictionaryCounts[word];
            }

            var i = FirstChildEdgeIndex[node];
            var last = FirstChildEdgeIndex[node + 1];
            for (; i < last; ++i)
            {
                builder[builderLength] = EdgeCharacters[i];
                var nextNode = EdgeToNodeIndex[i];

                reachableCount = AssignWordCounts(nextNode, builder, builderLength + 1, reachableCount);
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
                .ReserveAligned(EdgeCharacters)
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
            stream.WriteUtf8(EdgeCharacters);
            stream.WriteCompressed(ReachableTerminalNodes);
            stream.WriteCompressed(WordCounts);
        }

        public CompressedSparseRowGraph(Stream stream)
        {
            stream.ReadCompressedULong();
            RootNodeIndex = stream.ReadCompressedInt();

            var firstChildEdgeIndexCount = stream.ReadCompressedUInt();
            FirstChildEdgeIndex = new uint[firstChildEdgeIndexCount];
            stream.ReadSequentialCompressedUshortToUint(FirstChildEdgeIndex);

            var edgeToNodeIndexCount = stream.ReadCompressedUInt();
            EdgeToNodeIndex = new int[edgeToNodeIndexCount];
            stream.ReadCompressed(EdgeToNodeIndex);

            var byteLength = stream.ReadCompressedUInt();
            var charLength = stream.ReadCompressedUInt();
            EdgeCharacters = new char[charLength];
            stream.ReadUtf8(EdgeCharacters, byteLength);

            var reachableTerminalNodesCount = stream.ReadCompressedUInt();
            ReachableTerminalNodes = new ushort[reachableTerminalNodesCount];
            stream.ReadCompressed(ReachableTerminalNodes);

            var wordCount = stream.ReadCompressedUInt();
            WordCounts = new ulong[wordCount];
            stream.ReadCompressed(WordCounts);

            EdgeWeights = Array.Empty<float>();
            DictionaryCounts = new Dictionary<string, ulong>();
        }

        public void Dispose()
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
#pragma warning disable S1215 // "GC.Collect" should not be called
            GC.Collect();
#pragma warning restore S1215 // "GC.Collect" should not be called
        }
    }
}