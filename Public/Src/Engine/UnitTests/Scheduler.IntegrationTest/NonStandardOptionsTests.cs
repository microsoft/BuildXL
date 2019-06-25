// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using System.IO;
using Test.BuildXL.Scheduler;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using BuildXL.Storage;
using BuildXL.Native.IO;
using BuildXL.Pips.Builders;
using BuildXLConfiguration = BuildXL.Utilities.Configuration;
using StorageLogEventId = BuildXL.Storage.Tracing.LogEventId;

namespace IntegrationTest.BuildXL.Scheduler
{
    /// <summary>
    /// Tests for non-standard command line options that can be user-toggled
    /// </summary>
    [Feature(Features.NonStandardOptions)]
    [Trait("Category", "NonStandardOptionsTests")]
    public class NonStandardOptionsTests : SchedulerIntegrationTestBase
    {
        public NonStandardOptionsTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void ValidateCachingTreatDirectoryAsAbsentFileOnHashingInputContent()
        {
            Configuration.Schedule.TreatDirectoryAsAbsentFileOnHashingInputContent = true;

            // start with absent /dir
            DirectoryArtifact dir = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory());
            FileArtifact fileVersionOfDir = new FileArtifact(dir.Path);

            Process pip = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.Probe(fileVersionOfDir),
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            // /dir will be hashed as an absent file on every run, so the pip fingerprint remains the same

            // Create /dir
            Directory.CreateDirectory(ArtifactToString(dir));
            RunScheduler().AssertCacheHit(pip.PipId);

            // Create nested /dir/file
            CreateSourceFile(ArtifactToString(dir));
            RunScheduler().AssertCacheHit(pip.PipId);
        }

