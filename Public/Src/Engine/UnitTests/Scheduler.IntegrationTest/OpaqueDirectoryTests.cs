// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Native.IO;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace IntegrationTest.BuildXL.Scheduler
{
    [Trait("Category", "OpaqueDirectoryTests")]
    [Feature(Features.OpaqueDirectory)]
    public class OpaqueDirectoryTests : SchedulerIntegrationTestBase
    {
        public OpaqueDirectoryTests(ITestOutputHelper output) : base(output)
        {
            // TODO: remove when the default changes
            ((UnsafeSandboxConfiguration)(Configuration.Sandbox.UnsafeSandboxConfiguration)).IgnoreDynamicWritesOnAbsentProbes = false;
        }

        /// <summary>
        /// Creates an OpaqueDirectory producer & OpaqueDirectory consumer and verifies their usage and caching behavior
        /// </summary>
        [Fact]
        public void OpaqueDirectoryConsumptionCachingBehavior()
        {
            // Set up PipA  => opaqueDirectory => PipB
            string opaqueDir = Path.Combine(ObjectRoot, "opaquedir");
            AbsolutePath opaqueDirPath = AbsolutePath.Create(Context.PathTable, opaqueDir);
            FileArtifact outputInOpaque = CreateOutputFileArtifact(opaqueDir);
            FileArtifact source = CreateSourceFile();

            var pipA = CreateAndScheduleOpaqueProducer(opaqueDir, source, new KeyValuePair<FileArtifact, string>(outputInOpaque, null));

            var builderB = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(outputInOpaque, doNotInfer:true),
                Operation.WriteFile(CreateOutputFileArtifact())
            });

            builderB.AddInputDirectory(pipA.ProcessOutputs.GetOpaqueDirectory(opaqueDirPath));
            var pipB = SchedulePipBuilder(builderB);

            // B should be able to consume the file in the opaque directory. Second build should have both cached
            RunScheduler().AssertCacheMiss(pipA.Process.PipId, pipB.Process.PipId);
            RunScheduler().AssertCacheHit(pipA.Process.PipId, pipB.Process.PipId);

            // Make sure we can replay the file in the opaque directory
            File.Delete(ArtifactToString(outputInOpaque));
            RunScheduler().AssertCacheHit(pipA.Process.PipId, pipB.Process.PipId);
            XAssert.IsTrue(File.Exists(ArtifactToString(outputInOpaque)));

            // Modify the input and make sure both are rerun
            File.WriteAllText(ArtifactToString(source), "New content");
            RunScheduler().AssertCacheMiss(pipA.Process.PipId, pipB.Process.PipId);
            RunScheduler().AssertCacheHit(pipA.Process.PipId, pipB.Process.PipId);
        }

        [Fact]
        public void OpaqueDirectoryExternallyContributedFiles()
        {
            string opaqueDir = Path.Combine(ObjectRoot, "opaqueDir");
            FileArtifact producedOutput = CreateOutputFileArtifact(opaqueDir);
            string externallyProducedOutput = Path.Combine(opaqueDir, "FileProducedOutsideOfBuild");

            var builder = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact())
            });

            var pip = SchedulePipBuilder(builder);

            // Execute the build with a pip that produces a single file to the output directory
            RunScheduler().AssertCacheMiss(pip.Process.PipId);

            // Add a file to the output
            Directory.CreateDirectory(Path.GetDirectoryName(externallyProducedOutput));
            File.WriteAllText(externallyProducedOutput, "Hello!");

            // The pip should be a cache hit
            RunScheduler().AssertCacheHit(pip.Process.PipId);

            // If running with Incremental scheduling the stray file will still exist, otherwise it will not.
            if (Configuration.Schedule.IncrementalScheduling || Configuration.Schedule.GraphAgnosticIncrementalScheduling)
            {
                XAssert.IsTrue(File.Exists(externallyProducedOutput), "Expected {0} to exist when using incremental scheduling", externallyProducedOutput);
            }
            else
            {
                XAssert.IsFalse(File.Exists(externallyProducedOutput), "Did not expect {0} to exist when using incremental scheduling", externallyProducedOutput);
            }

            // Now delete the opaque directory. In both scheduling algorithms the stray file should no longer exist after replay.
            FileUtilities.DeleteDirectoryContents(opaqueDir, deleteRootDirectory: true);
            RunScheduler().AssertCacheHit(pip.Process.PipId);
            XAssert.IsFalse(File.Exists(externallyProducedOutput));
        }

        /// <summary>
        /// Creates an opaque directory and an output file outside of that directory.
        /// Checks that the opaque directory is replayed on a cash hit.
        /// </summary>
        [Fact]
        public void EmptyUpstreamOpaqueDirectoryCreation()
        {
            string opaqueDir = Path.Combine(ObjectRoot, "opaquedir");
            AbsolutePath opaqueDirPath = AbsolutePath.Create(Context.PathTable, opaqueDir);
            FileArtifact sourceA = CreateSourceFile();
            FileArtifact outputA = CreateOutputFileArtifact();

            var builderA = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(sourceA),
                Operation.WriteFile(outputA)
            });
            builderA.AddOutputDirectory(opaqueDirPath);
            var pipA = SchedulePipBuilder(builderA);

            RunScheduler().AssertSuccess();
            // make sure that the opaque directory was created even though it's empty
            XAssert.IsTrue(Directory.Exists(opaqueDir));

            // delete the opaque directory
            Directory.Delete(opaqueDir, true);

            //second run - the opaque directory should be replayed
            RunScheduler().AssertCacheHit(pipA.Process.PipId);
            XAssert.IsTrue(Directory.Exists(opaqueDir));
        }

        /// <summary>
        /// Consumes a particular file instead of the entire opaque directory. This also validates that a pip's
        /// file based output can overlap with its opaque directory output
        /// </summary>
        [Fact]
        public void ConsumeExplicitFileOutOfOpaque()
        {
            // Set up PipA  => opaqueDirectory => PipB
            string opaqueDir = Path.Combine(ObjectRoot, "opaquedir");
            AbsolutePath opaqueDirPath = AbsolutePath.Create(Context.PathTable, opaqueDir);
            FileArtifact consumedOutputInOpaque = CreateOutputFileArtifact(opaqueDir);
            FileArtifact unconsumedOutputInOpaque = CreateOutputFileArtifact(opaqueDir);

            var builderA = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(CreateSourceFile()),
                Operation.WriteFile(unconsumedOutputInOpaque, doNotInfer: true),
                Operation.WriteFile(consumedOutputInOpaque), // This file write is specified explicitly
            });
            builderA.AddOutputDirectory(opaqueDirPath);
            var pipA = SchedulePipBuilder(builderA);

            FileArtifact pipBSource = CreateSourceFile();
            var builderB = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(pipBSource),
                Operation.ReadFile(consumedOutputInOpaque),
                Operation.WriteFile(CreateOutputFileArtifact())
            });
            builderB.AddTags(Context.StringTable, "pipB");
            var pipB = SchedulePipBuilder(builderB);

            RunScheduler().AssertSuccess();
            RunScheduler().AssertCacheHit(pipA.Process.PipId, pipB.Process.PipId);

            // Delete the opaque directory, change pipB's inputs, and only request pipB
            FileUtilities.DeleteDirectoryContents(opaqueDir);
            File.WriteAllText(ArtifactToString(pipBSource), "Asdf");
            Configuration.Filter = "tag='pipB'";
            var run3Result = RunScheduler();
            run3Result.AssertCacheHit(pipA.Process.PipId);
            run3Result.AssertCacheMiss(pipB.Process.PipId);
            
            // TODO: we really only need the directly consumed file to be materialized instead of the full opaque
            // directory. This optimization could be added at some point.
            // XAssert.IsFalse(File.Exists(ToString(unconsumedOutputInOpaque)));
        }

        [Theory]
        [InlineData(true)]  // when there is an explicit dependency between the two pips --> allowed
        //[InlineData(false)] // when there is NO explicit dependency between the two pips --> DependencyViolationWriteOnAbsentPathProbe error
                              // NOTE: this is difficult to test reliably because the test depends on pips running in a particular order
        public void AbsentFileProbeFollowedByWriteInExclusiveOpaqueIsBlockedWhenPipsAreIndependent(bool forceDependency)
        {
            var opaqueDir = Path.Combine(ObjectRoot, "opaquedir");
            AbsolutePath opaqueDirPath = AbsolutePath.Create(Context.PathTable, opaqueDir);
            FileArtifact absentFile = CreateOutputFileArtifact(opaqueDir);
            var dummyOut = CreateOutputFileArtifact(prefix: "dummyOut");

            // PipA probes absentFile (which is absent at the time0
            var builderA = CreatePipBuilder(new Operation[]
                                            {
                                                Operation.Probe(absentFile, doNotInfer: true),
                                                Operation.WriteFile(dummyOut) // dummy output
                                            });
            var pipA = SchedulePipBuilder(builderA);

            // PipB writes to absentFile into an exclusive opaque directory
            var pipAoutput = pipA.ProcessOutputs.GetOutputFile(dummyOut);
            var builderB = CreatePipBuilder(new Operation[]
                                            {
                                                forceDependency
                                                    ? Operation.ReadFile(pipAoutput)                                // force a BuildXL dependency
                                                    : Operation.WaitUntilFileExists(pipAoutput, doNotInfer: true),  // force that writing to 'absentFile' happens after pipA
                                                Operation.WriteFile(absentFile, doNotInfer: true),
                                            });
            builderB.AddOutputDirectory(opaqueDirPath, SealDirectoryKind.Opaque);
            builderB.AddUntrackedFile(pipAoutput);
            var resB = SchedulePipBuilder(builderB);

            if (forceDependency)
            {
                RunScheduler().AssertSuccess();
            }
            else
            {
                RunScheduler().AssertFailure();
                // We are expecting a write after an absent path probe
                AssertVerboseEventLogged(LogEventId.DependencyViolationWriteOnAbsentPathProbe);
                AssertErrorEventLogged(EventId.FileMonitoringError);
            }
        }

        [Theory]
        [InlineData(true)]  // when there is an explicit dependency between the two pips --> allowed
        //[InlineData(false)] // when there is NO explicit dependency between the two pips --> DependencyViolationWriteOnAbsentPathProbe error
                              // NOTE: this is difficult to test reliably because the test depends on pips running in a particular order
        public void AbsentFileProbeFollowedByWriteInExclusiveOpaqueIsBlockedOnProbeCacheReplayWhenPipsAreIndependent(bool forceDependency)
        {
            var opaqueDir = Path.Combine(ObjectRoot, "opaquedir");
            AbsolutePath opaqueDirPath = AbsolutePath.Create(Context.PathTable, opaqueDir);
            FileArtifact absentFile = CreateOutputFileArtifact(opaqueDir);
            FileArtifact outputFilePipA = CreateOutputFileArtifact();
            var dummyOut = CreateOutputFileArtifact(prefix: "dummyOut");

            // Probe the absent file, run and cache the pip.
            var builderA = CreatePipBuilder(new Operation[]
                                            {
                                                Operation.Probe(absentFile, doNotInfer: true),
                                                Operation.WriteFile(dummyOut) // dummy output
                                            });
            var pipA = SchedulePipBuilder(builderA);

            // PipB writes absentFile into an exclusive opaque directory opaqueDir.
            var pipAoutput = pipA.ProcessOutputs.GetOutputFile(dummyOut);
            var builderB = CreatePipBuilder(new Operation[]
                                            {
                                                forceDependency
                                                    ? Operation.ReadFile(pipAoutput)                                // force a BuildXL dependency
                                                    : Operation.WaitUntilFileExists(pipAoutput, doNotInfer: true),  // force that writing to 'absentFile' happens after pipA
                                                Operation.WriteFile(absentFile, doNotInfer: true),
                                            });
            builderB.AddOutputDirectory(opaqueDirPath, SealDirectoryKind.Opaque);
            builderB.AddUntrackedFile(pipAoutput);
            SchedulePipBuilder(builderB);

            // first run -- cache PipA
            var firstResult = RunScheduler();

            FileUtilities.DeleteDirectoryContents(opaqueDir, deleteRootDirectory: true);
            FileUtilities.DeleteFile(pipAoutput.Path.ToString(Context.PathTable));

            // second run - PipA should come from cache, PipB should run, but hit the same violation
            var secondResult = RunScheduler();
            secondResult.AssertCacheHitWithoutAssertingSuccess(pipA.Process.PipId);

            if (forceDependency)
            {
                firstResult.AssertSuccess();
                secondResult.AssertSuccess();
            }
            else
            {
                // We are expecting a write after an absent path probe
                firstResult.AssertFailure();
                secondResult.AssertFailure();
                AssertVerboseEventLogged(LogEventId.DependencyViolationWriteOnAbsentPathProbe, 2);
                AssertVerboseEventLogged(LogEventId.AbsentPathProbeInsideUndeclaredOpaqueDirectory, 2);
                AssertErrorEventLogged(EventId.FileMonitoringError, 2);
            }
        }


        [Feature(Features.DirectoryEnumeration)]
        [Feature(Features.GraphFileSystem)]
        [Fact]
        public void EnumerateOpaqueDirectory()
        {
            Configuration.Sandbox.FileSystemMode = FileSystemMode.RealAndPipGraph;

            // PipA creates an opaque directory. PipB consumes a file in that directory and enumerates it
            FileArtifact inputA = CreateSourceFile();
            string opaqueDir = Path.Combine(ObjectRoot, "opaquedir");
            AbsolutePath opaqueDirPath = AbsolutePath.Create(Context.PathTable, opaqueDir);
            var firstFileAndOutput = new KeyValuePair<FileArtifact, string>(CreateOutputFileArtifact(opaqueDir), "1");
            FileArtifact pipBOutput = CreateOutputFileArtifact();

            var pipA = CreateAndScheduleOpaqueProducer(opaqueDir, inputA, firstFileAndOutput);
            var pipB = CreateAndScheduleConsumingPip(pipBOutput, pipA.ProcessOutputs.GetOpaqueDirectory(opaqueDirPath));

            // Ensure the correct baseline behavior
            RunScheduler().AssertSuccess();
            RunScheduler().AssertCacheHit(pipA.Process.PipId, pipB.Process.PipId);

            // Modify A's input. Its output is the same so B should still be a hit
            File.WriteAllText(ArtifactToString(inputA), "asdf");
            ResetPipGraphBuilder();
            pipA = CreateAndScheduleOpaqueProducer(opaqueDir, inputA, firstFileAndOutput);
            pipB = CreateAndScheduleConsumingPip(pipBOutput, pipA.ProcessOutputs.GetOpaqueDirectory(opaqueDirPath));

            var run2Result = RunScheduler();
            run2Result.AssertCacheHit(pipB.Process.PipId);
            run2Result.AssertCacheMiss(pipA.Process.PipId);

            // Now, modify A such that it produces an additional file in the opaque directory
            ResetPipGraphBuilder();
            pipA = CreateAndScheduleOpaqueProducer(opaqueDir, inputA, firstFileAndOutput,
                new KeyValuePair<FileArtifact, string>(CreateOutputFileArtifact(opaqueDir), "2"));
            pipB = CreateAndScheduleConsumingPip(pipBOutput, pipA.ProcessOutputs.GetOpaqueDirectory(opaqueDirPath));

            // A is a cache miss because its command line changes. B should be a miss because its enumeration changed
            // This is the case even if RealAndPipGraph was selected
            var run3Result = RunScheduler(); 
            run3Result.AssertCacheMiss(pipA.Process.PipId);
            run3Result.AssertCacheMiss(pipB.Process.PipId);
        }
        
        [Theory]
        [InlineData(Process.AbsentPathProbeInUndeclaredOpaquesMode.Strict, SealDirectoryKind.Opaque)]
        [InlineData(Process.AbsentPathProbeInUndeclaredOpaquesMode.Relaxed, SealDirectoryKind.Opaque)]
        [InlineData(Process.AbsentPathProbeInUndeclaredOpaquesMode.Unsafe, SealDirectoryKind.Opaque)]
        [InlineData(Process.AbsentPathProbeInUndeclaredOpaquesMode.Strict, SealDirectoryKind.SharedOpaque)]
        [InlineData(Process.AbsentPathProbeInUndeclaredOpaquesMode.Relaxed, SealDirectoryKind.SharedOpaque)]
        [InlineData(Process.AbsentPathProbeInUndeclaredOpaquesMode.Unsafe, SealDirectoryKind.SharedOpaque)]
        public void AbsentPathProbeUnderOpaquesModeBehavior(Process.AbsentPathProbeInUndeclaredOpaquesMode absentPathProbeMode, SealDirectoryKind directoryKind)
        {
            var opaqueDir = Path.Combine(ObjectRoot, "opaquedir");
            AbsolutePath opaqueDirPath = AbsolutePath.Create(Context.PathTable, opaqueDir);
            FileArtifact absentFile = CreateOutputFileArtifact(opaqueDir);

            var builderA = CreatePipBuilder(new Operation[]
                                            {
                                                Operation.Probe(absentFile, doNotInfer: true),
                                                Operation.WriteFile(CreateOutputFileArtifact()) // dummy output
                                            });
            builderA.AbsentPathProbeUnderOpaquesMode = absentPathProbeMode;
            var resA = SchedulePipBuilder(builderA);

            // a dummy pip (just to 'declare' opaque directory)
            var builderB = CreatePipBuilder(new Operation[]
                                            {
                                                Operation.ReadFile(resA.ProcessOutputs.GetOutputFiles().First()), // force a dependency
                                                Operation.WriteFile(CreateOutputFileArtifact()),
                                            });
            builderB.AddOutputDirectory(opaqueDirPath, directoryKind);
            SchedulePipBuilder(builderB);

            var result = RunScheduler();
            
            // In Strict mode we block absent path probes and fail a pip; otherwise, we should allow absent path probes.
            if (absentPathProbeMode != Process.AbsentPathProbeInUndeclaredOpaquesMode.Strict)
            {
                result.AssertSuccess();
                AssertVerboseEventLogged(LogEventId.AbsentPathProbeInsideUndeclaredOpaqueDirectory);
            }
            else
            {
                result.AssertFailure();
                AssertErrorEventLogged(EventId.FileMonitoringError);
                AssertVerboseEventLogged(LogEventId.DependencyViolationAbsentPathProbeInsideUndeclaredOpaqueDirectory);
            }
        }

        [Theory]
        [InlineData(Process.AbsentPathProbeInUndeclaredOpaquesMode.Strict, SealDirectoryKind.Opaque)]
        [InlineData(Process.AbsentPathProbeInUndeclaredOpaquesMode.Relaxed, SealDirectoryKind.Opaque)]
        [InlineData(Process.AbsentPathProbeInUndeclaredOpaquesMode.Unsafe, SealDirectoryKind.Opaque)]
        [InlineData(Process.AbsentPathProbeInUndeclaredOpaquesMode.Strict, SealDirectoryKind.SharedOpaque)]
        [InlineData(Process.AbsentPathProbeInUndeclaredOpaquesMode.Relaxed, SealDirectoryKind.SharedOpaque)]
        [InlineData(Process.AbsentPathProbeInUndeclaredOpaquesMode.Unsafe, SealDirectoryKind.SharedOpaque)]
        public void AbsentFileProbeIsAllowedInsideDirectoryDependency(Process.AbsentPathProbeInUndeclaredOpaquesMode absentPathProbeMode, SealDirectoryKind directoryKind)
        {
            // we should always allow absent path probes inside opaque directories a pip depends on

            var opaqueDir = Path.Combine(ObjectRoot, "opaquedir");
            AbsolutePath opaqueDirPath = AbsolutePath.Create(Context.PathTable, opaqueDir);
            FileArtifact absentFile = CreateOutputFileArtifact(opaqueDir);
            
            var builderA = CreatePipBuilder(new Operation[]
                                            {
                                                Operation.WriteFile(CreateOutputFileArtifact()),
                                            });
            builderA.AddOutputDirectory(opaqueDirPath, directoryKind);
            var pipA = SchedulePipBuilder(builderA);

            var builderB = CreatePipBuilder(new Operation[]
                                            {
                                                Operation.Probe(absentFile, doNotInfer: true),
                                                Operation.WriteFile(CreateOutputFileArtifact()) // dummy output
                                            });
            builderB.AddInputDirectory(pipA.ProcessOutputs.GetOutputDirectories().First().Root);
            builderB.AbsentPathProbeUnderOpaquesMode = absentPathProbeMode;
            SchedulePipBuilder(builderB);

            RunScheduler().AssertSuccess();
        }
    }
}
