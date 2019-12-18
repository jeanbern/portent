using System;
using System.IO;
using System.Linq;
using Portent;

namespace MinLA
{
    public static class Program
    {
        private const string NormalPath = @"C:\Users\JPelleri\source\repos\portent\portent.Benchmark\lev7.easyTopological";
        private const string TopologicallyAnnealedPath = @"C:\Users\JPelleri\source\repos\portent\portent.Benchmark\lev7.topoAnneal";
        private const string SpectralPath = @"C:\Users\JPelleri\source\repos\portent\portent.Benchmark\lev7.Spectral";
        private const string AnnealedPath = @"C:\Users\JPelleri\source\repos\portent\portent.Benchmark\lev7.annealed";
        private const string DictionaryPath = @"C:\Users\JPelleri\source\repos\portent\portent.Benchmark\frequency_dictionary_en_500_000.txt";
        private const string CacheAwareTopologicalPath = @"C:\Users\JPelleri\source\repos\portent\portent.Benchmark\lev7.cacheAwareTopological";
        private const string CacheAwarePath = @"C:\Users\JPelleri\source\repos\portent\portent.Benchmark\lev7.cacheAware";

        public static void Main()
        {
            //PrintGraphForJs();
            //ShrinkGraph();
            //ArrangeGraphs();
            ArrangeGraphsCacheAware();
        }

        private static void ShrinkGraph()
        {
            Console.WriteLine("Building");
            var graph = BuildGraph(DictionaryPath);
            Console.WriteLine("Converting");
            var nodes = ConvertToNewNodes(graph);
            Console.WriteLine("Converted");

            var round = 0;
            for (var coarse = 1; coarse < 10; coarse++)
            {
                int lastCost;
                var newCost = nodes.Count(x => !x.Removed);
                Console.WriteLine($"Coarsen: {coarse}");
                do
                {
                    var edgeCount = nodes.Sum(x => x.Children.Count);
                    Console.WriteLine($"Round: {round:D5} Count: {newCost:D6} Edges: {edgeCount:D6}");
                    round++;
                    UpDog(nodes, coarse);
                    lastCost = newCost;
                    newCost = nodes.Count(x => !x.Removed);
                } while (newCost != lastCost);
            }

            NewNode.WriteD3JsonFormat(nodes, @"C:\Users\JPelleri\source\repos\portent\portent.Benchmark\graph2.json");
        }

        private static NewNode[] ConvertToNewNodes(CompressedSparseRowGraph graph)
        {
            var nodes = new NewNode[graph.FirstChildEdgeIndex.Length - 1];
            for (var i = 0; i < nodes.Length; i++)
            {
                nodes[i] = new NewNode(i);
            }

            for (var i = 1; i <= nodes.Length; i++)
            {
                var first = graph.FirstChildEdgeIndex[i - 1];
                var last = graph.FirstChildEdgeIndex[i];
                var parent = nodes[i - 1];
                for (var j = first; j < last; j++)
                {
                    var index = graph.EdgeToNodeIndex[j];
                    var child = nodes[Math.Abs(index)];
                    /*if (index < 0)
                    {
                        child.Terminal = true;
                    }*/

                    parent.Children.Add(child);
                    child.Parents.Add(parent);
                }
            }

            return nodes;
        }

        private static bool UpDog(NewNode[] nodes, int coarseningSize)
        {
            Console.WriteLine("  " + nameof(DownDog));
            var result = false;
            for (var i = 76; i < nodes.Length; i++)
            {
                var node = nodes[i];
                if (node.Removed)
                {
                    continue;
                }

                var parentCount = node.Parents.Count;

                if (0 < parentCount && parentCount <= coarseningSize)
                {
                    while (node.Parents.Any())
                    {
                        var first = node.Parents.First();
                        first.MergeChild(node);
                    }

                    node.Removed = true;
                    node.Children.Clear();
                    result = true;
                }
            }

            return result;
        }
        private static bool DownDog(NewNode[] nodes, int coarseningSize)
        {
            Console.WriteLine("  " + nameof(DownDog));
            var result = false;
            for (var i = 0; i < nodes.Length; i++)
            {
                var node = nodes[i];
                if (node.Removed)
                {
                    continue;
                }

                if (node.Children.Count <= coarseningSize)
                {
                    while (node.Children.Any())
                    {
                        node.MergeChild(node.Children.First());
                    }

                    result = true;
                }
            }

            return result;
        }

