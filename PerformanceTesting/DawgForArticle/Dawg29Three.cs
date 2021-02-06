using Portent;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace PerformanceTesting
{
    public sealed unsafe class Dawg29Three : IDawg
    {
        private readonly PointerGraph _graph;

        public Dawg29Three(Stream stream)
            : this(new CompressedSparseRowGraph(stream))
        { }

        public Dawg29Three(CompressedSparseRowGraph graph) : this(new PointerGraph(graph))
        { }

        public Dawg29Three(PointerGraph graph)
        {
            _graph = graph;
            WordCount = (uint)graph.WordCounts.Length;
        }

        public uint WordCount { get; }

        //[StructLayout(LayoutKind.Explicit)]
        private struct ClosureVariable
        {
            public ClosureVariable(char* word, int wordLength, int maxEdits, PointerGraph graph, char* builder, List<SuggestItem> results) : this()
            {
                this.word = word;
                this.wordLength = wordLength;
                this.maxEdits = maxEdits;
                this.builder = builder;
                this.results = results;

                EdgeCharacters = graph.EdgeCharacters;
                FirstChildEdgeIndex = graph.FirstChildEdgeIndex;
                EdgeToNodeIndex = graph.EdgeToNodeIndex;

                depth = 0;
            }

            //[FieldOffset(0x00)]
            public readonly char* word;
            //[FieldOffset(0x08)]
            public readonly long wordLength;
            //[FieldOffset(0x10)]
            public readonly long maxEdits;
            //[FieldOffset(0x18)]
            public long depth;
            //[FieldOffset(0x28)]
            public readonly char* EdgeCharacters;
            //[FieldOffset(0x30)]
            public readonly uint* FirstChildEdgeIndex;
            //[FieldOffset(0x38)]
            public readonly int* EdgeToNodeIndex;
            //[FieldOffset(0x20)]
            public readonly char* builder;

            //[FieldOffset(0x40)]
            public readonly List<SuggestItem> results;
        }

        public IEnumerable<SuggestItem> Lookup(string word, uint maxEdits)
        {
            var builderLength = word.Length + (int)maxEdits + 1;
            var builder = stackalloc char[builderLength];
            for (var i = 0; i < builderLength; i++)
            {
                builder[i] = (char)0;
            }

            builder++;

            var results = new List<SuggestItem>();

            var rowLength = word.Length + 1;
            var rowCount = rowLength + (int)maxEdits;
            var matrix = stackalloc int[rowLength * rowCount];
            var mp1 = (int)maxEdits + 1;
            for (var i = 0; i < rowCount; i++)
            {

                matrix[i * rowLength] = i - mp1;

                var stripeEnd = i + mp1;
                if (stripeEnd <= word.Length)
                {
                    matrix[i * rowLength + stripeEnd] = -1;
                }
            }

            for (var i = 0; i < rowLength; i++)
            {
                matrix[i] = i - (int)maxEdits - 1;
            }

            var wordCopy = stackalloc char[word.Length];
            for (var i = 0; i < word.Length; i++)
            {
                wordCopy[i] = word[i];
            }

            var closure = new ClosureVariable(wordCopy - 1, word.Length, (int)maxEdits, _graph, builder, results);
            Recurse(_graph.RootNodeIndex, matrix, 1, ref closure);
            return results;
        }

        private static void Recurse(int currentNode, int* previousRow, long from, ref ClosureVariable closure)
        {
            var depth = closure.depth;
            var rowLength = closure.wordLength + 1;
            from -= (closure.maxEdits - depth) >> 63;
            var to = depth + closure.maxEdits + 2;
            if (rowLength < to)
            {
                to = rowLength;
            }

            if (from >= to)
            {
                goto end;
            }

            var currentRow = previousRow + rowLength;
            var builderPosition = closure.builder + depth;
            closure.depth = depth + 1;

            var fIndex = closure.FirstChildEdgeIndex + currentNode;
            var firstChild = *fIndex;
            var lastChild = *(fIndex + 1);
            if (firstChild >= lastChild)
            {
                goto end2;
            }

            do
            {
                var currentCharacter = closure.EdgeCharacters[firstChild];
                *builderPosition = currentCharacter;
                var previousRowEntry = previousRow[from - 1];
                var any = 0;
                var calculatedCost = 0;
                var targetCharacter = (char)0;
                for (var i = from; i < to; i++)
                {
                    var previousTargetCharacter = targetCharacter;
                    targetCharacter = closure.word[i];
                    if (currentCharacter == targetCharacter)
                    {
                        calculatedCost = previousRowEntry;
                        previousRowEntry = previousRow[i];
                    }
                    else
                    {
                        if (previousTargetCharacter == currentCharacter
                            && targetCharacter == *(builderPosition - 1))
                        {
                            previousRowEntry = previousRow[i - closure.wordLength - 3];
                        }

                        if (previousRowEntry < calculatedCost)
                        {
                            calculatedCost = previousRowEntry;
                        }

                        previousRowEntry = previousRow[i];
                        if (previousRowEntry < calculatedCost)
                        {
                            calculatedCost = previousRowEntry;
                        }

                        calculatedCost++;
                    }

                    any |= calculatedCost;

                    currentRow[i] = calculatedCost;
                }

                if (any >= 0)
                {
                    continue;
                }

                var nextNode = closure.EdgeToNodeIndex[firstChild];
                if (nextNode < 0)
                {
                    if (calculatedCost < 0
                        && (int)to > (int)closure.wordLength)
                    {
                        Add(ref closure);
                        nextNode = closure.EdgeToNodeIndex[firstChild];
                    }
                    nextNode = -nextNode;
                }

                if ((int)from < (int)closure.wordLength)
                {
                    var newFrom = from;
                    while (currentRow[newFrom] >= 0)
                    {
                        newFrom += 1;
                    }

                    Recurse(nextNode, currentRow, newFrom, ref closure);
                }
            } while (++firstChild < lastChild);

            end2:
            closure.depth -= 1;
            end:;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Add(ref ClosureVariable closure)
        {
            var str = new string(closure.builder, 0, (int)closure.depth);
            var si = new SuggestItem(str, 0);
            closure.results.Add(si);
        }

        public int GetIndex(in string word)
        {
            var number = -1;
            var currentNode = _graph.RootNodeIndex;

            for (var w = 0; w < word.Length; w++)
            {
                var target = word[w];
                var temp = Math.Abs(currentNode);
                var firstChild = _graph.FirstChildEdgeIndex[temp];
                var lastChild = _graph.FirstChildEdgeIndex[temp + 1];

                var matched = false;
                for (var edgeToChild = firstChild; edgeToChild < lastChild; edgeToChild++)
                {
                    var nextNode = _graph.EdgeToNodeIndex[edgeToChild];
                    if (_graph.EdgeCharacters[edgeToChild] != target)
                    {
                        number += _graph.ReachableTerminalNodes[Math.Abs(nextNode)];
                        continue;
                    }

                    currentNode = nextNode;
                    if (currentNode < 0)
                    {
                        ++number;
                    }

                    matched = true;
                    break;
                }

                if (!matched)
                {
                    return -1;
                }
            }

            Debug.Assert(currentNode < 0);

            if (currentNode < 0)
            {
                return number;
            }

            return -1;
        }

        public string GetWord(int index)
        {
            if (index < 0)
            {
                throw new ArgumentException("Index was outside the bounds of the array. " +
                                            $"Index must be greater than or equal to 0 but was: {index}.", nameof(index));
            }

            if (index > WordCount)
            {
                throw new ArgumentException("Index was outside the bounds of the array. " +
                                            $"Index must be less than the number of elements ({WordCount}) but was: {index}. " +
                                            $"Use {nameof(Dawg)}.{nameof(WordCount)} to check bounds.", nameof(index));
            }

            var builder = new StringBuilder();

            // Because we expect a 0-indexed input
            // but the "index" of the first word is 1 because it is terminal.
            index++;

            var currentNode = _graph.RootNodeIndex;
            while (index > 0)
            {
                var firstChildEdge = _graph.FirstChildEdgeIndex[currentNode];
                var lastChildEdge = _graph.FirstChildEdgeIndex[currentNode + 1];
                for (var i = firstChildEdge; i < lastChildEdge; i++)
                {
                    var nextNode = _graph.EdgeToNodeIndex[i];
                    var nextNumber = _graph.ReachableTerminalNodes[Math.Abs(nextNode)];
                    if (nextNumber < index)
                    {
                        index -= nextNumber;
                        continue;
                    }


                    currentNode = nextNode;
                    if (currentNode < 0)
                    {
                        index--;
                        currentNode = -currentNode;
                    }

                    builder.Append(_graph.EdgeCharacters[i]);
                    break;
                }
            }

            return builder.ToString();
        }
    }
}
