//#define PRINT_ASM

using BenchmarkDotNet.Attributes;
#if PRINT_ASM
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
#endif
using Portent;
using System.IO;
using CurrentDawg = PerformanceTesting.Dawg29;


namespace PerformanceTesting
{
#if PRINT_ASM
    [Config(typeof(JustDisassembly))]
#endif
    public class LookupBenchmark
    {
#if PRINT_ASM
        public class JustDisassembly : ManualConfig
        {
            public JustDisassembly()
            {
                AddJob(Job.Dry.WithJit(Jit.RyuJit).WithPlatform(Platform.X64).WithRuntime(CoreRuntime.Core50));

                AddDiagnoser(new DisassemblyDiagnoser(new DisassemblyDiagnoserConfig(maxDepth: 3, printSource: true)));
            }
        }
        [Benchmark]
        public void BenchMe()
        {
            Dawg.Lookup(_lookupTerms[0], 2);
        }
#endif
        
        private const string SaveLocation = @"C:\Users\jeanbern\source\repos\portent\portent.Benchmark\lev7.easyTopological";
        private const string LookupLocation = @"C:\Users\jeanbern\source\repos\portent\portent.Benchmark\frequencyQuery.txt";

        private readonly string[] _lookupTerms;

        public LookupBenchmark()
        {
            _lookupTerms = File.ReadAllLines(LookupLocation);
        }

        [Params(2u)]
        public uint MaxErrors { get; set; }

        private static readonly CurrentDawg Dawg = LoadDawg();
        private static CurrentDawg LoadDawg()
        {
            using var read = File.OpenRead(SaveLocation);
            var compressed = new CompressedSparseRowGraph(read);
            //var pointer = new PointerGraph(compressed);
            //return new CurrentDawg(pointer);
            return new CurrentDawg(compressed);
        }

#if !PRINT_ASM
        [Benchmark]
#endif
        public void BenchmarkB()
        {
            var dawg = Dawg;
            foreach (var term in _lookupTerms)
            {
                dawg.Lookup(term, MaxErrors);
            }
        }
    }
}
