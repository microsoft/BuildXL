// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using BuildXL.FrontEnd.JavaScript.Tracing;
using BuildXL.Utilities.Configuration;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.FrontEnd.Yarn.IntegrationTests
{
    public class YarnLocationTests : YarnIntegrationTestBase
    {
        public YarnLocationTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // We don't actually need to execute anything, scheduling is enough
        protected override EnginePhases Phase => EnginePhases.Schedule;

        [Fact]
        public void ExplicitInvalidYarnLocationIsHandled()
        {
            var config = Build(yarnLocation: "f`/path/to/foo`")
                    .AddJavaScriptProject("@ms/project-A", "src/A")
                    .PersistSpecsAndGetConfiguration();

            var engineResult = RunYarnProjects(config);

            Assert.False(engineResult.IsSuccess);
            AssertErrorEventLogged(LogEventId.ProjectGraphConstructionError);
            AssertErrorEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.CannotBuildWorkspace);
        }

        [Fact]
        public void ExplicitListOfDirectoriesIsHandled()
        {
            var pathToYarn = Path.GetDirectoryName(PathToYarn).Replace("\\", "/");

            var config = Build(yarnLocation: $"[d`/path/to/foo`, d`{pathToYarn}`]")
                    .AddJavaScriptProject("@ms/project-A", "src/A")
                    .PersistSpecsAndGetConfiguration();

            var engineResult = RunYarnProjects(config);

            Assert.True(engineResult.IsSuccess);
        }

        [Fact]
        public void InvalidListOfDirectoriesIsHandled()
        {
            var config = Build(yarnLocation: "[d`/path/to/foo`, d`/path/to/another/foo`]")
                    .AddJavaScriptProject("@ms/project-A", "src/A")
                    .PersistSpecsAndGetConfiguration();

            var engineResult = RunYarnProjects(config);

            Assert.False(engineResult.IsSuccess);
            AssertErrorEventLogged(LogEventId.CannotFindGraphBuilderTool);
            AssertErrorEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.CannotBuildWorkspace);
        }

        [Fact]
        public void PathIsUsedWhenYarnLocationIsUndefined()
        {
            // Explicitly undefine the yarn location, but add the path to yarn to PATH
            var environment = new Dictionary<string, string>
            {
                ["PATH"] = PathToNodeFolder + Path.PathSeparator + Path.GetDirectoryName(PathToYarn).Replace("\\", "/")
            };

            var config = Build(environment: environment, yarnLocation: null)
                    .AddJavaScriptProject("@ms/project-A", "src/A")
                    .PersistSpecsAndGetConfiguration();

            var engineResult = RunYarnProjects(config);

            Assert.True(engineResult.IsSuccess);
        }

        [Fact]
        public void YarnInstallationNotFoundIsProperlyHandled()
        {
            // Explicitly undefine the yarn location, but do not expose anything in PATH either
            var config = Build(yarnLocation: null)
                    .AddJavaScriptProject("@ms/project-A", "src/A")
                    .PersistSpecsAndGetConfiguration();

            var engineResult = RunYarnProjects(config);

            Assert.False(engineResult.IsSuccess);
            AssertErrorEventLogged(LogEventId.CannotFindGraphBuilderTool);
            AssertErrorEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.CannotBuildWorkspace);
        }
    }
}
