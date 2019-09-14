using System;
using System.Collections;
using System.Collections.Generic;

namespace portent
{
    internal sealed class SuggestItemCollection : IEnumerable<SuggestItem>, IDisposable
    {
        public SuggestItemCollection(int count)
        {
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
        public void Add(SuggestItem item)
        {
#if DEBUG
            if (Count == Capacity)
            {
                Capacity *= 2;
                Array.Resize(ref Items, Capacity);
            }
#endif
            Items[Count++] = item;
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

        public void Dispose()
        {
            _myEnumerator.Dispose();
        }

        public int Count { get; private set; }

        private sealed class SuggestItemEnumerator : IEnumerator<SuggestItem>
        {
            private readonly SuggestItemCollection _myList;
            public readonly SuggestItem[] Items;

            public SuggestItemEnumerator(SuggestItemCollection myList)
            {
                _myList = myList;
                Items = _myList.Items;
            }

            private int _index;
            private int _count;

            public SuggestItem Current { get; private set; }

            object IEnumerator.Current => throw new InvalidCastException();

            public bool MoveNext()
            {
                if (_index < _count)
                {
                    Current = Items[_index];
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
