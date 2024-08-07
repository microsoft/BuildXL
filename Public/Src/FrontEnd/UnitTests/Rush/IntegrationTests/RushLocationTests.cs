﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using BuildXL.FrontEnd.JavaScript.Tracing;
using BuildXL.Utilities.Configuration;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.FrontEnd.Rush.IntegrationTests
{
    [Trait("Category", "RushLibLocationTests")]
    public class RushLocationTests : RushIntegrationTestBase
    {
        public RushLocationTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // We don't actually need to execute anything, scheduling is enough
        protected override EnginePhases Phase => EnginePhases.Schedule;

        [Theory]
        [InlineData(true)]
        [InlineData(true)]
        public void ExplicitInvalidRushLocationOrLibLocationIsHandled(bool useRushLibLocation)
        {
            var config = (useRushLibLocation
                    ? Build(rushBaseLibLocation: "/path/to/foo")
                    : Build(rushLocation: "/path/to/foo"))
                    .AddJavaScriptProject("@ms/project-A", "src/A")
                    .PersistSpecsAndGetConfiguration();

            var engineResult = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A"),
            });

            Assert.False(engineResult.IsSuccess);
            AssertErrorEventLogged(LogEventId.ProjectGraphConstructionError);
            AssertErrorEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.CannotBuildWorkspace);
        }

        [Fact]
        public void RushInstallationIsUsedWhenRushLibLocationIsUndefined()
        {
            // In order to fake a rush installation, we need to simulate that the command 'rush'
            // is at the root of the installation. Rush is not actually executed here, but is the marker
            // for bxl to understand that a rush installation is there. So any content will do.
            var pathToRushRoot = Path.GetDirectoryName(PathToNodeModules);
            File.WriteAllText(Path.Combine(pathToRushRoot, "rush"), "fake rush");

            // Explicitly undefine the rush base lib location, but add the path to rush to PATH
            var environment = new Dictionary<string, string>
            {
                ["PATH"] = PathToNodeFolder + Path.PathSeparator + pathToRushRoot.Replace("\\", "/")
            };

            var config = Build(environment: environment, rushBaseLibLocation: null)
                    .AddJavaScriptProject("@ms/project-A", "src/A")
                    .PersistSpecsAndGetConfiguration();

            var engineResult = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A"),
            });

            Assert.True(engineResult.IsSuccess);
        }

        [Fact]
        public void RushInstallationNotFoundIsProperlyHandled()
        {
            // Explicitly undefine the rush base lib location, but do not expose anything in PATH
            // that points to a valid Rush installation
            var config = Build(rushBaseLibLocation: null)
                    .AddJavaScriptProject("@ms/project-A", "src/A")
                    .PersistSpecsAndGetConfiguration();

            var engineResult = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A"),
            });

            Assert.False(engineResult.IsSuccess);
            AssertErrorEventLogged(LogEventId.CannotFindGraphBuilderTool);
            AssertErrorEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.CannotBuildWorkspace);
        }

        [Fact]
        public void ExplicitListOfDirectoriesIsHandled()
        {
            var pathToNode = Path.GetDirectoryName(PathToNode).Replace("\\", "/");

            var config = Build(nodeExeLocation: $"[d`/path/to/foo`, d`{pathToNode}`]")
                    .AddJavaScriptProject("@ms/project-A", "src/A")
                    .PersistSpecsAndGetConfiguration();

            var engineResult = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A"),
            });

            Assert.True(engineResult.IsSuccess);
        }

        [Fact]
        public void InvalidListOfDirectoriesIsHandled()
        {
            var config = Build(nodeExeLocation: "[d`/path/to/foo`, d`/path/to/another/foo`]")
                    .AddJavaScriptProject("@ms/project-A", "src/A")
                    .PersistSpecsAndGetConfiguration();

            var engineResult = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A"),
            });

            Assert.False(engineResult.IsSuccess);
            AssertErrorEventLogged(LogEventId.CannotFindGraphBuilderTool);
            AssertErrorEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.CannotBuildWorkspace);
        }
    }
}
