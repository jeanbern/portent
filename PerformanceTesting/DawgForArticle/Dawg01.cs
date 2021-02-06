using Portent;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PerformanceTesting
{
    public sealed class Dawg01 : IDawg
    {
        private readonly CompressedSparseRowGraph _graph;

        public Dawg01(Stream stream)
            : this(new CompressedSparseRowGraph(stream))
        {
        }

        public Dawg01(CompressedSparseRowGraph graph)
        {
            _graph = graph;
            WordCount = (uint)graph.WordCounts.Length;
        }

        public uint WordCount { get; }

        public IEnumerable<SuggestItem> Lookup(string word, uint maxEdits)
        {
            var builder = new StringBuilder(word.Length + (int)maxEdits);
            builder.Append(new string(' ', word.Length + (int)maxEdits));
            var results = new List<SuggestItem>();

            var matrix = new int[word.Length + maxEdits + 1][];
            for(var i = 0; i < matrix.Length; i++)
            {
                matrix[i] = new int[word.Length + 1];
                matrix[i][0] = i;
            }

            for(var i = 0; i < matrix[0].Length; i++)
            {
                matrix[0][i] = i;
            }

            Recurse(_graph.RootNodeIndex, 0);
            return results;

            void Recurse(int currentNode, int depth)
            {
                if (depth == word.Length + maxEdits)
                {
                    return;
                }

                var firstChild = _graph.FirstChildEdgeIndex[currentNode];
                var lastChild = _graph.FirstChildEdgeIndex[currentNode + 1];

                for (var childEdge = firstChild; childEdge < lastChild; childEdge++)
                {
                    var currentCharacter = _graph.EdgeCharacters[childEdge];
                    builder[depth] = currentCharacter;
                    for (var i = 1; i < matrix[depth].Length; i++)
                    {
                        var targetCharacter = word[i - 1];
                        var cost = 1;
                        if (currentCharacter == targetCharacter)
                        {
                            cost = 0;
                        }

                        matrix[depth + 1][i] = Min(
                            matrix[depth][i] + 1,
                            matrix[depth + 1][i - 1] + 1,
                            matrix[depth][i - 1] + cost);

                        if (depth > 0 && i > 1 
                            && word[i-1] == builder[depth - 1]
                            && word[i-2] == builder[depth])
                        {
                            matrix[depth + 1][i] = Math.Min(
                                matrix[depth + 1][i],
                                matrix[depth - 1][i - 2] + 1);
                        }
                    }

                    var nextNode = _graph.EdgeToNodeIndex[childEdge];
                    if (nextNode < 0 && matrix[depth + 1][word.Length] <= maxEdits)
                    {
                        results.Add(new SuggestItem(builder.ToString(0, depth + 1), 0));
                    }

                    Recurse(Math.Abs(nextNode), depth + 1);
                }
            }

            static int Min(int a, int b, int c)
            {
                return Math.Min(a, Math.Min(b, c));
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
