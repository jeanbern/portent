using BenchmarkDotNet.Running;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

            //var results = string.Join(", ", benchmark._dawg.LookupSync("adventures", 3));
            var results2 = string.Join(", ", benchmark._dawg.Lookup("adventures", 3).Select(x => x.Term));
            //Console.WriteLine(results);
            Console.WriteLine(results2);

            for (var i = 0u; i < 4u; i++)
            {
                benchmark.MaxErrors = i;
                for (var j = 0; j < 1000; j++)
                {
                    //benchmark.GetTotalResults();
                }

                Console.WriteLine(benchmark.GetTotalResults());
                //benchmark.Benchmark();
            }
        }
    }
}
