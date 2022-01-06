// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using BuildXL.Scheduler.Tracing;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using ProcessLogId = BuildXL.Processes.Tracing.LogEventId;

namespace ExternalToolTest.BuildXL.Scheduler
{
    [TestClassIfSupported(requiresWindowsBasedOperatingSystem: true, requiresSymlinkPermission: true)]
    public class ExternalVmExecutionTests : ExternalToolExecutionTests
    {
        public ExternalVmExecutionTests(ITestOutputHelper output) : base(output)
        {
            Configuration.Sandbox.AdminRequiredProcessExecutionMode = global::BuildXL.Utilities.Configuration.AdminRequiredProcessExecutionMode.ExternalVM;
            Configuration.Sandbox.RedirectedTempFolderRootForVmExecution = CreateUniqueDirectory(ObjectRootPath);
        }

        [Fact]
        public void RunWithLimitedConcurrency()
        {
            Configuration.Sandbox.VmConcurrencyLimit = 1;
            const int PipCount = 5;

            FileArtifact shared = CreateOutputFileArtifact();

            for (int i = 0; i < PipCount; ++i)
            {
                ProcessBuilder builder = CreatePipBuilder(new[]
                {
                    Operation.ReadFile(CreateSourceFile()),
                    Operation.WriteFile(CreateOutputFileArtifact()) ,
                    Operation.WriteFile(shared, "#", doNotInfer: true)
                });
                builder.Options |= Process.Options.RequiresAdmin;
                builder.AddUntrackedFile(shared);
                SchedulePipBuilder(builder);
            }

            RunScheduler(
                verifySchedulerPostRun: ts => 
                {
                    XAssert.AreEqual(1, ts.MaxExternalProcessesRan);
                }).AssertSuccess();
            string sharedFileContent = File.ReadAllText(ArtifactToString(shared));
            XAssert.AreEqual(new string('#', PipCount), sharedFileContent);
        }

        /// <summary>
        /// Ensure that logging matches up and no crashes happen when a pip is retried due to VmCommandProxy or other Vm related errors.
        /// </summary>
        [Fact]
        public void VmRetryOnVmCommandProxyFailures()
        {
            Configuration.Schedule.MaxRetriesDueToRetryableFailures = 1;

            // Set the test hook and reset the graph bulider so it uses the context with the newly set test hook
            Context.TestHooks = new TestHooks()
            {
                SandboxedProcessExecutorTestHook = new SandboxedProcessExecutorTestHook
                {
                    FailVmConnection = true
                }
            };

            var builder = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact())
            });
            builder.Options |= Process.Options.RequiresAdmin;
            SchedulePipBuilder(builder);

            // The build is expected to fail, but all of the error logging consistency checks should pass meaning no crash
            RunScheduler().AssertFailure();

            AssertVerboseEventLogged(LogEventId.PipRetryDueToRetryableFailures, Configuration.Schedule.MaxRetriesDueToRetryableFailures, allowMore: true);
            AssertVerboseEventLogged(LogEventId.PipProcessRetriedInline, Configuration.Schedule.MaxRetriesDueToRetryableFailures, allowMore: true);
            AssertVerboseEventLogged(LogEventId.PipProcessRetriedByReschedule, Configuration.Schedule.MaxRetriesDueToRetryableFailures, allowMore: true);
            AssertErrorEventLogged(LogEventId.ExcessivePipRetriesDueToRetryableFailures);
        }

        /// <summary>
        /// Ensure that logging matches up and no crashes happen when a pip is retried due to Pip failure inside the Vm.
        /// </summary>
        [Fact]
        public void VmNotRetryOnUnsuccessfulPipExecution()
        {
            Configuration.Schedule.MaxRetriesDueToRetryableFailures = 1;

            ResetPipGraphBuilder();

            var builder = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact()),
                Operation.Fail(-1)
            });
            builder.Options |= Process.Options.RequiresAdmin;
            SchedulePipBuilder(builder);

            // The build is expected to fail, but all of the error logging consistency checks should pass meaning no crash
            RunScheduler().AssertFailure();

            AssertVerboseEventLogged(LogEventId.PipRetryDueToRetryableFailures, 0, allowMore: false);       // Should not be retried
            AssertVerboseEventLogged(LogEventId.PipProcessRetriedInline, 0, allowMore: false);        // Should not be retried on same worker
            AssertVerboseEventLogged(LogEventId.PipProcessRetriedByReschedule, 0, allowMore: false);   // Should not be retried on different worker
            AssertErrorEventLogged(global::BuildXL.Processes.Tracing.LogEventId.PipProcessError);
        }

        [Fact(Skip ="Not able to simulate a file being held because notepad does not survive.")]
        public void VmDeletesSharedOpaqueOutputsOnRetry()
        {
            Configuration.Schedule.MaxRetriesDueToRetryableFailures = 1;

            var sealDirPath = CreateUniqueDirectory();
            var fileToLock = CreateSourceFile(sealDirPath.ToString(Context.PathTable));
            var sealDir = DirectoryArtifact.CreateWithZeroPartialSealId(sealDirPath);

            var builder = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact()),
                Operation.SpawnExe(
                    Context.PathTable,
                    CmdExecutable,
                    $"notepad >{fileToLock.Path.ToString(Context.PathTable)}"), // redirected stdout will remain locked until notepad is killed
                Operation.Fail(1000)
            });
            builder.AddOutputDirectory(sealDir, SealDirectoryKind.SharedOpaque);
            builder.AllowedSurvivingChildProcessNames = ReadOnlyArray<PathAtom>.From(new PathAtom[] { PathAtom.Create(Context.StringTable, "notepad.exe") });
            builder.RetryExitCodes = ReadOnlyArray<int>.From(new int[] { 1000 });
            builder.Options |= Process.Options.RequiresAdmin;
            builder.SetProcessRetries(1);

            var process = SchedulePipBuilder(builder).Process;

            RunScheduler().AssertFailure();
            AssertVerboseEventLogged(LogEventId.PipWillBeRetriedDueToExitCode);
            AssertVerboseEventLogged(ProcessLogId.PipProcessOutputPreparationToBeRetriedInVM);
            AssertLogContains(caseSensitive: false, $"Deleted stale output: '{fileToLock.Path.ToString(Context.PathTable)}'");
        }
    }
}
