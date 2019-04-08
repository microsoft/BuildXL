// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using BuildXL.Pips.Builders;
using BuildXL.Utilities;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Scheduler
{
    [Trait("Category", "LazyMaterializationTests")]
    [Feature(Features.LazyOutputMaterialization)]
    public class LazyMaterializationTests : SchedulerIntegrationTestBase
    {
        /// <summary>
        ///  Testing Lazy Materialization under different engine, scheduler, and opaque configurations
        ///  These conditions are also tested with incremental scheduling in the child class LazyMaterializationTest_IncrementalScheduling
        /// </summary>
        /// <param name="output"></param>
        public LazyMaterializationTests(ITestOutputHelper output) : base(output)
        {
        }

        /// <summary>
        /// This test case tests expected behavior of all combinations of lazy materialization values
        /// with all combinations of the following conditions:
        ///     - file is materialized into an opaque dir and when not (set with opaqueDir param)
        ///     - where pipLining is on/off (we do not expect this to change behavior, since this is not a
        ///         distributed build, but testing to be sure)
        /// </summary>
        [Theory]
        [MemberData(nameof(TruthTable.GetTable), 2, MemberType = typeof(TruthTable))]     
        public void LazyMaterializationTest(bool lazyMaterial, bool opaqueDir)
        {
            // setting up the output dir for the pips
            string opaqueStrPath = Path.Combine(ObjectRoot, "OD");
            AbsolutePath opaqueDirPath = AbsolutePath.Create(Context.PathTable, opaqueStrPath);
            FileArtifact lazyFile = CreateOutputFileArtifact(opaqueStrPath);

            // ...........PIP A...........
            var builderA = CreatePipBuilder(new Operation[]
            {
                  Operation.WriteFile(lazyFile, doNotInfer:opaqueDir)
            });

            // when true, will lazily materialize a file into an opaque dir
            if (opaqueDir)
            {
                builderA.AddOutputDirectory(opaqueDirPath);
            }

            // appending a tag
            builderA.AddTags(Context.StringTable, "pipA");

            var pipA = SchedulePipBuilder(builderA);

            // ...........PIP B...........
            FileArtifact output2 = CreateOutputFileArtifact(ObjectRoot);

            var builderB = CreatePipBuilder(new Operation[]
            {
                 Operation.ReadFile(lazyFile, doNotInfer: opaqueDir),
                 Operation.WriteFile(output2)
            });
           
            if (opaqueDir)
            {
               // must set input as the directory and not the file
               // The spec requires that only the whole opaque dir can be consumed as input, no single file
               builderB.AddInputDirectory(pipA.ProcessOutputs.GetOpaqueDirectory(opaqueDirPath));
            }

            // appending a tag
            builderB.AddTags(Context.StringTable, "pipB");

            var pipB = SchedulePipBuilder(builderB);

            //// ...........Setting Modes...........
            Configuration.Schedule.EnableLazyOutputMaterialization = lazyMaterial;

            // run scheduler and assert cache miss
            RunScheduler().AssertCacheMiss(pipA.Process.PipId, pipB.Process.PipId);// nothing in cache

            string lazyFileContent = File.ReadAllText(ArtifactToString(lazyFile));

            // Now just run against pip B
            // This will also run pips that B depends on unless the outputs from 
            // those pips exist in cache and then there is no reason to run
            // those pips (in this case pipA)
            Configuration.Filter = "tag='pipB'";

            // Delete these files on disk 
            File.Delete(ArtifactToString(lazyFile));

            // Testing if deleting opaque dir interferes with pipA's and pipB's cache hit
            if (opaqueDir)
            {
                Directory.Delete(opaqueStrPath);
            }
            
            File.Delete(ArtifactToString(output2));

            // Run scheduler but grab artificats from cache (should be there from last run)...
            RunScheduler().AssertCacheHit(pipA.Process.PipId, pipB.Process.PipId);

            // testing what we expect when lazyMaterialization is on
            if (lazyMaterial)
            {
                // Does not exist since pipB had cache hit and since lazy materialization
                // is on there is not need to materialize the artifacts from pipA
                XAssert.IsTrue(!File.Exists(ArtifactToString(lazyFile)));
            }

            // testing what we expect when lazyMaterialization is off
            else
            {
                XAssert.IsTrue(File.Exists(ArtifactToString(lazyFile)));
            }

            // Pip B's file always exists
            XAssert.IsTrue(File.Exists(ArtifactToString(output2)));

            // Explicitly filter for lazily materialized output
            Configuration.Filter = "tag='pipA'";

            // Should replay original output from cache
            RunScheduler().AssertCacheHit(pipA.Process.PipId, pipB.Process.PipId);
            XAssert.IsTrue(File.Exists(ArtifactToString(lazyFile)));
            XAssert.AreEqual(lazyFileContent, File.ReadAllText(ArtifactToString(lazyFile)));
        }

        /// <summary>
        /// This test case tests expected behavior of all lazy materialization for write file pip outputs as inputs
        /// </summary>
        [Fact]
        public void LazyWriteFileMaterializationTest()
        {
            // setting up the output dir for the pips
            string writeFileOutput = Path.Combine(ObjectRoot, @"write\writeFile.txt");
            AbsolutePath writeFileOutputPath = AbsolutePath.Create(Context.PathTable, writeFileOutput);

            var sourceFile = CreateSourceFile();
            WriteSourceFile(sourceFile);

            // Graph:
            // W <- P

            var writeFileOutputContent = "WriteFileOutput";
            var lazyWriteFileOutput = WriteFile(writeFileOutputPath, writeFileOutputContent);

            // ...........PIP A...........
            var builderA = CreatePipBuilder(new Operation[]
            {
                  Operation.ReadFile(lazyWriteFileOutput),
                  Operation.ReadFile(sourceFile),
                  Operation.WriteFile(CreateOutputFileArtifact())
            });

            builderA.AddTags(Context.StringTable, "pipA");

            var pipA = SchedulePipBuilder(builderA);

            //// ...........Setting Modes...........
            Configuration.Schedule.EnableLazyOutputMaterialization = true;
            Configuration.Schedule.EnableLazyWriteFileMaterialization = true;

            // run scheduler and assert cache miss
            RunScheduler().AssertCacheMiss(pipA.Process.PipId);// nothing in cache

            // Exist since pipA must run and the write file output is an input
            XAssert.IsTrue(Exists(lazyWriteFileOutput));

            // Now just run against pip A
            // those pips exist in cache and then there is no reason to run
            // those pips (in this case pipA)
            Configuration.Filter = "tag='pipA'";

            // Delete these files on disk 
            Delete(lazyWriteFileOutput);

            // Run scheduler but grab artificats from cache (should be there from last run)...
            RunScheduler().AssertCacheHit(pipA.Process.PipId);

            // Does not exist since pipA had cache hit and since lazy materialization
            // is on there is not need to materialize the artifacts from the write file pip
            XAssert.IsFalse(Exists(lazyWriteFileOutput));

            // Clear the cache so no outputs can be retrieved from cache
            ClearArtifactCache();

            // Write source file to force cache miss
            WriteSourceFile(sourceFile);

            // run scheduler and assert cache hit
            RunScheduler().AssertCacheMiss(pipA.Process.PipId);

            // Write file output must be materialized as its an input to the pip
            XAssert.IsTrue(Exists(lazyWriteFileOutput));
        }

        /// <summary>
        /// This test case tests expected behavior of lazy materialization for write file pip outputs when copied
        /// </summary>
        [Fact]
        public void LazyWriteFileMaterializationCopyTest()
        {
            var sourceFile = CreateSourceFile();
            WriteSourceFile(sourceFile);

            // Graph:
            // W <- C <- C <- P

            var writeFileOutputContent = "WriteFileOutput";
            var lazyWriteFileOutputToBeCopied = WriteFile(CreateOutputFileArtifact(), writeFileOutputContent);

            var lazyCopyWriteFileOutput = CopyFile(lazyWriteFileOutputToBeCopied, CreateOutputFileArtifact(), tags: new[] { "copy1" });

            var lazyCopyWriteFileOutput2 = CopyFile(lazyCopyWriteFileOutput, CreateOutputFileArtifact(), tags: new[] { "copy2" });

            // ...........PIP A...........
            var builderA = CreatePipBuilder(new Operation[]
            {
                  Operation.ReadFile(lazyCopyWriteFileOutput2),
                  Operation.ReadFile(sourceFile),
                  Operation.WriteFile(CreateOutputFileArtifact())
            });

            builderA.AddTags(Context.StringTable, "pipA");

            var pipA = SchedulePipBuilder(builderA);

            //// ...........Setting Modes...........
            Configuration.Schedule.EnableLazyOutputMaterialization = true;
            Configuration.Schedule.EnableLazyWriteFileMaterialization = true;

            // run scheduler and assert cache miss
            RunScheduler().AssertCacheMiss(pipA.Process.PipId);// nothing in cache

            // Exist since pipA must run and the write file output is an input
            XAssert.IsTrue(Exists(lazyCopyWriteFileOutput2));

            // Now just run against pip A
            // those pips exist in cache and then there is no reason to run
            // those pips (in this case pipA)
            Configuration.Filter = "tag='pipA'";

            // Delete these files on disk 
            Delete(lazyWriteFileOutputToBeCopied);
            Delete(lazyCopyWriteFileOutput);
            Delete(lazyCopyWriteFileOutput2);

            // Run scheduler but grab artificats from cache (should be there from last run)...
            RunScheduler().AssertCacheHit(pipA.Process.PipId);

            // Does not exist since pipA had cache hit and since lazy materialization
            // is on there is not need to materialize the artifacts from the write file pip
            XAssert.IsFalse(Exists(lazyWriteFileOutputToBeCopied));
            XAssert.IsFalse(Exists(lazyCopyWriteFileOutput));
            XAssert.IsFalse(Exists(lazyCopyWriteFileOutput2));

            // Clear the cache so no outputs can be retrieved from cache
            ClearArtifactCache();

            // Delete these files on disk 
            Delete(lazyWriteFileOutputToBeCopied);
            Delete(lazyCopyWriteFileOutput);
            Delete(lazyCopyWriteFileOutput2);

            // Write source file to force cache miss
            WriteSourceFile(sourceFile);

            // run scheduler and assert cache hit
            RunScheduler().AssertCacheMiss(pipA.Process.PipId);

            // Write file output should exist (needed to materialize copy file)
            XAssert.IsTrue(Exists(lazyWriteFileOutputToBeCopied));

            // Intermediate copy should not exist
            XAssert.IsFalse(Exists(lazyCopyWriteFileOutput));

            // Final copy should exist on disk since its required by pipA
            XAssert.IsTrue(Exists(lazyCopyWriteFileOutput2));
        }

        [Fact]
        public void TestCopyWrittenFileWithLazyWrite()
        {
            const string ExpectedContent = "Test";

            var writtenFilePath = CreateUniqueObjPath("write");
            var writtenFile = WriteFile(writtenFilePath, ExpectedContent);
            var copyDestinationPath = CreateUniqueObjPath("copy");
            var copyDestination = CopyFile(writtenFile, copyDestinationPath);
            var anotherCopyDestinationPath = CreateUniqueObjPath("another_copy");
            var anotherCopyDestination = CopyFile(copyDestination, anotherCopyDestinationPath, tags: new[] { "TagA" });

            Configuration.Filter = "tag='TagA'";
            Configuration.Schedule.EnableLazyOutputMaterialization = true;

            Configuration.Schedule.EnableLazyWriteFileMaterialization = true;
            RunScheduler().AssertSuccess();

            XAssert.IsTrue(File.Exists(ArtifactToString(anotherCopyDestination)));

            var actualContent = File.ReadAllText(ArtifactToString(anotherCopyDestination));

            XAssert.AreEqual(ExpectedContent, actualContent);
        }

        [Fact]
        public void TestOutputMaterializationExclusion()
        {
            var exclusionRoot = Path.Combine(ObjectRoot, "exc");
            var excludedOutput = CreateOutputFileArtifact(exclusionRoot);
            var includedOutput = CreateOutputFileArtifact();

            // ...........PIP A...........
            var builderA = CreatePipBuilder(new Operation[]
            {
                  Operation.WriteFile(excludedOutput),
                  Operation.WriteFile(includedOutput)
            });

            builderA.AddTags(Context.StringTable, "pipA");

            // ...........PIP B...........
            var builderB = CreatePipBuilder(new Operation[]
            {
                  Operation.ReadFile(excludedOutput),
                  Operation.WriteFile(CreateOutputFileArtifact())
            });

            builderB.AddTags(Context.StringTable, "pipB");

            Configuration.Filter = "tag='pipA'";
            Configuration.Schedule.EnableLazyOutputMaterialization = false;
            Configuration.Schedule.OutputMaterializationExclusionRoots.Add(AbsolutePath.Create(Context.PathTable, exclusionRoot));

            var pipA = SchedulePipBuilder(builderA);
            var pipB = SchedulePipBuilder(builderB);

            RunScheduler().AssertSuccess();

            // For uncached execution, all outputs will be written
            XAssert.IsTrue(Exists(excludedOutput));
            XAssert.IsTrue(Exists(includedOutput));

            // Delete the outputs in order to test whether they are materialized for cached execution
            Delete(excludedOutput);
            Delete(includedOutput);

            RunScheduler().AssertCacheHit(pipA.Process.PipId);

            // For cached execution with full materialization, only the included output should exist
            XAssert.IsFalse(Exists(excludedOutput));
            XAssert.IsTrue(Exists(includedOutput));

            Configuration.Filter = "tag='pipB'";

            // Running a pip which depends on the excluded output
            RunScheduler().AssertCacheMiss(pipB.Process.PipId);
            XAssert.IsTrue(Exists(excludedOutput));
        }
    }
}
