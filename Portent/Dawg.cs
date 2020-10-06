using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
#if INTRINSICS
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
#endif
using System.Threading.Tasks;

// ReSharper disable BuiltInTypeReferenceStyle

// Use nint when referring to pointer values, long when referring to 64 bit values.
using nint = System.Int64;
using nuint = System.UInt64;
// ReSharper disable SuggestVarOrType_BuiltInTypes
// ReSharper disable SuggestVarOrType_Elsewhere

namespace Portent
{
    public sealed unsafe class Dawg : IDisposable
    {
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public IEnumerable<SuggestItem> Lookup(in string word, uint maxEdits)
        {
            if (maxEdits == 0)
            {
                var index = GetIndex(word);
                if (index < 0)
                {
                    return Enumerable.Empty<SuggestItem>();
                }

                _singleWordResult.Update(word, _jobs[0]._graph._wordCounts[index]);
                return _singleWordResult;
            }

            _compoundResultCollection.Clear();
            var wordLength = (uint) word.Length;

#pragma warning disable S1135 // Track uses of "TODO" tags
// TODO: Align these allocated chunks together.
            // + 1 for the (char)0 at the start.
            var inputLength = MemoryAlignmentHelper.GetCacheAlignedSize<char>(wordLength + 1);
#pragma warning restore S1135 // Track uses of "TODO" tags
            var inputBytes = stackalloc byte[inputLength];
            var input = MemoryAlignmentHelper.GetCacheAlignedStart<char>(inputBytes);
            *input = (char) 0;
            input++;

            // TODO: Use some kind of MemCopy or Unsafe.CopyBlock
            for (var x = 0; x < word.Length; ++x)
            {
                input[x] = word[x];
            }

            var maxDepth = wordLength + maxEdits;
            var toCacheLength = MemoryAlignmentHelper.GetCacheAlignedSize<uint>(maxDepth);
            var toCacheBytes = stackalloc byte[toCacheLength];
            var toCache = MemoryAlignmentHelper.GetCacheAlignedStart<uint>(toCacheBytes);

            // ReSharper disable once ArrangeRedundantParentheses
            var minTo = Math.Min(wordLength, (2 * maxEdits) + 1);
            for (var depth = 0u; depth < maxDepth; ++depth)
            {
                toCache[depth] = Math.Min(Math.Min(depth + 1 + maxEdits, wordLength + maxEdits - depth), minTo);
            }

            var rootFirst = _rootFirstChild;
            var rootLast = _rootLastChild;

            var maxPlusOne = maxEdits + 1;
            var run = stackalloc Run[1];
            run[0] = new Run(input, toCache, wordLength, maxPlusOne);
            // TODO: There's got to be a simpler way of doing this. Not a high priority though.
            var hasRun = stackalloc bool[(int)(rootLast -  rootFirst)];
            for (nint i = 0; i < rootLast - rootFirst; i++)
            {
                hasRun[i] = false;
            }

            var tasks = _tasks;

            // The corrected word must align within the first maxPlusOne characters of input.
            var end = (nint) Math.Min(wordLength, maxPlusOne);

            for (nint i = 0; i < end; i++)
            {
                if (!_characterIndex.TryGetValue(input[i], out var index))
                {
                    continue;
                }

                var position = index - rootFirst;
                // TODO: duplicate code from here to end of loop. Matches the next loop.
                if (hasRun[position])
                {
                    continue;
                }

                hasRun[position] = true;

                var job = _jobs[position];
                job._run = run;
                tasks[position] = Task.Run(job.DoWork());
            }

            for (nint position = 0; position < rootLast - rootFirst; ++position)
            {
                if (hasRun[position])
                {
                    continue;
                }

                var job = _jobs[position];
                job._run = run;
                tasks[position] = Task.Run(job.DoWork());
            }

            Task.WaitAll(tasks);
            return _compoundResultCollection;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        [SuppressMessage("Major Code Smell", "S907:\"goto\" statement should not be used")]
        private static void Search5(Job me)
        {
            Run* run = me._run;
            uint wordLength = run->_wordLength;

            Search3Closure closureArg;

            closureArg._first = run->_input;
            closureArg._toCache = run->_toCache;
            nint maxPlus1 = run->_maxPlusOne;
            closureArg._maxPlusOne = (uint) maxPlus1;
            closureArg._graph = me._graph;
            closureArg._results = me._results;
            closureArg._wordLength = wordLength;
            closureArg._builderDepth = 0;

            const uint lineSize = 64;
            var added = wordLength + (uint)maxPlus1;
            var bytesRequiredForBuilder = added * (uint)Unsafe.SizeOf<char>();
            var bytesPerStripe = 2* (uint)Unsafe.SizeOf<int>() * (uint)maxPlus1;
            var bytesUsedInLastLine = bytesRequiredForBuilder % lineSize;
            var bytesRequiredForMatrix = (added + 1) * bytesPerStripe;
            var bytesBuilderToMatrixPadding = (lineSize - bytesUsedInLastLine) % bytesPerStripe;
            var bytesForBuilderAndPadding = bytesRequiredForBuilder + bytesBuilderToMatrixPadding;
            var bytesRequiredTotal = bytesForBuilderAndPadding + bytesRequiredForMatrix + lineSize - 1;

            var allBytes = stackalloc byte[(int)bytesRequiredTotal];

            var builderStart = MemoryAlignmentHelper.GetCacheAlignedStart<char>(allBytes);
            *builderStart = (char)0;
            builderStart++;

            nint edge = me._edge;
            closureArg._node = closureArg._graph._edgeToNodeIndex[edge];
            *builderStart = closureArg._graph._edgeCharacters[edge];
            closureArg._builderStart = builderStart;

            // The real stripeWidth is 2*max + 1
            // Normally we would do a bound check and calculate a value for the cell in previousRow directly after our stripe.
            // Instead we pre-assign the cell when we have the values handy and then ignore bound checking.
            var twiceMaxPlusOne = 2 * maxPlus1;
            var editMatrix = (uint*) ((byte*) builderStart + bytesForBuilderAndPadding) + twiceMaxPlusOne;

            // Row 0 doesn't represent a character.
            // Row 1 is the first character and so on...
            // And we sneak in an extra row before row0, to ensure that the transposition row offset has room.
            closureArg._transpositionRow = (uint*) 0;
            closureArg._row0 = editMatrix;
            editMatrix--;
            nint x = 0;
            nint diagonal = 0;
            editMatrix[maxPlus1] = (uint) maxPlus1;
            do
            {
                editMatrix[diagonal] = (uint) x;
                editMatrix[x] = (uint) x;
                ++x;
                diagonal += twiceMaxPlusOne;
            } while (x < maxPlus1);

            static void MatchCharacter(ref Search3Closure closure, nuint skip)
            {
                nuint builderDepth = closure._builderDepth;
                uint* previousRow = closure._row0;
                uint mp1 = closure._maxPlusOne;

                // This is the value for the column directly before our diagonal stripe.
                // For the early rows, it's the 0'th column. After that, the value is > maxPlusOne anyways.
                // TODO: Potentially a place to cut down on an addition or two, if it fits in the branching done above.
                uint currentRowPreviousColumn = (uint) (builderDepth + skip + 1);
                nint wordArrayOffset = (nint) builderDepth - mp1 + 1;
                mp1 <<= 1;
                uint* currentRow = previousRow + mp1;
                uint* transpositionRow = previousRow - mp1 - 2;
                uint t2 = mp1 - 1;
                char* firstWithOffset = closure._first;
                uint to = closure._wordLength;

                uint previousRowPreviousColumn;

                // Very predictable branching
                if (wordArrayOffset > 0)
                {
                    // Normally the strip would travel diagonally through the matrix. We shift it left to keep it starting at 0.
                    firstWithOffset += wordArrayOffset;
                    to -= (uint) wordArrayOffset;

                    if (wordArrayOffset > 1)
                    {
                        transpositionRow+=2;
                    }
                    else
                    {
                        //TODO: Test alternative: move this outside of the conditional, and change the previous addition to +1?
                        transpositionRow++;
                    }

                    previousRowPreviousColumn = previousRow[skip];
                    previousRow++;
                }
                else
                {
                    t2 += (uint) wordArrayOffset;
                    previousRowPreviousColumn = currentRowPreviousColumn - 1;
                }

                // I tried to arrange these so that the order of operations leaves them as far away as possible from ops affecting their values.
                // To reduce latency.
                closure._transpositionRow = transpositionRow;

                // TODO: is this one predictable?
                if (t2 < to)
                {
                    to = t2;
                }

                uint maxPlusOne = closure._maxPlusOne;

                // Save a read by combining both characters into one register. Used ulong because comparison is best done between two int32
                // Shift the previous-previous character into the high bits, putting the previous character into the bottom 2.
                // Make sure it's unsigned so that >> 48 will shift 0's
                ulong edgeCharacter = MathUtils.RotateRight((ulong) *(uint*) (closure._builderStart + builderDepth - 1), 16);

                nuint j = skip;
                int any = 0;

                do
                {
                    uint previousRowCurrentColumn = previousRow[j];
                    ulong wordCharacter = MathUtils.RotateRight((ulong) *(uint*) (firstWithOffset + j - 1), 16);
                    if ((uint) edgeCharacter != (uint) wordCharacter)
                    {
                        // Non-branching Min() call here because it's not predictable.
                        currentRowPreviousColumn = MathUtils.Min(currentRowPreviousColumn, previousRowCurrentColumn);
                        uint diagonalEntry;
                        // ReSharper disable once ConvertIfStatementToConditionalTernaryExpression
                        if (edgeCharacter != ((wordCharacter >> 48) | (wordCharacter << 48)))
                        {
                            // Not a match. Expected case.
                            diagonalEntry = previousRowPreviousColumn;
                        }
                        else
                        {
                            // Transposition. Not expected case.
                            diagonalEntry = closure._transpositionRow[j];
                        }

                        currentRowPreviousColumn = MathUtils.Min(currentRowPreviousColumn, diagonalEntry) + 1;
                    }
                    else
                    {
                        // Character match. Not expected case.
                        currentRowPreviousColumn = previousRowPreviousColumn;
                    }

                    currentRow[j] = currentRowPreviousColumn;

                    // Doing this here to reduce latency between the add and the conditional
                    ++j;

                    // Will make `any` negative if currentRowPreviousColumn < maxErrors
                    any |= (int) currentRowPreviousColumn - (int) maxPlusOne;
                    previousRowPreviousColumn = previousRowCurrentColumn;
                } while ((uint) j < to);

                if (any >= 0)
                {
                    goto singleReturn;
                }

                nuint newDepth = closure._builderDepth + 1;
                int node = closure._node;
                if (node < 0)
                {
                    closure._node = -node;
                    if (currentRowPreviousColumn < maxPlusOne && (uint) newDepth + maxPlusOne > closure._wordLength)
                    {
                        char* bs = closure._builderStart;
                        string stringResult = new string(bs, 0, (int)newDepth);
                        ulong count = GetWordCount(bs, newDepth, closure._graph);
                        closure._results.Add(stringResult, count);

                        // If we followed this branch, the register was overwritten.
                        // Fetch the value from the correct place instead of having the compiler store it on the stack.
                        maxPlusOne = closure._maxPlusOne;
                    }
                }

                if ((uint) newDepth >= closure._wordLength + maxPlusOne -1)
                {
                    goto singleReturn;
                }

                currentRow[to] = currentRowPreviousColumn + 1;
                if (currentRow[skip] >= maxPlusOne)
                {
#if INTRINSICS
                    if (Sse2.IsSupported)
                    {
                        int trailingZeros;
                        if (Avx2.IsSupported)
                        {
                            // experimentally, this was slower than the Sse2 version
                            // Also, given maxEdits == 3, do we need to check more than 4 positions?
                            var v = Avx.LoadDquVector256(currentRow + skip);
                            var other2 = Vector256.Create(closureMaxPlusOne);
                            v = Avx2.CompareGreaterThan(other2, v);
                            var mask2 = (uint) Avx2.MoveMask(v.AsByte());
                            trailingZeros = (int) Bmi1.TrailingZeroCount(mask2);
                        }
                        else
                        {
                            var v = Sse2.LoadVector128(currentRow + skip);
                            var other = Vector128.Create(closureMaxPlusOne);
                            v = Sse2.CompareLessThan(v, other);
                            var mask = (uint) Sse2.MoveMask(v.AsByte());
                            var trailing = Bmi1.TrailingZeroCount(mask);
                            trailingZeros = 16 + (((int)trailing - 16) & (((int)trailing - 16) >> 31));
                        }

                        var skipBatch = trailingZeros >> 2;
                        skip += skipBatch;
                    }
                    else
#endif
                    {
                        do
                        {
                            skip++;
                        } while (currentRow[skip] >= maxPlusOne);
                    }
                }

                if ((uint) skip >= closure._toCache[newDepth])
                {
                    goto singleReturn;
                }

                // TODO: Save a read by using all 64 bits?
                // Explicit ulong because it's not a pointer type
                ulong temp = *(ulong*) (closure._graph._firstChildEdgeIndex + (uint) closure._node);
                // This one is used an a pointer offset, so it can be nuint. Also, taking only the lower 32 bits.
                // ReSharper disable once RedundantCast - Just being explicit and clear
#pragma warning disable IDE0004, S1905 // Remove Unnecessary Cast
                nuint childEdge = (nuint) (uint) temp;
#pragma warning restore IDE0004, S1905 // Remove Unnecessary Cast
                // Upper 32 bits. Not used as an offset, so keep as uint.
                uint childEdgeEnd = (uint) (temp >> 32);

                if ((uint)childEdge == childEdgeEnd)
                {
                    goto singleReturn;
                }

                // TODO: We have some extra registers to store stuff in, and they're getting push-popped anyways. Load things and prevent re-reading from memory.
                uint* row0 = closure._row0;
                closure._builderDepth = newDepth;
                // casting to byte* because otherwise it was doing both shl 1 and shl 2 to double _maxPlusOne and then convert it to pointer moves
                // ReSharper disable once ArrangeRedundantParentheses
                closure._row0 = (uint*) ((maxPlusOne * 8) + (byte*) row0);
                char* builderPointer = closure._builderStart + newDepth;
                char* edgeC = closure._graph._edgeCharacters;
                int* edgeN = closure._graph._edgeToNodeIndex;
                do
                {
                    // TODO: My thought is that 32 bit read/writes are faster, so just do it in 32 bit mode anyways. Check if this is true.
                    *(uint*) builderPointer = *(uint*) (edgeC + childEdge);
                    closure._node = edgeN[childEdge];
                    ++childEdge;
                    MatchCharacter(ref closure, skip);
                }
                while ((uint)childEdge < childEdgeEnd);

                --closure._builderDepth;
                closure._row0 = row0;

#pragma warning disable S1116 // Empty statements should be removed - This one serves a purpose
                singleReturn:;
#pragma warning restore S1116 // Empty statements should be removed
            }

            MatchCharacter(ref closureArg, 0);
        }

#pragma warning disable S3898 //Value types should implement "IEquatable<T>"
        private readonly ref struct Run
        {
            public readonly char* _input;
            public readonly uint* _toCache;
            public readonly uint _wordLength;
            public readonly uint _maxPlusOne;

            public Run(char* input, uint* toCache, uint wordLength, uint maxPlusOne)
            {
                _input = input;
                _toCache = toCache;
                _wordLength = wordLength;
                _maxPlusOne = maxPlusOne;
            }
        }
#pragma warning restore S3898 //Value types should implement "IEquatable<T>"

        private class Job
        {
            public Run* _run;
            public readonly nint _edge;
            public readonly DawgGraph _graph;
            public readonly SuggestItemCollection _results;

            public Job(nint edge, DawgGraph graph, SuggestItemCollection results)
            {
                _edge = edge;
                _graph = graph;
                _results = results;
            }

            public Action DoWork()
            {
                return SearchPrivate;
            }

            private void SearchPrivate()
            {
                Search5(this);
            }
        }

        [StructLayout(LayoutKind.Explicit)]
#pragma warning disable S3898 //Value types should implement "IEquatable<T>"
        private readonly struct DawgGraph
        {
            [FieldOffset(0x00)] //48
            public readonly ulong* _wordCounts;

            [FieldOffset(0x08)] //50
            public readonly ushort* _reachableTerminalNodes;

            [FieldOffset(0x10)] //58
            public readonly uint* _firstChildEdgeIndex;

            [FieldOffset(0x18)] //60
            public readonly int* _edgeToNodeIndex;

            [FieldOffset(0x20)] //68
            public readonly char* _edgeCharacters;

            [FieldOffset(0x28)] //70
            public readonly nint _rootNodeIndex;

            public DawgGraph(ushort* reachableTerminalNodes, ulong* wordCounts, uint* firstChildEdgeIndex, char* edgeCharacters, int* edgeToNodeIndex, nint rootNodeIndex)
            {
                _reachableTerminalNodes = reachableTerminalNodes;
                _wordCounts = wordCounts;
                _firstChildEdgeIndex = firstChildEdgeIndex;
                _edgeCharacters = edgeCharacters;
                _edgeToNodeIndex = edgeToNodeIndex;
                _rootNodeIndex = rootNodeIndex;
            }
        }
#pragma warning restore S3898 //Value types should implement "IEquatable<T>"

        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static ulong GetWordCount(char* word, nuint length, in DawgGraph dawg)
        {
            ulong* number = dawg._wordCounts;
            ushort* terminals = dawg._reachableTerminalNodes;
            uint* edgeChildIndex = dawg._firstChildEdgeIndex;
            int* edgeNodeIndex = dawg._edgeToNodeIndex;
            char* characters = dawg._edgeCharacters;
            nint currentNode = dawg._rootNodeIndex;
            // Because we want 0-indexed
            number--;
            char* end = word + length;
            do
            {
                char target = *word;
                word++;
                // ReSharper disable once RedundantCast - Just being explicit and clear
#pragma warning disable IDE0004 // Remove Unnecessary Cast
                nuint i = (nuint) edgeChildIndex[currentNode];
#pragma warning restore IDE0004 // Remove Unnecessary Cast
                uint lastChildIndex = edgeChildIndex[currentNode+1];
                do
                {
                    // ReSharper disable once RedundantCast - Just being explicit and clear
#pragma warning disable IDE0004, S1905 // Remove Unnecessary Cast
                    currentNode = (nint)edgeNodeIndex[i];
#pragma warning restore IDE0004, S1905 // Remove Unnecessary Cast
                    char currentEdgeChar = characters[i];
                    i++;
                    if (currentEdgeChar != target)
                    {
                        // TODO: This might be predictable? Otherwise use Abs
                        if (currentNode < 0)
                        {
                            currentNode = -currentNode;
                        }

                        number += (long)terminals[currentNode];
                        continue;
                    }

                    if (currentNode < 0)
                    {
                        ++number;
                        currentNode = -currentNode;
                    }

                    break;
                } while ((uint)i < lastChildIndex);
            } while (word < end);

            return *number;
        }

        [StructLayout(LayoutKind.Explicit)]
#pragma warning disable S3898 //Value types should implement "IEquatable<T>"
        private ref struct Search3Closure
        {
            #region CacheLine1
            [FieldOffset(0x00)]
            public nuint _builderDepth;

            [FieldOffset(0x08)]
            public char* _first;

            [FieldOffset(0x10)]
            public uint _wordLength;

            [FieldOffset(0x14)]
            public uint _maxPlusOne;

            [FieldOffset(0x18)]
            public uint* _row0;
            #endregion CacheLine1

            #region CacheLine2
            [FieldOffset(0x20)]
            public char* _builderStart;

            [FieldOffset(0x28)]
            public uint* _transpositionRow;

            [FieldOffset(0x30)]
            public uint* _toCache;

            [FieldOffset(0x38)]
            public int _node;
            #endregion CacheLine2

            #region CacheLine3
            [FieldOffset(0x48)]
            public DawgGraph _graph;

            [FieldOffset(0x78)]
            public SuggestItemCollection _results;
            #endregion CacheLine3
        }
#pragma warning restore S3898 //Value types should implement "IEquatable<T>"

        public uint WordCount { get; }

        private readonly nint _rootFirstChild;
        private readonly nint _rootLastChild;

        private readonly Task[] _tasks;
        private readonly Job[] _jobs;
        private readonly Dictionary<char, int> _characterIndex;

        private readonly SingleElementSuggestItemCollection _singleWordResult = new SingleElementSuggestItemCollection();
        private readonly CompoundSuggestItemCollection _compoundResultCollection;

        private readonly LargePageMemoryChunk _memoryBlock;

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
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

            ref var graph = ref Unsafe.AsRef(_jobs[0]._graph);
            ref var edgeChildIndex = ref Unsafe.AsRef<int>(graph._firstChildEdgeIndex);
            ref var edgeNodeIndex = ref Unsafe.AsRef<int>(graph._edgeToNodeIndex);
            ref var characters = ref Unsafe.AsRef<char>(graph._edgeCharacters);
            ref var terminals = ref Unsafe.AsRef<ushort>(graph._reachableTerminalNodes);

            var builderStart = stackalloc char[50];
            // Because we want 0-indexed
            var count = index + 1;
            var currentNode = (int)graph._rootNodeIndex;
            var builder = builderStart;
            do
            {
                var i = Unsafe.Add(ref edgeChildIndex, currentNode);
                var lastChildIndex = Unsafe.Add(ref edgeChildIndex, currentNode + 1);
                for (; i < lastChildIndex; ++i)
                {
                    var nextNode = Unsafe.Add(ref edgeNodeIndex, i);
                    var nextNumber = Unsafe.Add(ref terminals, Math.Abs(nextNode));
                    if (nextNumber < count)
                    {
                        count -= nextNumber;
                        continue;
                    }

                    currentNode = nextNode;
                    if (currentNode < 0)
                    {
                        count--;
                        currentNode = -currentNode;
                    }

                    *builder = Unsafe.Add(ref characters, i);
                    builder++;
                    break;
                }
            } while (count > 0);

            return new string(builderStart, 0, (int) (builder - builderStart));
        }

        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public int GetIndex(in string word)
        {
            ref var graph = ref Unsafe.AsRef(_jobs[0]._graph);
            // Because we want 0-indexed
            var number = -1;
            var currentNode = graph._rootNodeIndex;
            ref var edgeChildIndex = ref Unsafe.AsRef<int>(graph._firstChildEdgeIndex);
            ref var edgeNodeIndex = ref Unsafe.AsRef<int>(graph._edgeToNodeIndex);
            ref var characters = ref Unsafe.AsRef<char>(graph._edgeCharacters);
            ref var terminals = ref Unsafe.AsRef<ushort>(graph._reachableTerminalNodes);
            // ReSharper disable once ForCanBeConvertedToForeach
            for (var w = 0; w < word.Length; ++w)
            {
                var target = word[w];
                var temp = Math.Abs(currentNode);
                var i = Unsafe.Add(ref edgeChildIndex, (int)temp);
                var lastChildIndex = Unsafe.Add(ref edgeChildIndex, (int)temp + 1);

                for (; i < lastChildIndex; ++i)
                {
                    var nextNode = Unsafe.Add(ref edgeNodeIndex, i);
                    if (Unsafe.Add(ref characters, i) != target)
                    {
                        var nextNodeAbs = Math.Abs(nextNode);
                        number += Unsafe.Add(ref terminals, nextNodeAbs);
                        continue;
                    }

                    currentNode = nextNode;
                    if (currentNode < 0)
                    {
                        ++number;
                    }

#pragma warning disable S907 // "goto" statement should not be used - Actually performance tested it. The difference is substantial.
                    goto nextIteration;
#pragma warning restore S907 // "goto" statement should not be used
                }

#pragma warning disable S907 // "goto" statement should not be used
                goto singleReturnPoint;
#pragma warning restore S907 // "goto" statement should not be used

                nextIteration:
#pragma warning disable S1116 // Empty statements should be removed - This statement is important. It allows the label/goto to work.
                ;
#pragma warning restore S1116 // Empty statements should be removed
            }

            // Must end on a terminal currentNode.
            if (currentNode < 0)
            {
                return number;
            }

            singleReturnPoint:
            return -1;
        }

