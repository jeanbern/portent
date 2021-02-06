using Portent;
using System.Collections.Generic;

namespace PerformanceTesting
{
    public interface IDawg
    {
        public uint WordCount { get; }

        IEnumerable<SuggestItem> Lookup(string word, uint maxEdits);

        string GetWord(int index);
        int GetIndex(in string word);
    }
}
