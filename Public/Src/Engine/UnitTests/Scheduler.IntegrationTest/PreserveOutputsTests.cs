// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Scheduler
{
    [Trait("Category", "PreserveOutputsTests")]
    public class PreserveOutputsTests : SchedulerIntegrationTestBase
    {
        // Content we will be testing below in the output file
        internal const string CONTENT = "A";
        internal const string CONTENT_TWICE = CONTENT + CONTENT;
        internal const string CONTENT_THRICE = CONTENT_TWICE + CONTENT;

        public PreserveOutputsTests(ITestOutputHelper output) : base(output)
        {
        }

        // Helper method to schedule and create a pip
        private ProcessWithOutputs ScheduleAndGetPip(out FileArtifact input, out FileArtifact output, bool opaque, bool pipPreserveOutputsFlag)
        {
            input = CreateSourceFile();
            string opaqueStrPath = string.Empty;
            if (opaque)
            {
                opaqueStrPath = Path.Combine(ObjectRoot, "opaqueDir");
                output = CreateOutputFileArtifact(opaqueStrPath);
            }
            else
            {
                output = CreateOutputFileArtifact();
            }

            // ...........PIP A...........
            var builderA = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(input),
                // Appends to output unless output does not exist.
                // If output does not exist, then output is created and then written.
                Operation.WriteFile(output, CONTENT, doNotInfer:opaque)
            });

            if (opaque)
            {
                AbsolutePath opaqueDirPath = AbsolutePath.Create(Context.PathTable, opaqueStrPath);
                // Cache will materialize a file into an opaque dir or leave it there with preserve outputs.
                builderA.AddOutputDirectory(opaqueDirPath);
            }

            if (pipPreserveOutputsFlag)
            {
                builderA.Options |= Process.Options.AllowPreserveOutputs;
            }

            return SchedulePipBuilder(builderA);
        }

        private void ScheduleProcessAndCopy(out FileArtifact input, out FileArtifact preservedOutput, out FileArtifact copiedOutput, out Process preservingProcess)
        {
            // input -> ProcessPip -> preservedOutput -> CopyPip -> copiedOutput

            input = CreateSourceFile();
            preservedOutput = CreateOutputFileArtifact();

            var builder = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(input),
                // Appends to output unless output does not exist.
                // If output does not exist, then output is created and then written.
                Operation.WriteFile(preservedOutput, CONTENT, doNotInfer: false)
            });

            builder.Options |= Process.Options.AllowPreserveOutputs;

            var processOutputs = SchedulePipBuilder(builder);
            preservingProcess = processOutputs.Process;
            copiedOutput = CopyFile(preservedOutput, CreateOutputFileArtifact().Path);
        }

        private void ScheduleProcessConsumingDynamicOutput(
            out FileArtifact input, 
            out DirectoryArtifact outputDirectory, 
            out FileArtifact preservedOutput, 
            out Process dynamicOutputProducer,
            out Process preservingProcess)
        {
            // dummyInput -> Process A -> opaqueDir -> Process B -> preservedOutput
            //                                            ^
            //                                            |
            //                                 input -----+

            var dummyInput = CreateSourceFile();
            string opaqueStrPath = string.Empty;
            opaqueStrPath = Path.Combine(ObjectRoot, "opaqueDir");
            AbsolutePath opaqueDirPath = AbsolutePath.Create(Context.PathTable, opaqueStrPath);
            var dynamicOutput = CreateOutputFileArtifact(opaqueStrPath);

            // Pip A
            var builderA = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(dummyInput),
                Operation.WriteFile(dynamicOutput, CONTENT, doNotInfer: true)
            });

            builderA.AddOutputDirectory(opaqueDirPath);

            builderA.Options |= Process.Options.AllowPreserveOutputs;

            var processAndOutputsA = SchedulePipBuilder(builderA);
            outputDirectory = processAndOutputsA.ProcessOutputs.GetOpaqueDirectory(opaqueDirPath);
            dynamicOutputProducer = processAndOutputsA.Process;

            // Pip B
            input = CreateSourceFile();
            preservedOutput = CreateOutputFileArtifact();

            var builderB = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(input),
                Operation.WriteFile(preservedOutput, CONTENT, doNotInfer: false)
            });

            builderB.AddInputDirectory(outputDirectory);

            builderB.Options |= Process.Options.AllowPreserveOutputs;
            var processAndOutputsB = SchedulePipBuilder(builderB);
            preservingProcess = processAndOutputsB.Process;
        }

        private void ScheduleProcessConsumingPreservedOutput(
            out FileArtifact preservedOutput,
            out FileArtifact input,
            out Process preservingProcess,
            out Process consumingProcess)
        {
            // dummyInput -> Process A -> preservedOutput -> Process B -> output
            //                                                  ^
            //                                                  |
            //                                 input -----------+

            var dummyInput = CreateSourceFile();
            preservedOutput = CreateOutputFileArtifact();

            // Pip A
            var builderA = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(dummyInput),
                Operation.WriteFile(preservedOutput, CONTENT)
            });

            builderA.Options |= Process.Options.AllowPreserveOutputs;

            var processAndOutputsA = SchedulePipBuilder(builderA);
            preservingProcess = processAndOutputsA.Process;

            // Pip B
            input = CreateSourceFile();
            var dummyOutput = CreateOutputFileArtifact();

            var builderB = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(preservedOutput),
                Operation.ReadFile(input),
                Operation.WriteFile(dummyOutput)
            });

            builderB.Options |= Process.Options.AllowPreserveOutputs;
            var processAndOutputsB = SchedulePipBuilder(builderB);
            consumingProcess = processAndOutputsB.Process;
        }

        private void ScheduleRewriteProcess(out FileArtifact rewrittenOutput, out Process preservingProcessA, out Process preservingProcessB)
        {
            // dummyInput -> Process A -> rewrittenOutput -> Process B -> rewrittenOutput
            
            var dummyInput = CreateSourceFile();
            var rewrittenOutputRc1 = CreateOutputFileArtifact();

            // Pip A
            var builderA = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(dummyInput),
                Operation.WriteFile(rewrittenOutputRc1, CONTENT, doNotInfer: false)
            });

            builderA.Options |= Process.Options.AllowPreserveOutputs;

            var processAndOutputsA = SchedulePipBuilder(builderA);
            preservingProcessA = processAndOutputsA.Process;

            // Pip B
            rewrittenOutput = rewrittenOutputRc1.CreateNextWrittenVersion();

            var builderB = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(rewrittenOutputRc1),
                Operation.WriteFile(rewrittenOutput, CONTENT, doNotInfer: false)
            });

            builderB.Options |= Process.Options.AllowPreserveOutputs;
            var processAndOutputsB = SchedulePipBuilder(builderB);
            preservingProcessB = processAndOutputsB.Process;
        }

        private string RunSchedulerAndGetOutputContents(FileArtifact output, bool cacheHitAssert, PipId id)
        {
            if (cacheHitAssert)
            {
                RunScheduler().AssertCacheHit(id);
            }
            else
            {
                RunScheduler().AssertCacheMiss(id);
            }

            return File.ReadAllText(ArtifactToString(output));
        }

        [Fact]
        public void PreserveOutputsTest()
        {
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.PreserveOutputs = PreserveOutputsMode.Enabled;
            var input = CreateSourceFile();
            var output = CreateOutputFileArtifact(Path.Combine(ObjectRoot, @"nested\out\file"));

            var builder = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(input),
                Operation.WriteFile(output, CONTENT)
            });

            builder.Options |= Process.Options.AllowPreserveOutputs;
            var processAndOutputs = SchedulePipBuilder(builder);

            var outputContent = RunSchedulerAndGetOutputContents(output, false, processAndOutputs.Process.PipId);
            XAssert.AreEqual(CONTENT, outputContent);

            ModifyFile(input);

            outputContent = RunSchedulerAndGetOutputContents(output, false, processAndOutputs.Process.PipId);
            XAssert.AreEqual(CONTENT_TWICE, outputContent);
        }

        [Fact]
        public void PreserveOutputsTestWithWhitelist()
        {
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.PreserveOutputs = PreserveOutputsMode.Enabled;
            var input = CreateSourceFile();
            var outputPreserved = CreateOutputFileArtifact(Path.Combine(ObjectRoot, @"nested\out\filePreserved"));
            var outputUnpreserved = CreateOutputFileArtifact(Path.Combine(ObjectRoot, @"nested\out\fileUnpreserved"));

            var builder = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(input),
                Operation.WriteFile(outputPreserved, CONTENT),
                Operation.WriteFile(outputUnpreserved, CONTENT)
            });

            builder.Options |= Process.Options.AllowPreserveOutputs;
            builder.PreserveOutputWhitelist = ReadOnlyArray<AbsolutePath>.FromWithoutCopy(outputPreserved);
            var processAndOutputs = SchedulePipBuilder(builder);

            var outputContent = RunSchedulerAndGetOutputContents(outputPreserved, false, processAndOutputs.Process.PipId);
            XAssert.AreEqual(CONTENT, outputContent);
            XAssert.AreEqual(CONTENT, File.ReadAllText(ArtifactToString(outputUnpreserved)));
            
            ModifyFile(input);

            outputContent = RunSchedulerAndGetOutputContents(outputPreserved, false, processAndOutputs.Process.PipId);
            XAssert.AreEqual(CONTENT_TWICE, outputContent);
            XAssert.AreEqual(CONTENT, File.ReadAllText(ArtifactToString(outputUnpreserved)));

            outputContent = RunSchedulerAndGetOutputContents(outputPreserved, true, processAndOutputs.Process.PipId);
            XAssert.AreEqual(CONTENT_TWICE, outputContent);
            XAssert.AreEqual(CONTENT, File.ReadAllText(ArtifactToString(outputUnpreserved)));
        }

        /// <summary>
        /// Testing preserve outputs in an opaque dir
        /// </summary>
        [Fact]
        public void PreserveOutputsOpaqueTest()
        {
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.PreserveOutputs = PreserveOutputsMode.Enabled;

            // Output is in opaque dir and Unsafe.AllowPreservedOutputs = true
            var pipA = ScheduleAndGetPip(out var input, out var output, opaque: true, pipPreserveOutputsFlag: true);

            // No cache hit
            string outputContents = RunSchedulerAndGetOutputContents(output, cacheHitAssert: false, id: pipA.Process.PipId);
            XAssert.AreEqual(CONTENT, outputContents);

            // Change input
            ModifyFile(input);

            // No cache hit
            outputContents = RunSchedulerAndGetOutputContents(output, cacheHitAssert: false, id: pipA.Process.PipId);
            
            // As the opaque output is preserved, the pip appended the existing file.
            XAssert.AreEqual(CONTENT_TWICE, outputContents);

            outputContents = RunSchedulerAndGetOutputContents(output, cacheHitAssert: true, id: pipA.Process.PipId);

            // Cache hit and the appended file (CONTENT_TWICE) should remain the same.
            XAssert.AreEqual(CONTENT_TWICE, outputContents);
        }

        /// <summary>
        /// Testing preserve outputs in an opaque dir with preserveoutputwhitelist
        /// </summary>
        [Fact]
        public void PreserveOutputsOpaqueTestWithWhitelist()
        {
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.PreserveOutputs = PreserveOutputsMode.Enabled;

            var input = CreateSourceFile();
            var opaquePreservedPath = AbsolutePath.Create(Context.PathTable, Path.Combine(ObjectRoot, "opaquePreservedDir"));
            var outputUnderPreservedOpaque = CreateOutputFileArtifact(opaquePreservedPath);

            var opaqueUnpreservedPath = AbsolutePath.Create(Context.PathTable, Path.Combine(ObjectRoot, "opaqueUnpreservedDir"));
            var outputUnderUnpreservedOpaque = CreateOutputFileArtifact(opaqueUnpreservedPath);

            var builder = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(input),
                Operation.WriteFile(outputUnderPreservedOpaque, CONTENT, doNotInfer: true),
                Operation.WriteFile(outputUnderUnpreservedOpaque, CONTENT, doNotInfer: true)
            });

            builder.AddOutputDirectory(opaquePreservedPath);
            builder.AddOutputDirectory(opaqueUnpreservedPath);
            builder.Options |= Process.Options.AllowPreserveOutputs;
            builder.PreserveOutputWhitelist = ReadOnlyArray<AbsolutePath>.FromWithoutCopy(opaquePreservedPath);
            var processAndOutputs = SchedulePipBuilder(builder);

            // No cache hit
            string outputContents = RunSchedulerAndGetOutputContents(outputUnderPreservedOpaque, cacheHitAssert: false, id: processAndOutputs.Process.PipId);
            XAssert.AreEqual(CONTENT, outputContents);
            XAssert.AreEqual(CONTENT, File.ReadAllText(ArtifactToString(outputUnderUnpreservedOpaque)));

            // Change input
            ModifyFile(input);

            // No cache hit
            outputContents = RunSchedulerAndGetOutputContents(outputUnderPreservedOpaque, cacheHitAssert: false, id: processAndOutputs.Process.PipId);

            // As the opaque output is preserved, the pip appended the existing file.
            XAssert.AreEqual(CONTENT_TWICE, outputContents);
            // For the file under unpreserved opaque directory, the file was created, so we did not append.
            XAssert.AreEqual(CONTENT, File.ReadAllText(ArtifactToString(outputUnderUnpreservedOpaque)));

            // Cache hit
            outputContents = RunSchedulerAndGetOutputContents(outputUnderPreservedOpaque, cacheHitAssert: true, id: processAndOutputs.Process.PipId);

            // The appended file (CONTENT_TWICE) should remain the same.
            XAssert.AreEqual(CONTENT_TWICE, outputContents);
            XAssert.AreEqual(CONTENT, File.ReadAllText(ArtifactToString(outputUnderUnpreservedOpaque)));
        }

        /// <summary>
        /// Testing behavior of enabling preserve outputs and then reseting
        /// </summary>
        /// <param name="reset">true will reset preserve outputs after it is enabled, false will not reset preserve outputs</param>
        [Theory]
        [MemberData(nameof(TruthTable.GetTable), 1, MemberType = typeof(TruthTable))]
        public void PreserveOutputResetTest(bool reset)
        {
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.PreserveOutputs = PreserveOutputsMode.Enabled;

            var pipA = ScheduleAndGetPip(out var input, out var output, opaque: false, pipPreserveOutputsFlag: true);

            // No cache hit
            string outputContents = RunSchedulerAndGetOutputContents(output, cacheHitAssert: false, id: pipA.Process.PipId);

            // Change the input 
            ModifyFile(input);

            // No cache hit
            outputContents = RunSchedulerAndGetOutputContents(output, cacheHitAssert: false, id: pipA.Process.PipId);
            XAssert.AreEqual(CONTENT_TWICE, outputContents);

            // ... RESET PRESERVE OUTPUTS ...
            if (reset)
            {
                Configuration.Sandbox.UnsafeSandboxConfigurationMutable.PreserveOutputs = PreserveOutputsMode.Reset;

                // No cache hit
                outputContents = RunSchedulerAndGetOutputContents(output, cacheHitAssert: false, id: pipA.Process.PipId);
                XAssert.AreEqual(CONTENT_THRICE, outputContents);
            }
            else
            {
                // Cache hit
                outputContents = RunSchedulerAndGetOutputContents(output, cacheHitAssert: true, id: pipA.Process.PipId);
                XAssert.AreEqual(CONTENT_TWICE, outputContents);

                // Change the input again
                ModifyFile(input);

                // No cache hit
                outputContents = RunSchedulerAndGetOutputContents(output, cacheHitAssert: false, id: pipA.Process.PipId);
                XAssert.AreEqual(CONTENT_THRICE, outputContents);
            }
        }

        /// <summary>
        /// Testing behavior of when UnsafeSandboxConfigurationMutable.PreserveOutputs or
        /// Unsafe.AllowPreservedOutputs do not agree
        /// </summary>
        /// <param name="buildPreserve">UnsafeSandboxConfigurationMutable.PreserveOutputs value</param>
        /// <param name="pipPreserve">Unsafe.AllowPreservedOutputs value</param>
        [Theory]
        [MemberData(nameof(TruthTable.GetTable), 2, MemberType = typeof(TruthTable))]
        public void BuildAndPipFlagTest(bool buildPreserve, bool pipPreserve)
        {
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.PreserveOutputs = 
                buildPreserve 
                ? PreserveOutputsMode.Enabled 
                : PreserveOutputsMode.Disabled;

            var pipA = ScheduleAndGetPip(out var input, out var output, false, pipPreserve);

            // No cache hit
            string outputContents = RunSchedulerAndGetOutputContents(output, cacheHitAssert: false, id: pipA.Process.PipId);

            // Change the input
            ModifyFile(input);

            if (!buildPreserve || !pipPreserve)
            {
                // No cache hit
                outputContents = RunSchedulerAndGetOutputContents(output, cacheHitAssert: false, id: pipA.Process.PipId);
                XAssert.AreEqual(CONTENT, outputContents);
            }
            else
            {
                // No cache hit
                outputContents = RunSchedulerAndGetOutputContents(output, cacheHitAssert: false, id: pipA.Process.PipId);
                XAssert.AreEqual(CONTENT_TWICE, outputContents);
            }
        }

        /// <summary>
        /// Testing preserve outputs enabled or not and disabling after enabling
        /// </summary>
        /// <param name="preserveOutputs">value is used for config: UnsafeSandboxConfigurationMutable.PreserveOutputs and Unsafe.AllowPreservedOutputs</param>
        [Theory]
        [MemberData(nameof(TruthTable.GetTable), 1, MemberType = typeof(TruthTable))]
        public void PreserveOutputsTest2(bool preserveOutputs)
        {
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.PreserveOutputs = 
                preserveOutputs 
                ? PreserveOutputsMode.Enabled 
                : PreserveOutputsMode.Disabled;

            var pipA = ScheduleAndGetPip(out var input, out var output, false, preserveOutputs);

            // No cache hit
            string outputContents = RunSchedulerAndGetOutputContents(output, cacheHitAssert: false, id: pipA.Process.PipId);
            XAssert.AreEqual(CONTENT, outputContents);

            // Change the input
            ModifyFile(input);

            if (preserveOutputs)
            {
                // No cache hit
                outputContents = RunSchedulerAndGetOutputContents(output, cacheHitAssert: false, id: pipA.Process.PipId);
                XAssert.AreEqual(CONTENT_TWICE, outputContents);

                // Cache hit
                outputContents = RunSchedulerAndGetOutputContents(output, cacheHitAssert: true, id: pipA.Process.PipId);
                XAssert.AreEqual(CONTENT_TWICE, outputContents);

                // Turning off preserve outputs now should cause the fingerprint to change (even though input is same)
                // which causes a cache miss and the pip rerun after BuildXL deletes the file
                Configuration.Sandbox.UnsafeSandboxConfigurationMutable.PreserveOutputs = PreserveOutputsMode.Disabled;
            }

            // No cache hit preserve outputs is disabled by default or disabled after it is enabled (as in if statement above)
            outputContents = RunSchedulerAndGetOutputContents(output, cacheHitAssert: false, id: pipA.Process.PipId);
            XAssert.AreEqual(CONTENT, outputContents);
        }

        /// <summary>
        /// Testing that preserve outputs do not store outputs to cache.
        /// </summary>
        /// <param name="preserveOutputs">value is used for config: UnsafeSandboxConfigurationMutable.PreserveOutputs and Unsafe.AllowPreservedOutputs</param>
        [Theory]
        [MemberData(nameof(TruthTable.GetTable), 1, MemberType = typeof(TruthTable))]
        public void PreserveOutputsDoNotStoreOutputsToCacheTest(bool preserveOutputs)
        {
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.PreserveOutputs =
                preserveOutputs
                ? PreserveOutputsMode.Enabled
                : PreserveOutputsMode.Disabled;

            var pipA = ScheduleAndGetPip(out var input, out var output, opaque: false, pipPreserveOutputsFlag: true);

            // No cache hit
            string outputContents = RunSchedulerAndGetOutputContents(output, cacheHitAssert: false, id: pipA.Process.PipId);
            XAssert.AreEqual(CONTENT, outputContents);

            // Output is in artifact cache iff preserve output is off.
            XAssert.AreEqual(!preserveOutputs, FileContentExistsInArtifactCache(output));

            // Delete the output.
            FileUtilities.DeleteFile(ArtifactToString(output));

            // Cache hit only if preserved output is disabled.
            outputContents = RunSchedulerAndGetOutputContents(output, cacheHitAssert: !preserveOutputs, id: pipA.Process.PipId);
            XAssert.AreEqual(CONTENT, outputContents);
        }

        /// <summary>
        /// Testing that copying a preserved output using copy-file pip works.
        /// </summary>
        [Fact]
        public void CopyPreservedOutputTest()
        {
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.PreserveOutputs = PreserveOutputsMode.Enabled;

            FileArtifact input;
            FileArtifact preservedOutput;
            FileArtifact copiedOutput;
            Process preservingProcess;

            ScheduleProcessAndCopy(out input, out preservedOutput, out copiedOutput, out preservingProcess);
            RunScheduler().AssertCacheMiss(preservingProcess.PipId);

            // Due to copy, the preserved output needs to be restored to the cache.
            XAssert.IsTrue(FileContentExistsInArtifactCache(copiedOutput));

            // Change the input
            ModifyFile(input);

            RunScheduler().AssertCacheMiss(preservingProcess.PipId);

            var preservedOutputContent = File.ReadAllText(ArtifactToString(preservedOutput));
            var copiedOutputContent = File.ReadAllText(ArtifactToString(copiedOutput));

            XAssert.AreEqual(CONTENT_TWICE, preservedOutputContent);
            XAssert.AreEqual(CONTENT_TWICE, copiedOutputContent);
        }

        /// <summary>
        /// Testing that preserved output can be consumed.
        /// </summary>
        [Fact]
        public void ProcessConsumingPreservedOutputTest()
        {
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.PreserveOutputs = PreserveOutputsMode.Enabled;

            FileArtifact preservedOutput;
            FileArtifact input;
            Process preservingProcess;
            Process consumingProcess;

            ScheduleProcessConsumingPreservedOutput(out preservedOutput, out input, out preservingProcess, out consumingProcess);
            RunScheduler().AssertCacheMiss(preservingProcess.PipId, consumingProcess.PipId);

            // Preserved output is not in the cache.
            XAssert.IsFalse(FileContentExistsInArtifactCache(preservedOutput));

            // Change the input
            ModifyFile(input);

            var result = RunScheduler();
            result.AssertCacheMiss(consumingProcess.PipId);
            result.AssertCacheHit(preservingProcess.PipId);

            // Preserved output is not in the cache.
            XAssert.IsFalse(FileContentExistsInArtifactCache(preservedOutput));
        }

        /// <summary>
        /// Testing that preserve output pips can live with dynamic outputs.
        /// </summary>
        [Fact]
        public void PreservingProcessConsumingDynamicOutputTest()
        {
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.PreserveOutputs = PreserveOutputsMode.Enabled;

            FileArtifact input;
            FileArtifact preservedOutput;
            DirectoryArtifact outputDirectory;
            Process dynamicOutputProducer;
            Process preservingProcess;

            ScheduleProcessConsumingDynamicOutput(
                out input,
                out outputDirectory,
                out preservedOutput,
                out dynamicOutputProducer,
                out preservingProcess);

            RunScheduler().AssertCacheMiss(preservingProcess.PipId, dynamicOutputProducer.PipId);

            // Delete dynamic output.
            FileUtilities.DeleteDirectoryContents(ArtifactToString(outputDirectory), deleteRootDirectory: true);

            // Cache miss as the output is gone.
            RunScheduler().AssertCacheMiss(dynamicOutputProducer.PipId);

            // Dynamic output producer should result in cache hit.
            RunScheduler().AssertCacheHit(dynamicOutputProducer.PipId);
            var preservedOutputContent = File.ReadAllText(ArtifactToString(preservedOutput));
            XAssert.AreEqual(CONTENT, preservedOutputContent);

            // Modify input to preserving process.
            ModifyFile(input);

            var schedulerResult = RunScheduler();
            schedulerResult.AssertCacheHit(dynamicOutputProducer.PipId);
            schedulerResult.AssertCacheMiss(preservingProcess.PipId);
            preservedOutputContent = File.ReadAllText(ArtifactToString(preservedOutput));
            XAssert.AreEqual(CONTENT_TWICE, preservedOutputContent);
        }

        /// <summary>
        /// Testing the switch from disabling to enabling preserve output mode.
        /// </summary>
        [Fact]
        public void PreserveOutputsOffThenOn()
        {
            // Turn off.
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.PreserveOutputs = PreserveOutputsMode.Disabled;

            var pipA = ScheduleAndGetPip(out var input, out var output, false, true);

            // No cache hit.
            string outputContents = RunSchedulerAndGetOutputContents(output, cacheHitAssert: false, id: pipA.Process.PipId);
            XAssert.AreEqual(CONTENT, outputContents);

            // Cache hit.
            outputContents = RunSchedulerAndGetOutputContents(output, cacheHitAssert: true, id: pipA.Process.PipId);
            XAssert.AreEqual(CONTENT, outputContents);

            // Turn on.
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.PreserveOutputs = PreserveOutputsMode.Enabled;

            // Cache hit.
            outputContents = RunSchedulerAndGetOutputContents(output, cacheHitAssert: true, id: pipA.Process.PipId);
            XAssert.AreEqual(CONTENT, outputContents);

            // Change the input.
            ModifyFile(input);

            // This should be the only cache miss.
            outputContents = RunSchedulerAndGetOutputContents(output, cacheHitAssert: false, id: pipA.Process.PipId);
            XAssert.AreEqual(CONTENT_TWICE, outputContents);

            // Turn off again.
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.PreserveOutputs = PreserveOutputsMode.Disabled;

            // This hould result in cache miss.
            outputContents = RunSchedulerAndGetOutputContents(output, cacheHitAssert: false, id: pipA.Process.PipId);
            XAssert.AreEqual(CONTENT, outputContents);

            // Cache hit.
            outputContents = RunSchedulerAndGetOutputContents(output, cacheHitAssert: true, id: pipA.Process.PipId);
            XAssert.AreEqual(CONTENT, outputContents);
        }

        /// <summary>
        /// Validates that the command line preserveoutputs setting doesn't impact caching
        /// if the pip doesn't allow preserveoutputs
        /// </summary>
        [Fact]
        public void PreserveOutputsOnlyAppliesToSpecificPips()
        {
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.PreserveOutputs = PreserveOutputsMode.Enabled;

            var pipA = ScheduleAndGetPip(out var input, out var output, opaque: false, pipPreserveOutputsFlag: false);

            // No cache hit.
            string outputContents = RunSchedulerAndGetOutputContents(output, cacheHitAssert: false, id: pipA.Process.PipId);
            XAssert.AreEqual(CONTENT, outputContents);

            // Cache hit.
            outputContents = RunSchedulerAndGetOutputContents(output, cacheHitAssert: true, id: pipA.Process.PipId);
            XAssert.AreEqual(CONTENT, outputContents);

            // Disabling preserve outputs should have no impact because it was not enabled for this pip in the first run
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.PreserveOutputs = PreserveOutputsMode.Disabled;
            outputContents = RunSchedulerAndGetOutputContents(output, cacheHitAssert: true, id: pipA.Process.PipId);
            XAssert.AreEqual(CONTENT, outputContents);
        }

        /// <summary>
        /// Testing that rewritten preserved outputs are stored in cache.
        /// </summary>
        [Fact]
        public void RewrittenPreservedOutputsAreStoredInCache()
        {
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.PreserveOutputs = PreserveOutputsMode.Enabled;

            FileArtifact rewrittenOutput;
            Process preservingProcessA;
            Process preservingProcessB;

            ScheduleRewriteProcess(out rewrittenOutput, out preservingProcessA, out preservingProcessB);

            // No cache hit
            RunScheduler().AssertCacheMiss(preservingProcessA.PipId, preservingProcessB.PipId);
            string outputContents = File.ReadAllText(ArtifactToString(rewrittenOutput));
            XAssert.AreEqual(CONTENT_TWICE, outputContents);

            File.Delete(ArtifactToString(rewrittenOutput));

            // Cache hit
            RunScheduler().AssertCacheHit(preservingProcessA.PipId, preservingProcessB.PipId);
            outputContents = File.ReadAllText(ArtifactToString(rewrittenOutput));
            XAssert.AreEqual(CONTENT_TWICE, outputContents);
        }

        private void ModifyFile(FileArtifact file, string content = null)
        {
            File.WriteAllText(ArtifactToString(file), content ?? Guid.NewGuid().ToString());
        }
    }
}
