using Portent;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace PerformanceTesting
{
    public sealed class Dawg19 : IDawg
    {
        private readonly CompressedSparseRowGraph _graph;

        public Dawg19(Stream stream)
            : this(new CompressedSparseRowGraph(stream))
        {
        }

        public Dawg19(CompressedSparseRowGraph graph)
        {
            _graph = graph;
            WordCount = (uint)graph.WordCounts.Length;
        }

        public uint WordCount { get; }

        private readonly struct ClosureVariable
        {
            public ClosureVariable(string word, uint maxEdits, CompressedSparseRowGraph graph, int[][] matrix, StringBuilder builder, List<SuggestItem> results) : this()
            {
                this.word = word;
                this.maxEdits = (int)maxEdits;
                this.matrix = matrix;
                this.builder = builder;
                this.results = results;

                EdgeCharacters = graph.EdgeCharacters;
                FirstChildEdgeIndex = graph.FirstChildEdgeIndex;
                EdgeToNodeIndex = graph.EdgeToNodeIndex;
            }

            public readonly string word;
            public readonly int maxEdits;
            public readonly int[][] matrix;
            public readonly StringBuilder builder;
            public readonly List<SuggestItem> results;

            public readonly char[] EdgeCharacters;
            public readonly uint[] FirstChildEdgeIndex;
            public readonly int[] EdgeToNodeIndex;
        }

        public IEnumerable<SuggestItem> Lookup(string word, uint maxEdits)
        {
            var builder = new StringBuilder(word.Length + (int)maxEdits);
            builder.Append(new string(' ', word.Length + (int)maxEdits));
            var results = new List<SuggestItem>();

            var matrix = new int[word.Length + maxEdits + 1][];
            for (var i = 0; i < matrix.Length; i++)
            {
                matrix[i] = new int[word.Length + 1];
                matrix[i][0] = i - (int)maxEdits;

                var stripeEnd = i + maxEdits + 1;
                if (stripeEnd <= word.Length)
                {
                    matrix[i][stripeEnd] = 0;
                }
            }

            for (var i = 0; i < matrix[0].Length; i++)
            {
                matrix[0][i] = i - (int)maxEdits;
            }

            var closure = new ClosureVariable(word, maxEdits, _graph, matrix, builder, results);
            Recurse(_graph.RootNodeIndex, 0, ref closure);
            return results;
        }

        private static void Recurse(int currentNode, int depth, ref ClosureVariable closure)
        {
            if (depth == closure.word.Length + closure.maxEdits)
            {
                return;
            }

            var fIndex = closure.FirstChildEdgeIndex;
            var firstChild = fIndex[currentNode];
            var lastChild = fIndex[currentNode + 1];

            var from = depth - closure.maxEdits;
            if (from < 0)
            {
                from = 0;
            }

            from++;

            var to = (long)Math.Min(closure.word.Length + 1, depth + closure.maxEdits + 2);
            var previousCharacter = depth > 0 ? closure.builder[depth - 1] : (char)0;

            var previousRow = closure.matrix[depth];
            ++depth;
            var currentRow = closure.matrix[depth];

            for (var childEdge = firstChild; childEdge < lastChild; childEdge++)
            {
                var any = false;
                var currentCharacter = closure.EdgeCharacters[childEdge];
                closure.builder[depth - 1] = currentCharacter;
                var calculatedCost = depth;
                var previousRowEntry = previousRow[from - 1];
                var targetCharacter = (char)0;
                for (var i = from; i < to; i++)
                {
                    var previousTargetCharacter = targetCharacter;
                    targetCharacter = closure.word[i - 1];

                    var previousRowPreviousEntry = previousRowEntry;
                    previousRowEntry = previousRow[i];

                    if (currentCharacter == targetCharacter)
                    {
                        calculatedCost = previousRowPreviousEntry;
                    }
                    else
                    {
                        if (previousRowEntry < calculatedCost)
                        {
                            calculatedCost = previousRowEntry;
                        }

                        if (targetCharacter == previousCharacter
                            && previousTargetCharacter == currentCharacter)
                        {
                            previousRowPreviousEntry = closure.matrix[depth - 2][i - 2];
                        }

                        if (previousRowPreviousEntry < calculatedCost)
                        {
                            calculatedCost = previousRowPreviousEntry;
                        }

                        calculatedCost++;
                    }

                    if (calculatedCost <= 0)
                    {
                        any = true;
                    }

                    currentRow[i] = calculatedCost;
                }

                if (!any)
                {
                    continue;
                }

                var nextNode = closure.EdgeToNodeIndex[childEdge];
                if (nextNode < 0)
                {
                    nextNode = -nextNode;
                    if (depth >= closure.word.Length - closure.maxEdits
                        && calculatedCost <= 0)
                    {
                        closure.results.Add(new SuggestItem(closure.builder.ToString(0, depth), 0));
                    }
                }

                Recurse(nextNode, depth, ref closure);
            }
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
