namespace portent
{
    internal sealed class GraphEdge
    {
        public char Label;
        public long Count;
        public GraphNode Target;

        public GraphEdge(GraphNode target)
        {
            Target = target;
        }
    }
}
