using System;
using System.Collections;
using System.Collections.Generic;
#if !DEBUG
using System.Diagnostics;
#endif

namespace portent
{
    internal sealed class CompoundSuggestItemCollection : IEnumerable<SuggestItem>, IDisposable
    {
        private readonly SuggestItemEnumerator _myEnumerator;
        public readonly SuggestItemCollection[] Bags;
        private readonly int BagCount;

        // TODO: This is only for the test data. Should revert to auto-increasing lists.
        private static readonly int[] SublistCounts = { 6441, 5719, 4718, 5031, 5778, 4072, 5311, 4011, 3801, 3730, 3357, 4469, 5018, 3325, 3897, 3414, 2240, 2938, 2627, 2260, 1764, 2466, 728, 754, 1000, 615, 24, 23, 13, 18, 7, 3, 17, 10, 12, 10, 4, 10, 7, 10, 6, 3, 9, 8, 4, 7, 5, 3, 6, 2, 2, 2, 7, 4, 3, 4, 3, 3, 2, 2, 2, 3, 3, 2, 0, 2, 2, 1, 1, 1, 1, 1, 0, 1, 1 };

        public CompoundSuggestItemCollection(int count)
        {
#if !DEBUG
            Debug.Assert(count == SublistCounts.Length);
#endif
            BagCount = count;
            Bags = new SuggestItemCollection[count];
            for (var i = 0; i < Bags.Length; i++)
            {
                Bags[i] = new SuggestItemCollection(SublistCounts[i]);
            }

            _myEnumerator = new SuggestItemEnumerator(this);

#if DEBUG
            MaximumLengths = new int[count];
            MaximumBuffers = new long[count];
#endif
        }

        public void Clear()
        {
            for (var i = 0; i < Bags.Length; i++)
            {
                Bags[i].Clear();
            }
        }

#if DEBUG
        private readonly int[] MaximumLengths;
        private readonly long[] MaximumBuffers;
#endif

        public unsafe IEnumerator<SuggestItem> GetEnumerator()
        {
#if DEBUG
            for (var i = 0; i < BagCount; i++)
            {
                var count = Bags[i].Count;
                if (count > MaximumLengths[i])
                {
                    MaximumLengths[i] = count;
                }
            }
#endif
            _myEnumerator.Reset();
            return _myEnumerator;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            _myEnumerator.Reset();
            return _myEnumerator;
        }

#if DEBUG
        public int[] Capacities()
        {
            var result = new int[BagCount];
            for (var i = 0; i < result.Length; i++)
            {
                result[i] = Bags[i].Capacity;
            }

            return result;
        }

        public void PrintMax()
        {
            Console.WriteLine(string.Join(", ", MaximumLengths));
            Console.WriteLine(string.Join(", ", MaximumBuffers));
        }
#endif

        public void Dispose()
        {
            _myEnumerator.Dispose();
        }

        private sealed class SuggestItemEnumerator : IEnumerator<SuggestItem>
        {
            private readonly CompoundSuggestItemCollection _container;
            private int _containerIndex;
            private int _innerIndex;
            private SuggestItemCollection _currentList;

            public SuggestItemEnumerator(CompoundSuggestItemCollection container)
            {
                _container = container;
                _containerIndex = 0;
                _innerIndex = 0;
                _currentList = _container.Bags[0];
            }

            public bool MoveNext()
            {
                if (_innerIndex < _currentList.Count)
                {
                    Current = _currentList.Items[_innerIndex++];
                    return true;
                }

                while (++_containerIndex < _container.BagCount)
                {
                    _currentList = _container.Bags[_containerIndex];
                    if (_currentList == null || _currentList.Count <= 0)
                    {
                        continue;
                    }

                    _innerIndex = 0;
                    Current = _currentList.Items[_innerIndex++];
                    return true;
                }

                return false;
            }

            public void Reset()
            {
                _containerIndex = 0;
                _innerIndex = 0;
                _currentList = _container.Bags[0];
            }

            public SuggestItem Current { get; private set; } = new SuggestItem();

            object IEnumerator.Current => throw new InvalidCastException();

            public void Dispose()
            {
                //Empty - Required by IEnumerator<T>, but nothing to dispose.
            }
        }
    }
}
