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
    public class TestDependencyHashTests
    {
        #region AggregateHashes

        [Fact]
        public void HashAggregationIsDeterministicForIdenticalInputs()
        {
            using var temp = new TempDirectory();
            string sessionIdFile = temp.WriteFile("session-id.txt", "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

            // Identical inputs (same hashes/paths in the same order) must produce the same aggregate.
            string hash1 = AggregateHashFor(temp, sessionIdFile, "det1.json",
                "/testDependencyHash:abc123", "/testDependencyHash:def456", "/testDependencyPath:p/a", "/testDependencyPath:p/b");
            string hash2 = AggregateHashFor(temp, sessionIdFile, "det2.json",
                "/testDependencyHash:abc123", "/testDependencyHash:def456", "/testDependencyPath:p/a", "/testDependencyPath:p/b");

            Assert.NotNull(hash1);
            Assert.Equal(hash1, hash2);
        }

        [Fact]
        public void HashAggregationIsOrderDependent()
        {
            using var temp = new TempDirectory();
            string sessionIdFile = temp.WriteFile("session-id.txt", "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

            // The SDK emits dependencies in a deterministic order; aggregation is order-dependent so that a
            // reordering (e.g. a path swap, which changes the emitted sequence) yields a different fingerprint.
            string hash1 = AggregateHashFor(temp, sessionIdFile, "order1.json",
                "/testDependencyHash:abc123", "/testDependencyHash:def456", "/testDependencyPath:p/a", "/testDependencyPath:p/b");
            string hash2 = AggregateHashFor(temp, sessionIdFile, "order2.json",
                "/testDependencyHash:def456", "/testDependencyHash:abc123", "/testDependencyPath:p/a", "/testDependencyPath:p/b");

            Assert.NotNull(hash1);
            Assert.NotNull(hash2);
            Assert.NotEqual(hash1, hash2);
        }

        [Fact]
        public void PathSwapBetweenArtifactsChangesHash()
        {
            using var temp = new TempDirectory();
            string sessionIdFile = temp.WriteFile("session-id.txt", "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

            // Two artifacts with IDENTICAL content (same hash H) placed at paths p1, p2 versus the same content with
            // the two drop paths swapped. The multiset of content hashes is unchanged, yet the fingerprint must differ
            // because the worker VMs observe a different layout (which content lands at which path).
            string original = AggregateHashFor(temp, sessionIdFile, "swap-original.json",
                "/testDependencyHash:H", "/testDependencyHash:H",
                "/testDependencyPath:p1", "/testDependencyPath:p2");
            string swapped = AggregateHashFor(temp, sessionIdFile, "swap-swapped.json",
                "/testDependencyHash:H", "/testDependencyHash:H",
                "/testDependencyPath:p2", "/testDependencyPath:p1");

            Assert.NotNull(original);
            Assert.NotNull(swapped);
            Assert.NotEqual(original, swapped);
        }

        [Fact]
        public void MismatchedHashAndPathCountsThrow()
        {
            using var temp = new TempDirectory();
            string sessionIdFile = temp.WriteFile("session-id.txt", "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
            string configPath = temp.GetPath("mismatch.json");

            // The SDK always emits one drop-relative path per content hash. An unequal count indicates a wiring bug
            // and must fail loudly rather than silently produce a fingerprint from misaligned pairs.
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
                "/testDependencyHash:h1",
                "/testDependencyHash:h2",
                "/testDependencyPath:p1",
                "/configOutputFile:" + configPath,
            });

            Assert.Throws<InvalidOperationException>(() => ConfigGeneratorHelper.GenerateUpdateDynamicJobConfig(args));
        }

        [Fact]
        public void NoDependenciesProducesNoHash()
        {
            using var temp = new TempDirectory();
            string sessionIdFile = temp.WriteFile("session-id.txt", "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

            // A job referenced by ID with no inputs, no drop artifacts, and no group setup/cleanup or file providers
            // must omit the fingerprint entirely (null), rather than emit a constant hash shared by all input-less jobs.
            string hash = AggregateHashFor(temp, sessionIdFile, "no-deps.json");

            Assert.Null(hash);
        }

        private static string AggregateHashFor(TempDirectory temp, string sessionIdFile, string outputFileName, params string[] extraArgs)
        {
            string configPath = temp.GetPath(outputFileName);
            var argList = new List<string>
            {
                "/mode:generateUpdateDynamicJobConfig",
                "/image:ubuntu22.04",
                "/sku:Standard_D4s_v3",
                "/sessionIdFile:" + sessionIdFile,
                "/jobId:11111111-2222-3333-4444-555555555555",
                "/testFolder:TestSuite_A",
                "/jobExecutable:" + temp.GetPath("run.sh"),
                "/testExecutionType:Exe",
                "/configOutputFile:" + configPath,
            };
            argList.AddRange(extraArgs);

            ConfigGeneratorHelper.GenerateUpdateDynamicJobConfig(new CloudTestClientArgs(argList.ToArray()));

            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            return doc.RootElement.TryGetProperty("testDependencyHash", out var hash) ? hash.GetString() : null;
        }

        [Fact]
        public void TestDependencyPathsContributeToHash()
        {
            using var temp = new TempDirectory();
            string groupFile = GroupFileTestHelper.WriteGroupFile(temp, "group.json", jobsJson: """[{"name":"TestJob"}]""");
            string sessionConfigPath = temp.GetPath("session-config.json");
            GroupFileTestHelper.GenerateSessionConfigForTest(temp, sessionConfigPath, groupFile);

            // Same content hash, but different drop-relative paths must yield different aggregate hashes.
            string hashA = GroupFileTestHelper.GenerateUpdateConfigAndGetHash(temp, sessionConfigPath, "TestJob", "a.json",
                "/testDependencyHash:abc123", "/testDependencyPath:inputs/a.txt");
            string hashB = GroupFileTestHelper.GenerateUpdateConfigAndGetHash(temp, sessionConfigPath, "TestJob", "b.json",
                "/testDependencyHash:abc123", "/testDependencyPath:inputs/b.txt");

            Assert.NotNull(hashA);
            Assert.NotNull(hashB);
            Assert.NotEqual(hashA, hashB);
        }

        [Fact]
        public void GroupSetupCleanupContributesToHash()
        {
            using var temp = new TempDirectory();

            string setupJson = """{"scripts":[{"path":{"prefix":"WorkingDirectory","path":"setup.sh"}}],"timeoutMins":5}""";

            string groupNoSetup = GroupFileTestHelper.WriteGroupFile(temp, "group-no-setup.json", jobsJson: """[{"name":"TestJob"}]""");
            string groupWithSetup = GroupFileTestHelper.WriteGroupFile(temp, "group-with-setup.json", jobsJson: """[{"name":"TestJob"}]""", setupJson: setupJson);

            string sessionNoSetup = temp.GetPath("session-no-setup.json");
            string sessionWithSetup = temp.GetPath("session-with-setup.json");
            GroupFileTestHelper.GenerateSessionConfigForTest(temp, sessionNoSetup, groupNoSetup);
            GroupFileTestHelper.GenerateSessionConfigForTest(temp, sessionWithSetup, groupWithSetup);

            // Hold the explicit content hash/path constant so that the group setup/cleanup is the only difference.
            string hashNoSetup = GroupFileTestHelper.GenerateUpdateConfigAndGetHash(temp, sessionNoSetup, "TestJob", "no-setup.json", "/testDependencyHash:abc123", "/testDependencyPath:inputs/x.txt");
            string hashWithSetup = GroupFileTestHelper.GenerateUpdateConfigAndGetHash(temp, sessionWithSetup, "TestJob", "with-setup.json", "/testDependencyHash:abc123", "/testDependencyPath:inputs/x.txt");

            Assert.NotNull(hashNoSetup);
            Assert.NotNull(hashWithSetup);
            Assert.NotEqual(hashNoSetup, hashWithSetup);
        }

        [Fact]
        public void FileProvidersContributeToHash()
        {
            using var temp = new TempDirectory();
            string group = GroupFileTestHelper.BuildGroupJson(jobsJson: """[{"name":"TestJob"}]""");

            string fileProvidersJson = """
            [
                {
                    "type": "VsoDrop",
                    "properties": [ {"name": "DropUrl", "value": "https://drop.example.com/build/123"} ]
                }
            ]
            """;

            string inputNoProviders = GroupFileTestHelper.WriteSessionInputFile(temp, "input-no-providers.json", new[] { group });
            string inputWithProviders = GroupFileTestHelper.WriteSessionInputFile(temp, "input-with-providers.json", new[] { group }, fileProvidersJson: fileProvidersJson);

            string sessionNoProviders = temp.GetPath("session-no-providers.json");
            string sessionWithProviders = temp.GetPath("session-with-providers.json");
            GroupFileTestHelper.GenerateSessionConfigForTest(temp, sessionNoProviders, inputNoProviders);
            GroupFileTestHelper.GenerateSessionConfigForTest(temp, sessionWithProviders, inputWithProviders);

            string hashNoProviders = GroupFileTestHelper.GenerateUpdateConfigAndGetHash(temp, sessionNoProviders, "TestJob", "no-providers.json", "/testDependencyHash:abc123", "/testDependencyPath:inputs/x.txt");
            string hashWithProviders = GroupFileTestHelper.GenerateUpdateConfigAndGetHash(temp, sessionWithProviders, "TestJob", "with-providers.json", "/testDependencyHash:abc123", "/testDependencyPath:inputs/x.txt");

            Assert.NotNull(hashNoProviders);
            Assert.NotNull(hashWithProviders);
            Assert.NotEqual(hashNoProviders, hashWithProviders);
        }

        #endregion
    }
}
