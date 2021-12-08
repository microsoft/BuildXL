// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using SBOMConverter;
using Microsoft.Sbom.Contracts;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.Tool.SBOMConverter
{
    public class SBOMConverterTests : XunitBuildXLTest
    {
        public SBOMConverterTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void ComponentDetectionConverterTest()
        {
            var json = @"{
""componentsFound"": [
    {
    ""locationsFoundAt"": [
        ""/Public/Src/Tools/JavaScript/Tool.YarnGraphBuilder/src/package.json""
    ],
    ""component"": {
        ""name"": ""@microsoft/yarn-graph-builder"",
        ""version"": ""1.0.0"",
        ""hash"": null,
        ""type"": ""Npm"",
        ""id"": ""@microsoft/yarn-graph-builder 1.0.0 - Npm""
    },
    ""detectorId"": ""Npm"",
    ""isDevelopmentDependency"": null,
    ""topLevelReferrers"": [],
    ""containerDetailIds"": []
    }
],
""detectorsInScan"": [],
""ContainerDetailsMap"": {},
""resultCode"": ""Success""
}";

            var (result, packages, logMessages) = WriteJsonAndConvert(json);

            Assert.True(result);
            Assert.NotNull(packages);
            Assert.True(logMessages.Count == 0);
            Assert.True(packages.Count == 1);
            Assert.NotNull(packages[0]);
            Assert.Equal(packages[0].PackageName, "@microsoft/yarn-graph-builder");
            Assert.Equal(packages[0].PackageVersion, "1.0.0");
            Assert.NotNull(packages[0].Checksum);
            var checksums = packages[0].Checksum?.ToList();
            Assert.True(checksums?.Count == 1);
            Assert.Null(checksums[0].ChecksumValue);
        }

        [Fact]
        public void ComponentDetectionResultWithNoComponents()
        {
            var json = @"{
    ""componentsFound"": [],
    ""detectorsInScan"": [],
    ""ContainerDetailsMap"": {},
    ""resultCode"": ""Success""
}";
            var (result, packages, logMessages) = WriteJsonAndConvert(json);

            Assert.NotNull(packages);
            Assert.True(packages?.Count == 0);
            Assert.True(logMessages.Count == 0);
            Assert.True(result);
        }

        [Fact]
        public void MalformedJsonInput()
        {
            var json = "{";
            var (result, packages, logMessages) = WriteJsonAndConvert(json);

            Assert.False(result);
            Assert.True(logMessages.Count > 0);
            Assert.Null(packages);
            Assert.Contains("Unable to parse bcde-output.json", logMessages[0]);
        }

        private (bool, List<SBOMPackage>, List<string>) WriteJsonAndConvert(string json)
        {
            var logMessages = new List<string>();
            var result = ComponentDetectionConverter.TryConvertForUnitTest(json, m => logMessages.Add(m), out var packages);

            return (result, packages?.ToList(), logMessages);
        }
    }
}
