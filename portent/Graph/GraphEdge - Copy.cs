namespace Portent
{
    internal sealed class GraphEdge2
    {
        public char Label;
        public ulong Count;
        public GraphNode2 Target;

        public bool TerminalEdge;

        public GraphEdge2(GraphNode2 target)
        {
            Target = target;
        }
    }
}
