namespace Portent
{
    internal sealed class GraphEdge
    {
        public char Label;
        public ulong Count;
        public GraphNode Target;

        public GraphEdge(GraphNode target)
        {
            Target = target;
        }
    }
}
