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
        public IEnumerable<SuggestItem> Lookup(in string word, int maxEdits)
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

            var wordLength = word.Length;

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
            var toCacheLength = MemoryAlignmentHelper.GetCacheAlignedSize<int>(maxDepth);
            var toCacheBytes = stackalloc byte[toCacheLength];
            var toCache = MemoryAlignmentHelper.GetCacheAlignedStart<int>(toCacheBytes);

            var minTo = Math.Min(wordLength, (2 * maxEdits) + 1);
            for (var depth = 0; depth < maxDepth; ++depth)
            {
                toCache[depth] = Math.Min(Math.Min(depth + 1, wordLength - depth) + maxEdits, minTo);
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
                    tasks[i - rootFirst] = Task.Run(() => Search2Edits(i1, input, wordLength, toCache));
                }
            }
            else
            {
                for (var i = rootFirst; i < rootLast; ++i)
                {
                    //To avoid "access to modified closure" which was causing real issues.
                    var i1 = i;
                    tasks[i - rootFirst] = Task.Run(() => SearchWithEdits(i1, maxEdits, input, wordLength, toCache));
                }
            }

            Task.WaitAll(tasks);

            return _compoundResultCollection;
        }

        /// <summary>
        /// Finds words with edit distance 1 or less from the input.
        /// </summary>
        /// <param name="word">The input character string. This sequence must have a (char)0 appended to the end at position wordLength.</param>
        /// <param name="wordLength">The length of the word. The input must be 1 element longer than this value.</param>
        [LocalsInit(false)]
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private void Search1Edit(char* word, int wordLength)
        {
            var builderByteCount = MemoryAlignmentHelper.GetCacheAlignedSize<char>(wordLength + 1);
            var builderBytes = stackalloc byte[builderByteCount];

            // Technically it's const, but it's contents aren't
            var builderStart = MemoryAlignmentHelper.GetCacheAlignedStart<char>(builderBytes);

            var const_edgeToNodeIndex = _edgeToNodeIndex;
            var const_edgeCharacter = _edgeCharacter;
            var const_firstChildEdgeIndex = _firstChildEdgeIndex;
            var const_reachableTerminalNodes = _reachableTerminalNodes;
            var const_root = _rootNodeIndex;

            int GetIndex(int length)
            {
                // Because we want 0-indexed
                var number = -1;

                var currentNode = const_root;
                var word = builderStart;
                var end = builderStart + length;
                do
                {
                    var i = const_firstChildEdgeIndex[currentNode];
                    var lastChildIndex = const_firstChildEdgeIndex[currentNode + 1];
                    for (; i < lastChildIndex; ++i)
                    {
                        if (const_edgeCharacter[i] != *word)
                        {
                            var nextNode = const_edgeToNodeIndex[i];
                            var nextNodeAbs = Abs(nextNode);
                            number += const_reachableTerminalNodes[nextNodeAbs];
                            continue;
                        }

                        currentNode = const_edgeToNodeIndex[i];
                        if (currentNode < 0)
                        {
                            ++number;
                            currentNode = -currentNode;
                        }

                        break;
                    }

                    ++word;
                } while (word < end);

                return number;
            }

            var nextBuilderPosition = builderStart + 1;

            var const_results = _resultCollection;
            var const_wordEnd = word + wordLength;
            var const_wordCount = _wordCounts;

            void SearchWithoutEdits(int currentNode, char* input)
            {
                var t = const_edgeCharacter;
                var spot = input;
                do
                {
                    var temp = Abs(currentNode);
                    var g = const_firstChildEdgeIndex[temp];
                    var gLast = const_firstChildEdgeIndex[temp + 1];

                    var currentTarget = *spot;
                    for (; g < gLast; ++g)
                    {
                        if (currentTarget != t[g])
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
                    var inputRemaining = (int)(const_wordEnd - input);
                    Unsafe.CopyBlock(nextBuilderPosition, input, (uint)(inputRemaining * sizeof(char)));

                    var resultLength = (int)(nextBuilderPosition - builderStart) + inputRemaining;
                    const_results.Add(new SuggestItem(new string(builderStart, 0, resultLength), const_wordCount[GetIndex(resultLength)]));
                }
            }

            var nextNode = _rootNodeIndex;
            var currentWordCharacter = word;
            var nextWordCharacter = word + 1;
            var builderIndex = builderStart;

            for (var index = 0; index < wordLength - 2; ++index)
            {
                nextNode = Abs(nextNode);
                var i = const_firstChildEdgeIndex[nextNode];
                var iLast = const_firstChildEdgeIndex[nextNode + 1];
                nextNode = int.MaxValue;
                for (; i < iLast; ++i)
                {
                    var firstEdgeChar = const_edgeCharacter[i];
                    var edgeNode = const_edgeToNodeIndex[i];
                    *builderIndex = firstEdgeChar;

                    if (firstEdgeChar == *currentWordCharacter)
                    {
                        nextNode = edgeNode;
                        continue;
                    }

                    if (firstEdgeChar != *nextWordCharacter)
                    {
                        //substitution and continue
                        SearchWithoutEdits(edgeNode, nextWordCharacter);

                        //insertion and continue
                        SearchWithoutEdits(edgeNode, currentWordCharacter);

                        continue;
                    }

                    // firstEdgeChar == secondTarget
                    var temp = Abs(edgeNode);
                    var k = const_firstChildEdgeIndex[temp];
                    var kLast = const_firstChildEdgeIndex[temp + 1];

                    nextBuilderPosition++;
                    var thirdWordCharacter = nextWordCharacter + 1;
                    for (; k < kLast; ++k)
                    {
                        var secondEdgeChar = const_edgeCharacter[k];
                        if (secondEdgeChar == *currentWordCharacter)
                        {
                            // Can't use builderCurrent in this case because we incremented it outside this loop.
                            *(builderIndex + 1) = secondEdgeChar;
                            var noEditNode = const_edgeToNodeIndex[k];
                            // insertion + match + withoutEdits
                            SearchWithoutEdits(noEditNode, nextWordCharacter);
                            // transposition + withoutEdits
                            SearchWithoutEdits(noEditNode, thirdWordCharacter);
                        }
                        else if (secondEdgeChar == *nextWordCharacter)
                        {
                            // Can't use builderCurrent in this case because we incremented it outside this loop.
                            *(builderIndex + 1) = secondEdgeChar;
                            // substitution + match + withoutEdit
                            SearchWithoutEdits(const_edgeToNodeIndex[k], thirdWordCharacter);
                        }

                        // ReSharper disable once InvertIf
                        if (secondEdgeChar == *thirdWordCharacter)
                        {
                            if (index + 3 == wordLength)
                            {
                                // ReSharper disable once InvertIf
                                if (const_edgeToNodeIndex[k] < 0)
                                {
                                    // Can't use builderCurrent in this case because we incremented it outside this loop.
                                    *(builderIndex + 1) = secondEdgeChar;
                                    //deletion + match + match + end
                                    const_results.Add(new SuggestItem(new string(builderStart, 0, wordLength + 1), const_wordCount[GetIndex(wordLength + 1)]));
                                }
                            }
                            else
                            {
                                // Can't use builderCurrent in this case because we incremented it outside this loop.
                                *(builderIndex + 1) = secondEdgeChar;
                                //deletion + match + match + continue
                                SearchWithoutEdits(const_edgeToNodeIndex[k], thirdWordCharacter + 1);
                            }
                        }
                    }

                    nextBuilderPosition--;
                }

                if (nextNode == int.MaxValue)
                {
                    return;
                }

                *builderIndex = *currentWordCharacter;
                builderIndex++;
                nextBuilderPosition++;
                currentWordCharacter++;
                nextWordCharacter++;
            }

            //case : index + 2 == wordLength
            {
                nextNode = Abs(nextNode);
                var i = const_firstChildEdgeIndex[nextNode];
                var iLast = const_firstChildEdgeIndex[nextNode + 1];
                nextNode = int.MaxValue;
                for (; i < iLast; ++i)
                {
                    var firstEdgeChar = const_edgeCharacter[i];
                    var edgeNode = const_edgeToNodeIndex[i];
                    *builderIndex = firstEdgeChar;

                    if (firstEdgeChar == *currentWordCharacter)
                    {
                        nextNode = edgeNode;
                        continue;
                    }

                    if (firstEdgeChar != *nextWordCharacter)
                    {
                        //substitution and continue
                        SearchWithoutEdits(edgeNode, nextWordCharacter);

                        //insertion and continue
                        SearchWithoutEdits(edgeNode, currentWordCharacter);

                        continue;
                    }

                    // firstEdgeChar == secondTarget
                    var temp = Abs(edgeNode);
                    var k = const_firstChildEdgeIndex[temp];
                    var kLast = const_firstChildEdgeIndex[temp + 1];

                    if (edgeNode < 0)
                    {
                        //deletion + match:
                        const_results.Add(new SuggestItem(new string(builderStart, 0, wordLength -1), const_wordCount[GetIndex(wordLength - 1)]));
                    }

                    for (; k < kLast; ++k)
                    {
                        var secondEdgeChar = const_edgeCharacter[k];
                        if (secondEdgeChar == *currentWordCharacter)
                        {
                            var currentNode = const_edgeToNodeIndex[k];
                            if (currentNode < 0)
                            {
                                currentNode = -currentNode;
                                //transposition:
                                *nextBuilderPosition = secondEdgeChar;
                                const_results.Add(new SuggestItem(new string(builderStart, 0, wordLength), const_wordCount[GetIndex(wordLength)]));
                            }

                            var m = const_firstChildEdgeIndex[currentNode];
                            var mLast = const_firstChildEdgeIndex[currentNode + 1];
                            for (; m < mLast; ++m)
                            {
                                var thirdEdgeChar = const_edgeCharacter[m];
                                if (thirdEdgeChar == *nextWordCharacter && const_edgeToNodeIndex[m] < 0)
                                {
                                    *nextBuilderPosition = secondEdgeChar;
                                    *(nextBuilderPosition + 1) = thirdEdgeChar;
                                    //insertion + match
                                    const_results.Add(new SuggestItem(new string(builderStart, 0, wordLength + 1), const_wordCount[GetIndex(wordLength + 1)]));
                                    break;
                                }
                            }
                        }
                        else if (secondEdgeChar == *nextWordCharacter && const_edgeToNodeIndex[k] < 0)
                        {
                            //substitution + match:
                            *nextBuilderPosition = secondEdgeChar;
                            const_results.Add(new SuggestItem(new string(builderStart, 0, wordLength), const_wordCount[GetIndex(wordLength)]));
                        }
                    }
                }

                if (nextNode == int.MaxValue)
                {
                    return;
                }

                *builderIndex = *currentWordCharacter;
                builderIndex++;
                nextBuilderPosition++;
                currentWordCharacter++;
            }

            // case: index + 1 == wordLength
            if (nextNode < 0)
            {
                nextNode = -nextNode;
                //delete + end
                const_results.Add(new SuggestItem(new string(builderStart, 0, wordLength - 1), const_wordCount[GetIndex(wordLength - 1)]));
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
                    const_results.Add(new SuggestItem(new string(builderStart, 0, wordLength), const_wordCount[GetIndex(wordLength)]));
                    edgeNode = -edgeNode;
                }

                var j = const_firstChildEdgeIndex[edgeNode];
                var jLast = const_firstChildEdgeIndex[edgeNode + 1];
                if (firstEdgeChar != *currentWordCharacter)
                {
                    for (; j < jLast; ++j)
                    {
                        var secondEdgeChar = const_edgeCharacter[j];
                        if (secondEdgeChar == *currentWordCharacter && const_edgeToNodeIndex[j] < 0)
                        {
                            // insertions + match at end of word.
                            *nextBuilderPosition = *currentWordCharacter;
                            const_results.Add(new SuggestItem(new string(builderStart, 0, wordLength + 1), const_wordCount[GetIndex(wordLength + 1)]));
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
                            *nextBuilderPosition = const_edgeCharacter[j];
                            const_results.Add(new SuggestItem(new string(builderStart, 0, wordLength + 1), const_wordCount[GetIndex(wordLength + 1)]));
                        }
                    }
                }
            }
        }

        [LocalsInit(false)]
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private void Search2Edits(int edge, // Not captured
            char* word, int wordLength, int* toCache)
        {
            // TODO: Test most of these locals to see if it's faster to capture or re-calculate them every time.

            // The real stripeWidth is usually 2*max + 1 = 5
            // Normally we would do a bound check and calculate a value for the cell in previousRow directly after our stripe.
            // Instead we pre-assign the cell when we have the values handy and then ignore bound checking.
            // For this reason, the stripeWidth gets an additional + 1, making it 2 * max + 2 = 6
            // TODO: Testing with 8 for cache boundary divisibility. Shouldn't matter?
            const int stripeWidth = 8;

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

            // These 3 variables, in addition to the 2 later, are used for parameter passing in MatchCharacterX
            var previousRow = editMatrix + (3 * stripeWidth);
            var currentRow = previousRow + stripeWidth;
            var builderDepth = 4;

            // These two variables are used for parameter passing between MatchCharacter1-3
            var currentCharacter = builder[0] = edgeCharacters[edge];
            var node = edgeToNodeIndex[edge];

            void MatchCharacterX(int skipOriginal)
            {
                var skip = skipOriginal;
                // This is the value for the column directly before our diagonal stripe.
                // In this case, 3 is too many anyways, so no need to compute it.
                var currentRowPreviousColumn = 3;

                // Normally the strip would travel diagonally through the matrix. We shift it left to keep it starting at 0.
                // previousRowOffset == 1 once we have started shifting.
                // Our stripe ignores early characters once we've gone deep enough.

                var previousRowPreviousColumn = previousRow[skip];
                var wordWithOffset = word + builderDepth - 2;
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
                        if ((currentCharacter != previousWordCharacter) || (builder[builderDepth - 1] != wordCharacter))
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
                    results.Add(new SuggestItem(new string(builder, 0, builderDepth + 1), wordCount[GetIndex(builder, builderDepth + 1)]));
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

                // TODO: Is it faster to use this temp variable and not assign to skip as often?
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
                previousRow = currentRow;
                currentRow += stripeWidth;
                for (; i < last; ++i)
                {
                    currentCharacter = builder[builderDepth] = edgeCharacters[i];
                    node = edgeToNodeIndex[i];
                    MatchCharacterX(skip);
                }

                builderDepth--;
                currentRow = previousRow;
                previousRow -= stripeWidth;
            }

            // Some variables are not used in all MatchCharacter functions. Moved them closer to usage.
            // How do closures work here, does the struct getting passed contain all of them no matter where declared?
            // Does the deeper struct contain repeated information?
            // TODO: Consider building a nested struct myself. Nested as in union, not reference or copy.
            var to4 = toCache[4];
            var to3 = toCache[3];
            var row1 = editMatrix + stripeWidth;
            var row2 = editMatrix + (2 * stripeWidth);
            var row3 = editMatrix + (3 * stripeWidth);

            void MatchCharacter3(int skipOriginal)
            {
                var skip = skipOriginal;
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
                    results.Add(new SuggestItem(new string(builder, 0, 4), wordCount[GetIndex(builder, 4)]));
                }

                if (wordLength < 3)
                {
                    return;
                }

                // We make the strip wider by 1 and avoid bound checking conditionals.
                // This write to memory is cheap because this location should be in the L1 cache due to recent instructions.
                // In addition, we perform this write even when it's not necessary because checking that condition is more expensive.
                row3[to3] = currentRowPreviousColumn + 1;

                //TODO: Is it faster to use a temp for this loop and write to skip after? Would save some writes to memory? Unless it's registerized already.
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
                            // "Debug.Assert((previousRow - stripeWidth)[j - 2] == j + 1);"
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
                    results.Add(new SuggestItem(new string(builder, 0, 3), wordCount[GetIndex(builder, 3)]));
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
                //TODO: Is it faster to use a temp for this loop and write to skip after? Would save some writes to memory? Unless it's registerized already.
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
                    results.Add(new SuggestItem(new string(builder, 0, 2), wordCount[GetIndex(builder, 2)]));
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
                results.Add(new SuggestItem(new string(builder, 0, 1), wordCount[GetIndex(builder, 1)]));
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

        [LocalsInit(false)]
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private void SearchWithEdits(int edge, // Not captured
            int max, char* first, int wordLength, int* toCache)
        {
            var index = _edgeToNodeIndex;
            var characters = _edgeCharacter;
            var edgeIndex = _firstChildEdgeIndex;
            var wordCount = _wordCounts;
            var results = _compoundResultCollection.Bags[edge - _rootFirstChild];

            // TODO: test if this is faster captured in the closure or re-calculated every time.
            // The real stripeWidth is 2*max + 1
            // Normally we would do a bound check and calculate a value for the cell in previousRow directly after our stripe.
            // Instead we pre-assign the cell when we have the values handy and then ignore bound checking.
            var stripeWidth = (2 * max) + 2;

            var previousRow = stackalloc int[(wordLength + max + 1) * stripeWidth];

            // We're skipping the left-most column from the original algorithm. It's just a constant so ignore it.
            for (var x = 0; x <= max; ++x)
            {
                previousRow[x] = x + 1;
            }

            var builder = stackalloc char[wordLength + max];
            var edgeCharacter = characters[edge];
            builder[0] = edgeCharacter;

            var node = index[edge];

            // This pointer is incremented and decremented before and after recursive calls.
            // TODO: is it faster to have a local instead, and use builderDepth * stripeWidth
            var currentRow = previousRow + stripeWidth;
            var builderDepth = 0;

            void MatchCharacter(int skip)
            {
                // This is the value for the column directly before our diagonal stripe.
                // It's the one we avoided setting in previousRow earlier.
                // TODO: would it be faster to access it from memory rather than compute over and over?
                var currentRowPreviousColumn = builderDepth + skip + 1;

                char previousWordCharacter;
                int previousRowPreviousColumn;
                char* firstWithOffset;

                // Normally the strip would travel diagonally through the matrix. We shift it left to keep it starting at 0.
                // previousRowOffset == 1 once we have started shifting.
                int previousRowOffset;
                // Our stripe ignores early characters once we've gone deep enough.
                var wordArrayOffset = builderDepth - max;

                if (wordArrayOffset <= 0)
                {
                    // As with currentRowPreviousColumn, this is the cell before our stripe, so it can be computed instead of retrieved.
                    previousRowPreviousColumn = currentRowPreviousColumn - 1;
                    firstWithOffset = first;
                    previousWordCharacter = (char)0;
                    previousRowOffset = 0;
                }
                else
                {
                    previousRowPreviousColumn = previousRow[skip];
                    firstWithOffset = first + wordArrayOffset;
                    previousWordCharacter = firstWithOffset[skip];
                    previousRowOffset = 1;
                }

                var any = 0;
                var to = toCache[builderDepth];
                for (var j = skip; j < to; ++j)
                {
                    var previousRowCurrentColumn = previousRow[j + previousRowOffset];
                    var wordCharacter = firstWithOffset[j];
                    var result1 = Math.Min(currentRowPreviousColumn, previousRowCurrentColumn);
                    if (edgeCharacter != wordCharacter)
                    {
                        var result2 = Math.Min(result1, previousRowPreviousColumn) + 1;
                        if ((edgeCharacter != previousWordCharacter) || (builderDepth == 0) || (builder[builderDepth - 1] != wordCharacter))
                        {
                            // Non-match.
                            // Expected case.
                            currentRowPreviousColumn = result2;
                        }
                        else
                        {
                            // Transposition case!
                            // BuilderDepth > 0: Otherwise there is no previous character to transpose.
                            if (builderDepth == 1)
                            {
                                // There is no (previousRow - rowWidth). It would be row 0 in the Levenshtein matrix, the one with no letters.

                                // TODO: sanity check the following statement:
                                // In this case, the cell value is simply it's column # j

                                //when there is no previousPreviousRow to [-2] into
                                currentRowPreviousColumn = Math.Min(result2, j);
                            }
                            else if (builderDepth <= max)
                            {
                                // The stripes haven't started shifting yet: look up, move left 2. ((previousrow - rowWidth)[j - 2])
                                // If we can't move left:
                                if (j == 1)
                                {
                                    currentRowPreviousColumn = Math.Min(result2, builderDepth);
                                }
                                else
                                {
                                    currentRowPreviousColumn = Math.Min(result2, (previousRow - stripeWidth)[j - 2] + 1);
                                }
                            }
                            else
                            {
                                // It's the first stripe shift: look up, move left 1. ((previousrow - rowWidth)[j - 1])
                                // OR
                                // At least 2 stripes are shifted: look up. ((previousrow - rowWidth)[j])
                                var diff = builderDepth == max + 1 ? 1 : 0;
                                currentRowPreviousColumn = Math.Min(result2, (previousRow - stripeWidth)[j - diff] + 1);
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
                    any |= currentRowPreviousColumn - max - 1;
                }

                if (any >= 0)
                {
                    return;
                }

                // TODO: can I simplify these checks?
                if (node < 0 && currentRowPreviousColumn <= max && builderDepth + 1 + max >= wordLength)
                {
                    results.Add(new SuggestItem(new string(builder, 0, builderDepth + 1), wordCount[GetIndex(builder, builderDepth + 1)]));
                }

                if (builderDepth + 1 >= wordLength + max)
                {
                    return;
                }

                // We make the strip wider by 1 and avoid bound checking conditionals.
                // This write to memory is cheap because this location should be in the L1 cache due to recent instructions.
                // In addition, we perform this write even when it's not necessary because checking that condition is more expensive.
                var top = currentRow + to;
                *top = currentRowPreviousColumn + 1;

                // Would use a register instead of writing to a variable.
                var counterSpot = currentRow + skip;
                if (*counterSpot > max)
                {
                    ++counterSpot;
                    while (counterSpot <= top && *counterSpot > max)
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
                previousRow = currentRow;
                currentRow += stripeWidth;
                for (; i < last; ++i)
                {
                    builder[builderDepth] = edgeCharacter = characters[i];
                    node = index[i];
                    MatchCharacter(skip);
                }

                // Visual studio might tell you these 3 lines are unnecessary. Don't believe it.
#pragma warning disable IDE0059 // Value assigned to symbol is never used
                currentRow = previousRow;
                previousRow -= stripeWidth;
                --builderDepth;
#pragma warning restore IDE0059 // Value assigned to symbol is never used
            }

            MatchCharacter(0);
        }

        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public int GetIndex(in string word)
        {
            // Because we want 0-indexed
            var number = -1;
            var currentNode = _rootNodeIndex;
            var index = _edgeToNodeIndex;
            var characters = _edgeCharacter;
            var edgeIndex = _firstChildEdgeIndex;
            var reachableNodes = _reachableTerminalNodes;
            for (var w = 0; w < word.Length; ++w)
            {
                var target = word[w];
                var temp = Abs(currentNode);
                var i = edgeIndex[temp];
                var lastChildIndex = edgeIndex[temp + 1];

                for (; i < lastChildIndex; ++i)
                {
                    if (characters[i] != target)
                    {
                        number += reachableNodes[Abs(index[i])];
                        continue;
                    }

                    currentNode = index[i];
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

            if (index > this.Count)
            {
                throw new ArgumentException("Index was outside the bounds of the array. " +
                    $"Index must be less than the number of elements ({Count.ToString()}) but was: {index.ToString()}. " +
                    $"Use {nameof(Dawg)}.{nameof(Count)} to check bounds.", nameof(index));
            }

            // Because we want 0-indexed
            var count = index + 1;
            var currentNode = _rootNodeIndex;
            var edgeNodeIndex = _edgeToNodeIndex;
            var characters = _edgeCharacter;
            var edgeIndex = _firstChildEdgeIndex;
            var reachableNodes = _reachableTerminalNodes;
            var builder = _builder;
            builder.Length = 0;
            while (count > 0)
            {
                var i = edgeIndex[currentNode];
                var lastChildIndex = edgeIndex[currentNode + 1];
                for (; i < lastChildIndex; ++i)
                {
                    var nextNode = edgeNodeIndex[i];
                    var nextNumber = reachableNodes[Abs(nextNode)];
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

                    builder.Append(characters[i]);
                    break;
                }
            }

            Debug.Assert(count == 0);
            return builder.ToString();
        }

        // This one has fewer checks because it's private and we only call it with a real word
        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private int GetIndex(char* word, int length)
        {
            // Because we want 0-indexed
            var number = -1;

            var currentNode = _rootNodeIndex;
            var terminals = _reachableTerminalNodes;
            var index = _edgeToNodeIndex;
            var characters = _edgeCharacter;
            var edgeIndex = _firstChildEdgeIndex;
            var end = word + length;
            while (word < end)
            {
                var target = *word;
                ++word;
                var i = edgeIndex[currentNode];
                var lastChildIndex = edgeIndex[currentNode + 1];

                for (; i < lastChildIndex; ++i)
                {
                    if (characters[i] != target)
                    {
                        var nextNode = index[i];
                        var nextNodeAbs = Abs(nextNode);
                        number += terminals[nextNodeAbs];
                        continue;
                    }

                    currentNode = index[i];
                    if (currentNode < 0)
                    {
                        ++number;
                        currentNode = -currentNode;
                    }

                    break;
                }
            }

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

        public int Count { get; }

        private readonly int _rootNodeIndex;
        private readonly int _rootFirstChild;
        private readonly int _rootLastChild;

        private readonly int* _firstChildEdgeIndex;
        private readonly int* _edgeToNodeIndex;
        private readonly char* _edgeCharacter;
        private readonly ushort* _reachableTerminalNodes;

        private readonly long* _wordCounts;

        private readonly Task[] _tasks;
        private readonly LargePageMemoryChunk _memoryBlock;

        private readonly SingleElementSuggestItemCollection _singleWordResult = new SingleElementSuggestItemCollection();
        private readonly SuggestItemCollection _resultCollection = new SuggestItemCollection(293);
        private readonly CompoundSuggestItemCollection _compoundResultCollection;

        private readonly StringBuilder _builder = new StringBuilder(50);

        [SuppressMessage("Critical Code Smell", "S1215:\"GC.Collect\" should not be called", Justification = "This method allocates multiple large arrays in the Large Object Heap")]
        private Dawg(int rootNodeIndex, int[] firstChildEdgeIndex, int[] edgeToNodeIndex, char[] edgeCharacter, ushort[] reachableTerminalNodes, long[] wordCounts)
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();

            _rootNodeIndex = rootNodeIndex;
            Count = wordCounts.Length;

            _memoryBlock = LargePageMemoryChunk.Builder()
                .ReserveAligned(firstChildEdgeIndex)
                .ReserveAligned(edgeToNodeIndex)
                .ReserveAligned(edgeCharacter)
                .ReserveAligned(reachableTerminalNodes)
                .ReserveAligned(wordCounts)
                .Allocate();

            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();

            _firstChildEdgeIndex = _memoryBlock.CopyArrayAligned(firstChildEdgeIndex);
            _edgeToNodeIndex = _memoryBlock.CopyArrayAligned(edgeToNodeIndex);
            _edgeCharacter = _memoryBlock.CopyArrayAligned(edgeCharacter);
            _reachableTerminalNodes = _memoryBlock.CopyArrayAligned(reachableTerminalNodes);
            _wordCounts = _memoryBlock.CopyArrayAligned(wordCounts);

            _rootFirstChild = _firstChildEdgeIndex[_rootNodeIndex];
            _rootLastChild = _firstChildEdgeIndex[_rootNodeIndex + 1];

            var rootNodeChildCount = _rootLastChild - _rootFirstChild;
            _compoundResultCollection = new CompoundSuggestItemCollection(rootNodeChildCount);
            _tasks = new Task[rootNodeChildCount];

            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();

            _memoryBlock.Lock();
        }

        public Dawg(Stream stream) : this(CompressedSparseRowGraph.Read(stream))
        {
        }

        public Dawg(CompressedSparseRowGraph compressedSparseRows) : this(
            compressedSparseRows.RootNodeIndex,
            compressedSparseRows.FirstChildEdgeIndex,
            compressedSparseRows.EdgeToNodeIndex,
            compressedSparseRows.EdgeCharacter,
            compressedSparseRows.ReachableTerminalNodes,
            compressedSparseRows.WordCounts)
        {
        }

        public void Dispose()
        {
            _memoryBlock.Dispose();
            _singleWordResult.Dispose();
            _resultCollection.Dispose();
            _compoundResultCollection.Dispose();
        }
    }
}
