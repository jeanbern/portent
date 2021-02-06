using BenchmarkDotNet.Attributes;
using System;
using System.IO;
using System.Linq;
using System.Runtime;

namespace Portent.Benchmark
{
    [MemoryDiagnoser]
    public class DawgBenchmark : IDisposable
    {
        // Backtrack from /bin/$(Configuration)/netcore3.0/
        private const string SaveLocation = @"C:\Users\jeanbern\source\repos\portent\portent.Benchmark\lev7.easyTopological";
        //private const string SaveLocation = @"C:\Users\jeanbern\source\repos\portent\portent.Benchmark\partition2.aug";
        //private const string SaveLocation = @"C:\Users\jeanbern\source\repos\portent\portent.Benchmark\lev7.cacheAware";
        private const string Query1K = @"C:\Users\jeanbern\source\repos\portent\portent.Benchmark\noisy_query_en_1000.txt";

        internal readonly Dawg _dawg;
        private readonly string[] _words;

        public DawgBenchmark()
        {
            var prefix = string.Empty;
            using var dawgStream = File.OpenRead(prefix + SaveLocation);
            _dawg = new Dawg(dawgStream);
            //_dawg = CreateDictionary(@"C:\Users\jeanbern\source\repos\portent\portent.Benchmark\frequency_dictionary_en_500_000.txt", @"C:\Users\jeanbern\source\repos\portent\portent.Benchmark\partition2.aug");
            using var queryStream = File.OpenRead(prefix + Query1K);
            _words = BuildQuery1K(queryStream);
        }

        private static string[] BuildQuery1K(Stream stream)
        {
            var testList = new string[1000];
            var i = 0;
            if (stream == null)
            {
                throw new InvalidOperationException();
            }

            using var reader = new StreamReader(stream);

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var lineParts = line.Split(default(char[]), StringSplitOptions.None);
                if (lineParts.Length == 3)
                {
                    testList[i++] = lineParts[0];
                }
            }

            if (i != 1000)
            {
                // ReSharper disable once RedundantToStringCallForValueType - would box value type?
                throw new InvalidOperationException("Unexpected number of query inputs: " + i.ToString());
            }

            return testList;
        }

        // ReSharper disable once UnusedMember.Global
        public static Dawg CreateDictionary(string corpusPath, string savePath)
        {
            var builder = new PartitionedGraphBuilder2();
            using (var stream = File.OpenRead(corpusPath))
            {
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

            using (var compressedGraph = builder.AsCompressedSparseRows())
            {
                compressedGraph.Save(savePath);
            }

            using var dawgStream = File.OpenRead(savePath);
            return new Dawg(dawgStream);
        }

        [Params(2u)]
        //[Params(0u, 1u, 2u, 3u)]
        public uint MaxErrors { get; set; }

        [GlobalSetup]
#pragma warning disable CA1822 // Mark members as static - This is used by BenchmarkDotNet
        public void SetupForRun()
#pragma warning restore CA1822 // Mark members as static
        {
            GCSettings.LatencyMode = GCLatencyMode.Batch;
        }

        public bool VerifyDawgCorrectness()
        {
            for (var i = 0; i < _dawg.WordCount; i++)
            {
                var word = _dawg.GetWord(i);
                if (_dawg.GetIndex(word) != i)
                {
                    return false;
                }
            }

            return true;
        }

        public int GetTotalResults()
        {
            var total = 0;
            var dawg = _dawg;
            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var word in _words)
            {
                total += dawg.Lookup(word, MaxErrors).Count();
            }

            return total;
        }

        [Benchmark]
        public void Benchmark()
        {
            var dawg = _dawg;
            foreach (var word in _words)
            {
                dawg.Lookup(word, MaxErrors);
            }
        }

#region IDisposable Support
        private bool _disposedValue;

        protected virtual void Dispose(bool disposing)
        {
            if (_disposedValue)
            {
                return;
            }

            if (disposing)
            {
                _dawg.Dispose();
            }

            _disposedValue = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
#endregion
    }
}
