// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Native.IO;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Scheduler.Filter;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using BuildXL.Utilities.Configuration;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

using ProcessesLogEventId = BuildXL.Processes.Tracing.LogEventId;

namespace IntegrationTest.BuildXL.Scheduler.Containers
{
    [Trait("Category", "WindowsOSOnly")]
    public sealed class ContainerIntegrationTests : SchedulerIntegrationTestBase
    {
        public ContainerIntegrationTests(ITestOutputHelper output)
            : base(output) { }

        /// <summary>
        /// Runs two pips under Helium that incur in a double write in a a shared opaque. Tests the interaction between the isolation level and the double write policy regarding caching and filemon violations.
        /// TODO: the case for declared outputs and exclusive opaques is not working yet since the violation is caught at graph construction time. We'd need to relax those static checks as well.
        /// </summary>
        [TheoryIfSupported(requiresHeliumDriversAvailable: true)]
        // When all outputs are isolated and the policy allows for it, both pips get cached and the double write is just a warning
        [InlineData(ContainerIsolationLevel.IsolateAllOutputs, DoubleWritePolicy.UnsafeFirstDoubleWriteWins, true, false)]
        // When all outputs are isolated and the policy does not allow for it, the violator does not get cached and the double write is an error
        [InlineData(ContainerIsolationLevel.IsolateAllOutputs, DoubleWritePolicy.DoubleWritesAreErrors, false, true)]
        // Since double writes occur in a shared opaque, isolating exclusive opaques results in the violator being uncacheable. Still the violation is reported as a warning due to the specified policy
        [InlineData(ContainerIsolationLevel.IsolateExclusiveOpaqueOutputDirectories, DoubleWritePolicy.UnsafeFirstDoubleWriteWins, false, false)]
        public void DoubleWriteMakesPipCacheableWhenOutputsAreIsolated(ContainerIsolationLevel containerIsolationLevel, DoubleWritePolicy doubleWritePolicy, bool expectCacheHit, bool expectViolationIsError)
        {
            string sharedOpaqueDir = Path.Combine(ObjectRoot, "sharedopaquedir");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);

            FileArtifact doubleWriteArtifact = CreateOutputFileArtifact(sharedOpaqueDir);

            ScheduleDoubleWriteProducers(
                sharedOpaqueDirPath,
                doubleWriteArtifact,
                containerIsolationLevel, 
                doubleWritePolicy, 
                out ProcessWithOutputs firstProducer, 
                out ProcessWithOutputs secondProducer);

            var firstRunResult = RunScheduler();

            if (!expectViolationIsError)
            {
                firstRunResult.AssertSuccess();

                // Run a second time so we can check the caching behavior
                var result = RunScheduler().AssertSuccess();

                if (expectCacheHit)
                {
                    // In this case, both should be a hit
                    result.AssertCacheHit(firstProducer.Process.PipId);
                    result.AssertCacheHit(secondProducer.Process.PipId);
                }
                else
                {
                    // In this case, the second one should be a miss
                    result.AssertCacheHit(firstProducer.Process.PipId);
                    result.AssertCacheMiss(secondProducer.Process.PipId);

                    AssertWarningEventLogged(EventId.ProcessNotStoredToCacheDueToFileMonitoringViolations, 2);
                }

                // We are expecting a double write as a verbose message (twice, one for each run)
                AssertVerboseEventLogged(LogEventId.DependencyViolationDoubleWrite, 2);
            }

            // The violation is either an error or a warning depending on expectations
            if (expectViolationIsError)
            {
                AssertErrorEventLogged(EventId.FileMonitoringError);
                AssertErrorEventLogged(ProcessesLogEventId.DisallowedDoubleWriteOnMerge);
            }
            else
            {
                AssertWarningEventLogged(EventId.FileMonitoringWarning, 2);
            }
        }

        [FactIfSupported(requiresHeliumDriversAvailable: true)]
        public void AllowedDoubleWriteCachesTheRightContent()
        {
            string sharedOpaqueDir = Path.Combine(ObjectRoot, "sharedopaquedir");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);

            FileArtifact doubleWriteArtifact = CreateOutputFileArtifact(sharedOpaqueDir);

            var firstProducerBuilder = CreateFileInSharedOpaqueBuilder(ContainerIsolationLevel.IsolateAllOutputs, DoubleWritePolicy.UnsafeFirstDoubleWriteWins, doubleWriteArtifact, "first", sharedOpaqueDirPath);
            var secondProducerBuilder = CreateFileInSharedOpaqueBuilder(ContainerIsolationLevel.IsolateAllOutputs, DoubleWritePolicy.UnsafeFirstDoubleWriteWins, doubleWriteArtifact, "second", sharedOpaqueDirPath);
            var firstProducer = SchedulePipBuilder(firstProducerBuilder);
            var secondProducer = SchedulePipBuilder(secondProducerBuilder);

