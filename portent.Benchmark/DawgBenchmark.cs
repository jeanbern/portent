using BenchmarkDotNet.Attributes;
using System;
using System.IO;
using System.Runtime;

namespace portent
{
    public class DawgBenchmark : IDisposable
    {
        private const string SaveLocation = @"../../../../lev7.aug";
        private const string Query1K = @"../../../../noisy_query_en_1000.txt";

        private readonly Dawg _dawg = new Dawg(File.OpenRead(SaveLocation));
        private readonly string[] _words = BuildQuery1K();

        public DawgBenchmark()
        {
            if (!VerifyDawgCorrectness())
            {
                throw new InvalidOperationException("Dawg was not well formed.");
            }
        }

        private static string[] BuildQuery1K()
        {
            var testList = new string[1000];
            var i = 0;
            using (var stream = File.OpenRead(Query1K))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException();
                }

                using var reader = new StreamReader(stream);
                if (reader == null)
                {
                    throw new InvalidOperationException();
                }

                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    var lineParts = line.Split(null);
                    if (lineParts?.Length == 3)
                    {
                        testList[i++] = lineParts[0];
                    }
                }
            }

            if (i != 1000)
            {
                throw new InvalidOperationException("Unexpected number of query inputs: " + i);
            }

            return testList;
        }

        private static Dawg CreateDictionary(string corpusPath, string savePath)
        {
            var biggest = 0L;

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

                    if (!long.TryParse(lineTokens[1], out var me))
                    {
                        continue;
                    }

                    builder.Insert(lineTokens[0], me);
                    biggest = Math.Max(biggest, me);
                }
            }

            return new Dawg(builder, savePath);
        }

        [Params(0, 1, 2, 3)]
        public int MaxErrors { get; set; }

        [GlobalSetup]
        public void SetupDawg()
        {
            GCSettings.LatencyMode = GCLatencyMode.Batch;
        }

        public bool VerifyDawgCorrectness()
        {
            for (var i = 0; i < _dawg.Count; i++)
            {
                var word = _dawg.GetWord(i);
                if (word == null || _dawg.GetIndex(word) != i)
                {
                    return false;
                }
            }

            return true;
        }

        [Benchmark]
        public void Benchmark()
        {
            var dawg = _dawg;
            foreach (var word in _words)
            {
                var results = dawg.Lookup(word, MaxErrors);
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
                    // TODO: dispose managed state (managed objects).
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.

                _dawg.Dispose();

                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
        #endregion
    }
}
