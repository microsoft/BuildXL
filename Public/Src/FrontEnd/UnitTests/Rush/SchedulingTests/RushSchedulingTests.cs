// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using BuildXL.FrontEnd.Rush;
using BuildXL.FrontEnd.Utilities;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration.Mutable;
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
                !dep.Path.IsWithin(PathTable, RushPipConstructor.LogDirectoryBase(
                    result.Configuration, 
                    PathTable, 
                    KnownResolverKind.RushResolverKind))));
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

        [Theory]
        [InlineData("buildxl:bundle")]
        [InlineData("buildxl!@#$%^&*()<>bundle")]
        [InlineData("[]{};'<>/_+bxl")]
        public void RedirectedUserProfileSanitizesScriptCommandName(string scriptCommandName)
        {
            var project = CreateRushProject(scriptCommandName : scriptCommandName);
            var processOutputDirectories = Start()
                .Add(project)
                .ScheduleAll()
                .RetrieveSuccessfulProcess(project)
                .DirectoryOutputs;

            string sanitizedPath = PipConstructionUtilities.SanitizeStringForSymbol(scriptCommandName);

            XAssert.IsTrue(processOutputDirectories.Any(outputDirectory => outputDirectory.Path.ToString(PathTable).Contains(sanitizedPath)));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void BlockExclusionFlagIsHonored(bool blockWritesUnderNodeModules)
        {
            var project = CreateRushProject();

            var exclusions = Start(new RushResolverSettings { BlockWritesUnderNodeModules = blockWritesUnderNodeModules })
                .Add(project)
                .ScheduleAll()
                .RetrieveSuccessfulProcess(project)
                .OutputDirectoryExclusions;

            XAssert.AreEqual(blockWritesUnderNodeModules? 1 : 0, exclusions.Length);
        }

        [Fact]
        public void GlobalUntrackedDirectoryScopesAreHonored()
        {
            var projectB = CreateRushProject("@ms/B");
            var projectA = CreateRushProject("@ms/A", dependencies: new[] { projectB });

            var relativeScopeToUntrack = RelativePath.Create(StringTable, @"untracked\scope");

            var untrackedScopes = Start(new RushResolverSettings
                {
                    UntrackedGlobalDirectoryScopes = new[] { relativeScopeToUntrack }
                })
                .Add(projectB)
                .Add(projectA)
                .ScheduleAll()
                .RetrieveSuccessfulProcess(projectA)
                .UntrackedScopes;

            // The untracked scope should be configured under every project root
            XAssert.Contains(untrackedScopes, projectA.ProjectFolder.Combine(PathTable, relativeScopeToUntrack), projectB.ProjectFolder.Combine(PathTable, relativeScopeToUntrack));
        }

        [Fact]
        public void DoubleWritePolicyIsHonored()
        {
            var project = CreateRushProject();

            var rewritePolicy = Start(new RushResolverSettings { DoubleWritePolicy = global::BuildXL.Utilities.Configuration.RewritePolicy.UnsafeFirstDoubleWriteWins })
                .Add(project)
                .ScheduleAll()
                .RetrieveSuccessfulProcess(project)
                .RewritePolicy;

            XAssert.AreEqual(global::BuildXL.Utilities.Configuration.RewritePolicy.UnsafeFirstDoubleWriteWins, rewritePolicy & global::BuildXL.Utilities.Configuration.RewritePolicy.UnsafeFirstDoubleWriteWins);
        }

        [Fact]
        public void BreakawayProcessIsHonored()
        {
            var project = CreateRushProject();
            var breakawayTest = PathAtom.Create(StringTable, "test.exe");
            var breakawayProcesses = Start(new RushResolverSettings { ChildProcessesToBreakawayFromSandbox = new[] { breakawayTest } })
                .Add(project)
                .ScheduleAll()
                .RetrieveSuccessfulProcess(project)
                .ChildProcessesToBreakawayFromSandbox;

            XAssert.Contains(breakawayProcesses, breakawayTest);
        }

        [Fact]
        public void SurvivingProcessIsHonored()
        {
            var project = CreateRushProject();
            var survivingTest = PathAtom.Create(StringTable, "test.exe");
            var survivingProcesses = Start(new RushResolverSettings { AllowedSurvivingChildProcesses = new[] { survivingTest } })
                .Add(project)
                .ScheduleAll()
                .RetrieveSuccessfulProcess(project)
                .AllowedSurvivingChildProcessNames;

            XAssert.Contains(survivingProcesses, survivingTest);
        }

        [Fact]
        public void RetryCodesAreHonored()
        {
            var project = CreateRushProject();

            var retryExitCodes = Start(new RushResolverSettings { RetryExitCodes = new[] { 42 } })
                .Add(project)
                .ScheduleAll()
                .RetrieveSuccessfulProcess(project)
                .RetryExitCodes;

            XAssert.Contains(retryExitCodes, 42);
        }

        [Fact]
        public void SuccessfulCodesAreHonored()
        {
            var project = CreateRushProject();

            var successfullCodes = Start(new RushResolverSettings { SuccessExitCodes = new[] { 42 } })
                .Add(project)
                .ScheduleAll()
                .RetrieveSuccessfulProcess(project)
                .SuccessExitCodes;

            XAssert.Contains(successfullCodes, 42);
        }

        [Fact]
        public void ProcessRetriesAreHonored()
        {
            var project = CreateRushProject();

            var processRetries = Start(new RushResolverSettings { ProcessRetries = 42 })
                .Add(project)
                .ScheduleAll()
                .RetrieveSuccessfulProcess(project)
                .ProcessRetries;

            XAssert.AreEqual(processRetries, 42);
        }

        [Fact]
        public void ProcessNestedTerminationTimeoutIsHonored()
        {
            var project = CreateRushProject();

            var nestedProcessTerminationTimeout = Start(new RushResolverSettings { NestedProcessTerminationTimeoutMs = 42 })
                .Add(project)
                .ScheduleAll()
                .RetrieveSuccessfulProcess(project)
                .NestedProcessTerminationTimeout;

            XAssert.AreEqual(nestedProcessTerminationTimeout, TimeSpan.FromMilliseconds(42));
        }
    }
}
