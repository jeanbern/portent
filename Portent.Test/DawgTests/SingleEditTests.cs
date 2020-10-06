using System.Linq;
using Xunit;

namespace Portent.Test.DawgTests
{
    public class SingleEditTests
    {
        [Theory]
        [InlineData("ab--", "ba--")]
        [InlineData("-ab-", "-ba-")]
        [InlineData("--ab", "--ba")]
        [InlineData("--", "a--")]
        [InlineData("--", "-a-")]
        [InlineData("--", "--a")]
        [InlineData("abc", "bc")]
        [InlineData("abc", "ac")]
        [InlineData("abc", "ab")]
        [InlineData("abc", "xbc")]
        [InlineData("abc", "axc")]
        [InlineData("abc", "abx")]
        public void Lookup_SingleEdit_IsRejected(string word, string modifiedWord)
        {
            const uint editDistance = 0u;
            using var dawg = DawgHelper.Create(word);

            var lookup = dawg.Lookup(modifiedWord, editDistance).ToList();

            Assert.Empty(lookup);
        }

        [Theory]
        [InlineData("ab--", "ba--")]
        [InlineData("-ab-", "-ba-")]
        [InlineData("--ab", "--ba")]
        public void Lookup_SingleTransposition_IsAccepted(string word, string modifiedWord)
        {
            const uint editDistance = 1u;
            using var dawg = DawgHelper.Create(word);

            var lookup = dawg.Lookup(modifiedWord, editDistance).ToList();

            Assert.Single(lookup);
            Assert.Equal(word, lookup[0].Term);
        }

        [Theory]
        [InlineData("--", "a--")]
        [InlineData("--", "-a-")]
        [InlineData("--", "--a")]
        public void Lookup_SingleInsertion_IsAccepted(string word, string modifiedWord)
        {
            const uint editDistance = 1u;
            using var dawg = DawgHelper.Create(word);

            var lookup = dawg.Lookup(modifiedWord, editDistance).ToList();

            Assert.Single(lookup);
            Assert.Equal(word, lookup[0].Term);
        }

        [Theory]
        [InlineData("abc", "bc")]
        [InlineData("abc", "ac")]
        [InlineData("abc", "ab")]
        public void Lookup_SingleDeletion_IsAccepted(string word, string modifiedWord)
        {
            const uint editDistance = 1u;
            using var dawg = DawgHelper.Create(word);

            var lookup = dawg.Lookup(modifiedWord, editDistance).ToList();

            Assert.Single(lookup);
            Assert.Equal(word, lookup[0].Term);
        }

        [Theory]
        [InlineData("abc", "xbc")]
        [InlineData("abc", "axc")]
        [InlineData("abc", "abx")]
        public void Lookup_SingleSubstitution_IsAccepted(string word, string modifiedWord)
        {
            const uint editDistance = 1u;
            using var dawg = DawgHelper.Create(word);

            var lookup = dawg.Lookup(modifiedWord, editDistance).ToList();

            Assert.Single(lookup);
            Assert.Equal(word, lookup[0].Term);
        }

        [Theory]
        [InlineData("ab--", "bxa--")]
        [InlineData("-ab-", "-bxa-")]
        [InlineData("--ab", "--bxa")]
        // TODO: Get the proper definitions for the statement below.
        // This isn't full Damerau-Levensthein, it's the optimal string alignment thing instead.
        public void Lookup_InterruptedTransposition_IsNotAccepted(string word, string modifiedWord)
        {
            // Insertion between the transposed characters plus the transposition itself.
            const uint editDistance = 2u;
            using var dawg = DawgHelper.Create(word);

            var lookup = dawg.Lookup(modifiedWord, editDistance).ToList();

            Assert.Empty(lookup);
        }
    }
}
