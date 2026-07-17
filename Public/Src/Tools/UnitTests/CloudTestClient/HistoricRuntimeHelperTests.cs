// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Test.BuildXL.TestUtilities.Xunit;
using Tool.CloudTestClient;
using Xunit;
using ITestOutputHelper = Xunit.ITestOutputHelper;

namespace Test.Tool.CloudTestClient
{
    public class HistoricRuntimeHelperTests : TemporaryStorageTestBase
    {
        public HistoricRuntimeHelperTests(ITestOutputHelper output)
            : base(output)
        {
        }

        #region SanitizeFileName

        // The set of invalid file name characters is OS-specific: on Windows Path.GetInvalidFileNameChars()
        // includes \ / : | * ? < > ", whereas on Linux it only includes '\0' and '/'. These cases assume the
        // Windows character set, so restrict the test to Windows.
        [TheoryIfSupported(requiresWindowsBasedOperatingSystem: true)]
        [InlineData("simple", "simple")]
        [InlineData("with spaces", "with spaces")]
        [InlineData("no-change", "no-change")]
        [InlineData("path/sep", "path_sep")]
        [InlineData("path\\sep", "path_sep")]
        [InlineData("col:on", "col_on")]
        [InlineData("pipe|char", "pipe_char")]
        [InlineData("star*wild", "star_wild")]
        [InlineData("question?mark", "question_mark")]
        [InlineData("angle<>brackets", "angle__brackets")]
        [InlineData("quotes\"here", "quotes_here")]
        public void SanitizeFileNameReplacesInvalidChars(string input, string expected)
        {
            Assert.Equal(expected, HistoricRuntimeHelper.SanitizeFileName(input));
        }

        [Fact]
        public void SanitizeFileNamePreservesValidJobNames()
        {
            // Typical DScript job names are already valid path atoms
            string name = "BuildXL_Tests_Unit";
            Assert.Equal(name, HistoricRuntimeHelper.SanitizeFileName(name));
        }

        #endregion

        #region Batch

        [Fact]
        public void BatchSplitsCorrectly()
        {
            var items = Enumerable.Range(1, 10).ToList();
            var batches = HistoricRuntimeHelper.Batch(items, 3);

            Assert.Equal(4, batches.Count);
            Assert.Equal(new[] { 1, 2, 3 }, batches[0]);
            Assert.Equal(new[] { 4, 5, 6 }, batches[1]);
            Assert.Equal(new[] { 7, 8, 9 }, batches[2]);
            Assert.Equal(new[] { 10 }, batches[3]);
        }

        [Fact]
        public void BatchWithExactMultiple()
        {
            var items = Enumerable.Range(1, 6).ToList();
            var batches = HistoricRuntimeHelper.Batch(items, 3);

            Assert.Equal(2, batches.Count);
            Assert.Equal(3, batches[0].Count);
            Assert.Equal(3, batches[1].Count);
        }

        [Fact]
        public void BatchWithEmptyList()
        {
            var batches = HistoricRuntimeHelper.Batch(new List<int>(), 5);
            Assert.Empty(batches);
        }

        #endregion

        #region ExtractJobsFromSessionConfig

        [Fact]
        public void ExtractJobsFromSessionConfigParsesCorrectly()
        {
            string configPath = WriteFile("session-config.json", CreateSessionConfigJson("GroupA",
                ("job-id-1", "TestSuite_Alpha"),
                ("job-id-2", "TestSuite_Beta"),
                ("job-id-3", "TestSuite_Gamma")));

            var jobs = HistoricRuntimeHelper.ExtractJobsFromSessionConfig(configPath);

            Assert.Equal(3, jobs.Count);
            Assert.Equal(("GroupA", "TestSuite_Alpha"), jobs["job-id-1"]);
            Assert.Equal(("GroupA", "TestSuite_Beta"), jobs["job-id-2"]);
            Assert.Equal(("GroupA", "TestSuite_Gamma"), jobs["job-id-3"]);
        }

