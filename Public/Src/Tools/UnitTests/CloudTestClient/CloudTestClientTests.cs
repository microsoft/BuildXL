// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Text.Json;
using Tool.CloudTestClient;
using Xunit;

namespace Test.Tool.CloudTestClient
{
    public class CloudTestClientArgsTests
    {
        #region Argument parsing

        [Fact]
        public void ParseGenerateSessionConfigArgs()
        {
            var args = new CloudTestClientArgs(new[]
            {
                "/mode:generateSessionConfig",
                "/tenant:my-tenant",
                "/buildDropLocation:https://drop.example.com/build/123",
                "/sku:Standard_D4s_v3",
                "/image:ubuntu22.04",
                "/maxResources:3",
                "/jobName:TestSuite_A",
                "/jobName:TestSuite_B",
                "/configOutputFile:" + Path.Combine(Path.GetTempPath(), "session-config.json"),
            });

            Assert.Equal(CloudTestMode.GenerateSessionConfig, args.Mode);
            Assert.Equal("my-tenant", args.Tenant);
            Assert.Equal("https://drop.example.com/build/123", args.BuildDropLocation);
            Assert.Equal("Standard_D4s_v3", args.Sku);
            Assert.Equal("ubuntu22.04", args.Image);
            Assert.Equal(3, args.MaxResources);
            Assert.Equal(2, args.Jobs.Count);
            Assert.Equal("TestSuite_A", args.Jobs[0].Name);
            Assert.Equal("TestSuite_B", args.Jobs[1].Name);
        }

        [Fact]
        public void ParseGenerateUpdateDynamicJobConfigArgs()
        {
            string tempSessionIdFile = Path.GetTempFileName();
            File.WriteAllText(tempSessionIdFile, "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

            try
            {
                var args = new CloudTestClientArgs(new[]
                {
                    "/mode:generateUpdateDynamicJobConfig",
                    "/image:ubuntu22.04",
                    "/sku:Standard_D4s_v3",
                    "/sessionIdFile:" + tempSessionIdFile,
                    "/jobId:11111111-2222-3333-4444-555555555555",
                    "/testFolder:TestSuite_A",
                    "/jobExecutable:" + Path.Combine(Path.GetTempPath(), "run.sh"),
                    "/testExecutionType:Exe",
                    "/configOutputFile:" + Path.Combine(Path.GetTempPath(), "update-config.json"),
                });

                Assert.Equal(CloudTestMode.GenerateUpdateDynamicJobConfig, args.Mode);
                Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", args.SessionId);
                Assert.Equal("11111111-2222-3333-4444-555555555555", args.JobId);
                Assert.Equal("TestSuite_A", args.TestFolder);
                Assert.Equal("Exe", args.TestExecutionType);
            }
            finally
            {
                File.Delete(tempSessionIdFile);
            }
        }

        [Fact]
        public void MissingModeThrows()
        {
            Assert.ThrowsAny<Exception>(() => new CloudTestClientArgs(new[] { "/tenant:foo" }));
        }

        [Fact]
        public void InvalidModeThrows()
        {
            Assert.ThrowsAny<Exception>(() => new CloudTestClientArgs(new[] { "/mode:nonexistent", "/tenant:foo" }));
        }

        [Fact]
        public void MissingMandatoryArgThrows()
        {
            // generateSessionConfig without configOutputFile
            Assert.ThrowsAny<Exception>(() => new CloudTestClientArgs(new[]
            {
                "/mode:generateSessionConfig",
                "/tenant:my-tenant",
                "/buildDropLocation:https://drop.example.com",
                "/sku:Standard_D4s_v3",
                "/image:ubuntu22.04",
                "/maxResources:3",
                "/jobName:TestSuite_A",
            }));
        }

        #endregion

        #region Path validation

        [Fact]
        public void ValidPathsDoNotThrow()
        {
            // Ensure well-formed paths pass validation without throwing
            var args = new CloudTestClientArgs(new[]
            {
                "/mode:generateSessionConfig",
                "/tenant:my-tenant",
                "/buildDropLocation:https://drop.example.com",
                "/sku:Standard_D4s_v3",
                "/image:ubuntu22.04",
                "/maxResources:3",
                "/jobName:TestSuite_A",
                "/configOutputFile:" + Path.Combine(Path.GetTempPath(), "valid-path", "output.json"),
            });

            Assert.Equal(Path.Combine(Path.GetTempPath(), "valid-path", "output.json"), args.ConfigOutputFile);
        }

        #endregion
    }

    public class SessionConfigGeneratorTests
    {
        #region NewStableGuid (via GenerateSessionConfig)

