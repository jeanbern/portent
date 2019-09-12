using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using JBP;
using LocalsInit;

namespace portent
{
    public sealed unsafe class Dawg : IDisposable
    {
        private readonly SingleElementSuggestItemCollection _oneSuggestion = new SingleElementSuggestItemCollection();
        private readonly SuggestItemCollection _fakeList = new SuggestItemCollection(293);
        private readonly CompoundSuggestItemCollection _listContainer;

        [LocalsInit(false)]
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public IEnumerable<SuggestItem> Lookup(in string word, int maxEdits)
        {
            if (maxEdits == 0)
            {
                var index = GetIndex(word);
                if (index >= 0)
                {
                    _oneSuggestion.Update(word, _wordCounts[index]);
                    return _oneSuggestion;
                }
                else
                {
                    return Enumerable.Empty<SuggestItem>();
                }
            }

            var wordLength = word.Length;
            var input = stackalloc char[wordLength + 1];
            for (var x = 0; x < word.Length; ++x)
            {
                input[x] = word[x];
            }

            if (maxEdits == 1)
            {
                input[wordLength] = (char)0;
                _fakeList.Clear();
                OneEditLookup(input, word.Length);
                return _fakeList;
            }

            _listContainer.Clear();

            var minTo = Math.Min(wordLength, (2 * maxEdits) + 1);
            var maxDepth = wordLength + maxEdits;
            var toCache = stackalloc int[maxDepth];
            for (var depth = 0; depth < maxDepth; ++depth)
            {
                toCache[depth] = Math.Min(Math.Min(depth + 1, wordLength - depth) + maxEdits, minTo);
            }

            //fixed (char* wPointer = word)
            //Unsafe.CopyBlock(input, wPointer, (uint) (word.Length * sizeof(char)));

            /*
            Parallel.For(i, last, il => ParameterizedTaskStartV6(il, maxEdits, input, word.Length, toCache));
            Enumerable.Range(i, last - i).AsParallel().WithDegreeOfParallelism(12).ForAll(il => ParameterizedTaskStartV6(il, maxEdits, input, word.Length, toCache));
            //*/

            var tasks = _tasks;
            var rootFirst = _rootFirstChild;
            var rootLast = _rootLastChild;

            if (maxEdits == 2)
            {
                for (var i = rootFirst; i < rootLast; ++i)
                {
                    //To avoid "access to modified closure" which was causing real issues.
                    var i1 = i;
                    tasks[i - rootFirst] = Task.Run(() => ParameterizedTaskStartV6Max2(i1, input, wordLength, toCache));
                }
            }
            else
            {
                for (var i = rootFirst; i < rootLast; ++i)
                {
                    //To avoid "access to modified closure" which was causing real issues.
                    var i1 = i;
                    tasks[i - rootFirst] = Task.Run(() => ParameterizedTaskStartV6(i1, maxEdits, input, wordLength, toCache));
                }
            }

            Task.WaitAll(tasks);

            return _listContainer;
        }

