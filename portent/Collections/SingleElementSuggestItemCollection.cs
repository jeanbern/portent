using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace portent
{
    public sealed class SingleElementSuggestItemCollection : IEnumerable<SuggestItem>
    {
        private readonly SuggestItemEnumerator _enum = new SuggestItemEnumerator();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Update(SuggestItem item)
        {
            _enum.Current = item;
            _enum.Ready = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Update(string term, long count)
        {
            var thing = _enum.Current;
            thing.Term = term;
            thing.Count = count;
            _enum.Ready = true;
        }

        public IEnumerator<SuggestItem> GetEnumerator()
        {
            return _enum;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _enum;
        }

        private sealed class SuggestItemEnumerator : IEnumerator<SuggestItem>
        {
            public bool Ready;
            public SuggestItem Item;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(SuggestItem item)
            {
                Item = item;
                Ready = false;
            }

            public SuggestItem Current { get; internal set; }

            object IEnumerator.Current => throw new InvalidCastException();

            public void Dispose() { }

            public bool MoveNext()
            {
                if (Ready)
                {
                    Ready = false;
                    return true;
                }

                return false;
            }

            public void Reset()
            {
                Ready = true;
            }
        }
    }
}
