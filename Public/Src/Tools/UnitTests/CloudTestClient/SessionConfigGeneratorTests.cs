// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tool.CloudTestClient;
using Xunit;

namespace Test.Tool.CloudTestClient
{
    public class SessionConfigGeneratorTests
    {
        #region NewStableGuid (via GenerateSessionConfig)

        [Fact]
        public void GenerateSessionConfigProducesDeterministicJobIds()
        {
            using var temp = new TempDirectory();
            string groupFile = GroupFileTestHelper.WriteGroupFile(temp, "group.json", jobsJson: """[{"name":"TestSuite_Stable"}]""");

            string json1 = GroupFileTestHelper.GenerateSessionConfigJson(temp, groupFile);
            string json2 = GroupFileTestHelper.GenerateSessionConfigJson(temp, groupFile);

            // Extract job IDs from both runs
            string jobId1 = GroupFileTestHelper.ExtractJobId(json1);
            string jobId2 = GroupFileTestHelper.ExtractJobId(json2);

            Assert.Equal(jobId1, jobId2);
            // Ensure it's a valid GUID
            Assert.True(Guid.TryParse(jobId1, out _));
        }

        [Fact]
        public void DifferentJobNamesProduceDifferentIds()
        {
            using var temp = new TempDirectory();
            string groupFile1 = GroupFileTestHelper.WriteGroupFile(temp, "group1.json", jobsJson: """[{"name":"JobAlpha"}]""");
            string groupFile2 = GroupFileTestHelper.WriteGroupFile(temp, "group2.json", jobsJson: """[{"name":"JobBeta"}]""");

            string jobId1 = GroupFileTestHelper.ExtractJobId(GroupFileTestHelper.GenerateSessionConfigJson(temp, groupFile1));
            string jobId2 = GroupFileTestHelper.ExtractJobId(GroupFileTestHelper.GenerateSessionConfigJson(temp, groupFile2));

            Assert.NotEqual(jobId1, jobId2);
        }

        [Fact]
        public void ExplicitJobIdIsPreserved()
        {
            using var temp = new TempDirectory();
            string explicitId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
            string groupFile = GroupFileTestHelper.WriteGroupFile(temp, "group.json", jobsJson: $$"""[{"name":"MyJob","id":"{{explicitId}}"}]""");

            string jobId = GroupFileTestHelper.ExtractJobId(GroupFileTestHelper.GenerateSessionConfigJson(temp, groupFile));

            Assert.Equal(explicitId, jobId);
        }

        [Fact]
        public void MultipleGroupsAreIncludedInSessionConfig()
        {
            using var temp = new TempDirectory();
            string group1 = GroupFileTestHelper.BuildGroupJson(
                image: "ubuntu22.04", sku: "Standard_D4s_v3", maxResources: 2, jobsJson: """[{"name":"JobA"}]""");
            string group2 = GroupFileTestHelper.BuildGroupJson(
                image: "windows2022", sku: "Standard_D8s_v3", maxResources: 4, jobsJson: """[{"name":"JobB"},{"name":"JobC"}]""");
            string groupsFile = GroupFileTestHelper.WriteGroupsFile(temp, "groups.json", group1, group2);

            using var doc = JsonDocument.Parse(GroupFileTestHelper.GenerateSessionConfigJson(temp, groupsFile));
            var groups = doc.RootElement.GetProperty("dynamicGroupRequests");
            Assert.Equal(2, groups.GetArrayLength());

            // First group
            Assert.Equal("ubuntu22.04", groups[0].GetProperty("image").GetString());
            Assert.Equal("Standard_D4s_v3", groups[0].GetProperty("sku").GetString());
            Assert.Equal(2, groups[0].GetProperty("maxResources").GetInt32());
            Assert.Equal(1, groups[0].GetProperty("dynamicJobRequests").GetArrayLength());

            // Second group
            Assert.Equal("windows2022", groups[1].GetProperty("image").GetString());
            Assert.Equal("Standard_D8s_v3", groups[1].GetProperty("sku").GetString());
            Assert.Equal(4, groups[1].GetProperty("maxResources").GetInt32());
            Assert.Equal(2, groups[1].GetProperty("dynamicJobRequests").GetArrayLength());

            // Each group gets its own (distinct) groupId derived from image+sku
            Assert.NotEqual(groups[0].GetProperty("groupId").GetString(), groups[1].GetProperty("groupId").GetString());

            // When no explicit name is provided, the group name defaults to "image sku"
            Assert.Equal("ubuntu22.04 Standard_D4s_v3", groups[0].GetProperty("groupName").GetString());
            Assert.Equal("windows2022 Standard_D8s_v3", groups[1].GetProperty("groupName").GetString());
        }

        [Fact]
        public void ExplicitGroupNameIsUsedInSessionConfig()
        {
            using var temp = new TempDirectory();
            string groupFile = GroupFileTestHelper.WriteGroupFile(
                temp, "group.json", jobsJson: """[{"name":"JobA"}]""", name: "my-friendly-group");

            using var doc = JsonDocument.Parse(GroupFileTestHelper.GenerateSessionConfigJson(temp, groupFile));
            var group = doc.RootElement.GetProperty("dynamicGroupRequests")[0];
            Assert.Equal("my-friendly-group", group.GetProperty("groupName").GetString());
        }

