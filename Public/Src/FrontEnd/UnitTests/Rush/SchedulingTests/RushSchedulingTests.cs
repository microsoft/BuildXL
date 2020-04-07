// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.FrontEnd.Rush
{
    public sealed class RushSchedulingTests : RushPipSchedulingTestBase
    {
        public RushSchedulingTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void BasicScheduling()
        {
            Start()
                .Add(CreateRushProject())
                .ScheduleAll()
                .AssertSuccess();
        }

        [Fact]
        public void SimpleDependencyIsHonored()
        {
            var projectA = CreateRushProject("@ms/A");
            var projectB = CreateRushProject("@ms/B", dependencies: new[] { projectA });

            var result = Start()
                .Add(projectA)
                .Add(projectB)
                .ScheduleAll()
                .AssertSuccess();

            AssertDependencyAndDependent(projectA, projectB, result);
        }

        [Fact]
        public void TransitiveDependencyIsHonored()
        {
            // We create a graph A -> B -> C
            var projectC = CreateRushProject("@ms/C");
            var projectB = CreateRushProject("@ms/B", dependencies: new[] { projectC });
            var projectA = CreateRushProject("@ms/A", dependencies: new[] { projectB });

            var result = Start()
                .Add(projectC)
                .Add(projectB)
                .Add(projectA)
                .ScheduleAll()
                .AssertSuccess();

            // We verify B -> C, A -> B and A -> C
            AssertDependencyAndDependent(projectC, projectB, result);
            AssertDependencyAndDependent(projectB, projectA, result);
            AssertDependencyAndDependent(projectC, projectA, result);
        }

        [Fact(Skip = "This should eventually hold, but for now we are not declaring an opaque at the root to avoid node_modules scrubbing delays")]
        public void OutputDirectoryIsCreatedAtTheProjectRoot()
        {
            System.Diagnostics.Debugger.Launch();
            var project = CreateRushProject();

            var processOutputDirectories = Start()
                .Add(project)
                .ScheduleAll()
                .RetrieveSuccessfulProcess(project)
                .DirectoryOutputs;

            // An opaque should cover the project root
            XAssert.IsTrue(processOutputDirectories.Any(outputDirectory => project.ProjectFolder.IsWithin(PathTable, outputDirectory.Path)));
        }
    }
}
