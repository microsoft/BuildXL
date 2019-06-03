// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.FrontEnd.MsBuild;
using BuildXL.FrontEnd.MsBuild.Serialization;
using Test.BuildXL.TestUtilities.Xunit;
using Test.BuildXL.FrontEnd.MsBuild.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.FrontEnd.MsBuild
{
    public class MsBuildProcessBuilderTest : MsBuildPipSchedulingTestBase
    {
        public MsBuildProcessBuilderTest(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void SemistableHashesArePreservedForTheSameSchedule()
        {
            var projectA = CreateProjectWithPredictions("A.proj", outputs: new[] { TestPath.Combine(PathTable, "OutputDirA") }, inputs: new[] { TestPath.Combine(PathTable, "inputFileA") });
            var projectB = CreateProjectWithPredictions("B.proj", outputs: new[] { TestPath.Combine(PathTable, "OutputDirB") }, inputs: new[] { TestPath.Combine(PathTable, "inputFileB") });

            var processes = Start()
                .Add(projectB)
                .Add(projectA)
                .ScheduleAll()
                .AssertSuccess()
                .RetrieveAllProcesses();

            var hashes = new HashSet<string>(processes.Select(pip => pip.FormattedSemiStableHash));

            // Schedule again with the same set of specs
            processes = Start()
                .Add(projectB)
                .Add(projectA)
                .ScheduleAll()
                .AssertSuccess()
                .RetrieveAllProcesses();

            var hashes2 = new HashSet<string>(processes.Select(pip => pip.FormattedSemiStableHash));

            // Semistable hashes of both runs must be equivalent
            Assert.True(HashSet<string>.CreateSetComparer().Equals(hashes, hashes2));
        }

        [Fact]
        public void ProcessIsProperlyConfigured()
        {
            var project = CreateProjectWithPredictions("A.proj");

            var testProj = Start()
                .Add(project)
                .ScheduleAll()
                .AssertSuccess()
                .RetrieveSuccessfulProcess(project);

            // Undeclared sources are allowed as long as they are true sources
            Assert.True(testProj.AllowUndeclaredSourceReads);
            // Double writes are allowed as long as the written content is the same
            Assert.True(testProj.DoubleWritePolicy == DoubleWritePolicy.AllowSameContentDoubleWrites);
            // Working directory is the project directory
            Assert.True(testProj.WorkingDirectory == project.FullPath.GetParent(PathTable));
            // Log file is configured
            testProj.GetOutputs().Any(fa => fa.Path.GetName(PathTable).ToString(PathTable.StringTable) == "msbuild.log");
            // Surviving processes are configured
            testProj.AllowedSurvivingChildProcessNames.ToReadOnlySet().SetEquals(ReadOnlyArray<PathAtom>.FromWithoutCopy(
                PathAtom.Create(PathTable.StringTable, "mspdbsrv.exe"),
                PathAtom.Create(PathTable.StringTable, "vctip.exe"),
                PathAtom.Create(PathTable.StringTable, "conhost.exe")));
        }

        [Fact]
        public void SameProjectWithDifferentGlobalPropertiesCanBeScheduled()
        {
            var project1 = CreateProjectWithPredictions("A.proj", globalProperties: new GlobalProperties(new Dictionary<string, string>{ ["Test"] = "1" }));
            var project2 = CreateProjectWithPredictions("A.proj", globalProperties: new GlobalProperties(new Dictionary<string, string> { ["AnotherTest"] = "1" }));

            var projects = Start()
                .Add(project1)
                .Add(project2)
                .ScheduleAll()
                .AssertSuccess()
                .RetrieveAllProcesses();

            // We should have two processes scheduled
            Assert.Equal(2, projects.Count());
        }

        [Fact]
        public void SameProjectFilenameDifferentPathsSameGlobalPropertiesScheduleAsTwoPips()
        {
            var project1 = CreateProjectWithPredictions("A/Test.proj");
            var project2 = CreateProjectWithPredictions("B/Test.proj");

            var projects = Start()
                .Add(project1)
                .Add(project2)
                .ScheduleAll()
                .AssertSuccess()
                .RetrieveAllProcesses();

            // We should have two processes scheduled
            Assert.Equal(2, projects.Count());
        }

        /// <summary>
        /// This test uses the fact that the underlying representation of a qualifier is guaranteed to preserve key and value construction order.
        /// Otherwise writing a test to make sure the log directory is agnostic to the qualifier value ordering when enumerating is quite tricky
        /// since the accessible surface is based on IEnumerable
        /// </summary>
        [Fact]
        public void DifferentOrderInQualifiersResultsInTheSameLogDirectory()
        {
            var project = CreateProjectWithPredictions("Test.proj");
            var key1Key2Qualifier = FrontEndContext.QualifierTable.CreateQualifier(
                new Tuple<StringId, StringId>(StringId.Create(StringTable, "key1"), StringId.Create(StringTable, "value1")),
                new Tuple<StringId, StringId>(StringId.Create(StringTable, "key2"), StringId.Create(StringTable, "value2")));

            var key1Key2Project = Start(currentQualifier: key1Key2Qualifier)
                .Add(project)
                .ScheduleAll()
                .AssertSuccess()
                .RetrieveSuccessfulProcess(project);

            var key2Key1Qualifier = FrontEndContext.QualifierTable.CreateQualifier(
                new Tuple<StringId, StringId>(StringId.Create(StringTable, "key2"), StringId.Create(StringTable, "value2")),
                new Tuple<StringId, StringId>(StringId.Create(StringTable, "key1"), StringId.Create(StringTable, "value1")));

            var key2Key1Project = Start(currentQualifier: key2Key1Qualifier)
                .Add(project)
                .ScheduleAll()
                .AssertSuccess()
                .RetrieveSuccessfulProcess(project);

            // File outputs (which includes log files) should be the same, even though the qualifier was built with a different order
            Assert.True(key1Key2Project.FileOutputs.SequenceEqual(key2Key1Project.FileOutputs));
        }

        [Theory]
        [InlineData(BuildEnvironmentConstants.MsPdbSrvEndpointEnvVar)]
        [InlineData(BuildEnvironmentConstants.MsBuildLogAsyncEnvVar)]
        public void EnvironmentVariableIsAdded(string environmentVariable)
        {
            var project = CreateProjectWithPredictions("A.proj");
            var testProj = Start()
                .Add(project)
                .ScheduleAll()
                .AssertSuccess()
                .RetrieveSuccessfulProcess(project);

            StringId mspdbsrvEnvVarStringId = StringId.Create(PathTable.StringTable, environmentVariable);
            Assert.True(testProj.EnvironmentVariables.Any(e => e.Name.Equals(mspdbsrvEnvVarStringId)));
        }

        [Fact]
        public void EnvironmentIsHonored()
        {
            var project = CreateProjectWithPredictions("A.proj");
            var testProj = Start(new MsBuildResolverSettings { Environment = new Dictionary<string, string> { ["Test"] = "1" } })
                .Add(project)
                .ScheduleAll()
                .AssertSuccess()
                .RetrieveSuccessfulProcess(project);

            var testEnvironmentVariable = testProj.EnvironmentVariables.First(e => e.Name.ToString(PathTable.StringTable).Equals("Test"));
            Assert.Equal("1", testEnvironmentVariable.Value.ToString(PathTable));
        }

        [Fact]
        public void SettingAnEnvironmentRestrictsTheProcessEnvironment()
        {
            var envVar = Guid.NewGuid().ToString();
            Environment.SetEnvironmentVariable(envVar, "1");

            var project = CreateProjectWithPredictions("A.proj");
            var testProj = Start(new MsBuildResolverSettings { Environment = new Dictionary<string, string>() })
                .Add(project)
                .ScheduleAll()
                .AssertSuccess()
                .RetrieveSuccessfulProcess(project);

            Assert.True(testProj.EnvironmentVariables.All(var => !var.Name.ToString(PathTable.StringTable).Equals(envVar)));
        }

        [Fact]
        public void AbsenceOfEnvironmentExposesAllProcessEnvironment()
        {
            var envVar = Guid.NewGuid().ToString();
            Environment.SetEnvironmentVariable(envVar, "1");

            var project = CreateProjectWithPredictions("A.proj");
            var testProj = Start()
                .Add(project)
                .ScheduleAll()
                .AssertSuccess()
                .RetrieveSuccessfulProcess(project);

            var testEnvironmentVariable = testProj.EnvironmentVariables.First(e => e.Name.ToString(PathTable.StringTable).Equals(envVar));
            Assert.Equal("1", testEnvironmentVariable.Value.ToString(PathTable));
        }

        [FactIfSupported(requiresHeliumDriversAvailable: true)]
        public void RunInContainerFlagSetsTheRightProcessFlag()
        {
            var project = CreateProjectWithPredictions("A.proj");
            var testProj = Start(new MsBuildResolverSettings { RunInContainer = true} )
                .Add(project)
                .ScheduleAll()
                .AssertSuccess().
                RetrieveSuccessfulProcess(project);

            // The process should have the corresponding Process.Options.NeedsToRunInContainer flag and the right
            // isolation level
            Assert.True((testProj.ProcessOptions & Process.Options.NeedsToRunInContainer) != Process.Options.None);
            Assert.True(testProj.ContainerIsolationLevel.IsolateAllOutputs());
        }

        [Fact]
        public void ProjectIsBuiltInIsolationByDefault()
        {
            var project = CreateProjectWithPredictions("A.proj");

            var testProj = Start()
                .Add(project)
                .ScheduleAll()
                .AssertSuccess().
                RetrieveSuccessfulProcess(project);

            var arguments = RetrieveProcessArguments(testProj);

            // A project that is built in isolation always specifies an output cache file, which implies /isolate for MSBuild
            Assert.Contains("/orc", arguments);
            // A project that is built in isolation does not need to specify /p:buildprojectreferences=false
            Assert.DoesNotContain("/p:buildprojectreferences=false", arguments);
        }

        [Fact]
        public void BuildingInIsolationPropagatesCacheFiles()
        {
            var dep1 = CreateProjectWithPredictions("1.proj");
            var dep2 = CreateProjectWithPredictions("2.proj");
            var main = CreateProjectWithPredictions("3.proj", references: new[] { dep1, dep2 });

            var result = Start()
                .Add(dep1)
                .Add(dep2)
                .Add(main)
                .ScheduleAll()
                .AssertSuccess();

            var outputCacheFile1 = result.RetrieveSuccessfulProcess(dep1).FileOutputs.First(fa => fa.Path.GetName(PathTable).ToString(StringTable) == PipConstructor.OutputCacheFileName);
            var outputCacheFile2 = result.RetrieveSuccessfulProcess(dep2).FileOutputs.First(fa => fa.Path.GetName(PathTable).ToString(StringTable) == PipConstructor.OutputCacheFileName);

            string mainArgs = RetrieveProcessArguments(result.RetrieveSuccessfulProcess(main));

            // The arguments of the main project should contain the references to the dependencies' cache files
            Assert.Contains($"/irc:{outputCacheFile1.Path.ToString(PathTable)}", mainArgs);
            Assert.Contains($"/irc:{outputCacheFile2.Path.ToString(PathTable)}", mainArgs);
        }

        [Fact]
        public void ProjectIsBuiltWithLegacyIsolationWhenSpecified()
        {
            var project = CreateProjectWithPredictions("A.proj");

            var testProj = Start(new MsBuildResolverSettings { UseLegacyProjectIsolation = true })
                .Add(project)
                .ScheduleAll()
                .AssertSuccess().
                RetrieveSuccessfulProcess(project);

            var arguments = RetrieveProcessArguments(testProj);

            // A project that is not built in isolation shouldn't specify cache files, nor /isolate
            Assert.DoesNotContain("/orc", arguments);
            Assert.DoesNotContain("/isolate", arguments);
            // A project that is not built in isolation has to rely on /p:buildprojectreferences=false
            Assert.Contains("/p:buildprojectreferences=false", arguments);
        }

        [Theory]
        [InlineData("/noAutoResponse")]
        [InlineData("/nodeReuse:false")]
        public void CommonArgumentsAreSet(string argument)
        {
            var project = CreateProjectWithPredictions("A.proj");

            var testProj = Start(new MsBuildResolverSettings { UseLegacyProjectIsolation = true })
                .Add(project)
                .ScheduleAll()
                .AssertSuccess().
                RetrieveSuccessfulProcess(project);

            var arguments = RetrieveProcessArguments(testProj);

            // The auto-response option should be always off
            Assert.Contains(argument, arguments);
        }
    }
}