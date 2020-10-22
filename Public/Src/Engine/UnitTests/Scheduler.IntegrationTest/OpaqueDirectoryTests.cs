// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Native.IO;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Scheduler
{
    [Trait("Category", "OpaqueDirectoryTests")]
    [Feature(Features.OpaqueDirectory)]
    public class OpaqueDirectoryTests : SchedulerIntegrationTestBase
    {
        public OpaqueDirectoryTests(ITestOutputHelper output) : base(output)
        {
            // TODO: remove when the default changes
            ((UnsafeSandboxConfiguration)(Configuration.Sandbox.UnsafeSandboxConfiguration)).IgnoreDynamicWritesOnAbsentProbes = DynamicWriteOnAbsentProbePolicy.IgnoreNothing;
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
                Operation.WriteFile(producedOutput, doNotInfer: true)
            });
            builder.AddOutputDirectory(AbsolutePath.Create(Context.PathTable, opaqueDir));

            var pip = SchedulePipBuilder(builder);

            // Execute the build with a pip that produces a single file to the output directory
            RunScheduler().AssertCacheMiss(pip.Process.PipId);

            // Add a file to the output
            Directory.CreateDirectory(Path.GetDirectoryName(externallyProducedOutput));
            File.WriteAllText(externallyProducedOutput, "Hello!");

            // The pip should be a cache hit
            RunScheduler().AssertCacheHit(pip.Process.PipId);

            // The addition of external file makes the pip dirty. Thus, the pip will be replayed from the cache. This replay deletes
            // the externally contributed file.
            XAssert.IsFalse(File.Exists(externallyProducedOutput), "Did not expect {0} to exist", externallyProducedOutput);

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
                AssertErrorEventLogged(LogEventId.FileMonitoringError);
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
                AssertErrorEventLogged(LogEventId.FileMonitoringError, 2);
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
                AssertErrorEventLogged(LogEventId.FileMonitoringError);
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

        [Fact]
        public void OutputDirectoryNoChangeTracking()
        {
            var outputDirectory = CreateUniqueObjPath("outputDir");
            var outputFileA = CreateOutputFileArtifact(outputDirectory, "fileA");
            var outputFileB = CreateOutputFileArtifact(outputDirectory, "fileB");
            var outputFileC = CreateOutputFileArtifact(outputDirectory, "fileC");
            var sourceFile = CreateSourceFile();

            var builder = CreatePipBuilder(
                new[]
                {
                    Operation.ReadFile(sourceFile),
                    Operation.WriteFile(outputFileA, doNotInfer: true),
                    Operation.WriteFile(outputFileB, doNotInfer: true),
                    Operation.WriteFile(outputFileC, doNotInfer: true),
                });
            builder.AddOutputDirectory(outputDirectory);
            var process = SchedulePipBuilder(builder);

            RunScheduler().AssertSuccess();
            
            var testHooks = new SchedulerTestHooks();
            var result = RunScheduler(testHooks: testHooks);
            
            if (Configuration.Schedule.IncrementalScheduling)
            {
                result.AssertNotScheduled(process.Process.PipId);

                // This assertion ensures that journal processing does not detect possibility of membership change.
                // If it detects so, then the jounral processing has to do extra enumeration to validate that possibility.
                XAssert.AreEqual(
                    0,
                    testHooks.ScanningJournalResult.Stats.GetCounterValue(
                        global::BuildXL.Storage.ChangeJournalService.Protocol.ReadJournalCounter.ExistentialChangesSuppressedAfterVerificationCount));
            }
            else
            {
                result.AssertCacheHit(process.Process.PipId);
            }
        }

        [Fact]
        public void OutputDirectoryWithMembershipChange()
        {
            var outputDirectory = CreateUniqueObjPath("outputDir");
            var outputFileA = CreateOutputFileArtifact(outputDirectory, "fileA");
            var outputFileB = CreateOutputFileArtifact(outputDirectory, "fileB");
            var outputFileC = CreateOutputFileArtifact(outputDirectory, "fileC");
            var sourceFile = CreateSourceFile();

            var builder = CreatePipBuilder(
                new[]
                {
                    Operation.ReadFile(sourceFile),
                    Operation.WriteFile(outputFileA, doNotInfer: true),
                    Operation.WriteFile(outputFileB, doNotInfer: true),
                    Operation.WriteFile(outputFileC, doNotInfer: true),
                });
            builder.AddOutputDirectory(outputDirectory);
            var process = SchedulePipBuilder(builder);

            RunScheduler().AssertSuccess();

            // Change membership of output directory by adding an extra file.
            File.WriteAllText(outputDirectory.Combine(Context.PathTable, "fileD").ToString(Context.PathTable), "foo");

            RunScheduler()
                .AssertSuccess()
                .AssertScheduled(process.Process.PipId)
                .AssertCacheHit(process.Process.PipId);

            var testHooks = new SchedulerTestHooks();
            var result = RunScheduler(testHooks: testHooks);

            if (Configuration.Schedule.IncrementalScheduling)
            {
                result.AssertNotScheduled(process.Process.PipId);

                // This assertion ensures that journal processing does not detect possibility of membership change.
                // If it detects so, then the jounral processing has to do extra enumeration to validate that possibility.
                XAssert.AreEqual(
                    0,
                    testHooks.ScanningJournalResult.Stats.GetCounterValue(
                        global::BuildXL.Storage.ChangeJournalService.Protocol.ReadJournalCounter.ExistentialChangesSuppressedAfterVerificationCount));
            }
            else
            {
                result.AssertCacheHit(process.Process.PipId);
            }
        }

        [Fact]
        public void OutputDirectoryWithUntrackedPaths()
        {
            var outputDirectory = CreateUniqueObjPath("outputDir");
            var outputFileA = CreateOutputFileArtifact(outputDirectory, "fileA");
            var outputFileB = CreateOutputFileArtifact(outputDirectory, "fileB");
            var outputFileC = CreateOutputFileArtifact(outputDirectory, "fileC");
            var sourceFile = CreateSourceFile();

            var builder = CreatePipBuilder(
                new[]
                {
                    Operation.ReadFile(sourceFile),
                    Operation.WriteFile(outputFileA, doNotInfer: true),
                    Operation.WriteFile(outputFileB, doNotInfer: true),
                    Operation.WriteFile(outputFileC, doNotInfer: true),
                });
            builder.AddOutputDirectory(outputDirectory);
            builder.AddUntrackedFile(outputFileC);
            var process = SchedulePipBuilder(builder);

            RunScheduler().AssertSuccess();

            XAssert.IsTrue(File.Exists(ArtifactToString(outputFileC)));

            XAssert.IsTrue(File.Exists(ArtifactToString(outputFileA)));
            XAssert.IsTrue(File.Exists(ArtifactToString(outputFileB)));

            File.WriteAllText(ArtifactToString(outputFileC), "Modified-C");

            var result = RunScheduler();

            if (Configuration.Schedule.IncrementalScheduling)
            {
                result.AssertNotScheduled(process.Process.PipId);

                // Pip is not scheduled, output fileC is still there.
                XAssert.IsTrue(File.Exists(ArtifactToString(outputFileC)));
            }
            else
            {
                result.AssertCacheHit(process.Process.PipId);

                // Since output fileC is untracked, it should no longer exist when pip replayed outputs from cache.
                XAssert.IsFalse(File.Exists(ArtifactToString(outputFileC)));
            }

            XAssert.IsTrue(File.Exists(ArtifactToString(outputFileA)));
            XAssert.IsTrue(File.Exists(ArtifactToString(outputFileB)));
        }

        [Fact]
        public void OutputDirectoryWithUntrackedScopes()
        {
            var outputDirectory = CreateUniqueObjPath("outputDir");
            var nestedOutputDirectory = outputDirectory.Combine(Context.PathTable, "nested");
            var outputFileA = CreateOutputFileArtifact(nestedOutputDirectory, "fileA");
            var outputFileB = CreateOutputFileArtifact(nestedOutputDirectory, "fileB");
            var outputFileC = CreateOutputFileArtifact(nestedOutputDirectory, "fileC");
            var outputFileX = CreateOutputFileArtifact(outputDirectory, "fileX");
            var outputFileY = CreateOutputFileArtifact(outputDirectory, "fileY");
            var sourceFile = CreateSourceFile();

            var builder = CreatePipBuilder(
                new[]
                {
                    Operation.ReadFile(sourceFile),
                    Operation.WriteFile(outputFileA, doNotInfer: true),
                    Operation.WriteFile(outputFileB, doNotInfer: true),
                    Operation.WriteFile(outputFileC),
                    Operation.WriteFile(outputFileX, doNotInfer: true),
                    Operation.WriteFile(outputFileY, doNotInfer: true),
                });
            builder.AddOutputDirectory(outputDirectory);
            builder.AddUntrackedDirectoryScope(nestedOutputDirectory);
            var process = SchedulePipBuilder(builder);

            RunScheduler().AssertSuccess();

            XAssert.IsTrue(File.Exists(ArtifactToString(outputFileA)));
            XAssert.IsTrue(File.Exists(ArtifactToString(outputFileB)));
            XAssert.IsTrue(File.Exists(ArtifactToString(outputFileC)));
            XAssert.IsTrue(File.Exists(ArtifactToString(outputFileX)));
            XAssert.IsTrue(File.Exists(ArtifactToString(outputFileY)));

            var result = RunScheduler();

            if (Configuration.Schedule.IncrementalScheduling)
            {
                result.AssertNotScheduled(process.Process.PipId);

                XAssert.IsTrue(File.Exists(ArtifactToString(outputFileA)));
                XAssert.IsTrue(File.Exists(ArtifactToString(outputFileB)));
            }
            else
            {
                result.AssertCacheHit(process.Process.PipId);

                // Output fileA and fileB should no longer exist after cache replay, because nested directory is untracked.
                XAssert.IsFalse(File.Exists(ArtifactToString(outputFileA)));
                XAssert.IsFalse(File.Exists(ArtifactToString(outputFileB)));
            }

            // Although fileC is inside untracked nested directory, but it is specified as an output file, and so
            // it has to exist after cache replay.
            XAssert.IsTrue(File.Exists(ArtifactToString(outputFileC)));

            XAssert.IsTrue(File.Exists(ArtifactToString(outputFileX)));
            XAssert.IsTrue(File.Exists(ArtifactToString(outputFileY)));

            File.WriteAllText(ArtifactToString(outputFileC), "Modified-C");

            result = RunScheduler();
            result.AssertScheduled(process.Process.PipId).AssertCacheHit(process.Process.PipId);

            // Output fileA and fileB should no longer exist after cache replay, because nested directory is untracked.
            XAssert.IsFalse(File.Exists(ArtifactToString(outputFileA)));
            XAssert.IsFalse(File.Exists(ArtifactToString(outputFileB)));

            XAssert.IsTrue(File.Exists(ArtifactToString(outputFileC)));

            XAssert.IsTrue(File.Exists(ArtifactToString(outputFileX)));
            XAssert.IsTrue(File.Exists(ArtifactToString(outputFileY)));
        }

        [Fact]
        public void EnumerateOutputDirectoryWithUntrackedPaths()
        {
            var outputDirectory = CreateUniqueObjPath("outputDir");
            var outputFileA = CreateOutputFileArtifact(outputDirectory, "fileA");
            var outputFileB = CreateOutputFileArtifact(outputDirectory, "fileB");
            var outputFileC = CreateOutputFileArtifact(outputDirectory, "fileC");
            var sourceFile = CreateSourceFile();

            var builderA = CreatePipBuilder(
                new[]
                {
                    Operation.ReadFile(sourceFile),
                    Operation.WriteFile(outputFileA, doNotInfer: true),
                    Operation.WriteFile(outputFileB, doNotInfer: true),
                    Operation.WriteFile(outputFileC, doNotInfer: true),
                });
            builderA.AddOutputDirectory(outputDirectory);
            builderA.AddUntrackedFile(outputFileC);
            var processA = SchedulePipBuilder(builderA);

            var builderB = CreatePipBuilder(
                new[]
                {
                    Operation.ReadFile(sourceFile),
                    Operation.EnumerateDir(processA.ProcessOutputs.GetOpaqueDirectory(outputDirectory)),
                    Operation.WriteFile(CreateOutputFileArtifact()),
                });
            builderB.AddInputDirectory(processA.ProcessOutputs.GetOpaqueDirectory(outputDirectory));
            var processB = SchedulePipBuilder(builderB);

            // In this 1st build, process B will see the output directory outputDir consisting of
            // outputDir\A, outputDir\B, and outputDirC.
            RunScheduler().AssertSuccess();

            var result = RunScheduler();

            if (Configuration.Schedule.IncrementalScheduling)
            {
                result.AssertNotScheduled(processA.Process.PipId, processB.Process.PipId);
            }
            else
            {
                // In the 2nd build, since process A comes from the cache, outputDir will only consist of
                // outputDir\A and outputDir\B. Thus, in principle process B should re-run. However, due to
                // graph file system enumeration, process B gets a cache hit.
                RunScheduler().AssertCacheHit(processA.Process.PipId, processB.Process.PipId);
            }
        }
    }
}
