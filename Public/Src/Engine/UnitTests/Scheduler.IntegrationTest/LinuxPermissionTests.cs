// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Native.IO;
using BuildXL.Utilities.Core;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Scheduler
{
    [TestClassIfSupported(requiresUnixBasedOperatingSystem: true)]
    public class LinuxPermissionTests : SchedulerIntegrationTestBase
    {
        public LinuxPermissionTests(ITestOutputHelper output) : base(output)
        {
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void PermissionsAreHonoredWhenStoredInCache(bool useHardlinks)
        {
            Configuration.Engine.UseHardlinks = useHardlinks;

            FileArtifact outFile = CreateOutputFileArtifact();
            FileArtifact outCopyFile = CreateOutputFileArtifact();
            Operation[] ops = new Operation[]
            {
                Operation.WriteFile(outFile),
                Operation.SetExecutionPermissions(outFile),
                Operation.CopyFile(outFile, outCopyFile, doNotInfer: true)
            };

            var builder = CreatePipBuilder(ops);
            builder.AddOutputFile(outCopyFile.Path, FileExistence.Required);

            SchedulePipBuilder(builder);

            RunScheduler().AssertSuccess();

            // Both files should have the execution permission set
            var result = FileUtilities.TryGetIsExecutableIfNeeded(outFile.Path.ToString(Context.PathTable));
            XAssert.IsTrue(result.Succeeded && result.Result);

            result = FileUtilities.TryGetIsExecutableIfNeeded(outCopyFile.Path.ToString(Context.PathTable));
            XAssert.IsTrue(result.Succeeded && result.Result);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CacheReplayHonorExecutionPermissions(bool scrubOutputs)
        {
            FileArtifact outFile = CreateOutputFileArtifact();
            FileArtifact outCopyFile = CreateOutputFileArtifact();
            Operation[] ops = new Operation[]
            {
                Operation.WriteFile(outFile),
                Operation.SetExecutionPermissions(outFile),
                Operation.CopyFile(outFile, outCopyFile, doNotInfer: true)
            };
            
            var builder = CreatePipBuilder(ops);
            builder.AddOutputFile(outCopyFile.Path, FileExistence.Required);

            var pip = SchedulePipBuilder(builder);

            RunScheduler().AssertSuccess();

            if (scrubOutputs)
            {
                FileUtilities.DeleteFile(outFile.Path.ToString(Context.PathTable));
                FileUtilities.DeleteFile(outCopyFile.Path.ToString(Context.PathTable));
            }    

            RunScheduler().AssertCacheHit(pip.Process.PipId);

            var result = FileUtilities.TryGetIsExecutableIfNeeded(outFile.Path.ToString(Context.PathTable));
            XAssert.IsTrue(result.Succeeded && result.Result);

            result = FileUtilities.TryGetIsExecutableIfNeeded(outCopyFile.Path.ToString(Context.PathTable));
            XAssert.IsTrue(result.Succeeded && result.Result);
        }

    }
}
