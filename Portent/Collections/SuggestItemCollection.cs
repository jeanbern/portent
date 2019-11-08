using System;
using System.Collections;
using System.Collections.Generic;
#if DEBUG
using System.Diagnostics;
#endif

namespace Portent
{
    internal sealed class SuggestItemCollection : IEnumerable<SuggestItem>
    {
        public SuggestItemCollection(int count)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "count cannot be less than 0");
            }

            if (count == 0)
            {
                count = 1;
            }

            Items = new SuggestItem[count];
#if DEBUG
            Capacity = count;
#endif
            Count = 0;
            _myEnumerator = new SuggestItemEnumerator(this);
        }

#if DEBUG
        internal SuggestItem[] Items;
        internal int Capacity;
#else
        internal readonly SuggestItem[] Items;
#endif

        public void Add(string value, ulong count)
        {
#if DEBUG
            if (Count == Capacity)
            {
                Capacity *= 2;
                Array.Resize(ref Items, Capacity);
            }
            else
            {
                Debug.Assert(Items != null);
                Debug.Assert(Count < Items.Length);
            }
#endif
            Items[Count++] = new SuggestItem(value, count);
        }

        public void Clear()
        {
            Count = 0;
        }

        private readonly SuggestItemEnumerator _myEnumerator;

        public IEnumerator<SuggestItem> GetEnumerator()
        {
            _myEnumerator.Reset();
            return _myEnumerator;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            _myEnumerator.Reset();
            return _myEnumerator;
        }

        public int Count { get; private set; }

        private sealed class SuggestItemEnumerator : IEnumerator<SuggestItem>
        {
            private readonly SuggestItemCollection _myList;
#if DEBUG
            public readonly SuggestItem[] _items;
#else
            private readonly SuggestItem[] _items;
#endif

            public SuggestItemEnumerator(SuggestItemCollection myList)
            {
                _myList = myList;
                _items = _myList.Items;
                _index = 0;
                _count = _myList.Count;
                Current = default;
            }

            private int _index;
            private int _count;

            public SuggestItem Current { get; private set; }

            object IEnumerator.Current => Current;

            public bool MoveNext()
            {
                // ReSharper disable once InvertIf - Use most predicted branch first.
                if (_index < _count)
                {
                    Current = _items[_index];
                    ++_index;
                    return true;
                }

                return false;
            }

            public void Reset()
            {
                _index = 0;
                _count = _myList.Count;
            }

            public void Dispose()
            {
                //Empty - Required by IEnumerator<T>, but nothing to dispose.
            }
        }
    }
}
