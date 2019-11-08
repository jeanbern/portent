using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Portent
{
    internal sealed class SingleElementSuggestItemCollection : IEnumerable<SuggestItem>, IDisposable
    {
        private readonly SuggestItemEnumerator _enum = new SuggestItemEnumerator();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Update(string term, ulong count)
        {
            _enum.Item = new SuggestItem(term, count);
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

            public SuggestItem Current => Item;

            object IEnumerator.Current => Current;

            public void Dispose()
            {
                //Empty - Required by IEnumerator<T>, but nothing to dispose.
            }

            public bool MoveNext()
            {
                if (!Ready)
                {
                    return false;
                }

                Ready = false;
                return true;
            }

            public void Reset()
            {
                Ready = true;
            }
        }
    }
}
