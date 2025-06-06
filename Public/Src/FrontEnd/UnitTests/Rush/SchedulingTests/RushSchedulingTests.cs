// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.FrontEnd.Rush;
using BuildXL.FrontEnd.Utilities;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Native.IO;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities;
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

            XAssert.IsTrue(processOutputDirectories.Any(outputDirectory => RushPipConstructor.UserProfile(project, PathTable) == outputDirectory.Path));
        }

        [Theory]
        [InlineData("buildxl:bundle")]
        [InlineData("buildxl!@#$%^&*()<>bundle")]
        [InlineData("[]{};'<>/_+bxl")]
        public void RedirectedUserProfileSanitizesScriptCommandName(string scriptCommandName)
        {
            var project = CreateRushProject(scriptCommandName: scriptCommandName);
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

            XAssert.AreEqual(blockWritesUnderNodeModules ? 1 : 0, exclusions.Length);
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
            var breakawayTest = new BreakawayChildProcess() { ProcessName = PathAtom.Create(StringTable, "test.exe") };
            var breakawayProcesses = Start(new RushResolverSettings { ChildProcessesToBreakawayFromSandbox = new[] { new DiscriminatingUnion<PathAtom, IBreakawayChildProcess>(breakawayTest) } })
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
        public void RetryAttemptEnvironmentVariableIsHonored()
        {
            var project = CreateRushProject();

            var retryAttemptEnvVar = Start(new RushResolverSettings { RetryAttemptEnvironmentVariable = "foo" })
                .Add(project)
                .ScheduleAll()
                .RetrieveSuccessfulProcess(project)
                .RetryAttemptEnvironmentVariable;

            XAssert.AreEqual(retryAttemptEnvVar, StringId.Create(StringTable, "foo"));
        }

        [Fact]
        public void UncacheableExitCodesAreHonored()
        {
            var project = CreateRushProject();
            var uncacheableExitCodes = Start(new RushResolverSettings { UncacheableExitCodes = new[] { 42 } })
                .Add(project)
                .ScheduleAll()
                .RetrieveSuccessfulProcess(project)
                .UncacheableExitCodes;
            XAssert.Contains(uncacheableExitCodes, 42);
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

        [Fact]
        public void UndeclaredReadEnforcementIsHonored()
        {
            // Make a project chain such that C -> B -> A
            var projectA = CreateRushProject("@ms/A");
            var projectB = CreateRushProject("@ms/B", dependencies: new[] { projectA });
            var projectC = CreateRushProject("@ms/C", dependencies: new[] { projectB });

            var additionalSourceReadScope = AbsolutePath.Create(PathTable, TestRoot).Combine(PathTable, "additional-source-dir");
            var additionalRegex = ".*ignore.*";
            var additionalRegexId = StringId.Create(StringTable, additionalRegex);

            // Turn on source read scope enforcement and add an additional source read scope to all pips
            var result = Start(new RushResolverSettings
            {
                EnforceSourceReadsUnderPackageRoots = true,
                AdditionalSourceReadsScopes = new List<DiscriminatingUnion<DirectoryArtifact, string, IJavaScriptScopeWithSelector>>()
                        { 
                            new (DirectoryArtifact.CreateWithZeroPartialSealId(additionalSourceReadScope)),
                            new (additionalRegex),
                        },
            })
                .Add(projectA)
                .Add(projectB)
                .Add(projectC)
                .ScheduleAll();

            // Project A should be able to read from its own project folder and the additional scope
            var processA = result.RetrieveSuccessfulProcess(projectA);
            XAssert.Contains(
                processA.AllowedUndeclaredSourceReadScopes,
                projectA.ProjectFolder, 
                additionalSourceReadScope);
            // It should also contain the regex
            XAssert.Contains(
                processA.AllowedUndeclaredSourceReadRegexes.Select(regexDescriptor => regexDescriptor.Pattern),
                additionalRegexId);

            // Same for project B, plus being able to read from A's project folder
            var processB = result.RetrieveSuccessfulProcess(projectB);
            XAssert.Contains(
                processB.AllowedUndeclaredSourceReadScopes,
                projectB.ProjectFolder, 
                projectA.ProjectFolder, 
                additionalSourceReadScope);
            // It should also contain the regex
            XAssert.Contains(
                processB.AllowedUndeclaredSourceReadRegexes.Select(regexDescriptor => regexDescriptor.Pattern),
                additionalRegexId);

            // Same for project C, plus A and B project folder (the transitive closure)
            var processC = result.RetrieveSuccessfulProcess(projectC);
            XAssert.Contains(
                processC.AllowedUndeclaredSourceReadScopes,
                projectC.ProjectFolder, 
                projectB.ProjectFolder, 
                projectA.ProjectFolder, 
                additionalSourceReadScope);
            // It should also contain the regex
            XAssert.Contains(
                processC.AllowedUndeclaredSourceReadRegexes.Select(regexDescriptor => regexDescriptor.Pattern),
                additionalRegexId);
        }


        [Fact]
        public void UndeclaredReadScopesWithSelector()
        {
            // Create independent projects
            var projectA = CreateRushProject("@ms/Project-Named-A");
            var projectB = CreateRushProject("@ms/Project-Named-B");

            var additionalSourceReadScope = AbsolutePath.Create(PathTable, TestRoot).Combine(PathTable, "additional-source-dir");
            var additionalRegex = ".*ignore.*";
            var additionalRegexId = StringId.Create(StringTable, additionalRegex);

            // Turn on source read scope enforcement and add additional scopes selecting the project to which they apply
            var result = Start(new RushResolverSettings
            {
                EnforceSourceReadsUnderPackageRoots = true,
                AdditionalSourceReadsScopes = new List<DiscriminatingUnion<DirectoryArtifact, string, IJavaScriptScopeWithSelector>>()
                        {
                            new (new JavaScriptScopeWithSelector()
                            {
                                Scope = new (DirectoryArtifact.CreateWithZeroPartialSealId(additionalSourceReadScope)),
                                Packages = [new (new JavaScriptProjectRegexSelector() { PackageNameRegex = "Named-A" })],
                            }),
                            new (new JavaScriptScopeWithSelector()
                            {
                                Scope = new (additionalRegex),
                                Packages = [new (new JavaScriptProjectRegexSelector() { PackageNameRegex = "Named-B" })],
                            }),
                        },
                })
                .Add(projectA)
                .Add(projectB)
                .ScheduleAll();

            // Project A should be able to read from the additional scope
            var processA = result.RetrieveSuccessfulProcess(projectA);
            XAssert.Contains(
                processA.AllowedUndeclaredSourceReadScopes,
                additionalSourceReadScope);

            // It should not contain the regex
            XAssert.ContainsNot(
                processA.AllowedUndeclaredSourceReadRegexes.Select(regexDescriptor => regexDescriptor.Pattern),
                additionalRegexId);

            // Project B is the opposite
            var processB = result.RetrieveSuccessfulProcess(projectB);
            XAssert.ContainsNot(
                processB.AllowedUndeclaredSourceReadScopes,
                additionalSourceReadScope);

            XAssert.Contains(
                processB.AllowedUndeclaredSourceReadRegexes.Select(regexDescriptor => regexDescriptor.Pattern),
                additionalRegexId);
        }

        /// <summary>
        /// There is nothing Linux-specific with this test, but under CloudBuild we run with additional directory
        /// translations (e.g. Out folder is usually a reparse point) that this test infra is not aware of, so paths
        /// are not properly translated
        /// </summary>
        [FactIfSupported(requiresLinuxBasedOperatingSystem: true)]
        public void UndeclaredReadReparsePointsAreAutomaticallyIncluded()
        {
            using (var tempStorage = new TempFileStorage(canGetFileNames: true, rootPath: TestRoot))
            {
                var root = tempStorage.GetUniqueDirectory("repo-root");

                // Create a project folder for project A, plus two symlinks such that project-A-symlink-symlink -> project-A-symlink -> project-A
                var projectFolderString = Path.Combine(root, "project-A");
                Directory.CreateDirectory(projectFolderString);

                var projectFolderSymlinkedString = Path.Combine(root, "project-A-symlink");
                var createResult = FileUtilities.TryCreateSymbolicLink(projectFolderSymlinkedString, projectFolderString, isTargetFile: false);
                XAssert.IsTrue(createResult.Succeeded, !createResult.Succeeded ? createResult.Failure.Describe() : string.Empty);

                var projectFolderSymlinked2String = Path.Combine(root, "project-A-symlink-symlink");
                createResult = FileUtilities.TryCreateSymbolicLink(projectFolderSymlinked2String, projectFolderSymlinkedString, isTargetFile: false);
                XAssert.IsTrue(createResult.Succeeded, !createResult.Succeeded ? createResult.Failure.Describe() : string.Empty);

                var projectFolder = AbsolutePath.Create(PathTable, projectFolderString);
                var projectFolderSymlinked = AbsolutePath.Create(PathTable, projectFolderSymlinkedString);
                var projectFolderSymlinked2 = AbsolutePath.Create(PathTable, projectFolderSymlinked2String);

                // The project folder is project-A-symlink-symlink
                var projectA = CreateRushProject("@ms/A", projectFolder: projectFolderSymlinked2);

                // Turn on source read scope enforcement and turn on full reparse point resolution
                var result = Start(new RushResolverSettings
                {
                    Root = AbsolutePath.Create(PathTable, root),
                    EnforceSourceReadsUnderPackageRoots = true,
                },
                    sandboxConfiguration: new SandboxConfiguration() { UnsafeSandboxConfiguration = new UnsafeSandboxConfiguration() { EnableFullReparsePointResolving = true } }
                    )
                    .Add(projectA)
                    .ScheduleAll();

                // Project A should have the original scope (project-A-symlink-symlink) plus the fully resolved scope
                var processA = result.RetrieveSuccessfulProcess(projectA);
                // Project A should be able to read from the point symlinked locations that is needed to reach the fully resolved path
                XAssert.Contains(
                    processA.AllowedUndeclaredSourceReadPaths,
                    projectFolderSymlinked2,
                    projectFolderSymlinked);
            }
        }
    }
}
