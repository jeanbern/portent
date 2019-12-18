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
        public Dictionary<int, float> Neighbors { get; } = new Dictionary<int, float>();
    }
}