#nullable enable
using System;
using System.IO;
using System.Linq;

namespace Portent.Test.DawgTests
{
    internal static class DawgHelper
    {
        private const string TempAugPath = "writefile.aug";

        public static Dawg Create(params string[] words)
        {
            var builder = new PartitionedGraphBuilder();
            foreach (var word in words.OrderBy(x => x))
            {
                builder.Insert(word, 0);
            }

            using var compressed = builder.AsCompressedSparseRows();
            compressed.Save(TempAugPath);

            using var read = File.OpenRead(TempAugPath);
            return new Dawg(read);
        }

        public static Dawg CreateFromCorpus(string corpusLocation)
        {
            var builder = new PartitionedGraphBuilder();
            using var stream = File.OpenRead(corpusLocation);
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var lineTokens = line.Split(' ');
                if (lineTokens.Length != 2)
                {
                    continue;
                }

                if (!ulong.TryParse(lineTokens[1], out var count))
                {
                    continue;
                }

                builder.Insert(lineTokens[0], count);
            }

            using var compressedGraph = builder.AsCompressedSparseRows();
            compressedGraph.Save(TempAugPath);

            using var dawgStream = File.OpenRead(TempAugPath);
            return new Dawg(dawgStream);
        }

        public static string[] BuildQuery1K(string queryLocation)
        {
            using var stream = File.OpenRead(queryLocation);
            var testList = new string[1000];
            var i = 0;

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
    }
}