            // Given the policy and isolation level, both producers should get cached
            RunScheduler().AssertSuccess();
            AssertWarningEventLogged(EventId.FileMonitoringWarning);

            string doubleWritePath = doubleWriteArtifact.Path.ToString(Context.PathTable);

            // Run the first producer alone. It should be a cache hit, and the content of the produced
            // file should correspond to the first producer

            RootFilter filter = new RootFilter(new PipIdFilter(firstProducer.Process.SemiStableHash));
            var result = RunScheduler(filter: filter).AssertSuccess();
            result.AssertCacheHit(firstProducer.Process.PipId);
            XAssert.IsTrue(File.Exists(doubleWritePath));
            XAssert.Equals("first", File.ReadAllText(doubleWritePath));

            // Same procedure with the second producer
            filter = new RootFilter(new PipIdFilter(secondProducer.Process.SemiStableHash));
            result = RunScheduler(filter: filter).AssertSuccess();
            result.AssertCacheHit(secondProducer.Process.PipId);

            XAssert.IsTrue(File.Exists(doubleWritePath));
            XAssert.Equals("second", File.ReadAllText(doubleWritePath));
        }

        [TheoryIfSupported(requiresHeliumDriversAvailable: true)]
        [InlineData(SealDirectoryKind.Opaque)]
        [InlineData(SealDirectoryKind.SharedOpaque)]
        public void NestedOutputsInOpaqueAreMergedProperly(SealDirectoryKind sealDirectoryKind)
        {
            string opaqueDir = Path.Combine(ObjectRoot, "opaqueDir");
            AbsolutePath opaqueDirPath = AbsolutePath.Create(Context.PathTable, opaqueDir);

            var nestedFileArtifactInOpaque = CreateOutputFileArtifact(opaqueDir, @"nested\");

            IEnumerable<Operation> producerWrites =
                new Operation[]
                {
                    Operation.CreateDir(new DirectoryArtifact(nestedFileArtifactInOpaque.Path.GetParent(Context.PathTable), 0, isSharedOpaque: false)),
                    Operation.WriteFile(nestedFileArtifactInOpaque, doNotInfer: true),
                };

            var producerBuilder = CreatePipBuilder(producerWrites);
            producerBuilder.AddOutputDirectory(opaqueDirPath, sealDirectoryKind);
            producerBuilder.Options |= Process.Options.NeedsToRunInContainer;
            producerBuilder.ContainerIsolationLevel = ContainerIsolationLevel.IsolateAllOutputs;
            SchedulePipBuilder(producerBuilder);

            RunScheduler().AssertSuccess();

            XAssert.IsTrue(File.Exists(nestedFileArtifactInOpaque.Path.ToString(Context.PathTable)));
        }

        [FactIfSupported(requiresHeliumDriversAvailable: true)]
        public void RedirectedFileInSharedOpaqueHasRighTimestamp()
        {
            string opaqueDir = Path.Combine(ObjectRoot, "opaqueDir");
            AbsolutePath opaqueDirPath = AbsolutePath.Create(Context.PathTable, opaqueDir);

            var fileArtifactInOpaque = CreateOutputFileArtifact(opaqueDir);

            var builder = CreateFileInSharedOpaqueBuilder(ContainerIsolationLevel.IsolateSharedOpaqueOutputDirectories, DoubleWritePolicy.DoubleWritesAreErrors, fileArtifactInOpaque, "foo", opaqueDirPath);
            SchedulePipBuilder(builder);
            RunScheduler().AssertSuccess();

            // The file must exist with the right timestamp
            string fileArtifactInOpaquePath = fileArtifactInOpaque.Path.ToString(Context.PathTable);
            XAssert.IsTrue(File.Exists(fileArtifactInOpaquePath));
            XAssert.AreEqual(WellKnownTimestamps.OutputInSharedOpaqueTimestamp, FileUtilities.GetFileTimestamps(fileArtifactInOpaquePath).CreationTime);
        }

        [TheoryIfSupported(requiresHeliumDriversAvailable: true)]
        [InlineData(SealDirectoryKind.Opaque)]
        [InlineData(SealDirectoryKind.SharedOpaque)]
        public void TombstoneFileInOpaqueIsNotHardlinked(SealDirectoryKind sealDirectoryKind)
        {
            string opaqueDir = Path.Combine(ObjectRoot, "opaqueDir");
            AbsolutePath opaqueDirPath = AbsolutePath.Create(Context.PathTable, opaqueDir);

            var originalFile = CreateOutputFileArtifact(opaqueDir);
            var renamedFile = CreateOutputFileArtifact(opaqueDir);

            // We create a file and then move it. The move operation will leave a tombstone file when
            // running under Helium
            IEnumerable<Operation> operations =
                new Operation[]
                {
                    Operation.WriteFile(originalFile, doNotInfer: true),
                    Operation.MoveFile(originalFile, renamedFile, doNotInfer: true),
                };

            var producerBuilder = CreatePipBuilder(operations);
            producerBuilder.AddOutputDirectory(opaqueDirPath, sealDirectoryKind);
            producerBuilder.Options |= Process.Options.NeedsToRunInContainer;
            producerBuilder.ContainerIsolationLevel = ContainerIsolationLevel.IsolateAllOutputs;
            var result = SchedulePipBuilder(producerBuilder);

            RunScheduler().AssertSuccess();

            // In the original locations, the renamed file should be there, the original file shouldn't
            XAssert.IsTrue(File.Exists(renamedFile.Path.ToString(Context.PathTable)));
            XAssert.IsFalse(File.Exists(originalFile.Path.ToString(Context.PathTable)));
        }

        [FactIfSupported(requiresHeliumDriversAvailable: true)]
        public void DeclaredTombstoneFileIsNotHardlinked()
        {
            var originalFile = CreateSourceFile(ObjectRoot);
            var renamedFile = CreateSourceFile(ObjectRoot);

            // We create a file and then move it. The move operation will leave a tombstone file when
            // running under Helium
            IEnumerable<Operation> operations =
                new Operation[]
                {
                    Operation.WriteFile(originalFile, doNotInfer: true),
                    Operation.MoveFile(originalFile, renamedFile, doNotInfer: true),
                };

            var producerBuilder = CreatePipBuilder(operations);
            producerBuilder.Options |= Process.Options.NeedsToRunInContainer;
            producerBuilder.ContainerIsolationLevel = ContainerIsolationLevel.IsolateAllOutputs;
            producerBuilder.AddOutputFile(originalFile, FileExistence.Optional);
            producerBuilder.AddOutputFile(renamedFile, FileExistence.Required);

            var result = SchedulePipBuilder(producerBuilder);

            RunScheduler().AssertSuccess();

            // In the original locations, the renamed file should be there, the original file shouldn't
            XAssert.IsTrue(File.Exists(renamedFile.Path.ToString(Context.PathTable)));
            XAssert.IsFalse(File.Exists(originalFile.Path.ToString(Context.PathTable)));
        }

        private void ScheduleDoubleWriteProducers(
            AbsolutePath sharedOpaqueDirPath,
            FileArtifact doubleWriteArtifact,
            ContainerIsolationLevel containerIsolationLevel, 
            DoubleWritePolicy doubleWritePolicy, 
            out ProcessWithOutputs firstProducer, 
            out ProcessWithOutputs secondProducer)
        {
            var firstProducerBuilder = CreateFileInSharedOpaqueBuilder(containerIsolationLevel, doubleWritePolicy, doubleWriteArtifact, "first", sharedOpaqueDirPath);

            firstProducer = SchedulePipBuilder(firstProducerBuilder);

            var secondProducerBuilder = CreateFileInSharedOpaqueBuilder(containerIsolationLevel, doubleWritePolicy, doubleWriteArtifact, "second", sharedOpaqueDirPath);
            // Let's order this so who is the violator is deterministic
            secondProducerBuilder.AddInputDirectory(firstProducer.Process.DirectoryOutputs.First());

            secondProducer = SchedulePipBuilder(secondProducerBuilder);
        }

        private ProcessBuilder CreateFileInSharedOpaqueBuilder(ContainerIsolationLevel containerIsolationLevel, DoubleWritePolicy doubleWritePolicy, FileArtifact writeArtifact, string writeContent, AbsolutePath sharedOpaqueDirPath)
        {
            ProcessBuilder producerBuilder;
            IEnumerable<Operation> producerWrites =
                new Operation[]
                {
                    Operation.CreateDir(new DirectoryArtifact(writeArtifact.Path.GetParent(Context.PathTable), 0, isSharedOpaque: false)),
                    Operation.WriteFile(writeArtifact, writeContent, doNotInfer: true),
                    Operation.WriteFile(CreateOutputFileArtifact()), // so each builder is unique
                };

            producerBuilder = CreatePipBuilder(producerWrites);
            producerBuilder.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            producerBuilder.Options |= Process.Options.NeedsToRunInContainer;
            producerBuilder.ContainerIsolationLevel = containerIsolationLevel;
            producerBuilder.DoubleWritePolicy = doubleWritePolicy;
            return producerBuilder;
        }
    }
}
