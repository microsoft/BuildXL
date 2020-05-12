// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using BuildXL.FrontEnd.Rush;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.FrontEnd.Rush
{
    [Trait("Category", "RushSchedulingTests")]
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

        [Fact]
        public void OutputDirectoryIsCreatedAtTheProjectRoot()
        {
            var project = CreateRushProject();

            var processOutputDirectories = Start()
                .Add(project)
                .ScheduleAll()
                .RetrieveSuccessfulProcess(project)
                .DirectoryOutputs;

            // An opaque should cover the project root
            XAssert.IsTrue(processOutputDirectories.Any(outputDirectory => project.ProjectFolder.IsWithin(PathTable, outputDirectory.Path)));
        }

        [Fact]
        public void ScriptNameIsSetAsTag()
        {
            var project = CreateRushProject(scriptCommandName: "some-script");

            var processTags = Start()
                .Add(project)
                .ScheduleAll()
                .RetrieveSuccessfulProcess(project)
                .Tags;

            // The script name should be part of the process tags
            XAssert.Contains(processTags, StringId.Create(StringTable, "some-script"));
        }

        [Fact]
        public void LogFilesAreNotADependency()
        {
            var project1 = CreateRushProject();
            var project2 = CreateRushProject(dependencies: new[] { project1 });

            var result = Start()
                .Add(project1)
                .Add(project2)
                .ScheduleAll();
                
            var dependencies = result.RetrieveSuccessfulProcess(project2).Dependencies;
                
            // None of the dependencies should be under the log directory
            XAssert.IsTrue(dependencies.All(dep => 
                !dep.Path.IsWithin(PathTable, RushPipConstructor.LogDirectoryBase(result.Configuration, PathTable))));
        }

        [Fact]
        public void RedirectedUserProfileIsAnOutputDirectory()
        {
            var project = CreateRushProject();

            var processOutputDirectories = Start()
                .Add(project)
                .ScheduleAll()
                .RetrieveSuccessfulProcess(project)
                .DirectoryOutputs;

            XAssert.IsTrue(processOutputDirectories.Any(outputDirectory => RushPipConstructor.UserProfile(project, PathTable) ==  outputDirectory.Path));
        }
    }
}
