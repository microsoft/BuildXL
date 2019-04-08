// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Nuget;
using Xunit;

namespace Test.BuildXL.FrontEnd.Nuget
{
    public class NugetFrameworkMonikersTest
    {
        [Fact]
        public void TestNuget451()
        {
            // Perform a random sample for .net 452 things. Otherwise there are too many combinations.
            // This just guarantees a basic functionality of the Register function.
            var stringTable = new PathTable().StringTable;
            var monikers = new NugetFrameworkMonikers(stringTable);

            // Public member
            Assert.Equal("net451", monikers.Net451.ToString(stringTable));
            Assert.Equal("net452", monikers.Net452.ToString(stringTable));

            // Is well known
            Assert.True(monikers.WellknownMonikers.Contains(monikers.Net451));

            // 4.5.1 is compatible with 4.5
            Assert.True(monikers.CompatibilityMatrix.ContainsKey(monikers.Net451));
            Assert.True(monikers.CompatibilityMatrix[monikers.Net451].Contains(monikers.Net45));

            Assert.Equal(monikers.Net451, monikers.TargetFrameworkNameToMoniker[".NETFramework4.5.1"]);
        }
    }
}
