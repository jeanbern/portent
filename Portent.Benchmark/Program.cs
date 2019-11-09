using BenchmarkDotNet.Running;
using System;
using System.Diagnostics;
using System.Linq;

namespace Portent.Benchmark
{
    public static class Program
    {
        public static void Main()
        {
            if (Debugger.IsAttached)
            {
                PrintResultCounts();
            }
            else
            {
                BenchmarkRunner.Run<DawgBenchmark>();
            }
        }

        private static void PrintResultCounts()
        {
            using var benchmark = new DawgBenchmark();
            benchmark.SetupForRun();
            if (!benchmark.VerifyDawgCorrectness())
            {
                throw new InvalidOperationException();
            }

            var results = string.Join(", ", benchmark._dawg.Lookup("adventures", 3).Select(x => x.Term));
            Console.WriteLine(results);

            for (var i = 0u; i < 4u; i++)
            {
                benchmark.MaxErrors = i;
                Console.WriteLine(benchmark.GetTotalResults());
            }

            Console.ReadLine();
        }
    }
}
