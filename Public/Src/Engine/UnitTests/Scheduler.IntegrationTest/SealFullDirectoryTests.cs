// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Scheduler
{
    public class SealFullDirectoryTests : SchedulerIntegrationTestBase
    {
        public SealFullDirectoryTests(ITestOutputHelper output) : base(output)
        {
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ScrubFilesNotInContentsWhenFullySealDir(bool shouldScrub)
        {
            // simulating two builds here,
            // the second build should scrub the stray files
            // if they are in a fully sealed directory
            var sourceDir = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory());
            var sourceDirStr = ArtifactToString(sourceDir);
            var file1 = CreateOutputFileArtifact(sourceDirStr);
            var file1Str = ArtifactToString(file1);
            var file2 = CreateOutputFileArtifact(sourceDirStr);
            var file2Str = ArtifactToString(file2);

            var sealDir = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory());
            var sealDirStr = ArtifactToString(sealDir);
            var nestedDir1 = AbsolutePath.Create(Context.PathTable, Path.Combine(sealDirStr, "nested1"));
            var nestedDir1Str = nestedDir1.ToString(Context.PathTable);
            var nestedDir2 = AbsolutePath.Create(Context.PathTable, Path.Combine(sealDirStr, "nested2"));
            var nestedDir2Str = nestedDir2.ToString(Context.PathTable);
            var sealFile1 = CreateOutputFileArtifact(nestedDir1Str);
            var sealFile1Str = ArtifactToString(sealFile1);
            var sealFile2 = CreateOutputFileArtifact(nestedDir2Str);
            var sealFile2Str = ArtifactToString(sealFile2);

            // the first build depends on file1 and file2
            var builderA = CreatePipBuilder(new Operation[]
            {
                 Operation.WriteFile(file1, "First build write file1"),
                 Operation.WriteFile(file2, "First build write file2"),
            });
            SchedulePipBuilder(builderA);

            var builderB = CreatePipBuilder(new Operation[]
            {
                 Operation.CopyFile(file1, sealFile1),
                 Operation.CopyFile(file2, sealFile2),
            });
            SchedulePipBuilder(builderB);
            SealDirectory(sealDir, SealDirectoryKind.Full, true, sealFile1, sealFile2);
            RunScheduler().AssertSuccess();

            XAssert.IsTrue(File.Exists(sealFile1Str), $"File in the content list when seal was supposed to exist: {sealFile1Str}");
            XAssert.IsTrue(File.Exists(sealFile2Str), $"File in the content list when seal was supposed to exist: {sealFile2Str}");

            ResetPipGraphBuilder();

            // the second build only depends on file1
            var builderC = CreatePipBuilder(new Operation[]
            {
                 Operation.WriteFile(file1, "Second build write file1"),
            });
            SchedulePipBuilder(builderC);

            var builderD = CreatePipBuilder(new Operation[]
            {
                 Operation.CopyFile(file1, sealFile1),
            });
            SchedulePipBuilder(builderD);
            SealDirectory(sealDir, SealDirectoryKind.Full, shouldScrub, sealFile1);

            var opslist = new List<Operation>();
            opslist.Add(Operation.ReadFile(sealFile1));
            opslist.Add(Operation.WriteFile(CreateOutputFileArtifact()));

            if (!shouldScrub)
            {
                opslist.Add(Operation.ReadFile(sealFile2, true));
            }

            var builderE = CreatePipBuilder(opslist);
            SchedulePipBuilder(builderE);

            XAssert.IsTrue(File.Exists(sealFile1Str), $"File in the content list when seal was supposed to exist: {sealFile1Str}");
            if (shouldScrub)
            {
                RunScheduler().AssertSuccess();
                XAssert.IsFalse(Directory.Exists(nestedDir2Str), $"unseal directory wasn't supposed to exist: {nestedDir2Str}");
                XAssert.IsFalse(File.Exists(sealFile2Str), $"File not in the content list when seal wasn't supposed to exist: {sealFile2Str}");
            }
            else
            {
                RunScheduler().AssertFailure();
                AssertErrorEventLogged(EventId.FileMonitoringError);
                AssertVerboseEventLogged(EventId.PipProcessDisallowedFileAccess);
                AssertWarningEventLogged(EventId.ProcessNotStoredToCacheDueToFileMonitoringViolations);
                XAssert.IsTrue(Directory.Exists(nestedDir2Str), $"unseal directory was supposed to exist: {nestedDir2Str}");
                XAssert.IsTrue(File.Exists(sealFile2Str), $"File not in the content list when seal was supposed to exist: {sealFile2Str}");
            }
        }
    }
}
