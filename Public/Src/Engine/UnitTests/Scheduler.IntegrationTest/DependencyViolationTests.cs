// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Core;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using ProcessesLogEventId = BuildXL.Processes.Tracing.LogEventId;
using SchedulerLogEventId = BuildXL.Scheduler.Tracing.LogEventId;

namespace IntegrationTest.BuildXL.Scheduler
{
    /// <remarks>
    /// In general dependency analysis is covered fairly well via the 
    /// <see cref="FileMonitoringViolationAnalyzerTests"/> unit tests. This exists as an end to end integration test
    /// as well as to cover scenarios that are difficult to get coverage through the mocked out harness the unit
    /// tests use.
    /// 
    /// These tests also don't get wrapped by an IncrementalScheduling version because they are about runtime
    /// </remarks>
    public class DependencyViolationTests : SchedulerIntegrationTestBase
    {
        public DependencyViolationTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void ReadRaceFromOpaqueDirectory()
        {
            var opaqueOutput = CreateOutputDirectoryArtifact();
            var outputWithinOpaque = CreateOutputFileArtifact(opaqueOutput.Path.ToString(Context.PathTable));

            Directory.CreateDirectory(opaqueOutput.Path.ToString(Context.PathTable));
            File.WriteAllText(outputWithinOpaque.Path.ToString(Context.PathTable), "content");
            Configuration.Schedule.MaxProcesses = 1;

            var builderA = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact(ObjectRoot)),
                Operation.WriteFile(outputWithinOpaque, doNotInfer: true)

            });
            builderA.AddOutputDirectory(opaqueOutput, SealDirectoryKind.Opaque);
            var writer = SchedulePipBuilder(builderA);

            var builderB = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact(ObjectRoot)),
                Operation.ReadFile(outputWithinOpaque, doNotInfer:true)
            });

            var reader = SchedulePipBuilder(builderB);

            ScheduleRunResult result = RunScheduler(constraintExecutionOrder: new[] { ((Pip)writer.Process, (Pip)reader.Process)}).AssertFailure();

            AssertErrorEventLogged(SchedulerLogEventId.FileMonitoringError);
            AssertWarningEventLogged(SchedulerLogEventId.ProcessNotStoredToCacheDueToFileMonitoringViolations);
            AssertVerboseEventLogged(SchedulerLogEventId.DependencyViolationReadRace);
        }

        [Fact]
        public void UndeclaredOrderReadFromOpaqueDirectory()
        {
            var opaqueOutput = CreateOutputDirectoryArtifact();
            var outputWithinOpaque = CreateOutputFileArtifact(opaqueOutput.Path.ToString(Context.PathTable));
            var aOutput = CreateOutputFileArtifact(ObjectRoot);

            var builderA = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(aOutput),
                Operation.WriteFile(outputWithinOpaque, doNotInfer: true)

            });
            builderA.AddOutputDirectory(opaqueOutput, SealDirectoryKind.Opaque);
            SchedulePipBuilder(builderA);

            var builderB = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact(ObjectRoot)),
                Operation.ReadFile(outputWithinOpaque, doNotInfer:true),
                Operation.ReadFile(aOutput)

            });

            SchedulePipBuilder(builderB);

            ScheduleRunResult result = RunScheduler().AssertFailure();

            AssertErrorEventLogged(SchedulerLogEventId.FileMonitoringError);
            AssertWarningEventLogged(SchedulerLogEventId.ProcessNotStoredToCacheDueToFileMonitoringViolations);
            AssertVerboseEventLogged(global::BuildXL.Scheduler.Tracing.LogEventId.DependencyViolationReadRace);
        }


        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void UnallowedDirectoryCreation(bool failOnUnexpectedFileAccesses)
        {
            // If we allow directory creation under writable mounts, the test is not applicable because the directory it tries to create 
            // is in the scope of a writable mount, i.e., it will always be an allowed operation.
            Configuration.Sandbox.EnforceAccessPoliciesOnDirectoryCreation = true;
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.UnexpectedFileAccessesAreErrors = failOnUnexpectedFileAccesses;

            var declaredOutput = CreateOutputFileArtifact();
            var dir = CreateOutputDirectoryArtifact();

            var builderA = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(declaredOutput),
                Operation.CreateDir(dir, doNotInfer: true),
            });
            SchedulePipBuilder(builderA);

            // It's a clean build, 'dir' is not present, we should always detect directory creation (have errors/warnings).
            ScheduleRunResult result = RunScheduler();
            if (failOnUnexpectedFileAccesses)
            {
                result.AssertFailure();
                AssertErrorEventLogged(ProcessesLogEventId.PipProcessError);
                AssertErrorEventLogged(SchedulerLogEventId.FileMonitoringError);
                // Double check that the directory was not created
                Test.BuildXL.TestUtilities.Xunit.XAssert.IsFalse(Directory.Exists(dir.Path.ToString(Context.PathTable)), "Directory should not exist");
            }
            else
            {
                result.AssertSuccess();
                AssertWarningEventLogged(SchedulerLogEventId.FileMonitoringWarning);
                AssertWarningEventLogged(SchedulerLogEventId.ProcessNotStoredToCacheDueToFileMonitoringViolations, count: 2);
            }

            // Now test that BuildXL has the same behavior even if the directory is already on disk.
            // NOTE: on Unix we couldn't find a syscall that always requests to create a directory even if one already exists, hence not testing the following
            if (!OperatingSystemHelper.IsUnixOS)
            {
                // Create the directory manually
                Directory.CreateDirectory(dir.Path.ToString(Context.PathTable));

                result = RunScheduler();
                if (failOnUnexpectedFileAccesses)
                {
                    result.AssertFailure();
                    AssertErrorEventLogged(ProcessesLogEventId.PipProcessError);
                    AssertErrorEventLogged(SchedulerLogEventId.FileMonitoringError);
                }
                else
                {
                    result.AssertSuccess();
                    AssertWarningEventLogged(SchedulerLogEventId.FileMonitoringWarning);
                    AssertWarningEventLogged(SchedulerLogEventId.ProcessNotStoredToCacheDueToFileMonitoringViolations, count: 2);
                }
            }
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)] /* No AppData folder on Unix */
        public void CreateDirectoryOnSystemMountWhereAllowed()
        {
            // testing with a stricter behavior
            Configuration.Sandbox.EnforceAccessPoliciesOnDirectoryCreation = true;

            string appDataRoamingPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
            Test.BuildXL.TestUtilities.Xunit.XAssert.IsFalse(string.IsNullOrEmpty(appDataRoamingPath));

            var appData = AbsolutePath.Create(Context.PathTable, appDataRoamingPath);

            Expander.Add(
                Context.PathTable,
                new SemanticPathInfo(
                    rootName: PathAtom.Create(Context.PathTable.StringTable, "AppDataRoaming"),
                    root: appData,
                    allowHashing: true,
                    readable: true,
                    writable: false,
                    system: true,
                    allowCreateDirectory: true));

            string cookiesPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Cookies);

            if (!string.IsNullOrEmpty(cookiesPath))
            {
                AbsolutePath cookies = AbsolutePath.Create(Context.PathTable, cookiesPath);

                Expander.Add(
                Context.PathTable,
                new SemanticPathInfo(
                    rootName: PathAtom.Create(Context.PathTable.StringTable, "INetCookies"),
                    root: cookies,
                    allowHashing: true,
                    readable: true,
                    writable: false,
                    system: true,
                    allowCreateDirectory: true));
            }

            var builderA = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact()),
                Operation.CreateDir(DirectoryArtifact.CreateWithZeroPartialSealId(appData), doNotInfer: true),
            });
            SchedulePipBuilder(builderA);

            ScheduleRunResult result = RunScheduler();
            result.AssertSuccess();
        }
    }
}
