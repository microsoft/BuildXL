// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
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
        private const string TextToCopied = "Hello world";

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
            var result = FileUtilities.CheckForExecutePermission(outFile.Path.ToString(Context.PathTable));
            XAssert.IsTrue(result.Succeeded && result.Result);

            result = FileUtilities.CheckForExecutePermission(outCopyFile.Path.ToString(Context.PathTable));
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

            var result = FileUtilities.CheckForExecutePermission(outFile.Path.ToString(Context.PathTable));
            XAssert.IsTrue(result.Succeeded && result.Result);

            result = FileUtilities.CheckForExecutePermission(outCopyFile.Path.ToString(Context.PathTable));
            XAssert.IsTrue(result.Succeeded && result.Result);
        }

        /// <summary>
        /// Ensure that the destination file has the execute permission bit set if the source of the copy file pip is a source file.
        /// This test covers the scenario where the 'storeOutputsToCache' flag is either enabled or disabled.
        /// </summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void SetExecutePermissionOnDestinationWhenCopyPipSourceIsASourceFile(bool storeOutputsToCache)
        {
            Configuration.Schedule.StoreOutputsToCache = storeOutputsToCache;
            Configuration.Engine.UseHardlinks = false;

            // Source file to copy from
            var sourceFileForCopyPip = CreateSourceFile();
            // Path to copy to
            AbsolutePath destinationPath = CreateUniqueObjPath("destinationCopyFile");
            _ = FileUtilities.SetExecutePermissionIfNeeded(sourceFileForCopyPip.Path.ToString(Context.PathTable));

            // Create copy file pip and adds it to the graph
            var copiedFileA = CopyFile(sourceFileForCopyPip, destinationPath);

            // Run the scheduler and check the file permissions on the destination of the copy file.
            RunSchedulerAndCheckFilePermissionsOnDestination(destinationPath);
        }

        /// <summary>
        /// Ensure that the destination file has the execute permission bit set if the source of the copy file pip is an output of another pip.
        /// </summary>
        [Fact]
        public void SetExecutePermissionOnDestinationWhenCopyPipSourceIsAnOutputOfAnotherPip()
        {
            // Source file to copy from
            var sourceFileForCopyPip = CreateOutputFileArtifact();
            // Path to copy to
            AbsolutePath destinationPath = CreateUniqueObjPath("destinationCopyFile");

            var ops = (new Operation[]
            {
                Operation.WriteFile(sourceFileForCopyPip, TextToCopied),
                Operation.SetExecutionPermissions(sourceFileForCopyPip)
            });

            var pipBuilderA = CreatePipBuilder(ops);
            var scheduledProcessA = SchedulePipBuilder(pipBuilderA);
            // Create copy file pip and adds it to the graph
            var copiedFileA = CopyFile(sourceFileForCopyPip, destinationPath);

            // First run should be a cache miss, second one a cache hit.
            RunScheduler().AssertCacheMiss(scheduledProcessA.Process.PipId);
            // Check to ensure that the destination file also has the execute permission bit set.
            XAssert.IsTrue(FileUtilities.CheckForExecutePermission(destinationPath.ToString(Context.PathTable)).Result,
                           "Execute permission not set on destination of copy file!");
            // Delete the destination file and run the scheduler again.
            Delete(sourceFileForCopyPip);
            RunScheduler().AssertCacheHit(scheduledProcessA.Process.PipId);

            // Content of source and destination should be same.
            string actualContent = File.ReadAllText(copiedFileA.Path.ToString(Context.PathTable));
            XAssert.AreEqual(TextToCopied, actualContent);
            // Check to ensure that the destination file also has the execute permission bit set.
            XAssert.IsTrue(FileUtilities.CheckForExecutePermission(destinationPath.ToString(Context.PathTable)).Result,
                           "Execute permission not set on destination of copy file!");
        }

        /// <summary>
        /// Ensure that the destination file has the execute permission bit set if the symlink target has the bit set.
        /// </summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void SetExecutePermissionOnDestionationForSymlink(bool storeOutputsToCache)
        {
            Configuration.Schedule.AllowCopySymlink = true;
            Configuration.Schedule.StoreOutputsToCache = storeOutputsToCache;

            // Symlink chain:
            // symlinkFile1 -> symlinkFile2 -> targetFile
            FileArtifact targetFile = CreateSourceFile();
            FileArtifact symlinkFile1 = CreateOutputFileArtifact();
            FileArtifact symlinkFile2 = CreateOutputFileArtifact();
            FileArtifact destination = CreateOutputFileArtifact();
            // only the head of the symlink chain is created during the build -> valid chain
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(ArtifactToString(symlinkFile2), ArtifactToString(targetFile), isTargetFile: true));
            _ = FileUtilities.SetExecutePermissionIfNeeded(targetFile.Path.ToString(Context.PathTable));

            CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.CreateSymlink(symlinkFile1, symlinkFile2, Operation.SymbolicLinkFlag.FILE),
            });

            // CopyFilePip : copy symlinkFile1 to output
            FileArtifact output = CopyFile(symlinkFile1, destination);

            RunSchedulerAndCheckFilePermissionsOnDestination(destination);
        }

        public void RunSchedulerAndCheckFilePermissionsOnDestination(AbsolutePath destination)
        {
            RunScheduler().AssertSuccess();
            // Check to ensure that the destination file also has the execute permission bit set.
            XAssert.IsTrue(FileUtilities.CheckForExecutePermission(destination.ToString(Context.PathTable)).Result,
                           "Execute permission not set on destination of copy file!");
            // Delete the destination before running the build again.
            Delete(destination);
            RunScheduler().AssertSuccess();
            // Check to ensure that the destination file also has the execute permission bit set.
            XAssert.IsTrue(FileUtilities.CheckForExecutePermission(destination.ToString(Context.PathTable)).Result,
                           "Execute permission not set on destination of copy file!");
        }
    }
}
