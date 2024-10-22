using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildToolsInstaller.Utiltiies;
using Xunit;

namespace BuildToolsInstaller.Tests
{
    public class BuildXLInstallerTests
    {
        [Fact]
        public async Task AdoEnvironmentPickedUpByDefault()
        {
            // We may modify the environment during the test, this will restore it on dispose
            using var modifyEnvironment = new TemporaryTestEnvironment();

            // If we're not in ADO, this will simulate that we are
            var toolsDirectory = Path.Combine(Path.GetTempPath(), "Test");
            modifyEnvironment.Set("AGENT_TOOLSDIRECTORY", toolsDirectory);
            modifyEnvironment.Set("SYSTEM_COLLECTIONURI", "https://mseng.visualstudio.com/");
            modifyEnvironment.Set("TF_BUILD", "True");

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

                var args = new BuildToolsInstallerArgs(BuildTool.BuildXL, AdoUtilities.ToolsDirectory, configTempPath, false);
                var buildXLInstaller = new BuildXLNugetInstaller(mockDownloader, log);
                var result = await buildXLInstaller.InstallAsync(args);
                Assert.True(result, log.FullLog);

                Assert.Single(mockDownloader.Downloads);

                var download = mockDownloader.Downloads[0];
                // Default feed is within the org
                Assert.StartsWith("https://pkgs.dev.azure.com/mseng/_packaging", download.Repository);
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

                var downloadDirectory = Path.GetTempPath();
                var args = new BuildToolsInstallerArgs(BuildTool.BuildXL, downloadDirectory, configTempPath, false);
                var buildXLInstaller = new BuildXLNugetInstaller(mockDownloader, log);
                var result = await buildXLInstaller.InstallAsync(args);
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

                var args = new BuildToolsInstallerArgs(BuildTool.BuildXL, toolsDirectory, configTempPath, force);
                var buildXLInstaller = new BuildXLNugetInstaller(mockDownloader, log);
                var result = await buildXLInstaller.InstallAsync(args);
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