        /// <summary>
        /// Finds words with edit distance 1 or less from the input.
        /// </summary>
        /// <param name="input">The input character string. This sequence must have a (char)0 appended to the end at position wordLength.</param>
        /// <param name="wordLength">The length of the word. The input must be 1 element longer than this value.</param>
        [LocalsInit(false)]
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private void OneEditLookup(char* input, int wordLength)
        {
            var builder = stackalloc char[wordLength + 1];
            var nextNode = _rootNodeIndex;
            var edgeToNodeIndex = _edgeToNodeIndex;
            var characters = _edgeCharacter;
            var edgeIndex = _firstChildEdgeIndex;
            var results = _fakeList;
            var wordCount = _wordCounts;

            //TODO: unroll this to avoid conditional secondTarget = ... ? ... : ...;
            for (var index = 0; index < wordLength; ++index)
            {
                if (nextNode < 0)
                {
                    nextNode = -nextNode;
                    if (index + 1 == wordLength)
                    {
                        //delete + end
                        results.Add(new SuggestItem(new string(builder, 0, index), wordCount[GetIndex(builder, index)]));
                    }
                }

                var firstTarget = input[index];
                var i = edgeIndex[nextNode];
                var last = edgeIndex[nextNode + 1];
                nextNode = int.MaxValue;
                for (; i < last; ++i)
                {
                    var firstEdgeChar = characters[i];
                    var edgeNode = edgeToNodeIndex[i];
                    builder[index] = firstEdgeChar;
                    var secondTarget = input[index + 1];

                    if (firstEdgeChar == firstTarget)
                    {
                        nextNode = edgeNode;
                        continue;
                    }

                    if (firstEdgeChar != secondTarget)
                    {
                        if (secondTarget == (char)0)
                        {
                            if (edgeNode < 0)
                            {
                                //substitution and word ended
                                results.Add(new SuggestItem(new string(builder, 0, index + 1), wordCount[GetIndex(builder, index + 1)]));
                            }
                        }
                        else
                        {
                            //substitution and continue
                            SearchWithoutEdits(edgeNode, input + index + 1, builder, index + 1, wordLength - index - 1);
                        }

                        //insertion and continue
                        SearchWithoutEdits(edgeNode, input + index, builder, index + 1, wordLength - index);

                        continue;
                    }

                    //firstEdgeChar == secondTarget
                    var temp = Abs(edgeNode);
                    var k = edgeIndex[temp];
                    var kLast = edgeIndex[temp + 1];
                    if (index + 2 == wordLength)
                    {
                        if (edgeNode < 0)
                        {
                            //deletion + match:
                            results.Add(new SuggestItem(new string(builder, 0, index + 1), wordCount[GetIndex(builder, index + 1)]));
                        }

                        for (; k < kLast; ++k)
                        {
                            var secondEdgeChar = characters[k];
                            if (secondEdgeChar == firstTarget)
                            {
                                var currentNode = edgeToNodeIndex[k];
                                if (currentNode < 0)
                                {
                                    currentNode = -currentNode;
                                    //transposition:
                                    builder[index + 1] = secondEdgeChar;
                                    results.Add(new SuggestItem(new string(builder, 0, index + 2), wordCount[GetIndex(builder, index + 2)]));
                                }

                                var m = edgeIndex[currentNode];
                                var mLast = edgeIndex[currentNode + 1];
                                for (; m < mLast; ++m)
                                {
                                    var thirdEdgeChar = characters[m];
                                    if (thirdEdgeChar == secondTarget && edgeToNodeIndex[m] < 0)
                                    {
                                        builder[index + 1] = secondEdgeChar;
                                        builder[index + 2] = thirdEdgeChar;
                                        //insertion + match
                                        results.Add(new SuggestItem(new string(builder, 0, index + 3), wordCount[GetIndex(builder, index + 3)]));
                                        break;
                                    }
                                }
                            }
                            else if (secondEdgeChar == secondTarget && edgeToNodeIndex[k] < 0)
                            {
                                //substitution + match:
                                builder[index + 1] = secondEdgeChar;
                                results.Add(new SuggestItem(new string(builder, 0, index + 2), wordCount[GetIndex(builder, index + 2)]));
                            }
                        }

                        continue;
                    }

                    var thirdTarget = input[index + 2];
                    for (; k < kLast; ++k)
                    {
                        var secondEdgeChar = characters[k];
                        if (secondEdgeChar == firstTarget)
                        {
                            builder[index + 1] = secondEdgeChar;
                            var noEditNode = edgeToNodeIndex[k];
                            //insertion + match + withoutEdits
                            SearchWithoutEdits(noEditNode, input + index + 1, builder, index + 2, wordLength - index - 1);
                            //transposition + withoutEdits
                            SearchWithoutEdits(noEditNode, input + index + 2, builder, index + 2, wordLength - index - 2);
                        }
                        else if (secondEdgeChar == secondTarget)
                        {
                            builder[index + 1] = secondEdgeChar;
                            //substitution + match + withoutEdit
                            SearchWithoutEdits(edgeToNodeIndex[k], input + index + 2, builder, index + 2, wordLength - index - 2);
                        }
                        // ReSharper disable once InvertIf
                        if (secondEdgeChar == thirdTarget)
                        {
                            if (index + 3 == wordLength)
                            {
                                // ReSharper disable once InvertIf
                                if (edgeToNodeIndex[k] < 0)
                                {
                                    builder[index + 1] = secondEdgeChar;
                                    //deletion + match + match + end
                                    results.Add(new SuggestItem(new string(builder, 0, index + 2), wordCount[GetIndex(builder, index + 2)]));
                                }
                            }
                            else
                            {
                                builder[index + 1] = secondEdgeChar;
                                //deletion + match + match + continue
                                SearchWithoutEdits(edgeToNodeIndex[k], input + index + 3, builder, index + 2, wordLength - index - 3);
                            }
                        }
                    }
                }

                if (nextNode == int.MaxValue)
                {
                    return;
                }

                builder[index] = input[index];
            }

            if (nextNode < 0)
            {
                nextNode = -nextNode;
                //match at end of word.
                results.Add(new SuggestItem(new string(builder, 0, wordLength), wordCount[GetIndex(builder, wordLength)]));
            }

            var j = edgeIndex[nextNode];
            var jLast = edgeIndex[nextNode + 1];
            for (; j < jLast; ++j)
            {
                if (edgeToNodeIndex[j] < 0)
                {
                    //match + insertions at end of word.
                    builder[wordLength] = characters[j];
                    results.Add(new SuggestItem(new string(builder, 0, wordLength + 1), wordCount[GetIndex(builder, wordLength + 1)]));
                }
            }
        }

        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Abs(int value)
        {
            var mask = value >> 31;
            return (value + mask) ^ mask;
            //return value > 0 ? value : -value;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private void SearchWithoutEdits(int currentNode, char* input, char* builder, int builderLength, int inputRemaining)
        {
            var spot = input;
            var last = input + inputRemaining;

            var edgeToNodeIndex = _edgeToNodeIndex;
            var edgeCharacters = _edgeCharacter;
            var firstChildIndex = _firstChildEdgeIndex;

            do
            {
                var nextCharacter = *spot;
                var temp = Abs(currentNode);
                var i = firstChildIndex[temp];
                var lastChildIndex = firstChildIndex[temp + 1];

                for (; i < lastChildIndex; ++i)
                {
                    if (nextCharacter != edgeCharacters[i])
                    {
                        continue;
                    }

                    currentNode = edgeToNodeIndex[i];
                    ++spot;
                    goto next;
                }

                return;

next:;
            } while (spot < last);

            // ReSharper disable once InvertIf
            if (currentNode < 0)
            {
                var buffP = builder + builderLength;
                Unsafe.CopyBlock(buffP, input, (uint)(inputRemaining * sizeof(char)));

                // match{1..*}
                _fakeList.Add(new SuggestItem(new string(builder, 0, builderLength + inputRemaining), _wordCounts[GetIndex(builder, builderLength + inputRemaining)]));
            }
        }

        [LocalsInit(false)]
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private void ParameterizedTaskStartV6Max2(int edge, // Not captured
            char* word, int wordLength, int* toCache)
        {
            //TODO: Test most of these locals to see if it's faster to capture or re-calculate them every time.

            // Normally we would do a bound check and calculate a value for the cell in previousRow directly after our stripe.
            // Instead we pre-assign the cell when we have the values handy and then ignore bound checking. See: {05054B58-4553-4DAD-915F-25A3D4E3A735}
            // For this reason, the stripeWidth gets a +1
            // The real stripeWidth is usually 2*max + 1 = 5
            //TODO: Testing with 8 for cache boundary divisibility. Shouldn't matter?
            const int stripeWidth = 8;

            // +2 for maxEdits = 2, then distribute the multiplication to take advantage of const.
            // Would be otherwise be: (wordLength + maxEdits)*stripeWidth
            var matrixLength = MemoryAlignmentHelper.GetCacheAlignedSize<int>((wordLength * stripeWidth) + (2 * stripeWidth));
            var editMatrixAlloc = stackalloc byte[matrixLength];

            var builderByteCount = MemoryAlignmentHelper.GetCacheAlignedSize<char>(wordLength + 2);
            var builderBytes = stackalloc byte[builderByteCount];

            //TODO: If these are captured in a closure anyways, is it faster to just look them up?
            var edgeToNodeIndex = _edgeToNodeIndex;
            var edgeCharacters = _edgeCharacter;
            var edgeIndex = _firstChildEdgeIndex;
            var wordCount = _wordCounts;
            var results = _listContainer.Bags[edge - _rootFirstChild];

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
                //This is the value for the column directly before our diagonal stripe. See: {0FB913DD-0461-4DAF-8C97-ECDE0A9880AD}
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

                //TODO: can I simplify these checks?
                if (node < 0 && currentRowPreviousColumn <= 2 && wordLength <= builderDepth + 3)
                {
                    //TODO: investigate Tuple.Create vs new Tuple
                    results.Add(new SuggestItem(new string(builder, 0, builderDepth + 1), wordCount[GetIndex(builder, builderDepth + 1)]));
                }

                if (wordLength < builderDepth)
                {
                    return;
                }

                // {05054B58-4553-4DAD-915F-25A3D4E3A735}
                // We make the strip wider by 1 and avoid bound checking conditionals.
                // This write to memory is cheap because this location should be in the L1 cache due to recent instructions.
                // In addition, we perform this write even when it's not necessary because checking that condition is more expensive.
                currentRow[to] = currentRowPreviousColumn + 1;

                //TODO: Is it faster to use a temp for this loop and write to skip after? Would save some writes to memory, but probably pretty cheap since it's in L1 already.
                if (currentRow[skip] > 2)
                {
                    ++skip;
                    while (skip <= to && currentRow[skip] > 2)
                    {
                        ++skip;
                    }
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
                //var too = to3;// + 1; //NOTICE THIS OFFSET, it's for the stripe-width not all being in use.
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

                //TODO: can I simplify these checks?
                if (node < 0 && currentRowPreviousColumn <= 2 && wordLength <= 6)
                {
                    //TODO: investigate Tuple.Create vs new Tuple
                    results.Add(new SuggestItem(new string(builder, 0, 4), wordCount[GetIndex(builder, 4)]));
                }

                if (wordLength < 3)
                {
                    return;
                }

                // {05054B58-4553-4DAD-915F-25A3D4E3A735}
                // We make the strip wider by 1 and avoid bound checking conditionals.
                // This write to memory is cheap because this location should be in the L1 cache due to recent instructions.
                // In addition, we perform this write even when it's not necessary because checking that condition is more expensive.
                row3[to3] = currentRowPreviousColumn + 1;

                //TODO: Is it faster to use a temp for this loop and write to skip after? Would save some writes to memory, but probably pretty cheap since it's in L1 already.
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
                            //transposition
                            //Debug.Assert((previousRow - stripeWidth)[j - 2] == j + 1);
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

                //TODO: can I simplify these checks?
                if (node < 0 && currentRowPreviousColumn <= 2 && wordLength <= 5)
                {
                    //TODO: investigate Tuple.Create vs new Tuple
                    results.Add(new SuggestItem(new string(builder, 0, 3), wordCount[GetIndex(builder, 3)]));
                }

                if (wordLength < 2)
                {
                    return;
                }

                // {05054B58-4553-4DAD-915F-25A3D4E3A735}
                // We make the strip wider by 1 and avoid bound checking conditionals.
                // This write to memory is cheap because this location should be in the L1 cache due to recent instructions.
                // In addition, we perform this write even when it's not necessary because checking that condition is more expensive.
                row2[to2] = currentRowPreviousColumn + 1;

                var skip = 0;
                //TODO: Is it faster to use a temp for this loop and write to skip after? Would save some writes to memory, but probably pretty cheap since it's in L1 already.
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

            var currentRowPreviousColumn = 1;
            //TODO: unroll this
            var to = Math.Min(3, wordLength);
            for (var j = 0; j < to; ++j)
            {
                if (currentCharacter != word[j])
                {
                    row0[j] = currentRowPreviousColumn = Math.Min(currentRowPreviousColumn, j) + 1;
                }
                else
                {
                    row0[j] = currentRowPreviousColumn = Math.Min(currentRowPreviousColumn + 1, j);
                }
            }

            row0[to] = currentRowPreviousColumn + 1;

            //TODO: can I simplify these checks?
            if (node < 0 && currentRowPreviousColumn <= 2 && wordLength <= 3)
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
        private void ParameterizedTaskStartV6(int edge, // Not captured
            int max, char* first, int wordLength, int* toCache)
        {
            var index = _edgeToNodeIndex;
            var characters = _edgeCharacter;
            var edgeIndex = _firstChildEdgeIndex;
            var wordCount = _wordCounts;
            var results = _listContainer.Bags[edge % _listContainer.BagCount];

            //TODO: test if this is faster captured in the closure or re-calculated every time.
            // The real stripeWidth is 2*max + 1
            // Normally we would do a bound check and calculate a value for the cell in previousRow directly after our stripe.
            // Instead we pre-assign the cell when we have the values handy and then ignore bound checking. See: {05054B58-4553-4DAD-915F-25A3D4E3A735}
            var stripeWidth = (2 * max) + 2;

            var previousRow = stackalloc int[(wordLength + max + 1) * stripeWidth];

            // We're skipping the left-most column from the original algorithm. It's just a constant so ignore it.
            // Assigned here: {0FB913DD-0461-4DAF-8C97-ECDE0A9880AD}
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

            void SearchV6(int skipOriginal)
            {
                var skip = skipOriginal;
                // We can skip part of the stripe if it's value is larger than maxEdits.
                var j = skip;

                //This is the value for the column directly before our diagonal stripe. See: {0FB913DD-0461-4DAF-8C97-ECDE0A9880AD}
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
                    previousRowPreviousColumn = previousRow[j];
                    firstWithOffset = first + wordArrayOffset;
                    previousWordCharacter = firstWithOffset[j];
                    previousRowOffset = 1;
                }

                var any = 0;
                var to = toCache[builderDepth];
                for (; j < to; ++j)
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

                //TODO: can I simplify these checks?
                if (node < 0 && currentRowPreviousColumn <= max && builderDepth + 1 + max >= wordLength)
                {
                    //TODO: investigate Tuple.Create vs new Tuple
                    results.Add(new SuggestItem(new string(builder, 0, builderDepth + 1), wordCount[GetIndex(builder, builderDepth + 1)]));
                }

                if (builderDepth + 1 >= wordLength + max)
                {
                    return;
                }

                // {05054B58-4553-4DAD-915F-25A3D4E3A735}
                // We make the strip wider by 1 and avoid bound checking conditionals.
                // This write to memory is cheap because this location should be in the L1 cache due to recent instructions.
                // In addition, we perform this write even when it's not necessary because checking that condition is more expensive.
                currentRow[j] = currentRowPreviousColumn + 1;

                //TODO: Is it faster to use a temp for this loop and write to skip after? Would save some writes to memory, but probably pretty cheap since it's in L1 already.
                if (currentRow[skip] > max)
                {
                    ++skip;
                    while (skip <= j && currentRow[skip] > max)
                    {
                        ++skip;
                    }
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
                    SearchV6(skip);
                }

                // Visual studio might tell you these 3 lines are unnecessary. Don't believe it.
                currentRow = previousRow;
                previousRow -= stripeWidth;
                --builderDepth;
            }

            SearchV6(0);
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

                    goto nextIteration;
                }

                return -1;

nextIteration:;
            }

            // Must end on a terminal currentNode.
            if (currentNode < 0)
            {
                return number;
            }

            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public string? GetWord(int index)
        {
            if (index > Count)
            {
                return null;
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

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private int AssignCounts(int node, char* builder, int builderLength, int reachableCount, Dictionary<string, long> counts)
        {
            if (node < 0)
            {
                ++reachableCount;
                var word = new string(builder, 0, builderLength);
                _wordCounts[reachableCount] = counts[word];
                node = -node;
            }

            var i = _firstChildEdgeIndex[node];
            var last = _firstChildEdgeIndex[node + 1];
            for (; i < last; ++i)
            {
                builder[builderLength] = _edgeCharacter[i];
                var nextNode = _edgeToNodeIndex[i];

                var childReachable = AssignCounts(nextNode, builder, builderLength + 1, reachableCount, counts);
                var realReachable = _reachableTerminalNodes[Abs(nextNode)];
                Debug.Assert(realReachable == childReachable - reachableCount);
                reachableCount = childReachable;
            }

            return reachableCount;
        }

        public readonly int Count;
        private readonly int _rootNodeIndex;
        private readonly int _rootFirstChild;
        private readonly int _rootLastChild;

        private readonly int* _firstChildEdgeIndex;
        private readonly int* _edgeToNodeIndex;
        private readonly char* _edgeCharacter;
        private readonly ushort* _reachableTerminalNodes;

        private readonly long* _wordCounts;

        private readonly Task[] _tasks;
        private readonly StringBuilder _builder = new StringBuilder(50);
        private readonly LargePageMemoryChunk _memoryBlock;

        public Dawg(Stream stream)
        {
            if (stream == null)
            {
                throw new InvalidOperationException();
            }

            _rootNodeIndex = stream.Read<int>();
            var firstChildEdgeIndex = stream.ReadCompressedIntArray();
            var edgeToNodeIndex = stream.ReadArray<int>();
            var edgeCharacter = stream.ReadCharArray();
            var reachableTerminalNodes = stream.ReadCompressedUshortArray();
            var wordCounts = stream.ReadCompressedLongArray();
            Count = wordCounts.Length;

            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();

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
            _listContainer = new CompoundSuggestItemCollection(rootNodeChildCount);
            _tasks = new Task[rootNodeChildCount];

            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();

            _memoryBlock.Lock();
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public Dawg(PartitionedGraphBuilder builder, string path = "")
        {
            var root = builder.Finish();

            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();

            var edges = builder.EdgeCount;
            var allNodes = builder.OrderedNodes;

            var edgeToNodeIndex = new int[edges];
            var edgeCharacter = new char[edges];
            var firstChildEdgeIndex = new int[allNodes.Count + 1];
            firstChildEdgeIndex[^1] = edgeToNodeIndex.Length;
            _rootNodeIndex = root.OrderedId;
            var reachableTerminalNodes = new ushort[allNodes.Count];

            var edgeIndex = 0;
            var nodeCount = 0;
            foreach (var node in allNodes)
            {
                var nodeId = node.OrderedId;
                Debug.Assert(nodeId == nodeCount);
                ++nodeCount;
                firstChildEdgeIndex[nodeId] = edgeIndex;
                reachableTerminalNodes[nodeId] = (ushort)node.ReachableTerminalNodes;

                foreach (var child in node.SortedChildren)
                {
                    var terminalModifier = child.Value.IsTerminal ? -1 : 1;
                    edgeToNodeIndex[edgeIndex] = terminalModifier * child.Value.OrderedId;
                    edgeCharacter[edgeIndex] = child.Key;
                    ++edgeIndex;
                }
            }

            var wordCounts = new long[builder.WordCount];

            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();

            Count = wordCounts.Length;

            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();

            _memoryBlock = LargePageMemoryChunk.Builder()
                .ReserveUnaligned(firstChildEdgeIndex)
                .ReserveUnaligned(edgeToNodeIndex)
                .ReserveUnaligned(edgeCharacter)
                .ReserveUnaligned(reachableTerminalNodes)
                .ReserveUnaligned(wordCounts)
                .Allocate();

            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();

            _firstChildEdgeIndex = _memoryBlock.CopyArray(firstChildEdgeIndex);
            _edgeToNodeIndex = _memoryBlock.CopyArray(edgeToNodeIndex);
            _edgeCharacter = _memoryBlock.CopyArray(edgeCharacter);
            _reachableTerminalNodes = _memoryBlock.CopyArray(reachableTerminalNodes);
            _wordCounts = _memoryBlock.CopyArray(wordCounts);

            var stringBuilder = stackalloc char[100];
            AssignCounts(_rootNodeIndex, stringBuilder, 0, -1, builder.Counts);

            if (!string.IsNullOrEmpty(path))
            {
                using var stream = File.OpenWrite(path);
                if (stream == null)
                {
                    throw new InvalidOperationException();
                }

                stream.Write(_rootNodeIndex);
                stream.WriteCompressed(firstChildEdgeIndex);
                stream.Write(edgeToNodeIndex);
                stream.Write(edgeCharacter);
                stream.WriteCompressed(reachableTerminalNodes);
                stream.WriteCompressed(wordCounts);
            }

            _rootFirstChild = _firstChildEdgeIndex[_rootNodeIndex];
            _rootLastChild = _firstChildEdgeIndex[_rootNodeIndex + 1];

            var rootNodeChildCount = _rootLastChild - _rootFirstChild;
            _listContainer = new CompoundSuggestItemCollection(rootNodeChildCount);
            _tasks = new Task[rootNodeChildCount];

            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();

            _memoryBlock.Lock();
        }

        public bool Equals(Dawg other)
        {
            if (ReferenceEquals(other, this))
            {
                return true;
            }

            if (other.Count != Count)
            {
                return false;
            }

            for (var i = 0; i < Count; ++i)
            {
                if (other._wordCounts[i] != _wordCounts[i]
                    || other.GetWord(i) != GetWord(i))
                {
                    return false;
                }
            }

            return true;
        }

        public void Dispose()
        {
            _memoryBlock.Dispose();
        }
    }
}