        [Fact]
        public void GenerateSessionConfigProducesDeterministicJobIds()
        {
            string configPath = Path.Combine(Path.GetTempPath(), $"test-config-{Guid.NewGuid()}.json");

            try
            {
                var args1 = new CloudTestClientArgs(new[]
                {
                    "/mode:generateSessionConfig",
                    "/tenant:my-tenant",
                    "/buildDropLocation:https://drop.example.com/build/123",
                    "/sku:Standard_D4s_v3",
                    "/image:ubuntu22.04",
                    "/maxResources:1",
                    "/jobName:TestSuite_Stable",
                    "/configOutputFile:" + configPath,
                });

                ConfigGeneratorHelper.GenerateSessionConfig(args1);
                string json1 = File.ReadAllText(configPath);

                File.Delete(configPath);

                var args2 = new CloudTestClientArgs(new[]
                {
                    "/mode:generateSessionConfig",
                    "/tenant:my-tenant",
                    "/buildDropLocation:https://drop.example.com/build/123",
                    "/sku:Standard_D4s_v3",
                    "/image:ubuntu22.04",
                    "/maxResources:1",
                    "/jobName:TestSuite_Stable",
                    "/configOutputFile:" + configPath,
                });

                ConfigGeneratorHelper.GenerateSessionConfig(args2);
                string json2 = File.ReadAllText(configPath);

                // Extract job IDs from both runs
                string jobId1 = ExtractJobId(json1);
                string jobId2 = ExtractJobId(json2);

                Assert.Equal(jobId1, jobId2);
                // Ensure it's a valid GUID
                Assert.True(Guid.TryParse(jobId1, out _));
            }
            finally
            {
                if (File.Exists(configPath))
                    File.Delete(configPath);
            }
        }

        [Fact]
        public void DifferentJobNamesProduceDifferentIds()
        {
            string configPath1 = Path.Combine(Path.GetTempPath(), $"test-config-{Guid.NewGuid()}.json");
            string configPath2 = Path.Combine(Path.GetTempPath(), $"test-config-{Guid.NewGuid()}.json");

            try
            {
                var args1 = new CloudTestClientArgs(new[]
                {
                    "/mode:generateSessionConfig",
                    "/tenant:my-tenant",
                    "/buildDropLocation:https://drop.example.com/build/123",
                    "/sku:Standard_D4s_v3",
                    "/image:ubuntu22.04",
                    "/maxResources:1",
                    "/jobName:JobAlpha",
                    "/configOutputFile:" + configPath1,
                });

                var args2 = new CloudTestClientArgs(new[]
                {
                    "/mode:generateSessionConfig",
                    "/tenant:my-tenant",
                    "/buildDropLocation:https://drop.example.com/build/123",
                    "/sku:Standard_D4s_v3",
                    "/image:ubuntu22.04",
                    "/maxResources:1",
                    "/jobName:JobBeta",
                    "/configOutputFile:" + configPath2,
                });

                ConfigGeneratorHelper.GenerateSessionConfig(args1);
                ConfigGeneratorHelper.GenerateSessionConfig(args2);

                string jobId1 = ExtractJobId(File.ReadAllText(configPath1));
                string jobId2 = ExtractJobId(File.ReadAllText(configPath2));

                Assert.NotEqual(jobId1, jobId2);
            }
            finally
            {
                if (File.Exists(configPath1)) File.Delete(configPath1);
                if (File.Exists(configPath2)) File.Delete(configPath2);
            }
        }

        [Fact]
        public void ExplicitJobIdIsPreserved()
        {
            string configPath = Path.Combine(Path.GetTempPath(), $"test-config-{Guid.NewGuid()}.json");
            string explicitId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

            try
            {
                var args = new CloudTestClientArgs(new[]
                {
                    "/mode:generateSessionConfig",
                    "/tenant:my-tenant",
                    "/buildDropLocation:https://drop.example.com/build/123",
                    "/sku:Standard_D4s_v3",
                    "/image:ubuntu22.04",
                    "/maxResources:1",
                    $"/jobIdAndName:{explicitId}#MyJob",
                    "/configOutputFile:" + configPath,
                });

                ConfigGeneratorHelper.GenerateSessionConfig(args);
                string jobId = ExtractJobId(File.ReadAllText(configPath));

                Assert.Equal(explicitId, jobId);
            }
            finally
            {
                if (File.Exists(configPath)) File.Delete(configPath);
            }
        }

        #endregion

        #region GenerateUpdateDynamicJobConfig