        [Fact]
        public void AllowEmptyFilterWithUnsafeForceSkipDeps_Bug1102785()
        {
            Configuration.Schedule.ForceSkipDependencies = BuildXLConfiguration.ForceSkipDependenciesMode.Always;
            CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact())
            });

            RunScheduler().AssertSuccess();
        }

        [Fact]
        public void ValidateUnsafeForceSkipDeps()
        {
            // Forces skipping dependencies of explicitly scheduled pips unless inputs are non-existent on filesystem
            Configuration.Schedule.ForceSkipDependencies = BuildXLConfiguration.ForceSkipDependenciesMode.Always;

            // pipA outputs /outA
            FileArtifact srcA = CreateSourceFile();
            FileArtifact outA = CreateOutputFileArtifact();
            var pipBuilderA = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(srcA), // used to trigger pip re-run
                Operation.WriteFile(outA)
            });
            pipBuilderA.AddTags(Context.StringTable, "pipA");
            var pipA = SchedulePipBuilder(pipBuilderA);

            // pipB consumes /outA, produces /outB
            // pipB also consumes result of WriteFile & CopyFile primitives
            AbsolutePath writeFileDestination = CreateOutputFileArtifact().Path;
            FileArtifact writtenFile = WriteFile(writeFileDestination, "asdf", WriteFileEncoding.Ascii);
            AbsolutePath copyFileDestination = CreateOutputFileArtifact().Path;
            FileArtifact copiedFile = CopyFile(outA, copyFileDestination);
            FileArtifact srcB = CreateSourceFile();
            FileArtifact outB = CreateOutputFileArtifact();
            var pipBuilderB = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(srcB), // used to trigger pip re-run
                Operation.ReadFile(outA),
                Operation.ReadFile(writtenFile),
                Operation.ReadFile(copiedFile),
                Operation.WriteFile(outB),
            });
            pipBuilderB.AddTags(Context.StringTable, "pipB");
            Process pipB = SchedulePipBuilder(pipBuilderB).Process;

            // pipC consumes /outB, produces /outC
            FileArtifact srcC = CreateSourceFile();
            FileArtifact outC = CreateOutputFileArtifact();
            var pipBuilderC = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(srcC),
                Operation.ReadFile(outB),
                Operation.WriteFile(outC)
            });
            pipBuilderC.AddTags(Context.StringTable, "pipC");
            Process pipC = SchedulePipBuilder(pipBuilderC).Process;

            RunScheduler().AssertCacheMiss(pipA.Process.PipId, pipB.PipId, pipC.PipId);
            RunScheduler().AssertCacheHit(pipA.Process.PipId, pipB.PipId, pipC.PipId);

            // Save output to compare to later
            string contentA = File.ReadAllText(ArtifactToString(outA));
            string contentB = File.ReadAllText(ArtifactToString(outB));
            string contentC = File.ReadAllText(ArtifactToString(outC));

            // Change filter to last pip in chain, pipC
            Configuration.Filter = "tag='pipC'";

            // Modify all src files, this normally triggers pips to re-run
            File.WriteAllText(ArtifactToString(srcA), "abc");
            File.WriteAllText(ArtifactToString(srcB), "abc");
            File.WriteAllText(ArtifactToString(srcC), "abc");

            ScheduleRunResult filterChangeResult = RunScheduler();

            // Save output to compare to later
            string filterChangeContentA = File.ReadAllText(ArtifactToString(outA));
            string filterChangeContentB = File.ReadAllText(ArtifactToString(outB));
            string filterChangeContentC = File.ReadAllText(ArtifactToString(outC));

            // Skip running pipA and pipB since ForceSkipDependencies is on and /outB still exists on disk
            XAssert.AreEqual(contentA, filterChangeContentA);
            XAssert.AreEqual(contentB, filterChangeContentB);

            // pipC matches the filter and will run, cache miss because /srcC changed
            XAssert.AreNotEqual(contentC, filterChangeContentC);
            filterChangeResult.AssertCacheMiss(pipC.PipId);

            // Delete /outB
            File.Delete(ArtifactToString(outB));
            // Also delete the WriteFile/CopyFile outputs to make sure they are appropriately materialized so B can run
            File.Delete(ArtifactToString(writtenFile));
            File.Delete(ArtifactToString(copiedFile));

            // Modify all src files, this normally triggers pips to re-run
            File.WriteAllText(ArtifactToString(srcA), "abc");
            File.WriteAllText(ArtifactToString(srcB), "abc");
            File.WriteAllText(ArtifactToString(srcC), "abc");

            ScheduleRunResult deleteOutBResult = RunScheduler();

            // Skip running pipA since ForceSkipDependencies is on and /outA still exists on disk
            XAssert.AreEqual(filterChangeContentA, File.ReadAllText(ArtifactToString(outA)));

            // pipB will be run since it produces pipC's input /outB
            XAssert.AreNotEqual(filterChangeContentB, File.ReadAllText(ArtifactToString(outB)));
            XAssert.AreNotEqual(filterChangeContentC, File.ReadAllText(ArtifactToString(outC)));
            deleteOutBResult.AssertCacheMiss(pipB.PipId, pipC.PipId);
        }

        [Fact]
        public void ValidateCachingUnsafeUnexpectedFileAccessesAreErrorsInput()
        {
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.UnexpectedFileAccessesAreErrors = false;
            
            FileArtifact unexpectedFile = CreateSourceFile();
            Process pip = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact()),
                Operation.ReadFile(unexpectedFile, doNotInfer: true)
            }).Process;

            RunScheduler().AssertCacheMiss(pip.PipId);

            // Pip allowed to run successfully, but will not be cached due to file monitoring violations
            RunScheduler().AssertCacheMiss(pip.PipId);

            AssertVerboseEventLogged(EventId.PipProcessDisallowedFileAccess, 2);
            AssertWarningEventLogged(EventId.ProcessNotStoredToCacheDueToFileMonitoringViolations, 4);
            AssertVerboseEventLogged(LogEventId.DependencyViolationMissingSourceDependency, 2);
            AssertWarningEventLogged(EventId.FileMonitoringWarning, 2);
        }

        [Fact]
        public void ValidateCachingUnsafeUnexpectedFileAccessesAreErrorsOutput()
        {
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.UnexpectedFileAccessesAreErrors = false;

            FileArtifact unexpectedFile = CreateOutputFileArtifact();
            Process pip = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact()),
                Operation.WriteFile(unexpectedFile, doNotInfer: true)
            }).Process;

            RunScheduler().AssertCacheMiss(pip.PipId);

            AssertVerboseEventLogged(EventId.PipProcessDisallowedFileAccess, count: 1, allowMore: OperatingSystemHelper.IsUnixOS);
            AssertVerboseEventLogged(LogEventId.DependencyViolationUndeclaredOutput);
            AssertWarningEventLogged(EventId.FileMonitoringWarning);
            AssertWarningEventLogged(EventId.ProcessNotStoredToCacheDueToFileMonitoringViolations, count: 2);

            // Pip allowed to run successfully, but will not be cached due to file monitoring violations
            RunScheduler().AssertCacheMiss(pip.PipId);

            AssertVerboseEventLogged(EventId.PipProcessDisallowedFileAccess, count: 1, allowMore: OperatingSystemHelper.IsUnixOS);
            AssertVerboseEventLogged(LogEventId.DependencyViolationUndeclaredOutput);
            AssertWarningEventLogged(EventId.FileMonitoringWarning);
            AssertWarningEventLogged(EventId.ProcessNotStoredToCacheDueToFileMonitoringViolations, count: 2);
        }

        [FactIfSupported(requiresJournalScan: true, Skip = "Bug #1517905")]
        public void UseJunctionRoots()
        {
            AbsolutePath targetPath = CreateUniqueDirectory(SourceRoot);
            AbsolutePath junctionPath = CreateUniqueDirectory(SourceRoot);
            string targetPathStr = targetPath.ToString(Context.PathTable);
            string junctionPathStr = junctionPath.ToString(Context.PathTable);

            // .......... Creating the Junction ..........
            FileUtilities.CreateJunction(junctionPath.ToString(Context.PathTable), targetPathStr);

            FileArtifact sourceFile = CreateSourceFile(junctionPathStr);

            Configuration.Engine.DirectoriesToTranslate.Add(
                new BuildXLConfiguration.TranslateDirectoryData(
                    targetPath.ToString(Context.PathTable) + @"\<" + junctionPath.ToString(Context.PathTable) + @"\", targetPath, junctionPath));

            DirectoryTranslator.AddTranslation(targetPath, junctionPath, Context.PathTable);

            var pipBuilder = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(sourceFile),
                Operation.WriteFile(CreateOutputFileArtifact())
            });

            SchedulePipBuilder(pipBuilder);
            RunScheduler().AssertSuccess();

            // Remove junction and recreate one with the same target
            AssertTrue(FileUtilities.TryRemoveDirectory(junctionPathStr, out var hr));
            Directory.CreateDirectory(junctionPathStr);
            FileUtilities.CreateJunction(junctionPath.ToString(Context.PathTable), targetPathStr);
            
            RunScheduler().AssertSuccess();
            AssertVerboseEventLogged(EventId.ValidateJunctionRoot);
            AssertVerboseEventLogged(StorageLogEventId.IgnoredRecordsDueToUnchangedJunctionRootCount);

            // Remove junction and recreate one with the same target
            AssertTrue(FileUtilities.TryRemoveDirectory(junctionPathStr, out var hr2));
            Directory.CreateDirectory(junctionPathStr);
            FileUtilities.CreateJunction(junctionPath.ToString(Context.PathTable), targetPathStr);

            RunScheduler().AssertSuccess();
            AssertVerboseEventLogged(EventId.ValidateJunctionRoot);
            AssertVerboseEventLogged(StorageLogEventId.IgnoredRecordsDueToUnchangedJunctionRootCount);
        }
    }
}
