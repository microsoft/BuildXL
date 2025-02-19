// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using BuildToolsInstaller.Config;

namespace BuildToolsInstaller.Tests
{
    public class BuildXLInstallerTests : InstallerTestsBase
    {
        private const string DefaultVersion = "0.1.0-20252610.1";

        [Fact]
        public async Task AdoEnvironmentPickedUpByDefault()
        {
            // If we're not in ADO, this will simulate that we are
            var toolsDirectory = GetTempPathForTest();
            var mockAdoService = new MockAdoService()
            {
                ToolsDirectory = toolsDirectory
            };

            var configTempPath = Path.GetTempFileName();
            File.WriteAllText(configTempPath, """
                {
                    "Version": "0.1.0-20241026.1"
                }
                """);
            try
            {
                var mockDownloader = new MockNugetDownloader();
                var log = new TestLogger();

                var buildXLInstaller = new BuildXLNugetInstaller(mockDownloader, WriteMockedConfiguration(), mockAdoService, log);
                var result = await buildXLInstaller.InstallAsync(new InstallationArguments()
                {
                    VersionDescriptor = DefaultVersion,
                    OutputVariable = "ONEES_TOOL_BUILDXL_LOCATION",
                    ExtraConfiguration = configTempPath,
                    ToolsDirectory = mockAdoService.ToolsDirectory,
                    IgnoreCache = false
                });

                Assert.True(result, log.FullLog);

                Assert.Single(mockDownloader.Downloads);

                var download = mockDownloader.Downloads[0];
                // Default feed is within the org
                Assert.StartsWith($"https://pkgs.dev.azure.com/{MockAdoService.OrgName}/_packaging", download.Repository);
                Assert.Equal("0.1.0-20241026.1", download.Version);
            }
            finally
            {
                try
                {
                    Directory.Delete(toolsDirectory, true);
                    File.Delete(configTempPath);
                }
                catch { }
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task InstallTest(bool useCustomVersion)
        {
            var downloadDirectory = GetTempPathForTest(useCustomVersion.ToString());
            if (Directory.Exists(downloadDirectory))
            {
                Directory.Delete(downloadDirectory, true);
            }

            var configTempPath = Path.GetTempFileName();
            var monikerUsed = useCustomVersion ? "0.1.0-20241026.1" : GeneralPublicRing;
            try
            {
                var mockDownloader = new MockNugetDownloader();
                var log = new TestLogger();

                var mockAdoService = new MockAdoService()
                {
                    ToolsDirectory = downloadDirectory
                };

                var buildXLInstaller = new BuildXLNugetInstaller(mockDownloader, WriteMockedConfiguration(), mockAdoService, log);
                var result = await buildXLInstaller.InstallAsync(new InstallationArguments()
                {
                    VersionDescriptor = monikerUsed,
                    OutputVariable = $"ONEES_TOOL_LOCATION_BUILDXL",
                    ToolsDirectory = mockAdoService.ToolsDirectory,
                    IgnoreCache = false,
                    FeedOverride = "http://dummy-feed"
                });
                Assert.True(result, log.FullLog);

                Assert.Single(mockDownloader.Downloads);

                var download = mockDownloader.Downloads[0];
                Assert.Equal("http://dummy-feed", download.Repository);
                var versionUsed = useCustomVersion ? "0.1.0-20241026.1" : GeneralPublicVersion;
                Assert.Equal(Path.Combine(downloadDirectory, "BuildXL", versionUsed), download.DownloadLocation);
                Assert.Equal(versionUsed, download.Version);

                Assert.Equal(download.DownloadLocation, mockAdoService.Variables["ONEES_TOOL_LOCATION_BUILDXL"]);
            }
            finally
            {
                File.Delete(configTempPath);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task InstallationSkip(bool force)
        {
            var version = "0.1.0-20241026.1";
            var configTempPath = Path.GetTempFileName();
            File.WriteAllText(configTempPath, $@"
                {{
                    ""Version"": ""{version}""
                }}
                ");
            // Simulate the engine being already downloaded so the caching logic applies
            var toolsDirectory = GetTempPathForTest();
            try
            {
                var mockDownloader = new MockNugetDownloader();
                var log = new TestLogger();

                var mockAdoService = new MockAdoService() { ToolsDirectory = toolsDirectory };
                var buildXLInstaller = new BuildXLNugetInstaller(mockDownloader, WriteMockedConfiguration(), mockAdoService, log);
                var engineLocation = buildXLInstaller.GetDownloadLocation(toolsDirectory, version);
                Directory.CreateDirectory(engineLocation);
                var result = await buildXLInstaller.InstallAsync(new InstallationArguments
                {
                    VersionDescriptor = version,
                    OutputVariable = $"ONEES_TOOL_LOCATION_BUILDXL",
                    ExtraConfiguration = configTempPath,
                    ToolsDirectory = toolsDirectory,
                    IgnoreCache = force,
                    FeedOverride = "http://dummy-feed-wont-use"
                });
                Assert.True(result, log.FullLog);

                if (force)
                {
                    Assert.Single(mockDownloader.Downloads);
                    AssertLogContains(log, "Installation is forced");
                }
                else
                {
                    // No download was carried out
                    Assert.Empty(mockDownloader.Downloads);
                    AssertLogContains(log, "Skipping download");
                }

                Assert.Equal(engineLocation, mockAdoService.Variables["ONEES_TOOL_LOCATION_BUILDXL"]);
            }
            finally
            {
                File.Delete(configTempPath);
            }
        }

        [Theory]
        [InlineData("distributedrole=Orchestrator", true)]
        [InlineData("distributedrole=Orchestrator;;;;", true)]
        [InlineData("DistributedRole=Orchestrator;invocationkey=hola_hola", true)]
        [InlineData("distributedrole=Orchestrator;distributedrole=Orchestrator;invocationkey=hola_hola", true)]
        [InlineData("WorkerTimeoutmin=10", true)]
        [InlineData("Workertimeoutmin=dsjd", false)]
        [InlineData("distributedrole=Orchestrator;workertimeoutmin=10", true)]
        public void ConfigurationDeserializationFormat(string serialized, bool success)
        {
            var logger = new TestLogger();
            var config = BuildXLNugetInstallerConfig.DeserializeFromString(serialized, logger);
            if (success)
            {
                Assert.NotNull(config);
            }
            else
            {
                Assert.Null(config);
            }
        }

        [Fact]
        public void ConfigurationDeserializationCorrectness()
        {
            var serialized = "distributedrole=Orchestrator;invocationkey=myinvockey;workertimeoutmin=10";
            var logger = new TestLogger();
            var config = BuildXLNugetInstallerConfig.DeserializeFromString(serialized, logger);
            Assert.NotNull(config);
            Assert.Equal(10, config.WorkerTimeoutMin);
            Assert.Equal("myinvockey", config.InvocationKey);
            Assert.Equal(DistributedRole.Orchestrator, config.DistributedRole);
        }

        private static void AssertLogContains(TestLogger log, string contents)
        {
            Assert.Contains(contents, log.FullLog, StringComparison.OrdinalIgnoreCase);
        }
    }
}
