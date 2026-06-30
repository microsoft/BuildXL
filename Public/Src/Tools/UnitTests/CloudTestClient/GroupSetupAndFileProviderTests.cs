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
    public class GroupSetupAndFileProviderTests
    {
        #region Group setup/cleanup and file providers

        [Fact]
        public void GroupSetupIsIncludedInSessionConfig()
        {
            using var temp = new TempDirectory();

            // Build a group with a setup containing a copy entry using a PrefixedPath and a script with compound args
            string setupJson = """
            {
                "buildFiles": [
                    {
                        "source": {"prefix": "BuildRoot", "path": "bin/test.dll"},
                        "destination": "scripts/setup.ps1",
                        "isRecursive": true
                    }
                ],
                "scripts": [
                    {
                        "path": {"prefix": "WorkingDirectory", "path": "tools/setup.cmd"},
                        "args": {"values": ["--config", "test.json"], "separator": " "},
                        "timeoutMins": 10
                    }
                ],
                "timeoutMins": 30
            }
            """;
            string groupFile = GroupFileTestHelper.WriteGroupFile(temp, "group.json", jobsJson: """[{"name":"TestJob"}]""", setupJson: setupJson);

            string json = GroupFileTestHelper.GenerateSessionConfigJson(temp, groupFile);
            using var doc = JsonDocument.Parse(json);
            var group = doc.RootElement.GetProperty("dynamicGroupRequests")[0];
            var setup = group.GetProperty("dynamicGroupSetup");

            // Verify setup timeout
            Assert.Equal(30, setup.GetProperty("timeoutMins").GetInt32());

            // Verify copy entry with CloudTestPath resolution
            var buildFile = setup.GetProperty("buildFiles")[0];
            Assert.Equal(@"[BuildRoot]\bin/test.dll", buildFile.GetProperty("source").GetString());
            Assert.Equal(@"[WorkingDirectory]\scripts/setup.ps1", buildFile.GetProperty("destination").GetString());
            Assert.True(buildFile.GetProperty("isRecursive").GetBoolean());

            // Verify script with compound args resolved to string
            var script = setup.GetProperty("scripts")[0];
            Assert.Equal(@"[WorkingDirectory]\tools/setup.cmd", script.GetProperty("path").GetString());
            Assert.Equal("--config test.json", script.GetProperty("args").GetString());
            Assert.Equal(10, script.GetProperty("timeoutMins").GetInt32());
        }

        [Fact]
        public void GroupCleanupIsIncludedInSessionConfig()
        {
            using var temp = new TempDirectory();

            string cleanupJson = """
            {
                "scripts": [
                    {
                        "path": {"prefix": "WorkingDirectory", "path": "cleanup.ps1"},
                        "args": "--force"
                    }
                ],
                "timeoutMins": 5
            }
            """;
            string groupFile = GroupFileTestHelper.WriteGroupFile(temp, "group.json", jobsJson: """[{"name":"TestJob"}]""", cleanupJson: cleanupJson);

            string json = GroupFileTestHelper.GenerateSessionConfigJson(temp, groupFile);
            using var doc = JsonDocument.Parse(json);
            var group = doc.RootElement.GetProperty("dynamicGroupRequests")[0];
            var cleanup = group.GetProperty("dynamicGroupCleanup");

            Assert.Equal(5, cleanup.GetProperty("timeoutMins").GetInt32());

            var script = cleanup.GetProperty("scripts")[0];
            Assert.Equal(@"[WorkingDirectory]\cleanup.ps1", script.GetProperty("path").GetString());
            Assert.Equal("--force", script.GetProperty("args").GetString());
        }

        [Fact]
        public void FileProvidersAreIncludedInSessionConfig()
        {
            using var temp = new TempDirectory();
            string group = GroupFileTestHelper.BuildGroupJson(jobsJson: """[{"name":"TestJob"}]""");

            string fileProvidersJson = """
            [
                {
                    "type": "VsoDrop",
                    "properties": [
                        {"name": "DropUrl", "value": "https://drop.example.com/build/123"},
                        {"name": "BuildRoot", "value": ""}
                    ]
                }
            ]
            """;

            string sessionInput = GroupFileTestHelper.WriteSessionInputFile(
                temp,
                "session-input.json",
                new[] { group },
                fileProvidersJson: fileProvidersJson);

            string json = GroupFileTestHelper.GenerateSessionConfigJson(temp, sessionInput);
            using var doc = JsonDocument.Parse(json);
            var providers = doc.RootElement.GetProperty("fileProviders");

            Assert.Equal(1, providers.GetArrayLength());
            var provider = providers[0];
            Assert.Equal("VsoDrop", provider.GetProperty("type").GetString());

            var props = provider.GetProperty("properties");
            Assert.Equal(2, props.GetArrayLength());
            Assert.Equal("DropUrl", props[0].GetProperty("name").GetString());
            Assert.Equal("https://drop.example.com/build/123", props[0].GetProperty("value").GetString());
        }

        #endregion
    }
}