        [Fact]
        public void DuplicateGroupNameThrows()
        {
            using var temp = new TempDirectory();
            // Two groups with the same explicit name (here also same image/sku, but the name is what matters).
            string group1 = GroupFileTestHelper.BuildGroupJson(
                jobsJson: """[{"name":"JobA"}]""", name: "shared-group-name");
            string group2 = GroupFileTestHelper.BuildGroupJson(
                jobsJson: """[{"name":"JobB"}]""", name: "shared-group-name");
            string groupsFile = GroupFileTestHelper.WriteGroupsFile(temp, "groups.json", group1, group2);

            var ex = Assert.Throws<InvalidOperationException>(() => GroupFileTestHelper.GenerateSessionConfigJson(temp, groupsFile));
            Assert.Contains("Duplicate group name", ex.Message);
            Assert.Contains("shared-group-name", ex.Message);
        }

        [Fact]
        public void DuplicateDefaultGroupNameThrows()
        {
            using var temp = new TempDirectory();
            // Two groups with identical image/sku and no explicit name produce the same default group name.
            string group1 = GroupFileTestHelper.BuildGroupJson(
                image: "ubuntu22.04", sku: "Standard_D4s_v3", jobsJson: """[{"name":"JobA"}]""");
            string group2 = GroupFileTestHelper.BuildGroupJson(
                image: "ubuntu22.04", sku: "Standard_D4s_v3", jobsJson: """[{"name":"JobB"}]""");
            string groupsFile = GroupFileTestHelper.WriteGroupsFile(temp, "groups.json", group1, group2);

            var ex = Assert.Throws<InvalidOperationException>(() => GroupFileTestHelper.GenerateSessionConfigJson(temp, groupsFile));
            Assert.Contains("Duplicate group name", ex.Message);
        }

        [Fact]
        public void DuplicateJobNameWithinGroupThrows()
        {
            using var temp = new TempDirectory();
            // Two name-based jobs (no explicit ID) with the same name in the same group.
            string groupFile = GroupFileTestHelper.WriteGroupFile(
                temp, "group.json", jobsJson: """[{"name":"DupeJob"},{"name":"DupeJob"}]""");

            var ex = Assert.Throws<InvalidOperationException>(() => GroupFileTestHelper.GenerateSessionConfigJson(temp, groupFile));
            Assert.Contains("Duplicate job name", ex.Message);
            Assert.Contains("DupeJob", ex.Message);
        }

        [Fact]
        public void DuplicateJobNameWithExplicitIdsIsAllowed()
        {
            using var temp = new TempDirectory();
            // Same name but explicit (distinct) IDs are allowed — the duplicate check only applies to name-based jobs.
            string groupFile = GroupFileTestHelper.WriteGroupFile(
                temp,
                "group.json",
                jobsJson: """[{"name":"SameName","id":"11111111-1111-1111-1111-111111111111"},{"name":"SameName","id":"22222222-2222-2222-2222-222222222222"}]""");

            using var doc = JsonDocument.Parse(GroupFileTestHelper.GenerateSessionConfigJson(temp, groupFile));
            var jobs = doc.RootElement.GetProperty("dynamicGroupRequests")[0].GetProperty("dynamicJobRequests");
            Assert.Equal(2, jobs.GetArrayLength());
        }

        #endregion

        #region NewOptionalSessionConfigFields

        [Fact]
        public void SessionConfigIncludesOptionalFields()
        {
            using var temp = new TempDirectory();
            string group = GroupFileTestHelper.BuildGroupJson(maxParallelismForJobs: 5);
            string sessionInput = GroupFileTestHelper.WriteSessionInputFile(
                temp,
                "session-input.json",
                new[] { group },
                stamp: "wus2-default",
                propertiesJson: """[{"key":"key1","value":"value1"},{"key":"key2","value":"value2"}]""",
                featureExceptionsJson: """["EnableTCDForDynamicJobs"]""");

            string json = GroupFileTestHelper.GenerateSessionConfigJson(temp, sessionInput);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal("wus2-default", root.GetProperty("stamp").GetString());
            Assert.Equal("key1=value1;key2=value2", root.GetProperty("properties").GetString());
            Assert.Equal("EnableTCDForDynamicJobs", root.GetProperty("featureExceptions").GetString());

            var group0 = root.GetProperty("dynamicGroupRequests")[0];
            Assert.Equal(5, group0.GetProperty("maxParallelismForJobs").GetInt32());
        }

        [Fact]
        public void SessionConfigOmitsNullOptionalFields()
        {
            using var temp = new TempDirectory();
            string groupFile = GroupFileTestHelper.WriteGroupFile(temp, "group.json");

            string json = GroupFileTestHelper.GenerateSessionConfigJson(temp, groupFile);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // When not specified, these fields should be omitted from the JSON (DefaultIgnoreCondition.WhenWritingNull)
            Assert.False(root.TryGetProperty("stamp", out _));
            Assert.False(root.TryGetProperty("properties", out _));
            Assert.False(root.TryGetProperty("featureExceptions", out _));

            var group = root.GetProperty("dynamicGroupRequests")[0];
            Assert.False(group.TryGetProperty("maxParallelismForJobs", out _));
        }

        #endregion
    }
}
