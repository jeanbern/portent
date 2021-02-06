using System.Collections.Generic;

namespace MinLA
{
    public class UndirectedGraphNode
    {
        public UndirectedGraphNode(int it)
        {
            Id = it;
        }

        public bool Terminal;
        public int Id { get; }
        public List<int> Children { get; } = new List<int>();
        public Dictionary<int, double> Neighbors { get; } = new Dictionary<int, double>();
        public Dictionary<int, double> Siblings { get; } = new Dictionary<int, double>();
    }
}