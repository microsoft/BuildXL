using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace BuildToolsInstaller.Tests
{
    public class BuildXLInstallerTests
    {
        const string DefaultRing = "GeneralPublic";
        const string DefaultVersion = "0.1.0-20252610.1";

        [Fact]
        public async Task AdoEnvironmentPickedUpByDefault()
        {
             // If we're not in ADO, this will simulate that we are
            var toolsDirectory = Path.Combine(Path.GetTempPath(), "Test");
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

                var args = new BuildToolsInstallerArgs(BuildTool.BuildXL, "GeneralPublic", mockAdoService.ToolsDirectory, configTempPath, false);
                var buildXLInstaller = new BuildXLNugetInstaller(mockDownloader, mockAdoService, log);
                var result = await buildXLInstaller.InstallAsync(DefaultVersion, args);
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

        [Fact]
        public async Task InstallWithCustomConfig()
        {
            var downloadDirectory = Path.Join(Path.GetTempPath(), $"Test_{nameof(InstallWithCustomConfig)}");
            if (Directory.Exists(downloadDirectory))
            {
                Directory.Delete(downloadDirectory, true);
            }

            var configTempPath = Path.GetTempFileName();
            File.WriteAllText(configTempPath, """
                {
                    "FeedOverride": "http://dummy-feed-wont-use",
                    "Version": "0.1.0-20241026.1"
                }
                """);
            try
            {
                var mockDownloader = new MockNugetDownloader();
                var log = new TestLogger();

                var mockAdoService = new MockAdoService()
                {
                    ToolsDirectory = downloadDirectory
                };

                var args = new BuildToolsInstallerArgs(BuildTool.BuildXL, DefaultRing, downloadDirectory, configTempPath, false);

                var buildXLInstaller = new BuildXLNugetInstaller(mockDownloader, mockAdoService, log);
                var result = await buildXLInstaller.InstallAsync(DefaultVersion, args);
                Assert.True(result, log.FullLog);

                Assert.Single(mockDownloader.Downloads);

                var download = mockDownloader.Downloads[0];
                Assert.Equal("http://dummy-feed-wont-use", download.Repository);
                Assert.Equal(Path.Combine(downloadDirectory, "BuildXL", "x64"), download.DownloadLocation);
                Assert.Equal("0.1.0-20241026.1", download.Version);
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
                    ""FeedOverride"": ""http://dummy-feed-wont-use"",
                    ""Version"": ""{version}""
                }}
                ");
            // Simulate the engine being already downloaded so the caching logic applies
            var toolsDirectory = Path.GetTempPath();
            var engineLocation = BuildXLNugetInstaller.GetDownloadLocation(toolsDirectory, version);
            Directory.CreateDirectory(engineLocation);
            try
            {
                var mockDownloader = new MockNugetDownloader();
                var log = new TestLogger();

                var args = new BuildToolsInstallerArgs(BuildTool.BuildXL, DefaultRing, toolsDirectory, configTempPath, force);
                var mockAdoService = new MockAdoService() { ToolsDirectory = toolsDirectory };
                var buildXLInstaller = new BuildXLNugetInstaller(mockDownloader, mockAdoService, log);
                var result = await buildXLInstaller.InstallAsync(DefaultVersion, args);
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

            }
            finally
            {
                File.Delete(configTempPath);
            }
        }

        private static void AssertLogContains(TestLogger log, string contents)
        {
            Assert.Contains(contents, log.FullLog, StringComparison.OrdinalIgnoreCase);
        }
    }
}
