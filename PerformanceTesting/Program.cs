using BenchmarkDotNet.Running;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace PerformanceTesting
{
    public static class Program
    {
        public static void Main()
        {
            if (Debugger.IsAttached)
            {
                OldMain();
            }
            else
            {
                BenchmarkRunner.Run<LookupBenchmark>();
            }
        }

        private const string SaveLocation = @"C:\Users\jeanbern\source\repos\portent\portent.Benchmark\lev7.easyTopological";
        private const string Query1K = @"C:\Users\jeanbern\source\repos\portent\portent.Benchmark\noisy_query_en_1000.txt";
        public static void OldMain()
        {
            using var dawgStream = File.OpenRead(SaveLocation);
            var dawg = new Dawg11(dawgStream);
            Debug.Assert(VerifyDawgCorrectness(dawg));

            using var queryStream = File.OpenRead(Query1K);
            var query1K = BuildQuery1K(queryStream);

            var results = dawg.Lookup("adventures", 3);
            Console.WriteLine(string.Join(", ", results.Select(x => x.Term)));

            Console.WriteLine(GetTotalResults(dawg, query1K, 0));
            Console.WriteLine(GetTotalResults(dawg, query1K, 1));
            Console.WriteLine(GetTotalResults(dawg, query1K, 2));
            Console.WriteLine(GetTotalResults(dawg, query1K, 3));
        }

        private static int GetTotalResults(IDawg dawg, IEnumerable<string> words, uint maxErrors)
        {
            var total = 0;
            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var word in words)
            {
                total += dawg.Lookup(word, maxErrors).Count();
            }

            return total;
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

            string line;
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

        private static bool VerifyDawgCorrectness(IDawg dawg)
        {
            for (var i = 0; i < dawg.WordCount; i++)
            {
                var word = dawg.GetWord(i);
                if (dawg.GetIndex(word) != i)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
