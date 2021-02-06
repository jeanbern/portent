using System;

namespace Portent
{
    public class SuggestItem : IEquatable<SuggestItem>
    {
        public readonly string Term;
        public readonly ulong Count;

        public SuggestItem(string term, ulong count)
        {
            Term = term;
            Count = count;
        }

        public override bool Equals(object? obj)
        {
            return obj is SuggestItem item && Equals(item);
        }

        public bool Equals(SuggestItem other)
        {
            return Count == other.Count && string.Equals(Term, other.Term);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Count.GetHashCode(), Term.GetHashCode());
        }

        public static bool operator ==(SuggestItem left, SuggestItem right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(SuggestItem left, SuggestItem right)
        {
            return !(left == right);
        }
    }
}
