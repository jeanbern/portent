using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using LocalsInit;

namespace portent
{
    public sealed unsafe class Dawg : IDisposable
    {
        [LocalsInit(false)]
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public IEnumerable<SuggestItem> Lookup(in string word, uint maxEdits)
        {
            if (maxEdits == 0)
            {
                var index = GetIndex(word);
                if (index >= 0)
                {
                    _singleWordResult.Update(word, _wordCounts[index]);
                    return _singleWordResult;
                }
                else
                {
                    return Enumerable.Empty<SuggestItem>();
                }
            }

            var wordLength = (uint) word.Length;

            // TODO: Why does it have wordLength + 1?
            // I think that's for allowing transposition checks to happen at the end.
            var inputLength = MemoryAlignmentHelper.GetCacheAlignedSize<char>(wordLength + 1);
            var inputBytes = stackalloc byte[inputLength];
            var input = MemoryAlignmentHelper.GetCacheAlignedStart<char>(inputBytes);

            // Not going to bother with Unsafe.CopyBlock. It's a pain for no gain.
            for (var x = 0; x < word.Length; ++x)
            {
                input[x] = word[x];
            }

            if (maxEdits == 1)
            {
                input[wordLength] = (char)0;
                _resultCollection.Clear();
                Search1Edit(input, wordLength);
                return _resultCollection;
            }

            _compoundResultCollection.Clear();

            var maxDepth = wordLength + maxEdits;
            var toCacheLength = MemoryAlignmentHelper.GetCacheAlignedSize<uint>(maxDepth);
            var toCacheBytes = stackalloc byte[toCacheLength];
            var toCache = MemoryAlignmentHelper.GetCacheAlignedStart<uint>(toCacheBytes);

            var minTo = Math.Min(wordLength, (2 * maxEdits) + 1);
            for (var depth = 0u; depth < maxDepth; ++depth)
            {
                toCache[depth] = Math.Min(Math.Min(depth + maxEdits + 1, wordLength + maxEdits - depth), minTo);
            }

            var tasks = _tasks;
            var rootFirst = _rootFirstChild;
            var rootLast = _rootLastChild;

            if (maxEdits == 2)
            {
                for (var i = rootFirst; i < rootLast; ++i)
                {
                    //To avoid "access to modified closure" which was causing real issues.
                    var i1 = i;
#if DEBUG
                    Search2Edits(i1, input, wordLength, toCache);
#else
                    tasks[i - rootFirst] = Task.Run(() => Search2Edits(i1, input, wordLength, toCache));
#endif
                }
            }
            else
            {
                var maxPlusOne = maxEdits + 1;
                for (var i = rootFirst; i < rootLast; ++i)
                {
                    //To avoid "access to modified closure" which was causing real issues.
                    var i1 = i;
#if DEBUG
                    SearchWithEdits(i1, maxPlusOne, input, wordLength, toCache);
#else
                    tasks[i - rootFirst] = Task.Run(() => SearchWithEdits(i1, maxPlusOne, input, wordLength, toCache));
#endif
                }
            }

#if !DEBUG
            Task.WaitAll(tasks);
#endif
            return _compoundResultCollection;
        }

