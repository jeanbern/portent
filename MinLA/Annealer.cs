using System;

namespace MinLA
{
    public static class Annealer
    {
        public static (double newCost, TR Arrangement) Anneal<T, TR>(T annealingProblem, int seed, double temperature = 400.0d, double coolingRate = 0.999d, double lowestTemperature = 0.001d)
            where T : IAnnealingProblem<TR>
        {
            var random = new Random(seed);
            var count = 0;
            var kept = 0;
            var rejected = 0;
            var uphill = 0;
            while (temperature > lowestTemperature)
            {
                var delta = annealingProblem.MakeRandomMove();
                if (delta <= 0)
                {
                    kept++;
                    annealingProblem.KeepLastMove();
                }
                else if (random.NextDouble() < temperature)
                {
                    uphill++;
                    annealingProblem.KeepLastMove();
                }
                else
                {
                    rejected++;
                }

                if (count++ == 100000)
                {
                    count = 0;
                    Console.WriteLine(annealingProblem.Cost + ", " + kept.ToString("D5") + ", " + rejected.ToString("D5") + ", " + uphill.ToString("D5"));
                    kept = 0;
                    rejected = 0;
                    uphill = 0;
                }

                temperature *= coolingRate;
            }

            return (annealingProblem.Cost, annealingProblem.Result);
        }
    }
}