        public Dawg(Stream stream) : this(CompressedSparseRowPointerGraph.Read(stream))
        {
        }

        public Dawg(CompressedSparseRowPointerGraph compressedSparseRows)
        {
            var rootNodeIndex = compressedSparseRows.RootNodeIndex;
            WordCount = compressedSparseRows.WordCount;

            _memoryBlock = compressedSparseRows.MemoryChunk;
            var dawgGraph = new DawgGraph(compressedSparseRows.ReachableTerminalNodes,
                compressedSparseRows.WordCounts,
                compressedSparseRows.FirstChildEdgeIndex,
                compressedSparseRows.EdgeCharacter,
                compressedSparseRows.EdgeToNodeIndex,
                rootNodeIndex);

            _rootFirstChild = compressedSparseRows.FirstChildEdgeIndex[rootNodeIndex];
            _rootLastChild = compressedSparseRows.FirstChildEdgeIndex[rootNodeIndex + 1];

            var rootNodeChildCount = (uint) _rootLastChild - (uint) _rootFirstChild;
            _compoundResultCollection = new CompoundSuggestItemCollection(rootNodeChildCount);
            _tasks = new Task[rootNodeChildCount];

            _jobs = new Job[_rootLastChild - _rootFirstChild];
            _characterIndex = new Dictionary<char, int>();
            for (var i = _rootFirstChild; i < _rootLastChild; i++)
            {
                _characterIndex.Add(dawgGraph._edgeCharacters[i], (int) i);
                _jobs[i - _rootFirstChild] = new Job(i, dawgGraph, _compoundResultCollection.Bags[i - _rootFirstChild]);
            }
        }

        public void Dispose()
        {
            _memoryBlock.Dispose();
            _compoundResultCollection.Dispose();
        }
    }
}
