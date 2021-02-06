using Portent;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace PerformanceTesting
{
    public sealed unsafe class Dawg26 : IDawg
    {
        private readonly PointerGraph _graph;

        public Dawg26(Stream stream)
            : this(new CompressedSparseRowGraph(stream))
        { }

        public Dawg26(CompressedSparseRowGraph graph) : this(new PointerGraph(graph))
        { }

        public Dawg26(PointerGraph graph)
        {
            _graph = graph;
            WordCount = (uint)graph.WordCounts.Length;
        }

        public uint WordCount { get; }

        private readonly struct ClosureVariable
        {
            public ClosureVariable(char* word, int wordLength, int maxEdits, PointerGraph graph, int* matrix, char* builder, List<SuggestItem> results) : this()
            {
                this.word = word;
                this.wordLength = wordLength;
                this.maxEdits = maxEdits;
                this.matrix = matrix;
                this.builder = builder;
                this.results = results;

                EdgeCharacters = graph.EdgeCharacters;
                FirstChildEdgeIndex = graph.FirstChildEdgeIndex;
                EdgeToNodeIndex = graph.EdgeToNodeIndex;
            }

            public readonly char* word;
            public readonly int wordLength;
            public readonly long maxEdits;
            public readonly int* matrix;
            public readonly char* builder;
            public readonly List<SuggestItem> results;

            public readonly char* EdgeCharacters;
            public readonly uint* FirstChildEdgeIndex;
            public readonly int* EdgeToNodeIndex;
        }

        public IEnumerable<SuggestItem> Lookup(string word, uint maxEdits)
        {
            var builderLength = word.Length + (int)maxEdits + 1;
            var builder = stackalloc char[builderLength];
            for (var i = 0; i < builderLength; i++)
            {
                builder[i] = ' ';
            }

            builder++;

            var results = new List<SuggestItem>();

            var rowLength = word.Length + 1;
            var rowCount = rowLength + (int)maxEdits;
            var matrix = stackalloc int[rowLength * rowCount];
            for (var i = 0; i < rowCount; i++)
            {

                matrix[i * rowLength] = i - (int)maxEdits - 1;

                var stripeEnd = i + maxEdits + 1;
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

            var closure = new ClosureVariable(wordCopy - 1, word.Length, (int)maxEdits, _graph, matrix, builder, results);
            Recurse(_graph.RootNodeIndex, 0, ref closure);
            return results;
        }

        private static void Recurse(int currentNode, long depth, ref ClosureVariable closure)
        {
            if ((int)depth == closure.wordLength + (int)closure.maxEdits)
            {
                goto end;
            }

            var fIndex = closure.FirstChildEdgeIndex + currentNode;
            var firstChild = *fIndex;
            var lastChild = *(fIndex + 1);
            if (firstChild >= lastChild)
            {
                goto end;
            }

            var from = depth - closure.maxEdits;
            from &= ~from >> 31;

            from++;

            var rowLength = (long)closure.wordLength + 1;
            var to = depth + closure.maxEdits + 2;
            if (rowLength < to)
            {
                to = rowLength;
            }
            var previousRow = closure.matrix + depth * rowLength;
            var currentRow = previousRow + rowLength;
            var builderPosition = closure.builder + depth;
            ++depth;

            do
            {
                var any = 0;
                var currentCharacter = closure.EdgeCharacters[firstChild];
                *builderPosition = currentCharacter;
                var calculatedCost = 0;
                var previousRowEntry = previousRow[from - 1];
                var targetCharacter = (char) 0;
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
                    nextNode = -nextNode;
                    if ((int) to > closure.wordLength
                        && calculatedCost < 0)
                    {
                        var str = new string(closure.builder, 0, (int) depth);
                        var si = new SuggestItem(str, 0);
                        closure.results.Add(si);
                    }
                }

                Recurse(nextNode, depth, ref closure);
            } while (++firstChild < lastChild);

            end:;
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
