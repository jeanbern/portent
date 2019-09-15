using BenchmarkDotNet.Attributes;
using System;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Diagnostics.CodeAnalysis;

namespace portent.Benchmark
{
    public class DawgBenchmark : IDisposable
    {
        // Backtrack from /bin/$(Configuration)/netcore3.0/
        private const string SaveLocation = "../../../lev7.aug";
        private const string Query1K = "../../../noisy_query_en_1000.txt";

        private readonly Dawg _dawg;
        private readonly string[] _words;

        public DawgBenchmark(bool fromBenchmarkRunner)
        {
            //Add a another level for the BenchmarkDotNet GUID folder
            var prefix = fromBenchmarkRunner ? "../" : string.Empty;

            using var dawgStream = File.OpenRead(prefix + SaveLocation);
            _dawg = new Dawg(dawgStream);
            using var queryStream = File.OpenRead(prefix + Query1K);
            _words = BuildQuery1K(queryStream);
        }

        public DawgBenchmark() : this(true)
        {
            if (!VerifyDawgCorrectness())
            {
                throw new InvalidOperationException("Dawg was not well formed.");
            }
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
                if (lineParts?.Length == 3)
                {
                    testList[i++] = lineParts[0];
                }
            }

            if (i != 1000)
            {
                throw new InvalidOperationException("Unexpected number of query inputs: " + i.ToString());
            }

            return testList;
        }

        public static Dawg CreateDictionary(string corpusPath, string savePath)
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

                    if (!long.TryParse(lineTokens[1], out var count))
                    {
                        continue;
                    }

                    builder.Insert(lineTokens[0], count);
                }
            }

            var compressedGraph = builder.AsCompressedSparseRows();
            compressedGraph.Save(savePath);
            return new Dawg(compressedGraph);
        }

        [Params(0, 1, 2, 3)]
        public int MaxErrors { get; set; }

        [GlobalSetup]
        [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Used via reflection by DotNetBenchmark")]
        public void SetupForRun()
        {
            GCSettings.LatencyMode = GCLatencyMode.Batch;
        }

        public bool VerifyDawgCorrectness()
        {
            for (var i = 0; i < _dawg.Count; i++)
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
            foreach (var word in _words)
            {
                total += dawg.Lookup(word, MaxErrors).ToList().Count;
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
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _dawg.Dispose();
                }

                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
