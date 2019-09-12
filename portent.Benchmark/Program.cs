#if DEBUG
using System;
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

            for (var i = 1; i < 2; i++)
            {
                benchmark.MaxErrors = i;
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