        /// <summary>
        /// Finds words with edit distance 1 or less from the input.
        /// </summary>
        /// <param name="word">The input character string. This sequence must have a (char)0 appended to the end at position wordLength.</param>
        /// <param name="wordLength">The length of the word. The input must be 1 element longer than this value.</param>
        [LocalsInit(false)]
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private void Search1Edit(char* word, uint wordLength)
        {
            var builderByteCount = MemoryAlignmentHelper.GetCacheAlignedSize<char>(wordLength + 1);
            var builderBytes = stackalloc byte[builderByteCount];

            // Technically it's const, but it's contents aren't
            var builderStart = MemoryAlignmentHelper.GetCacheAlignedStart<char>(builderBytes);

            var const_firstChildEdgeIndex = _firstChildEdgeIndex;
            var const_edgeToNodeIndex = _edgeToNodeIndex;
            var const_edgeCharacter = _edgeCharacter;
            var const_reachableTerminalNodes = _reachableTerminalNodes;
            var const_root = _rootNodeIndex;

            // Things I tried that don't make a difference:
            // 1. Making this method static and using a readonly ref struct for the const_ variables
            //    "static int GetIndex(in Holder consts, int length)"
            int GetIndex(uint length)
            {
                // Because we want 0-indexed
                var number = -1;

                var currentNode = const_root;
                ref var word = ref Unsafe.AsRef<char>(builderStart);
                ref var end = ref Unsafe.Add(ref Unsafe.AsRef<char>(builderStart), (int) length);
                do
                {
                    var target = word;
                    word = ref Unsafe.Add(ref word, 1);
                    var i = const_firstChildEdgeIndex[currentNode];
                    var lastChildIndex = const_firstChildEdgeIndex[currentNode + 1];
                    do
                    {
                        var nextNode = const_edgeToNodeIndex[i];
                        if (const_edgeCharacter[i] != target)
                        {
                            number += const_reachableTerminalNodes[Abs(nextNode)];
                            continue;
                        }

                        currentNode = nextNode;
                        if (currentNode < 0)
                        {
                            ++number;
                            currentNode = -currentNode;
                        }

                        break;
                    } while (++i < lastChildIndex);
                } while (Unsafe.IsAddressLessThan(ref word, ref end));

                return number;
            }

            var builderIndex = builderStart;

            var const_results = _resultCollection;
            var const_wordEnd = word + wordLength;
            var const_wordCount = _wordCounts;

            // Things I tried that don't make a difference:
            // 1. Making this method static and using a readonly ref struct for the const_ variables
            //    "static void SearchWithoutEdits(in Holder consts, char* builderPosition, int currentNode, char* input)"
            void SearchWithoutEdits(int currentNode, char* input)
            {
                var spot = input;
                do
                {
                    var temp = Abs(currentNode);
                    var g = const_firstChildEdgeIndex[temp];
                    var gLast = const_firstChildEdgeIndex[temp + 1];

                    var currentTarget = *spot;
                    for (; g < gLast; ++g)
                    {
                        if (currentTarget != const_edgeCharacter[g])
                        {
                            continue;
                        }

                        currentNode = const_edgeToNodeIndex[g];
#pragma warning disable S907 // "goto" statement should not be used - Actually performance tested it. The difference is substantial.
                        goto next;
#pragma warning restore S907 // "goto" statement should not be used
                    }

#pragma warning disable S1751 // Loops with at most one iteration should be refactored - The analysis tool ignores my goto statement. There is actual iteration.
                    return;
#pragma warning restore S1751 // Loops with at most one iteration should be refactored

                    next:
                    ++spot;
                } while (spot < const_wordEnd);

                // ReSharper disable once InvertIf
                if (currentNode < 0)
                {
                    var inputRemaining = (uint)(const_wordEnd - input);
                    Unsafe.CopyBlock(builderIndex + 1, input, (uint)(inputRemaining * sizeof(char)));

                    var resultLength = (uint)(builderIndex + 1 - builderStart) + inputRemaining;
                    const_results.Add(new string(builderStart, 0, (int) resultLength), const_wordCount[GetIndex(resultLength)]);
                }
            }

            var nextNode = _rootNodeIndex;
            var nextWordCharacter = word + 1;
            var target = *word;

            if (wordLength > 1)
            {
                var secondTarget = *nextWordCharacter;
                for (var index = 0; index < wordLength - 2; ++index)
                {
                    nextNode = Abs(nextNode);
                    var i = const_firstChildEdgeIndex[nextNode];
                    var iLast = const_firstChildEdgeIndex[nextNode + 1];
                    nextNode = int.MaxValue;
                    var thirdTarget = *(nextWordCharacter + 1);
                    for (; i < iLast; ++i)
                    {
                        var firstEdgeChar = const_edgeCharacter[i];
                        var edgeNode = const_edgeToNodeIndex[i];
                        *builderIndex = firstEdgeChar;
                        if (firstEdgeChar == target)
                        {
                            nextNode = edgeNode;
                            continue;
                        }

                        if (firstEdgeChar != secondTarget)
                        {
                            //substitution and continue
                            SearchWithoutEdits(edgeNode, nextWordCharacter);

                            //insertion and continue
                            SearchWithoutEdits(edgeNode, nextWordCharacter - 1);

                            continue;
                        }

                        // firstEdgeChar == secondTarget
                        var temp = Abs(edgeNode);
                        var k = const_firstChildEdgeIndex[temp];
                        var kLast = const_firstChildEdgeIndex[temp + 1];

                        builderIndex++;
                        for (; k < kLast; ++k)
                        {
                            var secondEdgeChar = const_edgeCharacter[k];
                            if (secondEdgeChar == target)
                            {
                                // Can't use builderCurrent in this case because we incremented it outside this loop.
                                *builderIndex = secondEdgeChar;
                                var noEditNode = const_edgeToNodeIndex[k];
                                // insertion + match + withoutEdits
                                SearchWithoutEdits(noEditNode, nextWordCharacter);
                                // transposition + withoutEdits
                                SearchWithoutEdits(noEditNode, nextWordCharacter + 1);
                            }
                            else if (secondEdgeChar == secondTarget)
                            {
                                // Can't use builderCurrent in this case because we incremented it outside this loop.
                                *builderIndex = secondEdgeChar;
                                // substitution + match + withoutEdit
                                SearchWithoutEdits(const_edgeToNodeIndex[k], nextWordCharacter + 1);
                            }

                            // ReSharper disable once InvertIf
                            if (secondEdgeChar == thirdTarget)
                            {
                                if (index + 3 == wordLength)
                                {
                                    // ReSharper disable once InvertIf
                                    if (const_edgeToNodeIndex[k] < 0)
                                    {
                                        // Can't use builderCurrent in this case because we incremented it outside this loop.
                                        *builderIndex = secondEdgeChar;
                                        //deletion + match + match + end
                                        var wordIndex = GetIndex(wordLength - 1);
                                        const_results.Add(new string(builderStart, 0, (int) wordLength - 1), const_wordCount[wordIndex]);
                                    }
                                }
                                else
                                {
                                    // Can't use builderCurrent in this case because we incremented it outside this loop.
                                    *builderIndex = secondEdgeChar;
                                    //deletion + match + match + continue
                                    SearchWithoutEdits(const_edgeToNodeIndex[k], nextWordCharacter + 2);
                                }
                            }
                        }

                        builderIndex--;
                    }

                    if (nextNode == int.MaxValue)
                    {
                        return;
                    }

                    *builderIndex = target;
                    target = secondTarget;
                    secondTarget = thirdTarget;
                    builderIndex++;
                    nextWordCharacter++;
                }

                //case : index + 2 == wordLength
                var jTemp = Abs(nextNode);
                var j = const_firstChildEdgeIndex[jTemp];
                var jLast = const_firstChildEdgeIndex[jTemp + 1];
                nextNode = int.MaxValue;
                for (; j < jLast; ++j)
                {
                    var firstEdgeChar = const_edgeCharacter[j];
                    var edgeNode = const_edgeToNodeIndex[j];
                    *builderIndex = firstEdgeChar;

                    if (firstEdgeChar == target)
                    {
                        nextNode = edgeNode;
                        continue;
                    }

                    if (firstEdgeChar != secondTarget)
                    {
                        //substitution and continue
                        SearchWithoutEdits(edgeNode, nextWordCharacter);

                        //insertion and continue
                        SearchWithoutEdits(edgeNode, nextWordCharacter - 1);

                        continue;
                    }

                    // firstEdgeChar == secondTarget
                    var temp = Abs(edgeNode);
                    var k = const_firstChildEdgeIndex[temp];
                    var kLast = const_firstChildEdgeIndex[temp + 1];

                    if (edgeNode < 0)
                    {
                        //deletion + match:
                        const_results.Add(new string(builderStart, 0, (int) wordLength - 1), const_wordCount[GetIndex(wordLength - 1)]);
                    }

                    builderIndex++;
                    for (; k < kLast; ++k)
                    {
                        var secondEdgeChar = const_edgeCharacter[k];
                        if (secondEdgeChar == target)
                        {
                            var currentNode = const_edgeToNodeIndex[k];
                            if (currentNode < 0)
                            {
                                currentNode = -currentNode;
                                //transposition:
                                *builderIndex = secondEdgeChar;
                                const_results.Add(new string(builderStart, 0, (int) wordLength), const_wordCount[GetIndex(wordLength)]);
                            }

                            var m = const_firstChildEdgeIndex[currentNode];
                            var mLast = const_firstChildEdgeIndex[currentNode + 1];
                            for (; m < mLast; ++m)
                            {
                                var thirdEdgeChar = const_edgeCharacter[m];
                                if (thirdEdgeChar == secondTarget && const_edgeToNodeIndex[m] < 0)
                                {
                                    *builderIndex = secondEdgeChar;
                                    *(builderIndex + 1) = thirdEdgeChar;
                                    //insertion + match
                                    const_results.Add(new string(builderStart, 0, (int) wordLength + 1), const_wordCount[GetIndex(wordLength + 1)]);
                                    break;
                                }
                            }
                        }
                        else if (secondEdgeChar == secondTarget && const_edgeToNodeIndex[k] < 0)
                        {
                            //substitution + match:
                            *builderIndex = secondEdgeChar;
                            const_results.Add(new string(builderStart, 0, (int) wordLength), const_wordCount[GetIndex(wordLength)]);
                        }
                    }

                    builderIndex--;
                }

                if (nextNode == int.MaxValue)
                {
                    return;
                }

                *builderIndex = target;
                target = secondTarget;
                builderIndex++;
            }

            // case: index + 1 == wordLength
            if (nextNode < 0)
            {
                nextNode = -nextNode;
                //delete + end
                const_results.Add(new string(builderStart, 0, (int) wordLength - 1), const_wordCount[GetIndex(wordLength - 1)]);
            }

            // Now searching for the last character.
            var n = const_firstChildEdgeIndex[nextNode];
            var nLast = const_firstChildEdgeIndex[nextNode + 1];
            for (; n < nLast; ++n)
            {
                var firstEdgeChar = const_edgeCharacter[n];
                var edgeNode = const_edgeToNodeIndex[n];
                *builderIndex = firstEdgeChar;

                if (edgeNode < 0)
                {
                    // substitution and word ended
                    // OR match at end of word
                    // Don't have to check, both are within 1 error.
                    const_results.Add(new string(builderStart, 0, (int) wordLength), const_wordCount[GetIndex(wordLength)]);
                    edgeNode = -edgeNode;
                }

                var j = const_firstChildEdgeIndex[edgeNode];
                var jLast = const_firstChildEdgeIndex[edgeNode + 1];
                if (firstEdgeChar != target)
                {
                    for (; j < jLast; ++j)
                    {
                        var secondEdgeChar = const_edgeCharacter[j];
                        if (secondEdgeChar == target && const_edgeToNodeIndex[j] < 0)
                        {
                            // insertions + match at end of word.
                            *(builderIndex + 1) = target;
                            const_results.Add(new string(builderStart, 0, (int) wordLength + 1), const_wordCount[GetIndex(wordLength + 1)]);
                        }
                    }
                }
                else
                {
                    for (; j < jLast; ++j)
                    {
                        if (const_edgeToNodeIndex[j] < 0)
                        {
                            //match + insertions at end of word.
                            *(builderIndex + 1) = const_edgeCharacter[j];
                            const_results.Add(new string(builderStart, 0, (int) wordLength + 1), const_wordCount[GetIndex(wordLength + 1)]);
                        }
                    }
                }
            }
        }