        [Fact]
        public void ExtractJobsThrowsForEntriesWithMissingFields()
        {
            // A job missing jobId or jobName is malformed and must fail fast
            string json = @"{
                ""dynamicGroupRequests"": [{
                    ""groupName"": ""GroupA"",
                    ""dynamicJobRequests"": [
                        { ""jobId"": ""id-1"", ""jobName"": ""Name1"" },
                        { ""jobId"": ""id-2"" }
                    ]
                }]
            }";

            string configPath = WriteFile("session-config-missing.json", json);

            var ex = Assert.Throws<InvalidOperationException>(() => HistoricRuntimeHelper.ExtractJobsFromSessionConfig(configPath));
            Assert.Contains("jobName", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ExtractJobsReturnsEmptyForNoDynamicGroupRequests()
        {
            string configPath = WriteFile("session-config-empty.json", @"{ ""someOtherProperty"": 42 }");

            var jobs = HistoricRuntimeHelper.ExtractJobsFromSessionConfig(configPath);
            Assert.Empty(jobs);
        }

        [Fact]
        public void ExtractJobsThrowsWhenGroupNameMissing()
        {
            // A group carrying jobs but no resolved groupName is malformed and must fail fast.
            string json = @"{
                ""dynamicGroupRequests"": [{
                    ""dynamicJobRequests"": [
                        { ""jobId"": ""id-1"", ""jobName"": ""Name1"" }
                    ]
                }]
            }";

            string configPath = WriteFile("session-config-no-groupname.json", json);

            var ex = Assert.Throws<InvalidOperationException>(() => HistoricRuntimeHelper.ExtractJobsFromSessionConfig(configPath));
            Assert.Contains("groupName", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region WriteHistoricRuntimeFiles

        [Fact]
        public void WriteHistoricRuntimeFilesCreatesCorrectFiles()
        {
            string outputDir = GetFullPath("runtimes");

            var runtimes = new Dictionary<string, long>
            {
                ["job-id-1"] = 12000,
                ["job-id-2"] = 45000,
            };

            var jobIdToInfo = new Dictionary<string, (string GroupName, string JobName)>
            {
                ["job-id-1"] = ("GroupA", "TestSuite_Alpha"),
                ["job-id-2"] = ("GroupA", "TestSuite_Beta"),
                ["job-id-3"] = ("GroupA", "TestSuite_Gamma"),
            };

            HistoricRuntimeHelper.WriteHistoricRuntimeFiles(outputDir, runtimes, jobIdToInfo);

            // Verify all 3 files exist
            Assert.True(File.Exists(Path.Combine(outputDir, "GroupA_TestSuite_Alpha.json")));
            Assert.True(File.Exists(Path.Combine(outputDir, "GroupA_TestSuite_Beta.json")));
            Assert.True(File.Exists(Path.Combine(outputDir, "GroupA_TestSuite_Gamma.json")));

            // Verify content for jobs with data
            AssertRuntimeFileContent(Path.Combine(outputDir, "GroupA_TestSuite_Alpha.json"), 12000);
            AssertRuntimeFileContent(Path.Combine(outputDir, "GroupA_TestSuite_Beta.json"), 45000);

            // Verify content for job without data (should be -1)
            AssertRuntimeFileContent(Path.Combine(outputDir, "GroupA_TestSuite_Gamma.json"), -1);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void WriteHistoricRuntimeFilesSanitizesNames()
        {
            string outputDir = GetFullPath("runtimes-sanitize");

            var runtimes = new Dictionary<string, long> { ["id-1"] = 100 };
            // The invalid characters here (':', '<') are only replaced on Windows, so this test is Windows-only.
            var jobIdToInfo = new Dictionary<string, (string GroupName, string JobName)> { ["id-1"] = ("grp:1", "job:with/invalid<chars") };

            HistoricRuntimeHelper.WriteHistoricRuntimeFiles(outputDir, runtimes, jobIdToInfo);

            string expectedFile = Path.Combine(outputDir, "grp_1_job_with_invalid_chars.json");
            Assert.True(File.Exists(expectedFile));
            AssertRuntimeFileContent(expectedFile, 100);
        }

        [Fact]
        public void WriteEmptyHistoricRuntimeFilesCreatesMissingFilesOnly()
        {
            string outputDir = GetFullPath("runtimes-empty");
            Directory.CreateDirectory(outputDir);

            // Pre-existing file with real data must not be clobbered.
            string existingFile = Path.Combine(outputDir, "GroupA_TestSuite_Alpha.json");
            File.WriteAllText(existingFile, @"{""avgDurationMs"":9999}");

            var jobIdToInfo = new Dictionary<string, (string GroupName, string JobName)>
            {
                ["job-id-1"] = ("GroupA", "TestSuite_Alpha"),
                ["job-id-2"] = ("GroupA", "TestSuite_Beta"),
            };

            HistoricRuntimeHelper.WriteEmptyHistoricRuntimeFiles(outputDir, jobIdToInfo);

            // Existing file preserved.
            AssertRuntimeFileContent(existingFile, 9999);

            // Missing file created as empty.
            string emptyFile = Path.Combine(outputDir, "GroupA_TestSuite_Beta.json");
            Assert.True(File.Exists(emptyFile));
            Assert.Equal(string.Empty, File.ReadAllText(emptyFile));
        }

        #endregion

        #region ReadRuntimeFromFile

        [Theory]
        [InlineData(@"{""avgDurationMs"":42000}", 42000L)]   // present value
        [InlineData(@"{""avgDurationMs"":-1}", -1L)]          // sentinel: no historic data
        [InlineData(@"{""otherProperty"":123}", null)]        // missing property
        [InlineData("", null)]                                // empty file
        public void ReadRuntimeFromFileReturnsExpectedValue(string content, long? expected)
        {
            string filePath = WriteFile("runtime.json", content);
            long? result = HistoricRuntimeHelper.ReadRuntimeFromFile(filePath);
            Assert.Equal(expected, result);
        }

        #endregion

        #region E2E with mock provider

        [Fact]
        public async Task RetrieveAndWriteWithMockProvider()
        {
            string configPath = WriteFile("session-config-e2e.json", CreateSessionConfigJson("GroupA",
                ("11111111-1111-1111-1111-111111111111", "UnitTests"),
                ("22222222-2222-2222-2222-222222222222", "IntegrationTests"),
                ("33333333-3333-3333-3333-333333333333", "E2ETests")));

            string outputDir = GetFullPath("runtimes-e2e");

            // Mock provider returns data for 2 of 3 jobs
            var mockRuntimes = new Dictionary<string, long>
            {
                ["11111111-1111-1111-1111-111111111111"] = 30000,
                ["22222222-2222-2222-2222-222222222222"] = 120000,
                // E2ETests intentionally omitted to test -1 fallback
            };

            var mockHelper = new MockHistoricRuntimeHelper(mockRuntimes);

            bool result = await mockHelper.RetrieveAndWriteRuntimesAsync(configPath, outputDir, debug: false);
            Assert.True(result);

            // Verify per-job files
            AssertRuntimeFileContent(Path.Combine(outputDir, "GroupA_UnitTests.json"), 30000);
            AssertRuntimeFileContent(Path.Combine(outputDir, "GroupA_IntegrationTests.json"), 120000);
            AssertRuntimeFileContent(Path.Combine(outputDir, "GroupA_E2ETests.json"), -1);
        }

        [Fact]
        public async Task RetrieveAndWriteHonorsCancellation()
        {
            string configPath = WriteFile("session-config-cancel.json", CreateSessionConfigJson("GroupA",
                ("11111111-1111-1111-1111-111111111111", "UnitTests")));

            string outputDir = GetFullPath("runtimes-cancel");

            // Mock query never completes in time; the token cancels first.
            var mockHelper = new MockHistoricRuntimeHelper(new Dictionary<string, long>(), TimeSpan.FromSeconds(30));

            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => mockHelper.RetrieveAndWriteRuntimesAsync(configPath, outputDir, debug: false, log: null, cancellationToken: cts.Token));
        }

        [Fact]
        public void RuntimeFilesConsumedByUpdateDynamicJobEmitBuildXLLines()
        {
            string outputDir = GetFullPath("runtimes-consume");
            Directory.CreateDirectory(outputDir);

            File.WriteAllText(Path.Combine(outputDir, "UnitTests.json"), @"{""avgDurationMs"":30000}");
            File.WriteAllText(Path.Combine(outputDir, "IntegrationTests.json"), @"{""avgDurationMs"":120000}");
            File.WriteAllText(Path.Combine(outputDir, "E2ETests.json"), @"{""avgDurationMs"":-1}");

            // Simulate what UpdateDynamicJobAsync does: read the file and decide whether to emit
            VerifyRuntimeEmission(Path.Combine(outputDir, "UnitTests.json"), expectedEmit: true, expectedValue: 30000);
            VerifyRuntimeEmission(Path.Combine(outputDir, "IntegrationTests.json"), expectedEmit: true, expectedValue: 120000);
            VerifyRuntimeEmission(Path.Combine(outputDir, "E2ETests.json"), expectedEmit: false, expectedValue: -1);
        }

        #endregion

        #region CloudTestClientArgs

        [Theory]
        [InlineData(null, null)]                          // outputDir but no identity is invalid
        [InlineData("my-connection", "MY_ENTRA_TOKEN")]   // both identities yield exactly-one violation
        public void CreateSessionWithInvalidHistoricRuntimeIdentityThrows(string serviceConnectionId, string entraTokenEnvVar)
        {
            string sessionIdFile = Path.GetTempFileName();
            string bodyFile = Path.GetTempFileName();
            try
            {
                Assert.ThrowsAny<Exception>(() => new CloudTestClientArgs(
                    BuildCreateSessionArgs(sessionIdFile, bodyFile, serviceConnectionId, entraTokenEnvVar)));
            }
            finally
            {
                File.Delete(sessionIdFile);
                File.Delete(bodyFile);
            }
        }

        [Theory]
        [InlineData("my-connection", null)]     // ADO service connection identity
        [InlineData(null, "MY_ENTRA_TOKEN")]    // Entra token env var identity
        public void CreateSessionWithSingleHistoricRuntimeIdentityEnablesHistoricRuntimes(string serviceConnectionId, string entraTokenEnvVar)
        {
            string sessionIdFile = Path.GetTempFileName();
            string bodyFile = Path.GetTempFileName();
            try
            {
                var args = new CloudTestClientArgs(
                    BuildCreateSessionArgs(sessionIdFile, bodyFile, serviceConnectionId, entraTokenEnvVar));

                Assert.Equal(CloudTestMode.CreateSession, args.Mode);
                Assert.True(args.HistoricRuntimesEnabled);
                Assert.Equal(serviceConnectionId, args.HistoricRuntimeServiceConnectionId);
                Assert.Equal(entraTokenEnvVar, args.HistoricRuntimeEntraTokenEnvVar);
            }
            finally
            {
                File.Delete(sessionIdFile);
                File.Delete(bodyFile);
            }
        }

        [Fact]
        public void ParseHistoricRuntimeFileForUpdateDynamicJob()
        {
            string sessionIdFile = Path.GetTempFileName();
            string bodyFile = Path.GetTempFileName();
            string runtimeFile = Path.GetTempFileName();
            File.WriteAllText(sessionIdFile, "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

            try
            {
                var args = new CloudTestClientArgs(new[]
                {
                    "/mode:updateDynamicJob",
                    "/tenant:my-tenant",
                    "/tokenEnvVar:DUMMY_TOKEN",
                    "/bodyFile:" + bodyFile,
                    "/sessionIdFile:" + sessionIdFile,
                    "/historicRuntimeFile:" + runtimeFile,
                });

                Assert.Equal(CloudTestMode.UpdateDynamicJob, args.Mode);
                Assert.Equal(runtimeFile, args.HistoricRuntimeFile);
            }
            finally
            {
                File.Delete(sessionIdFile);
                File.Delete(bodyFile);
                File.Delete(runtimeFile);
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Builds createSession CLI args (always including historicRuntimesOutputDir), optionally adding the
        /// service connection and/or Entra token env var identity options when non-null.
        /// </summary>
        private static string[] BuildCreateSessionArgs(string sessionIdFile, string bodyFile, string serviceConnectionId, string entraTokenEnvVar)
        {
            var args = new List<string>
            {
                "/mode:createSession",
                "/tenant:my-tenant",
                "/tokenEnvVar:DUMMY_TOKEN",
                "/bodyFile:" + bodyFile,
                "/sessionIdFile:" + sessionIdFile,
                "/historicRuntimesOutputDir:" + Path.GetTempPath(),
            };

            if (serviceConnectionId != null)
            {
                args.Add("/historicRuntimeServiceConnectionId:" + serviceConnectionId);
            }

            if (entraTokenEnvVar != null)
            {
                args.Add("/historicRuntimeEntraTokenEnvVar:" + entraTokenEnvVar);
            }

            return args.ToArray();
        }

        private static void AssertRuntimeFileContent(string filePath, long expectedAvgDurationMs)
        {
            string json = File.ReadAllText(filePath);
            using var doc = JsonDocument.Parse(json);
            Assert.Equal(expectedAvgDurationMs, doc.RootElement.GetProperty("avgDurationMs").GetInt64());
        }

        /// <summary>
        /// Simulates what UpdateDynamicJobAsync does with a runtime file, verifying whether
        /// a ##buildxl[runtime] line should be emitted based on the avgDurationMs value.
        /// </summary>
        private static void VerifyRuntimeEmission(string filePath, bool expectedEmit, long expectedValue)
        {
            long? avgDurationMs = HistoricRuntimeHelper.ReadRuntimeFromFile(filePath);
            Assert.NotNull(avgDurationMs);
            Assert.Equal(expectedValue, avgDurationMs.Value);

            bool shouldEmit = avgDurationMs.Value >= 0;
            Assert.Equal(expectedEmit, shouldEmit);

            if (shouldEmit)
            {
                string expected = $"##buildxl[runtime]{avgDurationMs.Value}";
                Assert.StartsWith("##buildxl[runtime]", expected);
            }
        }

        /// <summary>
        /// Creates a minimal session config JSON with a single group of the given name and the given (jobId, jobName) pairs.
        /// </summary>
        private static string CreateSessionConfigJson(string groupName, params (string jobId, string jobName)[] jobs)
        {
            var jobRequests = jobs.Select(j => new { jobId = j.jobId, jobName = j.jobName });
            var config = new
            {
                dynamicGroupRequests = new[]
                {
                    new { groupName = groupName, dynamicJobRequests = jobRequests.ToArray() }
                }
            };
            return JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        }

        #endregion
    }

    /// <summary>
    /// A mock implementation of HistoricRuntimeHelper that returns predefined runtime values
    /// without connecting to Kusto.
    /// </summary>
    internal class MockHistoricRuntimeHelper : HistoricRuntimeHelper
    {
        private readonly Dictionary<string, long> m_runtimes;
        private readonly TimeSpan m_delay;

        public MockHistoricRuntimeHelper(Dictionary<string, long> runtimes)
            : this(runtimes, TimeSpan.Zero)
        {
        }

        public MockHistoricRuntimeHelper(Dictionary<string, long> runtimes, TimeSpan delay)
        {
            m_runtimes = runtimes;
            m_delay = delay;
        }

        public override async Task<Dictionary<string, long>> QueryRuntimesAsync(List<string> jobIds, CancellationToken cancellationToken)
        {
            // Simulate a slow query so tests can exercise cancellation via the deadline.
            if (m_delay > TimeSpan.Zero)
            {
                await Task.Delay(m_delay, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            var result = new Dictionary<string, long>();
            foreach (string jobId in jobIds)
            {
                if (m_runtimes.TryGetValue(jobId, out long value))
                {
                    result[jobId] = value;
                }
            }

            return result;
        }
    }
}
