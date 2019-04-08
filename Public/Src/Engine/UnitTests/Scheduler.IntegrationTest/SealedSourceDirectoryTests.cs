// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
    [Feature(Features.SealedSourceDirectory)]
    public class SealedSourceDirectoryTests : SchedulerIntegrationTestBase
    {
        public SealedSourceDirectoryTests(ITestOutputHelper output) : base(output)
        {
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ValidateCachingBehaviorTopOnlySealedSourceDirectory(bool probeOnly)
        {
            ValidateCachingBehavior(topOnly: true, probeOnly: probeOnly);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ValidateCachingBehaviorNestedSealedSourceDirectory(bool probeOnly)
        {
            ValidateCachingBehavior(topOnly: false, probeOnly:probeOnly);
        }

        [Fact]
        public void ConsumeNestedFileUnderTopOnlyMount()
        {
            var source = CreateSourceFile(Path.Combine(SourceRoot, "nested", "someFile.txt"));
            WriteSourceFile(source);
            var output = CreateOutputFileArtifact(ObjectRoot);

            DirectoryArtifact dir = SealDirectory(SourceRootPath, SealDirectoryKind.SourceTopDirectoryOnly);

            var builder = CreatePipBuilder(new Operation[]
                {
                    Operation.ReadFile(source, doNotInfer: true),
                    Operation.WriteFile(output),
                });
            builder.AddInputDirectory(dir);
            SchedulePipBuilder(builder);

            RunScheduler().AssertFailure();
            AssertVerboseEventLogged(EventId.DisallowedFileAccessInTopOnlySourceSealedDirectory);
            AssertVerboseEventLogged(EventId.PipProcessDisallowedFileAccess);
            AssertVerboseEventLogged(LogEventId.DependencyViolationMissingSourceDependency);
            AssertWarningEventLogged(EventId.ProcessNotStoredToCacheDueToFileMonitoringViolations);
            AssertErrorEventLogged(EventId.FileMonitoringError);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ConsumeFilterPassingFile(bool topOnly)
        {
            FileArtifact source;
            if (topOnly)
            {
                source = FileArtifact.CreateSourceFile(SourceRootPath.Combine(Context.PathTable, "file.txt"));
            }
            else
            {
                var nestedDir = AbsolutePath.Create(Context.PathTable, Path.Combine(SourceRoot, "nested"));
                source = FileArtifact.CreateSourceFile(nestedDir.Combine(Context.PathTable, "file.txt"));
            }

            WriteSourceFile(source);
            var output = CreateOutputFileArtifact(ObjectRoot);

            SealDirectory sealedDirectory = CreateSourceSealDirectory(SourceRootPath, topOnly ? SealDirectoryKind.SourceTopDirectoryOnly : SealDirectoryKind.SourceAllDirectories, "*.txt", "*.cs");
            DirectoryArtifact dir = PipGraphBuilder.AddSealDirectory(sealedDirectory);

            var builder = CreatePipBuilder(new Operation[]
                {
                    Operation.ReadFile(source, doNotInfer: true),
                    Operation.WriteFile(output),
                });
            builder.AddInputDirectory(dir);
            SchedulePipBuilder(builder);

            RunScheduler().AssertSuccess();
        }

        [Fact]
        public void ConsumeFilterNotPassingFile()
        {
            FileArtifact source = FileArtifact.CreateSourceFile(SourceRootPath.Combine(Context.PathTable, "file.txt"));
            WriteSourceFile(source);
            var output = CreateOutputFileArtifact(ObjectRoot);

            SealDirectory sealedDirectory = CreateSourceSealDirectory(SourceRootPath, SealDirectoryKind.SourceTopDirectoryOnly, "*.cs");
            DirectoryArtifact dir = PipGraphBuilder.AddSealDirectory(sealedDirectory);

            var builder = CreatePipBuilder(new Operation[]
                {
                    Operation.ReadFile(source, doNotInfer: true),
                    Operation.WriteFile(output),
                });
            builder.AddInputDirectory(dir);
            SchedulePipBuilder(builder);

            RunScheduler().AssertFailure();
            AssertVerboseEventLogged(EventId.PipProcessDisallowedFileAccess);
            AssertVerboseEventLogged(LogEventId.DependencyViolationMissingSourceDependency);
            AssertWarningEventLogged(EventId.ProcessNotStoredToCacheDueToFileMonitoringViolations);
            AssertErrorEventLogged(EventId.FileMonitoringError);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void InputOverlappingSealedSourceDirectory(bool probeOnly)
        {
            ValidateCachingBehavior(topOnly: false, probeOnly: probeOnly, alsoDeclareAsInput: true);
        }

        [Feature(Features.AbsentFileProbe)]
        [Fact]
        public void ProbeWithinSealedSourceDirectory()
        {
            var output = CreateOutputFileArtifact(ObjectRoot);
            var input = CreateSourceFile(SourceRoot);
            var absentPath = FileArtifact.CreateSourceFile(CreateUniqueSourcePath(SourceRoot));
            DirectoryArtifact dir = SealDirectory(SourceRootPath, SealDirectoryKind.SourceAllDirectories);

            var builder = CreatePipBuilder(new Operation[]
                {
                    Operation.ReadFile(input),
                    Operation.ReadFile(absentPath, doNotInfer: true),
                    Operation.WriteFile(output)
                });
            builder.AddInputDirectory(dir);
            Process p = SchedulePipBuilder(builder).Process;

            RunScheduler().AssertSuccess();
            RunScheduler().AssertCacheHit(p.PipId);

            // Make the absent path exist and make sure it is cached appropriately
            WriteSourceFile(absentPath);
            RunScheduler().AssertFailure();
            AssertVerboseEventLogged(EventId.PipProcessDisallowedFileAccess);
            AssertVerboseEventLogged(LogEventId.DependencyViolationMissingSourceDependency);
            AssertWarningEventLogged(EventId.ProcessNotStoredToCacheDueToFileMonitoringViolations);
            AssertErrorEventLogged(EventId.FileMonitoringError);
        }

        [Fact]
        public void TestSourceDirectoryUsedAsInputFails()
        {
            var sourceDirectory = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueSourcePath("src-dir"));
            var builder = CreatePipBuilder(new Operation[]
                {
                    Operation.WriteFile(CreateOutputFileArtifact())
                });
            builder.ArgumentsBuilder.Add(sourceDirectory);
            builder.AddInputDirectory(sourceDirectory);
            Assert.Throws<BuildXLTestException>(() => SchedulePipBuilder(builder));
            AssertErrorEventLogged(EventId.SourceDirectoryUsedAsDependency);
        }

        /// <param name="topOnly">When true, the SourceSealedDirectory will be a topOnly. When false, it will be a recursive SourceSealedDirectory</param>
        /// <param name="probeOnly">When true, the source file will be probed without reading</param>
        /// <param name="alsoDeclareAsInput">When true, the file accessed under the SourceSealDirectory will also be declared as an input</param>
        public void ValidateCachingBehavior(bool topOnly, bool probeOnly, bool alsoDeclareAsInput = false)
        {
            // Create a very simple graph with a process that consumes files from a source sealed directory
            var source1 = topOnly ? CreateSourceFile(SourceRoot) : CreateSourceFile(Path.Combine(SourceRoot, "nested"));
            WriteSourceFile(source1);

            // Create a graph with a SealedSourceDirectory
            DirectoryArtifact dir = SealDirectory(SourceRootPath, topOnly ? SealDirectoryKind.SourceTopDirectoryOnly : SealDirectoryKind.SourceAllDirectories);

            // Set up test process
            var builder = CreatePipBuilder(new Operation[]
                {
                    Operation.WriteFile(CreateOutputFileArtifact(ObjectRoot)),
                    // don't take dependencies on files in SealedSourceDirectory
                    probeOnly ? Operation.Probe(source1, doNotInfer: !alsoDeclareAsInput) :  Operation.ReadFile(source1, doNotInfer: !alsoDeclareAsInput)
                });
            builder.AddInputDirectory(dir);
            Process process = SchedulePipBuilder(builder).Process;

            // Perform builds:
            RunScheduler().AssertCacheMiss(process.PipId);
            RunScheduler().AssertCacheHit(process.PipId);

            // Modify an input file and make sure there's a cache miss
            WriteSourceFile(source1);

            if (probeOnly && !alsoDeclareAsInput)
            {
                // Files probes that are not declared as input do not cause cache misses when the content changes.
                RunScheduler().AssertCacheHit(process.PipId);
            }
            else
            {
                RunScheduler().AssertCacheMiss(process.PipId);
            }

            RunScheduler().AssertCacheHit(process.PipId);
        }
    }
}
