// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildToolsInstaller.Config;
using BuildToolsInstaller.Utilities;
using Microsoft.Extensions.Logging;
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
                    Tool = BuildTool.BuildXL,
                    Version = $"0.1.0-20250131.{i}",
                    OutputVariable = $"ONEES_TOOL_LOCATION_BUILDXL_{i}"
                })
                .ToList()
            };

            var toolsDirectory = GetTempPathForTest();
            Directory.CreateDirectory(toolsDirectory);
            var mockDownloader = new MockNugetDownloader();
            var log = new TestLogger();
            var mockAdoService = new MockAdoService() { ToolsDirectory = toolsDirectory };

            var installer = new BuildXLNugetInstaller(mockDownloader, configurationPath, mockAdoService, log);
            var tasks = toolsToInstall.Tools.Select(async tool =>
            {
                return await installer.InstallAsync(new InstallationArguments()
                {
                    VersionDescriptor = tool.Version,
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

            // Let's try to install the same version of BuildXL a thousand times
            var toolsToInstall = new ToolsToInstall()
            {
                Tools = Enumerable.Range(0, 1000).Select(i => new ToolInstallationConfig()
                {
                    Tool = BuildTool.BuildXL,
                    OutputVariable = $"ONEES_TOOL_LOCATION_BUILDXL",
                    Version = "Dogfood"
                })
                .ToList()
            };

            var toolsDirectory = GetTempPathForTest();
            Directory.CreateDirectory(toolsDirectory);
            var mockDownloader = new MockNugetDownloader();
            var log = new TestLogger();
            var mockAdoService = new MockAdoService() { ToolsDirectory = toolsDirectory };

            var installer = new BuildXLNugetInstaller(mockDownloader, configurationPath, mockAdoService, log);
            var tasks = toolsToInstall.Tools.Select(async tool =>
            {
                return await installer.InstallAsync(new InstallationArguments()
                {
                    VersionDescriptor = tool.Version,
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
