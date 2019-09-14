using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace portent
{
    internal sealed class SingleElementSuggestItemCollection : IEnumerable<SuggestItem>, IDisposable
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
            //TODO: does this even work?
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

        public void Dispose()
        {
            _enum.Dispose();
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

            public void Dispose()
            {
                //Empty - Required by IEnumerator<T>, but nothing to dispose.
            }

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
