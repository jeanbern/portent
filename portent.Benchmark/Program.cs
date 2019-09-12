using BenchmarkDotNet.Running;

namespace portent.Benchmark
{
    class Program
    {
        static void Main()
        {
            BenchmarkRunner.Run<DawgBenchmark>();
        }
    }
}
