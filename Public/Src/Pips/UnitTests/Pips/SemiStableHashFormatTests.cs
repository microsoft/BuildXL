// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Pips.Operations;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Pips
{
    public sealed class SemiStableHashFormaTests : XunitBuildXLTest
    {
        public SemiStableHashFormaTests(ITestOutputHelper output) : base(output) { }

        [Theory]
        [InlineData(0L)]
        [InlineData(-1L)]
        [InlineData(0x1111222233334444L)]
        public void FormatTests(long id)
        {
            Assert.True(Pip.TryParseSemiStableHash(Pip.FormatSemiStableHash(id), out var parsed));
            Assert.Equal(id, parsed);
        }

        [Fact]
        public void ExtractTests()
        {
            var id = 0x123456789ABCDEF0L;
            var formatted = Pip.FormatSemiStableHash(id);
            var text = $"A pip id inside a text: {formatted}";
            var matches = Pip.ExtractSemistableHashes(text);
            Assert.Equal(1, matches.Count);

            text = $"A text without pip ids";
            matches = Pip.ExtractSemistableHashes(text);
            AssertExtractMatches(id, 0, matches);

            text = @$"One pipId ""{formatted}"" then another ({formatted}), and two more {formatted}{formatted}";
            matches = Pip.ExtractSemistableHashes(text);
            AssertExtractMatches(id, 4, matches);

            text = $"Almost matches: {formatted.Substring(5)}, {formatted}2930";
            matches = Pip.ExtractSemistableHashes(text);
            AssertExtractMatches(id, 0, matches);

            
        }

        private void AssertExtractMatches(long id, int count, IReadOnlyList<long> matches)
        {
            Assert.Equal(count, matches.Count);
            foreach (var match in matches)
            {
                Assert.Equal(id, match);
            }
        }

    }
}
