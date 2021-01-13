// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using BuildXL.Scheduler.Tracing;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

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
            AssertVerboseEventLogged(LogEventId.PipProcessRetriedOnSameWorker, Configuration.Schedule.MaxRetriesDueToRetryableFailures, allowMore: true);
            AssertVerboseEventLogged(LogEventId.PipProcessRetriedOnDifferentWorker, Configuration.Schedule.MaxRetriesDueToRetryableFailures, allowMore: true);
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

                // Pip will fail with the same exit code as VmInfrastructureFailure code.
                // However, pip execution should not be retried.
                Operation.Fail(global::BuildXL.Processes.ExitCodes.VmInfrastructureFailure)
            });
            builder.Options |= Process.Options.RequiresAdmin;
            SchedulePipBuilder(builder);

            // System.Diagnostics.Debugger.Launch();
            // The build is expected to fail, but all of the error logging consistency checks should pass meaning no crash
            RunScheduler().AssertFailure();

            AssertVerboseEventLogged(LogEventId.PipRetryDueToRetryableFailures, 0, allowMore: false);       // Should not be retried
            AssertVerboseEventLogged(LogEventId.PipProcessRetriedOnSameWorker, 0, allowMore: false);        // Should not be retried on same worker
            AssertVerboseEventLogged(LogEventId.PipProcessRetriedOnDifferentWorker, 0, allowMore: false);   // Should not be retried on different worker
            AssertErrorEventLogged(global::BuildXL.Processes.Tracing.LogEventId.PipProcessError);
        }
    }
}
