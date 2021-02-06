using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PerformanceTesting
{
    internal static class SearchListCreator
    {
        private static readonly Random random = new Random(1234);
        private static string Pick(IEnumerable<Tuple<string, long>> strings, long max)
        {
            var value = random.NextLong(max);
            foreach (var tuple in strings)
            {
                value -= tuple.Item2;
                if (value < 0)
                {
                    return tuple.Item1;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(max));
        }

        public static IEnumerable<string> Pick(string fileName, int count)
        {
            var strings = File.ReadAllLines(fileName);
            var splits = strings.Select(x => x.Split()).Select(x => Tuple.Create(x[0], long.Parse(x[1]))).ToList();
            var total = splits.Aggregate(0L, (i, x) => i + x.Item2);
            
            for (var i = 0; i < count; i++)
            {
                yield return Pick(splits, total);
            }
        }

        public static void SortDictionaryFile()
        {
            var strings = File.ReadAllLines(@"C:\Users\jeanbern\Downloads\frequency_dictionary_en_82_765.txt");
            var splits = strings.Select(x => x.Split()).Select(x => Tuple.Create(x[0], long.Parse(x[1]))).ToList();
            var total = splits.Aggregate(0L, (i, x) => i + x.Item2);
            Console.WriteLine(total);
            for (var i = 0; i < 1000; i++)
            {
                Console.WriteLine(Pick(splits, total));
            }
            Console.ReadKey();
            Array.Sort(strings);
            File.WriteAllLines(@"C:\Users\jeanbern\Downloads\frequency_dictionary_en_82_765_sorted.txt", strings);
        }
    }
}
