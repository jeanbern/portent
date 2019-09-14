﻿#if DEBUG
using System;
using System.Linq;
#else
using BenchmarkDotNet.Running;
#endif

namespace portent.Benchmark
{
    public static class Program
    {
        public static void Main()
        {
#if DEBUG
            using var benchmark = new DawgBenchmark(false);
            benchmark.SetupForRun();
            if (!benchmark.VerifyDawgCorrectness())
            {
                throw new InvalidOperationException();
            }

            // 497, 34814, 869864, 8775261
            for (var i = 0; i < 4; i++)
            {
                benchmark.MaxErrors = i;
                Console.WriteLine(benchmark.GetTotalResults());

                for (var j = 0; j < 400; j++)
                {
                    benchmark.Benchmark();
                }
            }

            Console.WriteLine("No errors, press {ENTER} to continue...");
            Console.ReadLine();
#else
            BenchmarkRunner.Run<DawgBenchmark>();
#endif
        }
    }
}