        private static void ArrangeGraphsCacheAware()
        {
            Console.WriteLine("Starting Topological");
            var graph = BuildGraph(DictionaryPath);
            Console.WriteLine($"Default Topological ordering cost: {graph.CacheArrangementCost():E}");
            graph.Save(NormalPath);
            /*
            Console.WriteLine("Starting CacheAwareTopological Annealing");
            var (_, topologicalCacheArrangement) = Annealer.Anneal<CacheAwareInstance, int[]>(
                new CacheAwareInstance(graph),
                7654323,
                0.0001d,
                .9999999d,
                .000000001d);
            Console.WriteLine("Arranging");
            var topologicalCacheGraph = graph.Arrange(topologicalCacheArrangement);
            Console.WriteLine($"CacheAwareTopological ordering cost: {topologicalCacheGraph.CacheArrangementCost():E}");
            topologicalCacheGraph.Save(CacheAwareTopologicalPath);
            //*/var topologicalCacheGraph = graph;

            Console.WriteLine("Starting CacheAware Annealing");
            var (_, cacheArrangement) = Annealer.Anneal<CacheAwareInstance, int[]>(
                new CacheAwareInstance(topologicalCacheGraph),
                7654324,
                0.005d,
                .999999995d,
                .000000001d);
            Console.WriteLine("Arranging");
            var cacheGraph = topologicalCacheGraph.Arrange(cacheArrangement);
            Console.WriteLine($"CacheAware ordering cost: {cacheGraph.CacheArrangementCost():E}");
            cacheGraph.Save(CacheAwarePath);
        }

        private static void ArrangeGraphs()
        {
            Console.WriteLine("Starting Topological");
            var graph = BuildGraph(DictionaryPath);
            Console.WriteLine($"Default Topological ordering cost: {graph.ArrangementCost():E}");
            graph.Save(NormalPath);

            Console.WriteLine("Starting Annealed Topological");
            var (_, topologicalArrangement) = Annealer.Anneal<TopologicalOrderingInstance, int[]>(
                new TopologicalOrderingInstance(graph),
                7654321,
                50.0d,
                .9999995d,
                .0000001d);
            Console.WriteLine("Arranging");
            var newTopologicalGraph = graph.Arrange(topologicalArrangement);
            Console.WriteLine($"Annealed Topological ordering cost: {newTopologicalGraph.ArrangementCost():E}");
            newTopologicalGraph.Save(TopologicallyAnnealedPath);

            Console.WriteLine("Starting Spectral");
            var spectralArrangement = ChacoSequencer.SpectralSequence(newTopologicalGraph);
            Console.WriteLine("Arranging");
            var spectralGraph = newTopologicalGraph.Arrange(spectralArrangement);
            Console.WriteLine($"Spectral ordering cost: {spectralGraph.ArrangementCost():E}");
            spectralGraph.Save(SpectralPath);

            Console.WriteLine("Starting Annealing");
            var (_, annealingArrangement) = Annealer.Anneal<OrderingInstance, int[]>(
                new OrderingInstance(spectralGraph),
                7654322,
                500000.0d,
                .999999d,
                .00001d);
            Console.WriteLine("Arranging");
            var annealedGraph = spectralGraph.Arrange(annealingArrangement);
            Console.WriteLine($"Annealed ordering cost: {annealedGraph.ArrangementCost():E}");
            annealedGraph.Save(AnnealedPath);
            Console.WriteLine("Starting Annealing");
        }

        private static void PrintGraphForJs()
        {
            Console.WriteLine("Building");
            var graph = BuildGraph(DictionaryPath);
            Console.WriteLine("Converting");
            graph.WriteD3JsonFormat(@"C:\Users\JPelleri\source\repos\portent\portent.Benchmark\graph.json");
        }

        private static CompressedSparseRowGraph BuildGraph(string corpusPath)
        {
            var builder = new PartitionedGraphBuilder();
            using (var stream = File.OpenRead(corpusPath))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException();
                }

                using var reader = new StreamReader(stream);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    var lineTokens = line.Split(' ');
                    if (lineTokens == null || lineTokens.Length != 2)
                    {
                        continue;
                    }

                    if (!ulong.TryParse(lineTokens[1], out var count))
                    {
                        continue;
                    }

                    builder.Insert(lineTokens[0], count);
                }
            }

            var result = builder.AsCompressedSparseRows();
            return result;
        }
    }
}