        [LocalsInit(false)]
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private void Search2Edits(uint edge, // Not captured
            char* word, uint wordLength, uint* toCache)
        {
            // The real stripeWidth is usually 2*max + 1 = 5
            // Normally we would do a bound check and calculate a value for the cell in previousRow directly after our stripe.
            // Instead we pre-assign the cell when we have the values handy and then ignore bound checking.
            // For this reason, the stripeWidth gets an additional + 1, making it 2 * max + 2 = 6
            // I already tried making it 8 for cache alignment, it didn't matter.
            const int stripeWidth = 6;

            // +2 for maxEdits = 2, then distribute the multiplication to take advantage of const.
            // Would be otherwise be: (wordLength + maxEdits)*stripeWidth
            var matrixLength = MemoryAlignmentHelper.GetCacheAlignedSize<int>((wordLength * stripeWidth) + (2 * stripeWidth));
            var editMatrixAlloc = stackalloc byte[matrixLength];

            var builderByteCount = MemoryAlignmentHelper.GetCacheAlignedSize<char>(wordLength + 2);
            var builderBytes = stackalloc byte[builderByteCount];

            // TODO: If these are captured in a closure anyways, is it faster to just look them up?
            var edgeToNodeIndex = _edgeToNodeIndex;
            var edgeCharacters = _edgeCharacter;
            var edgeIndex = _firstChildEdgeIndex;
            var wordCount = _wordCounts;
            var results = _compoundResultCollection.Bags[edge - _rootFirstChild];

            // These 2 variables are used in all MatchCharacter functions
            var builder = MemoryAlignmentHelper.GetCacheAlignedStart<char>(builderBytes);
            var editMatrix = MemoryAlignmentHelper.GetCacheAlignedStart<int>(editMatrixAlloc);

            // This variable, in addition to the 2 later, are used for parameter passing in MatchCharacterX
            var builderDepth = 4u;

            // These two variables are used for parameter passing between MatchCharacter1-3
            var currentCharacter = builder[0] = edgeCharacters[edge];
            var node = edgeToNodeIndex[edge];

            void MatchCharacterX(int skip)
            {
                // One row was skipped when compared to SearchWithEdits.
                // It's the one that gets assigned in a loop: "const_row0[x] = x + 1"
                // For this reason, currentRow and previousRow are one stripeWidth behind.
                var currentRow = editMatrix + (builderDepth * stripeWidth);
                var previousRow = currentRow - stripeWidth;

                // This is the value for the column directly before our diagonal stripe.
                // In this case, 3 is too many anyways, so no need to compute it.
                var currentRowPreviousColumn = 3;

                // Normally the strip would travel diagonally through the matrix. We shift it left to keep it starting at 0.
                // previousRowOffset == 1 once we have started shifting.
                // Our stripe ignores early characters once we've gone deep enough.
                var previousRowPreviousColumn = previousRow[skip];
                var wordWithOffset = word + (int)builderDepth - 2;
                var previousWordCharacter = wordWithOffset[skip];

                var any = 0;
                var to = toCache[builderDepth];
                for (var j = skip; j < to; ++j)
                {
                    var previousRowCurrentColumn = (previousRow + 1)[j];
                    var wordCharacter = wordWithOffset[j];
                    var result1 = Math.Min(currentRowPreviousColumn, previousRowCurrentColumn);
                    if (currentCharacter != wordCharacter)
                    {
                        // Non-match.
                        // Expected case.
                        var result2 = Math.Min(result1, previousRowPreviousColumn) + 1;
                        if ((currentCharacter != previousWordCharacter) || (builder[(int)builderDepth - 1] != wordCharacter))
                        {
                            // Non-transposition.
                            // Expected case.
                            currentRow[j] = currentRowPreviousColumn = result2;
                        }
                        else
                        {
                            // Transposition!
                            // At least 2 stripes are shifted: look up. ((previousrow - rowWidth)[j])
                            currentRow[j] = currentRowPreviousColumn = Math.Min(result2, (previousRow - stripeWidth)[j] + 1);
                        }
                    }
                    else
                    {
                        // Character match, not-expected case
                        currentRow[j] = currentRowPreviousColumn = Math.Min(result1 + 1, previousRowPreviousColumn);
                    }

                    previousRowPreviousColumn = previousRowCurrentColumn;
                    previousWordCharacter = wordCharacter;
                    // Will make `any` negative if currentRowPreviousColumn > 2
                    any |= currentRowPreviousColumn - 3;
                }

                if (any >= 0)
                {
                    return;
                }

                if (node < 0 && currentRowPreviousColumn <= 2 && wordLength <= builderDepth + 3)
                {
                    results.Add(new string(builder, 0, (int) builderDepth + 1), wordCount[GetIndex(ref Unsafe.AsRef<char>(builder), builderDepth + 1)]);
                }

                if (wordLength < builderDepth)
                {
                    return;
                }

                // We make the strip wider by 1 and avoid bound checking conditionals.
                // This write to memory is cheap because this location should be in the L1 cache due to recent instructions.
                // In addition, we perform this write even when it's not necessary because checking that condition is more expensive.
                var top = currentRow + to;
                *top = currentRowPreviousColumn + 1;

                var counterSpot = currentRow + skip;
                if (*counterSpot > 2)
                {
                    ++counterSpot;
                    while (counterSpot <= top && *counterSpot > 2)
                    {
                        ++counterSpot;
                    }

                    skip = (int)(counterSpot - currentRow);
                }

                if (skip >= toCache[builderDepth + 1])
                {
                    return;
                }

                var temp = Abs(node);
                var i = edgeIndex[temp];
                var last = edgeIndex[temp + 1];

                ++builderDepth;
                for (; i < last; ++i)
                {
                    currentCharacter = builder[builderDepth] = edgeCharacters[i];
                    node = edgeToNodeIndex[i];
                    MatchCharacterX(skip);
                }

                builderDepth--;
            }

            // Some variables are not used in all MatchCharacter functions. Moved them closer to usage.
            var to4 = toCache[4];
            var to3 = toCache[3];
            var row1 = editMatrix + stripeWidth;
            var row2 = editMatrix + (2 * stripeWidth);
            var row3 = editMatrix + (3 * stripeWidth);

            void MatchCharacter3(int skip)
            {
                var currentRowPreviousColumn = 3;
                var previousRowPreviousColumn = row2[skip];
                var firstWithOffset = word + 1;
                var previousWordCharacter = firstWithOffset[skip];

                var any = 0;
                for (var j = skip; j < to3; ++j)
                {
                    var previousRowCurrentColumn = row2[j + 1];
                    var wordCharacter = firstWithOffset[j];
                    var result1 = Math.Min(currentRowPreviousColumn, previousRowCurrentColumn);
                    if (currentCharacter != wordCharacter)
                    {
                        var result2 = Math.Min(result1, previousRowPreviousColumn) + 1;
                        if ((currentCharacter != previousWordCharacter) || (builder[2] != wordCharacter))
                        {
                            // Non-match.
                            // Expected case.
                            row3[j] = currentRowPreviousColumn = result2;
                        }
                        else
                        {
                            // Transposition case!
                            // It's the first stripe shift: look up, move left 1. ((previousrow - rowWidth)[j - 1])
                            row3[j] = currentRowPreviousColumn = Math.Min(result2, row1[j - 1] + 1);
                        }
                    }
                    else
                    {
                        // Character match, not-expected case
                        row3[j] = currentRowPreviousColumn = Math.Min(result1 + 1, previousRowPreviousColumn);
                    }

                    previousRowPreviousColumn = previousRowCurrentColumn;
                    previousWordCharacter = wordCharacter;
                    any |= currentRowPreviousColumn - 3;
                }

                if (any >= 0)
                {
                    return;
                }

                if (node < 0 && currentRowPreviousColumn <= 2 && wordLength <= 6)
                {
                    results.Add(new string(builder, 0, 4), wordCount[GetIndex(ref Unsafe.AsRef<char>(builder), 4)]);
                }

                if (wordLength < 3)
                {
                    return;
                }

                // We make the strip wider by 1 and avoid bound checking conditionals.
                // This write to memory is cheap because this location should be in the L1 cache due to recent instructions.
                // In addition, we perform this write even when it's not necessary because checking that condition is more expensive.
                row3[to3] = currentRowPreviousColumn + 1;

                if (row3[skip] > 2)
                {
                    ++skip;
                    while (skip <= to3 && row3[skip] > 2)
                    {
                        ++skip;
                    }
                }

                if (skip >= to4)
                {
                    return;
                }

                var temp = Abs(node);
                var i = edgeIndex[temp];
                var last = edgeIndex[temp + 1];

                for (; i < last; ++i)
                {
                    currentCharacter = builder[4] = edgeCharacters[i];
                    node = edgeToNodeIndex[i];
                    MatchCharacterX(skip);
                }
            }

            var to2 = toCache[2];
            var row0 = editMatrix;

            void MatchCharacter2()
            {
                var currentRowPreviousColumn = 3;
                var previousRowPreviousColumn = 2;
                var previousWordCharacter = (char)0;
                var any = 0;

                for (var j = 0; j < to2; ++j)
                {
                    var previousRowCurrentColumn = row1[j];
                    var wordCharacter = word[j];
                    var result1 = Math.Min(currentRowPreviousColumn, previousRowCurrentColumn);
                    if (currentCharacter != wordCharacter)
                    {
                        var result2 = Math.Min(result1, previousRowPreviousColumn) + 1;
                        if ((j == 1) || (currentCharacter != previousWordCharacter) || (builder[1] != wordCharacter))
                        {
                            // Non-match.
                            // Expected case.
                            row2[j] = currentRowPreviousColumn = result2;
                        }
                        else
                        {
                            // transposition
                            row2[j] = currentRowPreviousColumn = Math.Min(result2, row0[j - 2] + 1);
                        }
                    }
                    else
                    {
                        // Character match, not-expected case
                        row2[j] = currentRowPreviousColumn = Math.Min(result1 + 1, previousRowPreviousColumn);
                    }

                    previousRowPreviousColumn = previousRowCurrentColumn;
                    previousWordCharacter = wordCharacter;
                    any |= currentRowPreviousColumn - 3;
                }

                if (any >= 0)
                {
                    return;
                }

                if (node < 0 && currentRowPreviousColumn <= 2 && wordLength <= 5)
                {
                    results.Add(new string(builder, 0, 3), wordCount[GetIndex(ref Unsafe.AsRef<char>(builder), 3)]);
                }

                if (wordLength < 2)
                {
                    return;
                }

                // We make the strip wider by 1 and avoid bound checking conditionals.
                // This write to memory is cheap because this location should be in the L1 cache due to recent instructions.
                // In addition, we perform this write even when it's not necessary because checking that condition is more expensive.
                row2[to2] = currentRowPreviousColumn + 1;

                var skip = 0;
                if (row2[skip] > 2)
                {
                    ++skip;
                    while (skip <= to3 && row2[skip] > 2)
                    {
                        ++skip;
                    }
                }

                if (skip >= to3)
                {
                    return;
                }

                var temp = Abs(node);
                var i = edgeIndex[temp];
                var last = edgeIndex[temp + 1];

                for (; i < last; ++i)
                {
                    currentCharacter = builder[3] = edgeCharacters[i];
                    node = edgeToNodeIndex[i];
                    MatchCharacter3(skip);
                }
            }

            var to1 = toCache[1];

            void MatchCharacter1()
            {
                var currentRowPreviousColumn = 2;
                var previousRowPreviousColumn = 1;
                var previousWordCharacter = (char)0;

                var j = 0;
                for (; j < to1; ++j)
                {
                    var previousRowCurrentColumn = row0[j];
                    var wordCharacter = word[j];
                    var result1 = Math.Min(currentRowPreviousColumn, previousRowCurrentColumn);
                    if (currentCharacter != wordCharacter)
                    {
                        var result2 = Math.Min(result1, previousRowPreviousColumn) + 1;
                        if ((currentCharacter != previousWordCharacter) || (builder[0] != wordCharacter))
                        {
                            // Non-match.
                            // Expected case.
                            row1[j] = currentRowPreviousColumn = result2;
                        }
                        else
                        {
                            // Transposition case!
                            // There is no (previousRow - rowWidth). It would be row 0 in the Levenshtein matrix, the one with no letters.
                            row1[j] = currentRowPreviousColumn = Math.Min(result2, j);
                        }
                    }
                    else
                    {
                        // Character match, not-expected case
                        row1[j] = currentRowPreviousColumn = Math.Min(result1 + 1, previousRowPreviousColumn);
                    }

                    previousRowPreviousColumn = previousRowCurrentColumn;
                    previousWordCharacter = wordCharacter;
                }

                row1[to1] = currentRowPreviousColumn + 1;
                if (node < 0 && currentRowPreviousColumn <= 2 && wordLength <= 4)
                {
                    results.Add(new string(builder, 0, 2), wordCount[GetIndex(ref Unsafe.AsRef<char>(builder), 2)]);
                }

                var temp = Abs(node);
                var i = edgeIndex[temp];
                var last = edgeIndex[temp + 1];
                for (; i < last; ++i)
                {
                    currentCharacter = builder[2] = edgeCharacters[i];
                    node = edgeToNodeIndex[i];
                    MatchCharacter2();
                }
            }

            row0[0] = 1;
            row0[1] = 2;
            row0[2] = 3;
            row0[3] = 3;
            if (currentCharacter == *word)
            {
                row0[0] = 0;
                row0[1] = 1;
                row0[2] = 2;
            }
            else if (wordLength > 1)
            {
                if (currentCharacter == word[1])
                {
                    // 1
                    row0[1] = 1;
                    row0[2] = 2;
                }
                else if (wordLength > 2 && currentCharacter == word[2])
                {
                    row0[2] = 2;
                }
            }

            if (node < 0 && (wordLength < 3 || (wordLength == 3 && row0[2] == 2)))
            {
                results.Add(new string(builder, 0, 1), wordCount[GetIndex(ref Unsafe.AsRef<char>(builder), 1)]);
            }

            var temp = Abs(node);
            var i = edgeIndex[temp];
            var last = edgeIndex[temp + 1];
            for (; i < last; ++i)
            {
                currentCharacter = builder[1] = edgeCharacters[i];
                node = edgeToNodeIndex[i];
                MatchCharacter1();
            }
        }

