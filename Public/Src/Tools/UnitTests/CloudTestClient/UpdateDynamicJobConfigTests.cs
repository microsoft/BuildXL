// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tool.CloudTestClient;
using Xunit;

namespace Test.Tool.CloudTestClient
{
    public class UpdateDynamicJobConfigTests
    {
        #region GenerateUpdateDynamicJobConfig

        [Fact]
        public void GroupIdIsResolvedFromSessionConfigByJobName()
        {
            using var temp = new TempDirectory();
            string sessionConfigPath = temp.GetPath("session-config.json");

            // Generate a session config containing a single group with a known job.
            string groupFile = GroupFileTestHelper.WriteGroupFile(
                temp, "group.json", image: "ubuntu22.04", sku: "Standard_D4s_v3", jobsJson: """[{"name":"ResolveMe"}]""");

            var sessionArgs = new CloudTestClientArgs(new[]
            {
                "/mode:generateSessionConfig",
                "/sessionInputFile:" + groupFile,
                "/configOutputFile:" + sessionConfigPath,
            });
            ConfigGeneratorHelper.GenerateSessionConfig(sessionArgs);

            // Read the group's groupId and the job's jobId straight from the generated session config.
            using var sessionDoc = JsonDocument.Parse(File.ReadAllText(sessionConfigPath));
            var groupElement = sessionDoc.RootElement.GetProperty("dynamicGroupRequests")[0];
            string expectedGroupId = groupElement.GetProperty("groupId").GetString();
            string expectedJobId = groupElement.GetProperty("dynamicJobRequests")[0].GetProperty("jobId").GetString();

            // Now generate an UpdateDynamicJob config referencing the job by name. No image/sku is provided —
            // the groupId (and jobId) must be resolved from the session config.
            string sessionIdFile = temp.WriteFile("session-id.txt", "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
            string updateConfigPath = temp.GetPath("update-config.json");

            var updateArgs = new CloudTestClientArgs(new[]
            {
                "/mode:generateUpdateDynamicJobConfig",
                "/sessionIdFile:" + sessionIdFile,
                "/jobName:ResolveMe",
                "/sessionConfigPath:" + sessionConfigPath,
                "/testFolder:ResolveMe",
                "/jobExecutable:" + temp.GetPath("run.sh"),
                "/testExecutionType:Exe",
                "/configOutputFile:" + updateConfigPath,
            });

            Assert.Equal(expectedJobId, updateArgs.JobId);
            Assert.Equal(expectedGroupId, updateArgs.GroupId);

            ConfigGeneratorHelper.GenerateUpdateDynamicJobConfig(updateArgs);

            using var updateDoc = JsonDocument.Parse(File.ReadAllText(updateConfigPath));
            Assert.Equal(expectedGroupId, updateDoc.RootElement.GetProperty("groupId").GetString());
            Assert.Equal(expectedJobId, updateDoc.RootElement.GetProperty("jobId").GetString());
        }

        [Fact]
        public void AmbiguousJobNameAcrossGroupsThrowsWithoutGroupName()
        {
            using var temp = new TempDirectory();
            string sessionConfigPath = temp.GetPath("session-config.json");

            // Two groups (different image/sku) both contain a job with the same name.
            string group1 = GroupFileTestHelper.BuildGroupJson(
                image: "ubuntu22.04", sku: "Standard_D4s_v3", jobsJson: """[{"name":"SharedJob"}]""");
            string group2 = GroupFileTestHelper.BuildGroupJson(
                image: "windows2022", sku: "Standard_D8s_v3", jobsJson: """[{"name":"SharedJob"}]""");
            string groupsFile = GroupFileTestHelper.WriteGroupsFile(temp, "groups.json", group1, group2);

            var sessionArgs = new CloudTestClientArgs(new[]
            {
                "/mode:generateSessionConfig",
                "/sessionInputFile:" + groupsFile,
                "/configOutputFile:" + sessionConfigPath,
            });
            ConfigGeneratorHelper.GenerateSessionConfig(sessionArgs);

            string sessionIdFile = temp.WriteFile("session-id.txt", "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
            string updateConfigPath = temp.GetPath("update-config.json");

            // Referencing the job by name only is ambiguous (it exists in both groups).
            var ex = Assert.Throws<InvalidOperationException>(() => new CloudTestClientArgs(new[]
            {
                "/mode:generateUpdateDynamicJobConfig",
                "/sessionIdFile:" + sessionIdFile,
                "/jobName:SharedJob",
                "/sessionConfigPath:" + sessionConfigPath,
                "/testFolder:SharedJob",
                "/jobExecutable:" + temp.GetPath("run.sh"),
                "/testExecutionType:Exe",
                "/configOutputFile:" + updateConfigPath,
            }));

            Assert.Contains("ambiguous", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void GroupNameDisambiguatesJobNameAcrossGroups()
        {
            using var temp = new TempDirectory();
            string sessionConfigPath = temp.GetPath("session-config.json");

            // Two groups both contain a job named "SharedJob". Give the second group an explicit name.
            string group1 = GroupFileTestHelper.BuildGroupJson(
                image: "ubuntu22.04", sku: "Standard_D4s_v3", jobsJson: """[{"name":"SharedJob"}]""");
            string group2 = GroupFileTestHelper.BuildGroupJson(
                image: "windows2022", sku: "Standard_D8s_v3", jobsJson: """[{"name":"SharedJob"}]""", name: "windows-group");
            string groupsFile = GroupFileTestHelper.WriteGroupsFile(temp, "groups.json", group1, group2);

            var sessionArgs = new CloudTestClientArgs(new[]
            {
                "/mode:generateSessionConfig",
                "/sessionInputFile:" + groupsFile,
                "/configOutputFile:" + sessionConfigPath,
            });
            ConfigGeneratorHelper.GenerateSessionConfig(sessionArgs);

            // Read the expected jobId/groupId from the named (second) group.
            using var sessionDoc = JsonDocument.Parse(File.ReadAllText(sessionConfigPath));
            var namedGroup = sessionDoc.RootElement.GetProperty("dynamicGroupRequests")[1];
            string expectedGroupId = namedGroup.GetProperty("groupId").GetString();
            string expectedJobId = namedGroup.GetProperty("dynamicJobRequests")[0].GetProperty("jobId").GetString();

            string sessionIdFile = temp.WriteFile("session-id.txt", "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
            string updateConfigPath = temp.GetPath("update-config.json");

            // Providing the group name disambiguates the lookup.
            var updateArgs = new CloudTestClientArgs(new[]
            {
                "/mode:generateUpdateDynamicJobConfig",
                "/sessionIdFile:" + sessionIdFile,
                "/jobName:SharedJob",
                "/groupName:windows-group",
                "/sessionConfigPath:" + sessionConfigPath,
                "/testFolder:SharedJob",
                "/jobExecutable:" + temp.GetPath("run.sh"),
                "/testExecutionType:Exe",
                "/configOutputFile:" + updateConfigPath,
            });

            Assert.Equal(expectedJobId, updateArgs.JobId);
            Assert.Equal(expectedGroupId, updateArgs.GroupId);
        }

        [Fact]
        public void GroupNameNotFoundThrows()
        {
            using var temp = new TempDirectory();
            string sessionConfigPath = temp.GetPath("session-config.json");

            string groupFile = GroupFileTestHelper.WriteGroupFile(
                temp, "group.json", image: "ubuntu22.04", sku: "Standard_D4s_v3", jobsJson: """[{"name":"MyJob"}]""", name: "linux-group");

            var sessionArgs = new CloudTestClientArgs(new[]
            {
                "/mode:generateSessionConfig",
                "/sessionInputFile:" + groupFile,
                "/configOutputFile:" + sessionConfigPath,
            });
            ConfigGeneratorHelper.GenerateSessionConfig(sessionArgs);

            string sessionIdFile = temp.WriteFile("session-id.txt", "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
            string updateConfigPath = temp.GetPath("update-config.json");

            // The job exists, but not in a group with the provided name.
            var ex = Assert.Throws<InvalidOperationException>(() => new CloudTestClientArgs(new[]
            {
                "/mode:generateUpdateDynamicJobConfig",
                "/sessionIdFile:" + sessionIdFile,
                "/jobName:MyJob",
                "/groupName:does-not-exist",
                "/sessionConfigPath:" + sessionConfigPath,
                "/testFolder:MyJob",
                "/jobExecutable:" + temp.GetPath("run.sh"),
                "/testExecutionType:Exe",
                "/configOutputFile:" + updateConfigPath,
            }));

            Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("does-not-exist", ex.Message);
        }

        [Fact]
        public void UniqueJobNameAcrossGroupsResolvesWithoutGroupName()
        {
            using var temp = new TempDirectory();
            string sessionConfigPath = temp.GetPath("session-config.json");

            // Two groups with distinct job names. A name-based lookup is unambiguous without a group name,
            // and must resolve to the correct (second) group's id.
            string group1 = GroupFileTestHelper.BuildGroupJson(
                image: "ubuntu22.04", sku: "Standard_D4s_v3", jobsJson: """[{"name":"JobA"}]""");
            string group2 = GroupFileTestHelper.BuildGroupJson(
                image: "windows2022", sku: "Standard_D8s_v3", jobsJson: """[{"name":"JobB"}]""");
            string groupsFile = GroupFileTestHelper.WriteGroupsFile(temp, "groups.json", group1, group2);

            var sessionArgs = new CloudTestClientArgs(new[]
            {
                "/mode:generateSessionConfig",
                "/sessionInputFile:" + groupsFile,
                "/configOutputFile:" + sessionConfigPath,
            });
            ConfigGeneratorHelper.GenerateSessionConfig(sessionArgs);

            using var sessionDoc = JsonDocument.Parse(File.ReadAllText(sessionConfigPath));
            var secondGroup = sessionDoc.RootElement.GetProperty("dynamicGroupRequests")[1];
            string expectedGroupId = secondGroup.GetProperty("groupId").GetString();
            string expectedJobId = secondGroup.GetProperty("dynamicJobRequests")[0].GetProperty("jobId").GetString();

            string sessionIdFile = temp.WriteFile("session-id.txt", "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
            string updateConfigPath = temp.GetPath("update-config.json");

            var updateArgs = new CloudTestClientArgs(new[]
            {
                "/mode:generateUpdateDynamicJobConfig",
                "/sessionIdFile:" + sessionIdFile,
                "/jobName:JobB",
                "/sessionConfigPath:" + sessionConfigPath,
                "/testFolder:JobB",
                "/jobExecutable:" + temp.GetPath("run.sh"),
                "/testExecutionType:Exe",
                "/configOutputFile:" + updateConfigPath,
            });

            Assert.Equal(expectedJobId, updateArgs.JobId);
            Assert.Equal(expectedGroupId, updateArgs.GroupId);
        }

        [Fact]
        public void GroupNameArgumentIsParsed()
        {
            using var temp = new TempDirectory();
            string sessionConfigPath = temp.GetPath("session-config.json");

            string groupFile = GroupFileTestHelper.WriteGroupFile(
                temp, "group.json", image: "ubuntu22.04", sku: "Standard_D4s_v3", jobsJson: """[{"name":"MyJob"}]""", name: "linux-group");

            var sessionArgs = new CloudTestClientArgs(new[]
            {
                "/mode:generateSessionConfig",
                "/sessionInputFile:" + groupFile,
                "/configOutputFile:" + sessionConfigPath,
            });
            ConfigGeneratorHelper.GenerateSessionConfig(sessionArgs);

            string sessionIdFile = temp.WriteFile("session-id.txt", "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
            string updateConfigPath = temp.GetPath("update-config.json");

            var updateArgs = new CloudTestClientArgs(new[]
            {
                "/mode:generateUpdateDynamicJobConfig",
                "/sessionIdFile:" + sessionIdFile,
                "/jobName:MyJob",
                "/groupName:linux-group",
                "/sessionConfigPath:" + sessionConfigPath,
                "/testFolder:MyJob",
                "/jobExecutable:" + temp.GetPath("run.sh"),
                "/testExecutionType:Exe",
                "/configOutputFile:" + updateConfigPath,
            });

            Assert.Equal("linux-group", updateArgs.GroupName);
        }

        [Fact]
        public void GenerateUpdateDynamicJobConfigProducesValidJson()
        {
            using var temp = new TempDirectory();
            string sessionIdFile = temp.WriteFile("session-id.txt", "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
            string configPath = temp.GetPath("update-config.json");

            var args = new CloudTestClientArgs(new[]
            {
                "/mode:generateUpdateDynamicJobConfig",
                "/image:ubuntu22.04",
                "/sku:Standard_D4s_v3",
                "/sessionIdFile:" + sessionIdFile,
                "/jobId:11111111-2222-3333-4444-555555555555",
                "/testFolder:TestSuite_A",
                "/jobExecutable:" + temp.GetPath("run.sh"),
                "/testExecutionType:Exe",
                "/testParserType:TAP",
                "/configOutputFile:" + configPath,
            });

            ConfigGeneratorHelper.GenerateUpdateDynamicJobConfig(args);

            string json = File.ReadAllText(configPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", root.GetProperty("sessionId").GetString());
            Assert.Equal("11111111-2222-3333-4444-555555555555", root.GetProperty("jobId").GetString());
            Assert.Equal("TestSuite_A", root.GetProperty("testFolder").GetString());
            Assert.Equal(temp.GetPath("run.sh"), root.GetProperty("jobExecutable").GetString());
            Assert.Equal("Exe", root.GetProperty("testExecutionType").GetString());
            Assert.Equal("TAP", root.GetProperty("testParserType").GetString());
        }


        [Fact]
        public void RelativeJobExecutableGetsPrefixedWithWorkingDirectory()
        {
            using var temp = new TempDirectory();
            string sessionIdFile = temp.WriteFile("session-id.txt", "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
            string configPath = temp.GetPath("workdir-config.json");

            var args = new CloudTestClientArgs(new[]
            {
                "/mode:generateUpdateDynamicJobConfig",
                "/image:ubuntu22.04",
                "/sku:Standard_D4s_v3",
                "/sessionIdFile:" + sessionIdFile,
                "/jobId:11111111-2222-3333-4444-555555555555",
                "/testFolder:TestSuite_A",
                "/jobExecutable:relative/run.sh",
                "/testExecutionType:Exe",
                "/configOutputFile:" + configPath,
            });

            ConfigGeneratorHelper.GenerateUpdateDynamicJobConfig(args);

            string json = File.ReadAllText(configPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal(Path.Join("[WorkingDirectory]", "relative/run.sh"), root.GetProperty("jobExecutable").GetString());
        }

        #endregion
    }
}
