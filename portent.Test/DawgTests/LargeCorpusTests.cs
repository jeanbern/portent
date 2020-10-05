using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Portent.Test.DawgTests
{
    public class LargeCorpusTests
    {
        [Theory]
        [InlineData(
            "TestData/frequency_dictionary_en_500_000.txt",
            "TestData/noisy_query_en_1000.txt",
            497,
            34814,
            869864,
            8775261)]
        public void Lookup_CountMatchesExpected_EmptyDictionary(string corpusLocation, string queryLocation, int matchCount0Errors, int matchCount1Errors, int matchCount2Errors, int matchCount3Errors)
        {
            using var dawg = DawgHelper.CreateFromCorpus(corpusLocation);
            var terms = DawgHelper.BuildQuery1K(queryLocation);
            Assert.Equal(matchCount0Errors, ResultTotal(dawg, terms, 0u));
            Assert.Equal(matchCount1Errors, ResultTotal(dawg, terms, 1u));
            Assert.Equal(matchCount2Errors, ResultTotal(dawg, terms, 2u));
            Assert.Equal(matchCount3Errors, ResultTotal(dawg, terms, 3u));
        }

        private static int ResultTotal(Dawg dawg, IEnumerable<string> searchTerms, uint maxEdits)
        {
            return searchTerms.Sum(searchTerm => dawg.Lookup(searchTerm, maxEdits).Count());
        }
    }
}