        private readonly struct Invariants
        {
            public readonly Dawg _dawg;
            public readonly ulong* _wordCounts;
            public readonly uint* _firstChildEdgeIndex;
            public readonly int* _edgeToNodeIndex;
            public readonly char* _edgeCharacters;

            public Invariants(ulong* wordCounts, uint* firstChildEdgeIndex, char* edgeCharacters, Dawg dawg, int* edgeToNodeIndex)
            {
                _wordCounts = wordCounts;
                _firstChildEdgeIndex = firstChildEdgeIndex;
                _edgeCharacters = edgeCharacters;
                _dawg = dawg;
                _edgeToNodeIndex = edgeToNodeIndex;
            }
        }

        private ref struct Variants
        {
            public readonly Invariants _invariants;
            public readonly SuggestItemCollection _results;
            public readonly uint* _const_row0;
            public readonly uint _const_stripeWidth;
            public readonly uint* _toCache;
            public readonly uint _wordLength;
            public readonly uint _maxPlusOne;
            public readonly char* _const_builder;
            public readonly char* _first;
            public uint _builderDepth;
            public int _node;
            public char _edgeCharacter;

            public Variants(Invariants invariants, SuggestItemCollection results, uint* const_row0, uint const_stripeWidth, char* first, uint* toCache, char* const_builder, uint wordLength, uint maxPlusOne, char edgeCharacter, int node)
            {
                _invariants = invariants;
                _results = results;
                _const_row0 = const_row0;
                _const_stripeWidth = const_stripeWidth;
                _first = first;
                _toCache = toCache;
                _const_builder = const_builder;
                _wordLength = wordLength;
                _maxPlusOne = maxPlusOne;
                _builderDepth = 0u;
                _edgeCharacter = edgeCharacter;
                _node = node;
            }
        }