        [Fact]
        public void GenerateUpdateDynamicJobConfigProducesValidJson()
        {
            string sessionIdFile = Path.GetTempFileName();
            string configPath = Path.Combine(Path.GetTempPath(), $"test-update-{Guid.NewGuid()}.json");
            File.WriteAllText(sessionIdFile, "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

            try
            {
                var args = new CloudTestClientArgs(new[]
                {
                    "/mode:generateUpdateDynamicJobConfig",
                    "/image:ubuntu22.04",
                    "/sku:Standard_D4s_v3",
                    "/sessionIdFile:" + sessionIdFile,
                    "/jobId:11111111-2222-3333-4444-555555555555",
                    "/testFolder:TestSuite_A",
                    "/jobExecutable:" + Path.Combine(Path.GetTempPath(), "run.sh"),
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
                Assert.Equal(Path.Combine(Path.GetTempPath(), "run.sh"), root.GetProperty("jobExecutable").GetString());
                Assert.Equal("Exe", root.GetProperty("testExecutionType").GetString());
                Assert.Equal("TAP", root.GetProperty("testParserType").GetString());
            }
            finally
            {
                if (File.Exists(sessionIdFile)) File.Delete(sessionIdFile);
                if (File.Exists(configPath)) File.Delete(configPath);
            }
        }


        [Fact]
        public void RelativeJobExecutableGetsPrefixedWithWorkingDirectory()
        {
            string sessionIdFile = Path.GetTempFileName();
            string configPath = Path.Combine(Path.GetTempPath(), $"test-workdir-{Guid.NewGuid()}.json");
            File.WriteAllText(sessionIdFile, "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

            try
            {
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
            finally
            {
                if (File.Exists(sessionIdFile)) File.Delete(sessionIdFile);
                if (File.Exists(configPath)) File.Delete(configPath);
            }
        }

        #endregion

        #region AggregateHashes

        [Fact]
        public void MultipleHashesAreAggregatedDeterministically()
        {
            string sessionIdFile = Path.GetTempFileName();
            string configPath1 = Path.Combine(Path.GetTempPath(), $"test-hash1-{Guid.NewGuid()}.json");
            string configPath2 = Path.Combine(Path.GetTempPath(), $"test-hash2-{Guid.NewGuid()}.json");
            File.WriteAllText(sessionIdFile, "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

            try
            {
                // Same hashes in different order should produce the same aggregate
                var args1 = new CloudTestClientArgs(new[]
                {
                    "/mode:generateUpdateDynamicJobConfig",
                    "/image:ubuntu22.04",
                    "/sku:Standard_D4s_v3",
                    "/sessionIdFile:" + sessionIdFile,
                    "/jobId:11111111-2222-3333-4444-555555555555",
                    "/testFolder:TestSuite_A",
                    "/jobExecutable:" + Path.Combine(Path.GetTempPath(), "run.sh"),
                    "/testExecutionType:Exe",
                    "/testDependencyHash:abc123",
                    "/testDependencyHash:def456",
                    "/configOutputFile:" + configPath1,
                });

                var args2 = new CloudTestClientArgs(new[]
                {
                    "/mode:generateUpdateDynamicJobConfig",
                    "/image:ubuntu22.04",
                    "/sku:Standard_D4s_v3",
                    "/sessionIdFile:" + sessionIdFile,
                    "/jobId:11111111-2222-3333-4444-555555555555",
                    "/testFolder:TestSuite_A",
                    "/jobExecutable:" + Path.Combine(Path.GetTempPath(), "run.sh"),
                    "/testExecutionType:Exe",
                    "/testDependencyHash:def456",
                    "/testDependencyHash:abc123",
                    "/configOutputFile:" + configPath2,
                });

                ConfigGeneratorHelper.GenerateUpdateDynamicJobConfig(args1);
                ConfigGeneratorHelper.GenerateUpdateDynamicJobConfig(args2);

                string json1 = File.ReadAllText(configPath1);
                string json2 = File.ReadAllText(configPath2);

                using var doc1 = JsonDocument.Parse(json1);
                using var doc2 = JsonDocument.Parse(json2);

                string hash1 = doc1.RootElement.GetProperty("testDependencyHash").GetString();
                string hash2 = doc2.RootElement.GetProperty("testDependencyHash").GetString();

                Assert.Equal(hash1, hash2);
                Assert.NotNull(hash1);
            }
            finally
            {
                if (File.Exists(sessionIdFile)) File.Delete(sessionIdFile);
                if (File.Exists(configPath1)) File.Delete(configPath1);
                if (File.Exists(configPath2)) File.Delete(configPath2);
            }
        }

        #endregion

        private static string ExtractJobId(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var groups = doc.RootElement.GetProperty("dynamicGroupRequests");
            var jobs = groups[0].GetProperty("dynamicJobRequests");
            return jobs[0].GetProperty("jobId").GetString();
        }
    }
}