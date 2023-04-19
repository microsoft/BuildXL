// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.FrontEnd.Nuget;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Utilities.Core;
using Xunit;

namespace Test.BuildXL.FrontEnd.Nuget
{
    public class NugetRelativePathComparerTests
    {
        private readonly FrontEndContext m_context;

        public NugetRelativePathComparerTests()
        {
            m_context = FrontEndContext.CreateInstanceForTesting();
        }

        [Theory]
        [InlineData("same", "same", 0)]
        [InlineData("same/path","same/path", 0)]
        [InlineData("same/PATH", "same/path", 0)]
        [InlineData("short", "shortNot", -1)]
        [InlineData("prefix/short", "prefix/shortNot", -1)]
        [InlineData("short/but/longer/path", "shortNot/shorterPath", -1)]
        [InlineData("lib/net6.0/Microsoft.Identity.Client.dll", "lib/net6.0-android31.0/Microsoft.Identity.Client.dll", -1)]
        [InlineData("path/with/a/lot/of/atoms", "path/with/a/lot", 1)]
        public void ValidateComparison(string left, string right, int expectedResult)
        {
            var comparer = new NugetRelativePathComparer(m_context.StringTable);
            var leftPath = RelativePath.Create(m_context.StringTable, left);
            var rightPath = RelativePath.Create(m_context.StringTable, right);

            var result = comparer.Compare(leftPath, rightPath);
            switch (expectedResult)
            {
                case -1:
                    Assert.True(result < 0);
                    break;
                case 0:
                    Assert.True(result == 0);
                    break;
                case 1:
                    Assert.True(result > 0);
                    break;
            }
        }
    }
}
