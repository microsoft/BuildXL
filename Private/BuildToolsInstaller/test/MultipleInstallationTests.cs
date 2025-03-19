// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildToolsInstaller.Config;
using BuildToolsInstaller.Installers;
using Xunit;

namespace BuildToolsInstaller.Tests
{
    public class MultipleInstallationTests : InstallerTestsBase
    {
        [Fact]
        public async Task ConcurrencyTest()
        {
            var configurationPath = WriteMockedConfiguration();

            // Let's try to install a thousand versions
            var toolsToInstall = new ToolsToInstall()
            {
                Tools = Enumerable.Range(0, 1000).Select(i => new ToolInstallationConfig()
                {
                    Tool = ToolName,
                    Version = $"0.1.0-20250131.{i}",
                    OutputVariable = $"ONEES_TOOL_LOCATION_{ToolName}_{i}",
                    PackageSelector = "Linux"
                })
                .ToList()
            };

            var toolsDirectory = GetTempPathForTest();
            Directory.CreateDirectory(toolsDirectory);
            var mockDownloader = new MockNugetDownloader();
            var log = new TestLogger();
            var mockAdoService = new MockAdoService() { ToolsDirectory = toolsDirectory };

            var installer = new ToolInstaller(ToolName, mockDownloader, configurationPath, mockAdoService, log);
            var tasks = toolsToInstall.Tools.Select(async tool =>
            {
                return await installer.InstallAsync(new InstallationArguments()
                {
                    VersionDescriptor = tool.Version,
                    PackageSelector = tool.PackageSelector,
                    OutputVariable = tool.OutputVariable,
                    ToolsDirectory = toolsDirectory,
                });
            });

            var results = await Task.WhenAll(tasks);
            Assert.All(results, Assert.True);
            Assert.Equal(1000, mockDownloader.Downloads.Length);
        }

        [Fact]
        public async Task SameVersionIsOnlyInstalledOnce()
        {
            var configurationPath = WriteMockedConfiguration();

            // Let's try to install the same version a thousand times
            var toolsToInstall = new ToolsToInstall()
            {
                Tools = Enumerable.Range(0, 1000).Select(i => new ToolInstallationConfig()
                {
                    Tool = ToolName,
                    OutputVariable = $"ONEES_TOOL_LOCATION_{ToolName}",
                    Version = "Ring0",
                    PackageSelector = "Linux"
                })
                .ToList()
            };

            var toolsDirectory = GetTempPathForTest();
            Directory.CreateDirectory(toolsDirectory);
            var mockDownloader = new MockNugetDownloader();
            var log = new TestLogger();
            var mockAdoService = new MockAdoService() { ToolsDirectory = toolsDirectory };

            var installer = new ToolInstaller(ToolName, mockDownloader, configurationPath, mockAdoService, log);
            var tasks = toolsToInstall.Tools.Select(async tool =>
            {
                return await installer.InstallAsync(new InstallationArguments()
                {
                    VersionDescriptor = tool.Version,
                    PackageSelector = tool.PackageSelector,
                    OutputVariable = tool.OutputVariable,
                    ToolsDirectory = toolsDirectory,
                });
            });

            var results = await Task.WhenAll(tasks);
            Assert.All(results, Assert.True);

            // But we only downloaded the package a single time
            Assert.Single(mockDownloader.Downloads);
        }
    }
}
