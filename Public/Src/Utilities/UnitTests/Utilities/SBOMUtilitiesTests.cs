// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if MICROSOFT_INTERNAL
using System.IO;
using BuildXL.Utilities.SBOMUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Microsoft.Sbom.Contracts;
using Xunit.Abstractions;
using System;
using Microsoft.Sbom.Contracts.Enums;

namespace Test.BuildXL.Utilities
{
    public class SBOMUtilitiesTest : XunitBuildXLTest
    {
        public SBOMUtilitiesTest(ITestOutputHelper output) : base(output) { }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void SBOMMetadataFromBuildSessionInfoFile(bool useStaticMethod)
        {
            var path = Path.GetTempFileName();
            var uri = "https://mseng.visualstudio.com/DefaultCollection/Domino/_git/BuildXL.Internal";
            File.WriteAllText(path, SampleBsiJson(uri));
            var packageName = "myPackageName";
            SBOMMetadata metadata;

            if (useStaticMethod)
            {
                metadata = BsiMetadataExtractor.ProduceSbomMetadata(path, packageName);
            }
            else
            {
                var bsiHelper = new BsiMetadataExtractor(path);
                _ = bsiHelper.ProduceSbomMetadata("someOtherPackageName");
                metadata = bsiHelper.ProduceSbomMetadata(packageName);
            }

            XAssert.AreEqual(BuildEnvironmentName.BuildXL, metadata.BuildEnvironmentName);
            XAssert.AreEqual(packageName, metadata.PackageName);
            XAssert.AreEqual(SampleBranch, metadata.Branch);
            XAssert.AreEqual(SampleBuildQueueName, metadata.BuildName);
            XAssert.AreEqual(SampleSessionId, metadata.BuildId);
            XAssert.AreEqual(SampleChangeId, metadata.CommitId);
            XAssert.AreEqual(uri, metadata.RepositoryUri);
        }

        [Theory]
        [InlineData("https://mseng.visualstudio.com/DefaultCollection/Domino/_git/BuildXL.Internal", true)]
        [InlineData("git://github.com/chelo/repo", true)]
        [InlineData("OFFDEPOT1:7727", true)]    // This is actually a valid URI
        [InlineData("OFFDEPOT_7727", false)]    
        public void ExtractValidUriFromMetadata(string jsonUri, bool isValid)
        {
            XAssert.AreEqual(Uri.IsWellFormedUriString(jsonUri, UriKind.Absolute), isValid);
            var path = Path.GetTempFileName();
            File.WriteAllText(path, SampleBsiJson(jsonUri));
            var metadata = BsiMetadataExtractor.ProduceSbomMetadata(path, "packageName");
            XAssert.IsTrue(Uri.IsWellFormedUriString(metadata.RepositoryUri, UriKind.Absolute));
        }

        [Fact]
        public void DoNotFailWhenFieldsAreMissing()
        {
            var path = Path.GetTempFileName();
            File.WriteAllText(path, "{ }");
            var metadata = BsiMetadataExtractor.ProduceSbomMetadata(path, "packageName");
            XAssert.AreEqual(BuildEnvironmentName.BuildXL, metadata.BuildEnvironmentName);
            XAssert.AreEqual("packageName", metadata.PackageName);
            XAssert.IsNull(metadata.Branch);
            XAssert.IsNull(metadata.BuildName);
            XAssert.IsNull(metadata.BuildId);
            XAssert.IsNull(metadata.CommitId);
            XAssert.IsNull(metadata.RepositoryUri);
        }

        [Fact]
        public void DeserializationExceptionWhenMalformedJson()
        {
            var path = Path.GetTempFileName();
            File.WriteAllText(path, @"{ ""foo }");
            Assert.Throws<DeserializationException>(() => BsiMetadataExtractor.ProduceSbomMetadata(path, "packageName"));
        }

        // Text extracted from an actual bsi.json, with most fields trimmed down. 
        // Some irrelevant fields are left just to test that deserialization
        // works if the data object underapproximates the actual JSON.
        private const string SampleBuildQueueName = "BuildXL_Internal_Rolling";
        private const string SampleBranch = "main";
        private const string SampleSessionId = "cb3ae7e6-fc66-f1fb-e2a8-7169af1c82fa";
        private const string SampleChangeId = "ff8315980bb436a8c4a10e108e9421a577e6c04d";
        private string SampleBsiJson(string repoUriField) => $@"{{
  ""ClientReturnCodeStr"": ""Success"",
  ""UniqueSessionId"": ""{SampleSessionId}"",
  ""BranchName"": ""{SampleBranch}"",
  ""SessionId"": ""1101_152843"",
  ""ChangeList"": ""{SampleChangeId}"",
  ""BuildQueue"": ""{SampleBuildQueueName}"",
  ""SourceControlServer"": {{
    ""DpkFileExtension"": "".dpk"",
    ""Type"": 2,
    ""Uri"": ""{repoUriField}"",
    ""FriendlyName"": ""BuildXL.Internal"",
    ""CredentialsKey"": ""CBServiceAccts"",
  }},
  ""ChangeSummary"": {{
    ""ChangeId"": ""{SampleChangeId}"",
    ""SubmitTime"": ""2021-11-01T22:14:47Z""
  }},
}}";
    }
}
#endif