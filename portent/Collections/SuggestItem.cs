namespace portent
{
    public struct SuggestItem
    {
        public string Term;
        public long Count;

        public SuggestItem(string term, long count)
        {
            Term = term;
            Count = count;
        }

        public static bool Equals(SuggestItem obj, SuggestItem obj2)
        {
            return obj.Count == obj2.Count && string.Equals(obj.Term, obj2.Term);
        }

        public override bool Equals(object? obj)
        {
            return obj is SuggestItem item && Equals(this, item);
        }

        public override int GetHashCode()
        {
            return Term.GetHashCode();
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
