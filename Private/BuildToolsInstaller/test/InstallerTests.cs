// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using System.Linq;
using BuildToolsInstaller.Installers;

namespace BuildToolsInstaller.Tests
{
    /// <summary>
    /// Test the general functionality of the installer
    /// </summary>
    public class InstallerTests : InstallerTestsBase
    {
        [Fact]
        public async Task AdoEnvironmentPickedUpByDefault()
        {
            // If we're not in ADO, this will simulate that we are
            var toolsDirectory = GetTempPathForTest();
            var mockAdoService = new MockAdoService()
            {
                ToolsDirectory = toolsDirectory
            };

            try
            {
                var mockDownloader = new MockNugetDownloader();
                var log = new TestLogger();

                var installer = new ToolInstaller(ToolName, mockDownloader, WriteMockedConfiguration(), mockAdoService, log);
                var result = await installer.InstallAsync(new InstallationArguments()
                {
                    VersionDescriptor = DefaultVersion,
                    OutputVariable = $"ONEES_TOOL_LOCATION_${ToolName}",
                    ToolsDirectory = mockAdoService.ToolsDirectory,
                    IgnoreCache = false,
                    PackageSelector = "Linux"
                });

                Assert.True(result, log.FullLog);

                Assert.Single(mockDownloader.Downloads);

                var download = mockDownloader.Downloads[0];
                // Default feed is within the org
                Assert.StartsWith($"https://pkgs.dev.azure.com/{MockAdoService.OrgName}/_packaging", download.Repository);
                Assert.Equal(DefaultVersion, download.Version);
            }
            finally
            {
                try
                {
                    Directory.Delete(toolsDirectory, true);
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
            var monikerUsed = useCustomVersion ? "0.1.0-20241026.1" : DefaultRing;
            try
            {
                var mockDownloader = new MockNugetDownloader();
                var log = new TestLogger();

                var mockAdoService = new MockAdoService()
                {
                    ToolsDirectory = downloadDirectory
                };

                var installer = new ToolInstaller(ToolName, mockDownloader, WriteMockedConfiguration(), mockAdoService, log);
                var result = await installer.InstallAsync(new InstallationArguments()
                {
                    VersionDescriptor = monikerUsed,
                    PackageSelector = "Linux",
                    OutputVariable = $"ONEES_TOOL_LOCATION_{ToolName}",
                    ToolsDirectory = mockAdoService.ToolsDirectory,
                    IgnoreCache = false,
                    FeedOverride = "http://dummy-feed"
                });
                Assert.True(result, log.FullLog);

                Assert.Single(mockDownloader.Downloads);

                var download = mockDownloader.Downloads[0];
                Assert.Equal("http://dummy-feed", download.Repository);
                string versionUsed = useCustomVersion ? "0.1.0-20241026.1" : DefaultVersion;
                Assert.Equal(Path.Combine(downloadDirectory, ToolName, versionUsed), download.DownloadLocation);
                Assert.Equal(versionUsed, download.Version);

                Assert.Equal(download.DownloadLocation, mockAdoService.Variables[$"ONEES_TOOL_LOCATION_{ToolName}"]);
            }
            finally
            {
                File.Delete(configTempPath);
            }
        }

        [Theory]
        [InlineData("Windows", $"{ToolName}.win-x64")]
        [InlineData("windows", $"{ToolName}.win-x64")]
        [InlineData("Linux", $"{ToolName}.linux-x64")]
        [InlineData("linux", $"{ToolName}.linux-x64")]
        public async Task PackageSelectorTest(string packageSelector, string expectedPackage)
        {
            var downloadDirectory = GetTempPathForTest(packageSelector+expectedPackage);
            if (Directory.Exists(downloadDirectory))
            {
                Directory.Delete(downloadDirectory, true);
            }

            var configTempPath = Path.GetTempFileName();
            try
            {
                var mockDownloader = new MockNugetDownloader();
                var log = new TestLogger();

                var mockAdoService = new MockAdoService()
                {
                    ToolsDirectory = downloadDirectory
                };

                var installer = new ToolInstaller(ToolName, mockDownloader, WriteMockedConfiguration(), mockAdoService, log);
                var result = await installer.InstallAsync(new InstallationArguments()
                {
                    VersionDescriptor = DefaultRing,
                    PackageSelector = packageSelector,
                    OutputVariable = $"ONEES_TOOL_LOCATION_{ToolName}",
                    ToolsDirectory = mockAdoService.ToolsDirectory,
                    IgnoreCache = false,
                    FeedOverride = "http://dummy-feed"
                });
                Assert.True(result, log.FullLog);

                Assert.Single(mockDownloader.Downloads);
                Assert.Contains(expectedPackage, mockDownloader.Downloads.Select(d => d.Package).ToList());
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
            // Simulate the engine being already downloaded so the caching logic applies
            var toolsDirectory = GetTempPathForTest();
            
            var mockDownloader = new MockNugetDownloader();
            var log = new TestLogger();

            var mockAdoService = new MockAdoService() { ToolsDirectory = toolsDirectory };
            var installer = new ToolInstaller(ToolName, mockDownloader, WriteMockedConfiguration(), mockAdoService, log);
            var engineLocation = installer.GetDownloadLocation(toolsDirectory, version);
            Directory.CreateDirectory(engineLocation);
            var result = await installer.InstallAsync(new InstallationArguments
            {
                VersionDescriptor = version,
                PackageSelector = "Linux",
                OutputVariable = $"ONEES_TOOL_LOCATION_{ToolName}",
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

            Assert.Equal(engineLocation, mockAdoService.Variables[$"ONEES_TOOL_LOCATION_{ToolName}"]);
        }

        private static void AssertLogContains(TestLogger log, string contents)
        {
            Assert.Contains(contents, log.FullLog, StringComparison.OrdinalIgnoreCase);
        }
    }
}
