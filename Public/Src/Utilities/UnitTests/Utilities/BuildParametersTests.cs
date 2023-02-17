// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
    public sealed class BuildParametersTests : XunitBuildXLTest
    {
        public BuildParametersTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestEnvVarCasing()
        {
            var envVars = new[]
            {
                new KeyValuePair<string, string>("En1", "V1"),
                new KeyValuePair<string, string>("En2", "V2"),
                new KeyValuePair<string, string>("en1", "V3"),
                new KeyValuePair<string, string>("eN1", "V4"),
            };

            var duplicates = new HashSet<(string, string, string)>();
            var factory = BuildParameters.GetFactory((key, value1, value2) => duplicates.Add((key, value1, value2)));
            factory.PopulateFromDictionary(envVars);

            if (OperatingSystemHelper.IsEnvVarComparisonCaseSensitive)
            {
                XAssert.AreEqual(0, duplicates.Count);
            }
            else
            {
                XAssert.AreEqual(2, duplicates.Count);
                XAssert.IsTrue(duplicates.Contains(("en1", "V1", "V3")));
                XAssert.IsTrue(duplicates.Contains(("eN1", "V1", "V4")));
            }
        }
    }
}
