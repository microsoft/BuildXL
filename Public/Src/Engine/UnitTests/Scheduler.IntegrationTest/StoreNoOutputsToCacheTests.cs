// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Scheduler
{
    [Trait("Category", "StoreNoOutputsToCacheTests")]
    public class StoreNoOutputsToCacheTests : SchedulerIntegrationTestBase
    {
        public StoreNoOutputsToCacheTests(ITestOutputHelper output) : base(output)
        {
            Configuration.Schedule.StoreOutputsToCache = false;
        }

        private void CreateAndScheduleProcess(out FileArtifact output, out ProcessWithOutputs scheduledProcess)
        {
            FileArtifact input = CreateSourceFile();
            output = CreateOutputFileArtifact();
            var pipBuilder = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(input),
                Operation.WriteFile(output)
            });

            scheduledProcess = SchedulePipBuilder(pipBuilder);
        }

        /// <summary>
        /// Ensures output content for a process which has a metadata change (i.e. different file id or USN)
        /// but the same content can be materialzed even though the file is not present in the cache.
        /// </summary>
        [Fact]
        public void UnavailableMetadataChangedContentInCacheCanBeMaterializedIfUpToDate()
        {
            Configuration.Schedule.EnableLazyOutputMaterialization = false;

            CreateAndScheduleProcess(out var output, out var scheduledProcess);
            RunScheduler().AssertCacheMiss(scheduledProcess.Process.PipId);
            XAssert.IsFalse(FileContentExistsInArtifactCache(output));

            // Delete and re-add output so we know the file is changed (different file id and USN), but has same content
            var outputPath = output.Path.ToString(Context.PathTable);
            var outputBytes = File.ReadAllBytes(outputPath);
            Delete(output.Path);
            File.WriteAllBytes(outputPath, outputBytes);

            RunScheduler().AssertCacheHit(scheduledProcess.Process.PipId);
            XAssert.IsFalse(FileContentExistsInArtifactCache(output));
        }

        [Fact]
        public void BasicProcessTest()
        {
            CreateAndScheduleProcess(out var output, out var scheduledProcess);
            RunScheduler().AssertCacheMiss(scheduledProcess.Process.PipId);

            XAssert.IsFalse(FileContentExistsInArtifactCache(output));
        }

        [Fact]
        public void BasicWriteFileTest()
        {
            AbsolutePath outputPath = CreateUniqueObjPath("write");
            FileArtifact output = WriteFile(outputPath, Guid.NewGuid().ToString());

            RunScheduler();

            XAssert.IsTrue(FileExistsOnDisk(output));
            XAssert.IsFalse(FileContentExistsInArtifactCache(output));
        }

        [Fact]
        public void BasicCopyFileTest()
        {
            FileArtifact input = CreateSourceFile();
            FileArtifact output = CopyFile(input, CreateUniqueObjPath("copy"));

            RunScheduler();

            XAssert.IsTrue(FileExistsOnDisk(output));
            XAssert.IsFalse(FileContentExistsInArtifactCache(input));
            XAssert.IsFalse(FileContentExistsInArtifactCache(output));
        }

        [Fact]
        public void ProcessFollowedByCopyFileTest()
        {
            CreateAndScheduleProcess(out var output, out var scheduledProcess);

            FileArtifact copiedOutput = CopyFile(output, CreateUniqueObjPath("copy"));

            RunScheduler();

            XAssert.IsTrue(FileExistsOnDisk(output));
            XAssert.IsTrue(FileExistsOnDisk(copiedOutput));
            XAssert.IsFalse(FileContentExistsInArtifactCache(output));
            XAssert.IsFalse(FileContentExistsInArtifactCache(copiedOutput));
        }

        [Fact]
        public void WriteFileFollowedByCopyFileTest()
        {
            AbsolutePath outputPath = CreateUniqueObjPath("write");
            FileArtifact output = WriteFile(outputPath, Guid.NewGuid().ToString());

            AbsolutePath copiedOutputPath = CreateUniqueObjPath("copy");
            FileArtifact copiedOutput = CopyFile(output, copiedOutputPath);

            RunScheduler();

            XAssert.IsTrue(FileExistsOnDisk(output));
            XAssert.IsTrue(FileExistsOnDisk(copiedOutput));
            XAssert.IsFalse(FileContentExistsInArtifactCache(output));
            XAssert.IsFalse(FileContentExistsInArtifactCache(copiedOutput));
        }

        [Fact]
        public void CopyFileFollowedByCopyFileTest()
        {
            FileArtifact input = CreateSourceFile();
            FileArtifact output = CopyFile(input, CreateUniqueObjPath("copy1"));

            FileArtifact copiedOutput = CopyFile(output, CreateUniqueObjPath("copy2"));

            RunScheduler();

            XAssert.IsTrue(FileExistsOnDisk(output));
            XAssert.IsTrue(FileExistsOnDisk(copiedOutput));
            XAssert.IsFalse(FileContentExistsInArtifactCache(output));
            XAssert.IsFalse(FileContentExistsInArtifactCache(copiedOutput));
        }

        [Fact]
        public void RewriteProcessStoreOutputsToCacheTest()
        {
            // dummyInput -> Process A -> rewrittenOutput -> Process B -> rewrittenOutput

            const string Content = "Test";
            var dummyInput = CreateSourceFile();
            var rewrittenOutputRc1 = CreateOutputFileArtifact();

            // Pip A
            var builderA = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(dummyInput),
                Operation.WriteFile(rewrittenOutputRc1, Content)
            });

            var processAndOutputsA = SchedulePipBuilder(builderA);

            // Pip B
            FileArtifact rewrittenOutput = rewrittenOutputRc1.CreateNextWrittenVersion();

            var builderB = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(rewrittenOutputRc1),
                Operation.WriteFile(rewrittenOutput, Content)
            });

            var processAndOutputsB = SchedulePipBuilder(builderB);

            RunScheduler();

            XAssert.IsTrue(FileExistsOnDisk(rewrittenOutput));
            XAssert.IsTrue(FileContentExistsInArtifactCache(rewrittenOutput));
            XAssert.IsTrue(ContentExistsInArtifactCache(Content));

            FileUtilities.DeleteFile(ArtifactToString(rewrittenOutput));

            RunScheduler().AssertCacheHit(processAndOutputsA.Process.PipId, processAndOutputsB.Process.PipId);
            XAssert.IsTrue(FileExistsOnDisk(rewrittenOutput));
        }

        [Fact]
        public void RewriteCopyFileTarget()
        {
            const string Content = "Test";
            var copySource = CreateSourceFile();
            var copyTarget = CreateOutputFileArtifact();

            File.WriteAllText(ArtifactToString(copySource), Content);
            var copy = CreateAndScheduleCopyFile(copySource, copyTarget);

            var rewrittenCopyTarget = copyTarget.CreateNextWrittenVersion();

            var processAndOutputs = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.ReadFile(copyTarget),
                Operation.WriteFile(rewrittenCopyTarget, Content)
            });

            RunScheduler().AssertSuccess();

            XAssert.IsTrue(FileExistsOnDisk(rewrittenCopyTarget));
            XAssert.IsTrue(FileContentExistsInArtifactCache(rewrittenCopyTarget));
            XAssert.IsTrue(ContentExistsInArtifactCache(Content));

            FileUtilities.DeleteFile(ArtifactToString(rewrittenCopyTarget));

            RunScheduler().AssertCacheHit(processAndOutputs.Process.PipId);
            XAssert.IsTrue(FileExistsOnDisk(rewrittenCopyTarget));
        }

        [Fact]
        public void RewriteWriteFileTarget()
        {
            const string Content = "Test";

            var writeTarget = CreateOutputFileArtifact();
            var write = CreateAndScheduleWriteFile(writeTarget, string.Empty, new[] { Content });

            var rewrittenWriteTarget = writeTarget.CreateNextWrittenVersion();

            var processAndOutputs = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.ReadFile(writeTarget),
                Operation.WriteFile(rewrittenWriteTarget, Content)
            });

            RunScheduler().AssertSuccess();

            XAssert.IsTrue(FileExistsOnDisk(rewrittenWriteTarget));
            XAssert.IsTrue(FileContentExistsInArtifactCache(rewrittenWriteTarget));
            XAssert.IsTrue(ContentExistsInArtifactCache(Content));

            FileUtilities.DeleteFile(ArtifactToString(rewrittenWriteTarget));

            RunScheduler().AssertCacheHit(processAndOutputs.Process.PipId);
            XAssert.IsTrue(FileExistsOnDisk(rewrittenWriteTarget));
        }

        [Fact]
        public void CacheHitTest()
        {
            // Momentarily store outputs to cache.
            Configuration.Schedule.StoreOutputsToCache = true;

            CreateAndScheduleProcess(out var output, out var scheduledProcess);
            RunScheduler().AssertCacheMiss(scheduledProcess.Process.PipId);

            XAssert.IsTrue(FileContentExistsInArtifactCache(output));

            // Don't store output to cache now.
            Configuration.Schedule.StoreOutputsToCache = false;

            RunScheduler().AssertCacheHit(scheduledProcess.Process.PipId);

            // No effect on what has been stored in cache.
            XAssert.IsTrue(FileContentExistsInArtifactCache(output));
        }

        [Fact]
        public void CopyFileFollowedBySealDirectoryTest()
        {
            FileArtifact input = CreateSourceFile();
            FileArtifact output = CopyFile(input, CreateUniqueObjPath("copy"));

            SealDirectory(output.Path.GetParent(Context.PathTable), global::BuildXL.Pips.Operations.SealDirectoryKind.Full, output);
            
            RunScheduler();

            XAssert.IsTrue(FileExistsOnDisk(output));
            XAssert.IsFalse(FileContentExistsInArtifactCache(output));
        }

        private bool FileExistsOnDisk(FileArtifact file) => File.Exists(ArtifactToString(file));
    }
}
