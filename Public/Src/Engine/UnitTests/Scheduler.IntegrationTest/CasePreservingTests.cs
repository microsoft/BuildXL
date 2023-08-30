// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
    // Case preserving tests only make sense for Windows
    [TestClassIfSupported(requiresWindowsBasedOperatingSystem: true)]
    public class CasePreservingTests : SchedulerIntegrationTestBase
    {
        public CasePreservingTests(ITestOutputHelper output) : base(output)
        {
            Configuration.Cache.HonorDirectoryCasingOnDisk = true;
        }

        [Fact]
        public void CasingIsPreservedForOutputs()
        {
            string opaqueDir = Path.Combine(ObjectRoot, "opaquedir");
            AbsolutePath opaqueDirPath = AbsolutePath.Create(Context.PathTable, opaqueDir);

            string directory = Path.Combine(opaqueDir, "foo");
            string file = "BAR";
            string fullPath = Path.Combine(directory, $"{file}_0");

            // Let's pollute the path table with a casing incompatible with the one that is going to be produced by the pip. 
            // And make sure the path table is actually polluted before moving forward
            string pollutedRelativePath = "FOO\\bar_0";
            var pollutedPath = AbsolutePath.Create(Context.PathTable, Path.Combine(opaqueDir, pollutedRelativePath));
            XAssert.AreEqual(Path.Combine(opaqueDir, pollutedRelativePath), pollutedPath.ToString(Context.PathTable));

            var builderA = CreatePipBuilder(new Operation[]
            {
                Operation.CreateDir(directory, doNotInfer: true),
                Operation.WriteFile(CreateOutputFileAsString(root: directory, prefix: file), doNotInfer: true)
            });
            builderA.AddOutputDirectory(DirectoryArtifact.CreateWithZeroPartialSealId(opaqueDirPath), global::BuildXL.Pips.Operations.SealDirectoryKind.SharedOpaque);

            var pipA = SchedulePipBuilder(builderA);

            RunScheduler().AssertSuccess();

            // Retrieve the path on disk and make sure casing matches
            string pathOnDisk = FileUtilities.GetPathWithExactCasing(pollutedPath.ToString(Context.PathTable)).Result;
            XAssert.AreEqual(pathOnDisk, fullPath);

            // Delete the produced file and nested directory and run from cache
            FileUtilities.DeleteDirectoryContents(opaqueDir, deleteRootDirectory: false);
            RunScheduler().AssertCacheHit(pipA.Process.PipId);

            // Make sure the replay honored casing.
            pathOnDisk = FileUtilities.GetPathWithExactCasing(pollutedPath.ToString(Context.PathTable)).Result;
            XAssert.AreEqual(pathOnDisk, fullPath);
        }
    }
}