        [LocalsInit(false)]
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private void SearchWithEdits(uint edge, // Not captured
            uint maxPlusOne, char* first, uint wordLength, uint* toCache)
        {
            ref var invariants = ref Unsafe.AsRef(_invariants);
            // The real stripeWidth is 2*max + 1
            // Normally we would do a bound check and calculate a value for the cell in previousRow directly after our stripe.
            // Instead we pre-assign the cell when we have the values handy and then ignore bound checking.
            // For this reason, the stripeWidth gets an additional + 1, making it 2 * max + 2
            var const_stripeWidth = 2 * maxPlusOne;

            var matrixLength = MemoryAlignmentHelper.GetCacheAlignedSize<uint>((wordLength + maxPlusOne) * const_stripeWidth);
            var editMatrixAlloc = stackalloc byte[matrixLength];
            var const_row0 = MemoryAlignmentHelper.GetCacheAlignedStart<uint>(editMatrixAlloc);

            for (var x = 0u; x < maxPlusOne; ++x)
            {
                const_row0[x] = x + 1;
            }

            var builderByteLength = MemoryAlignmentHelper.GetCacheAlignedSize<char>(wordLength + maxPlusOne - 1);
            var builderAlloc = stackalloc byte[builderByteLength];
            var const_builder = MemoryAlignmentHelper.GetCacheAlignedStart<char>(builderAlloc);

            var edgeCharacter = invariants._edgeCharacters[edge];
            const_builder[0] = edgeCharacter;

            var node = invariants._edgeToNodeIndex[edge];
            var results = _compoundResultCollection.Bags[edge - _rootFirstChild];
            var variants = new Variants(invariants, results, const_row0, const_stripeWidth, first, toCache, const_builder, wordLength, maxPlusOne, edgeCharacter, node);

            static void MatchCharacter(ref Variants variants, uint skip)
            {
                // This is the value for the column directly before our diagonal stripe.
                // It's the one we avoided setting in previousRow earlier.
                var currentRowPreviousColumn = variants._builderDepth + skip + 1;

                var previousRow = variants._const_row0 + (variants._builderDepth * variants._const_stripeWidth);
                var currentRow = previousRow + variants._const_stripeWidth;

                char previousWordCharacter;
                uint previousRowPreviousColumn;
                char* firstWithOffset;

                // Normally the strip would travel diagonally through the matrix. We shift it left to keep it starting at 0.
                // previousRowOffset == 1 once we have started shifting.
                int previousRowOffset;
                // Our stripe ignores early characters once we've gone deep enough.
                var wordArrayOffset = (int)variants._builderDepth - (int)variants._maxPlusOne;

                if (wordArrayOffset < 0)
                {
                    // As with currentRowPreviousColumn, this is the cell before our stripe, so it can be computed instead of retrieved.
                    previousRowPreviousColumn = currentRowPreviousColumn - 1;
                    firstWithOffset = variants._first;
                    previousWordCharacter = (char)0;
                    previousRowOffset = 0;
                }
                else
                {
                    previousRowPreviousColumn = previousRow[skip];
                    firstWithOffset = variants._first + wordArrayOffset + 1;
                    previousWordCharacter = firstWithOffset[skip];
                    previousRowOffset = 1;
                }

                var any = 0;
                var to = variants._toCache[variants._builderDepth];
                for (var j = (int)skip; j < to; ++j)
                {
                    var previousRowCurrentColumn = previousRow[j + previousRowOffset];
                    var wordCharacter = firstWithOffset[j];
                    var result1 = Math.Min(currentRowPreviousColumn, previousRowCurrentColumn);
                    if (variants._edgeCharacter != wordCharacter)
                    {
                        var result2 = Math.Min(result1, previousRowPreviousColumn) + 1;
                        if ((variants._edgeCharacter != previousWordCharacter) || (variants._builderDepth == 0) || (variants._const_builder[(int)variants._builderDepth - 1] != wordCharacter))
                        {
                            // Non-match.
                            // Expected case.
                            currentRowPreviousColumn = result2;
                        }
                        else
                        {
                            // Transposition case!
                            // Assert BuilderDepth > 0: Otherwise there is no previous character to transpose.
                            if (variants._builderDepth == 1)
                            {
                                // There is no (previousRow - rowWidth). It would be row 0 in the Levenshtein matrix, the one with no letters.
                                // In this case, the cell value is simply it's column # j
                                // Use that instead of (previousRow - stripeWidth)[j - 2]
                                currentRowPreviousColumn = Math.Min(result2, (uint)j);
                            }
                            else if (variants._builderDepth < variants._maxPlusOne)
                            {
                                // The stripes haven't started shifting yet: look up, move left 2. ((previousrow - rowWidth)[j - 2])
                                // If we can't move left:
                                if (j == 1)
                                {
                                    currentRowPreviousColumn = Math.Min(result2, variants._builderDepth);
                                }
                                else
                                {
                                    currentRowPreviousColumn = Math.Min(result2, (previousRow - variants._const_stripeWidth)[j - 2] + 1);
                                }
                            }
                            else
                            {
                                // It's the first stripe shift: look up, move left 1. ((previousrow - rowWidth)[j - 1])
                                // OR
                                // At least 2 stripes are shifted: look up. ((previousrow - rowWidth)[j])
                                var diff = variants._builderDepth == variants._maxPlusOne ? 1 : 0;
                                currentRowPreviousColumn = Math.Min(result2, (previousRow - variants._const_stripeWidth)[j - diff] + 1);
                            }
                        }
                    }
                    else
                    {
                        // Character match, not-expected case
                        currentRowPreviousColumn = Math.Min(result1 + 1, previousRowPreviousColumn);
                    }

                    previousRowPreviousColumn = previousRowCurrentColumn;

                    previousWordCharacter = wordCharacter;
                    currentRow[j] = currentRowPreviousColumn;
                    // Will make `any` negative if currentRowPreviousColumn > maxErrors
                    any |= (int)currentRowPreviousColumn - (int)variants._maxPlusOne;
                }

                if (any >= 0)
                {
#pragma warning disable S907 // "goto" statement should not be used - TODO: This one hasn't been tested. The resulting asm is smaller, but performance unknown.
                    goto singleReturn;
#pragma warning restore S907 // "goto" statement should not be used
                }

                if (variants._node < 0 && currentRowPreviousColumn < variants._maxPlusOne && variants._builderDepth + variants._maxPlusOne >= variants._wordLength)
                {
                    variants._results.Add(new string(variants._const_builder, 0, (int) variants._builderDepth + 1), variants._invariants._wordCounts[variants._invariants._dawg.GetIndex(ref Unsafe.AsRef<char>(variants._const_builder), variants._builderDepth + 1)]);
                }

                if (variants._builderDepth + 2 >= variants._wordLength + variants._maxPlusOne)
                {
#pragma warning disable S907 // "goto" statement should not be used - TODO: This one hasn't been tested. The resulting asm is smaller, but performance unknown.
                    goto singleReturn;
#pragma warning restore S907 // "goto" statement should not be used
                }

                // We make the strip wider by 1 and avoid bound checking conditionals.
                // This write to memory is cheap because this location should be in the L1 cache due to recent instructions.
                // In addition, we perform this write even when it's not necessary because checking that condition is more expensive.
                var top = currentRow + to;
                *top = currentRowPreviousColumn + 1;

                // Would use a register instead of writing to a variable.
                var counterSpot = currentRow + skip;
                if (*counterSpot >= variants._maxPlusOne)
                {
                    ++counterSpot;
                    while (counterSpot <= top && *counterSpot >= variants._maxPlusOne)
                    {
                        ++counterSpot;
                    }

                    skip = (uint)(counterSpot - currentRow);
                }

                if (skip >= variants._toCache[variants._builderDepth + 1])
                {
#pragma warning disable S907 // "goto" statement should not be used - TODO: This one hasn't been tested. The resulting asm is smaller, but performance unknown.
                    goto singleReturn;
#pragma warning restore S907 // "goto" statement should not be used
                }

                var temp = Abs(variants._node);
                var i = variants._invariants._firstChildEdgeIndex[temp];
                var last = variants._invariants._firstChildEdgeIndex[temp + 1];

                ++variants._builderDepth;
                for (; i < last; ++i)
                {
                    variants._const_builder[variants._builderDepth] = variants._edgeCharacter = variants._invariants._edgeCharacters[i];
                    variants._node = variants._invariants._edgeToNodeIndex[i];
                    MatchCharacter(ref variants, skip);
                }

#pragma warning disable IDE0059 // Value assigned to symbol is never used - Visual studio might tell you this line is unnecessary. Don't believe it.
                --variants._builderDepth;
#pragma warning restore IDE0059 // Value assigned to symbol is never used
#pragma warning disable S1116 // Empty statements should be removed - This statement is important. It allows the label/goto to work.
                singleReturn:;
#pragma warning restore S1116 // Empty statements should be removed
            }

            MatchCharacter(ref variants, 0);
        }

        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public int GetIndex(in string word)
        {
            // Because we want 0-indexed
            var number = -1;
            var currentNode = _rootNodeIndex;
            ref var edgeChildIndex = ref Unsafe.AsRef<int>(_firstChildEdgeIndex);
            ref var edgeNodeIndex = ref Unsafe.AsRef<int>(_edgeToNodeIndex);
            ref var characters = ref Unsafe.AsRef<char>(_edgeCharacter);
            ref var terminals = ref Unsafe.AsRef<ushort>(_reachableTerminalNodes);
            for (var w = 0; w < word.Length; ++w)
            {
                var target = word[w];
                var temp = Abs(currentNode);
                var i = Unsafe.Add(ref edgeChildIndex, temp);
                var lastChildIndex = Unsafe.Add(ref edgeChildIndex, temp + 1);

                for (; i < lastChildIndex; ++i)
                {
                    var nextNode = Unsafe.Add(ref edgeNodeIndex, i);
                    if (Unsafe.Add(ref characters, i) != target)
                    {
                        var nextNodeAbs = Abs(nextNode);
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

#pragma warning disable S1751 // Loops with at most one iteration should be refactored - The analysis tool ignores my goto statement. There is actual iteration.
                return -1;
#pragma warning restore S1751 // Loops with at most one iteration should be refactored

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

            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public string GetWord(int index)
        {
            if (index < 0)
            {
                throw new ArgumentException($"Index was outside the bounds of the array. " +
                    $"Index must be greater than or equal to 0 but was: {index.ToString()}.", nameof(index));
            }

            if (index > Count)
            {
                throw new ArgumentException("Index was outside the bounds of the array. " +
                    $"Index must be less than the number of elements ({Count.ToString()}) but was: {index.ToString()}. " +
                    $"Use {nameof(Dawg)}.{nameof(Count)} to check bounds.", nameof(index));
            }

            // Because we want 0-indexed
            var count = index + 1;
            var currentNode = _rootNodeIndex;
            ref var edgeChildIndex = ref Unsafe.AsRef<int>(_firstChildEdgeIndex);
            ref var edgeNodeIndex = ref Unsafe.AsRef<int>(_edgeToNodeIndex);
            ref var characters = ref Unsafe.AsRef<char>(_edgeCharacter);
            ref var terminals = ref Unsafe.AsRef<ushort>(_reachableTerminalNodes);
            var builder = _builder;
            builder.Length = 0;
            do
            {
                var i = Unsafe.Add(ref edgeChildIndex, currentNode);
                var lastChildIndex = Unsafe.Add(ref edgeChildIndex, currentNode + 1);
                for (; i < lastChildIndex; ++i)
                {
                    var nextNode = Unsafe.Add(ref edgeNodeIndex, i);
                    var nextNumber = Unsafe.Add(ref terminals, Abs(nextNode));
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

                    builder.Append(Unsafe.Add(ref characters, i));
                    break;
                }
            } while (count > 0);

            Debug.Assert(count == 0);
            return builder.ToString();
        }

        // This one has fewer checks because it's private and we only call it with a real word
        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private int GetIndex(ref char word, uint length)
        {
            // Because we want 0-indexed
            var number = -1;
            ref char pointy = ref word;

            var currentNode = _rootNodeIndex;
            ref var edgeChildIndex = ref Unsafe.AsRef<int>(_firstChildEdgeIndex);
            ref var edgeNodeIndex = ref Unsafe.AsRef<int>(_edgeToNodeIndex);
            ref var characters = ref Unsafe.AsRef<char>(_edgeCharacter);
            ref var terminals = ref Unsafe.AsRef<ushort>(_reachableTerminalNodes);
            ref var end = ref Unsafe.Add(ref pointy, (int) length);
            do
            {
                var target = pointy;
                pointy = ref Unsafe.Add(ref pointy, 1);
                var i = Unsafe.Add(ref edgeChildIndex, currentNode);
                var lastChildIndex = Unsafe.Add(ref edgeChildIndex, currentNode + 1);
                do
                {
                    var nextNode = Unsafe.Add(ref edgeNodeIndex, i);
                    if (Unsafe.Add(ref characters, i) != target)
                    {
                        number += Unsafe.Add(ref terminals, Abs(nextNode));
                        continue;
                    }

                    currentNode = nextNode;
                    if (currentNode < 0)
                    {
                        ++number;
                        currentNode = -currentNode;
                    }

                    break;
                } while (++i < lastChildIndex);
            } while (Unsafe.IsAddressLessThan(ref pointy, ref end));

            return number;
        }

        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Abs(int value)
        {
            // Non-branching alternative to: "return value > 0 ? value : -value;"
            var mask = value >> 31;
            return (value + mask) ^ mask;
        }

        public uint Count { get; }

        private readonly int _rootNodeIndex;
        private readonly uint _rootFirstChild;
        private readonly uint _rootLastChild;

        private readonly uint* _firstChildEdgeIndex;
        private readonly int* _edgeToNodeIndex;
        private readonly char* _edgeCharacter;
        private readonly ushort* _reachableTerminalNodes;

        private readonly ulong* _wordCounts;

        private readonly Task[] _tasks;
        private readonly LargePageMemoryChunk _memoryBlock;

        private readonly SingleElementSuggestItemCollection _singleWordResult = new SingleElementSuggestItemCollection();
        private readonly SuggestItemCollection _resultCollection = new SuggestItemCollection(293);
        private readonly CompoundSuggestItemCollection _compoundResultCollection;

        private readonly StringBuilder _builder = new StringBuilder(50);

        private readonly Invariants _invariants;

        public Dawg(Stream stream) : this(CompressedSparseRowPointerGraph.Read(stream))
        {
        }

        public Dawg(CompressedSparseRowPointerGraph compressedSparseRows)
        {
            _rootNodeIndex = compressedSparseRows.RootNodeIndex;
            Count = compressedSparseRows.WordCount;

            _memoryBlock = compressedSparseRows.MemoryChunk;

            _firstChildEdgeIndex = compressedSparseRows.FirstChildEdgeIndex;
            _edgeToNodeIndex = compressedSparseRows.EdgeToNodeIndex;
            _edgeCharacter = compressedSparseRows.EdgeCharacter;
            _reachableTerminalNodes = compressedSparseRows.ReachableTerminalNodes;
            _wordCounts = compressedSparseRows.WordCounts;

            _rootFirstChild = _firstChildEdgeIndex[_rootNodeIndex];
            _rootLastChild = _firstChildEdgeIndex[_rootNodeIndex + 1];

            var rootNodeChildCount = _rootLastChild - _rootFirstChild;
            _compoundResultCollection = new CompoundSuggestItemCollection(rootNodeChildCount);
            _tasks = new Task[rootNodeChildCount];

            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();

            _memoryBlock.Lock();

            _invariants = new Invariants(_wordCounts, _firstChildEdgeIndex, _edgeCharacter, this, _edgeToNodeIndex);
        }

        public void Dispose()
        {
            _memoryBlock.Dispose();
            _singleWordResult.Dispose();
            _compoundResultCollection.Dispose();
        }
    }
}
