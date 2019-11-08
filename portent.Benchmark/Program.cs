using BenchmarkDotNet.Running;
using System;
using System.Diagnostics;
using System.Linq;

namespace portent.Benchmark
{
    public static class Program
    {
        public static void Main()
        {
            if (Debugger.IsAttached)
            {
                RunForProfiler();
            }
            else
            {
                BenchmarkRunner.Run<DawgBenchmark>();
            }
        }

        private static void RunForProfiler()
        {
            Console.WriteLine("reading");
            using var benchmark = new DawgBenchmark();
            Console.WriteLine("setup for run");
            benchmark.SetupForRun();
            Console.WriteLine("verify correctness");
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

            Console.WriteLine("Done, press {ENTER} to continue...");
            Console.ReadLine();
        }
    }
}
