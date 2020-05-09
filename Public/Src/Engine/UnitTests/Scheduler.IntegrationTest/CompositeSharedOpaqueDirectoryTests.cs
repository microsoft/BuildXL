// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Scheduler
{
    [Trait("Category", "SharedOpaqueDirectoryTests")]
    [Feature(Features.SharedOpaqueDirectory)]
    public class CompositeSharedOpaqueDirectoryTests : SchedulerIntegrationTestBase
    {
        public CompositeSharedOpaqueDirectoryTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void CompositeSharedOpaqueDirectoryConsumptionCachingBehavior()
        {
            CreatePip(
                @"root\pipA",
                out FileArtifact inputA,
                out FileArtifact outputA,
                out ProcessWithOutputs pipA,
                out DirectoryArtifact soA);

            CreatePip(
                @"root\pipB",
                out FileArtifact inputB,
                out FileArtifact outputB,
                out ProcessWithOutputs pipB,
                out DirectoryArtifact soB);

            var root = AbsolutePath.Create(Context.PathTable, Path.Combine(ObjectRoot, "root"));

            // We create a composite shared opaque containing both shared opaques
            var result = PipConstructionHelper.TryComposeSharedOpaqueDirectory(root, new[] { soA, soB }, contentFilter: null, description: null, tags: new string[] { }, out var composedOpaque);
            XAssert.IsTrue(result);

            // PipC consumes the composed shared opaque and reads from both outputs
            var builder = CreatePipBuilder(new List<Operation>
                             {
                                 Operation.ReadFile(outputA, doNotInfer: true),
                                 Operation.ReadFile(outputB, doNotInfer: true),
                                 Operation.WriteFile(CreateOutputFileArtifact())
                             });
            builder.AddInputDirectory(composedOpaque);
            var pipC = SchedulePipBuilder(builder);

            // First time all should miss, second time all should hit
            RunScheduler().AssertCacheMiss(pipA.Process.PipId, pipB.Process.PipId, pipC.Process.PipId).AssertSuccess();
            RunScheduler().AssertCacheHit(pipA.Process.PipId, pipB.Process.PipId, pipC.Process.PipId);

            // Modify inputA and make sure that only pipA and PipC re-run
            File.WriteAllText(ArtifactToString(inputA), "New content");
            RunScheduler().AssertCacheMiss(pipA.Process.PipId, pipC.Process.PipId).AssertCacheHit(pipB.Process.PipId);
            RunScheduler().AssertCacheHit(pipA.Process.PipId, pipB.Process.PipId, pipC.Process.PipId);
        }

        [Fact]
        public void CompositeSharedOpaqueDirectoryReadBehavior()
        {
            CreatePip(
                @"root\pipA",
                out FileArtifact _,
                out FileArtifact outputA,
                out ProcessWithOutputs _,
                out DirectoryArtifact soA);

            CreatePip(
                @"root\pipB",
                out FileArtifact _,
                out FileArtifact outputB,
                out ProcessWithOutputs _,
                out DirectoryArtifact soB);

            var root = AbsolutePath.Create(Context.PathTable, Path.Combine(ObjectRoot, "root"));

            // We construct a composite shared opaque that only contains soa (and *not* sob)
            var result = PipConstructionHelper.TryComposeSharedOpaqueDirectory(root, new[] { soA }, contentFilter: null, description: null, tags: new string[] { }, out var composedOpaque);
            XAssert.IsTrue(result);

            // PipC consumes the composed shared opaque and reads from both outputs
            var builder = CreatePipBuilder(new List<Operation>
                             {
                                 Operation.ReadFile(outputA, doNotInfer: true),
                                 Operation.ReadFile(outputB, doNotInfer: true), // this one should be disallowed
                                 Operation.WriteFile(CreateOutputFileArtifact())
                             });
            builder.AddInputDirectory(composedOpaque);
            var pipC = SchedulePipBuilder(builder);

            IgnoreWarnings();
            // pipC should not be allowed to read from outputB, since the composed shared opaque does not contain it
            RunScheduler().AssertFailure();
            AssertErrorEventLogged(LogEventId.FileMonitoringError);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CompositeSharedOpaqueDirectoryWithFilterReadBehavior(bool validFileAccess)
        {
            var includeKind = global::BuildXL.Pips.Operations.SealDirectoryContentFilter.ContentFilterKind.Include;
            
            CreatePip(
                @"root\pipA",
                out FileArtifact _,
                out FileArtifact outputA,
                out ProcessWithOutputs pipA,
                out DirectoryArtifact soA);

            CreatePip(
                @"root\pipB",
                out FileArtifact _,
                out FileArtifact outputB,
                out ProcessWithOutputs _,
                out DirectoryArtifact soB);

            var root = AbsolutePath.Create(Context.PathTable, Path.Combine(ObjectRoot, "root"));

            // We construct a composite shared opaque using both soA and soB, but apply the filter that matches only soA
            var filter = $".*{outputA.Path.GetName(Context.PathTable).ToString(Context.StringTable)}$";
            var result = PipConstructionHelper.TryComposeSharedOpaqueDirectory(root, new[] { soA, soB }, contentFilter: new SealDirectoryContentFilter(includeKind, filter), description: null, tags: new string[] { }, out var composedOpaque);
            XAssert.IsTrue(result);

            // PipC consumes the composed shared opaque and reads a file
            //   validFileAccess == true  => outputA => allowed access
            //   validFileAccess == false => outputB => DFA (outputB does not match the filter, hence it's not a part of a composite SOD)
            var builder = CreatePipBuilder(new List<Operation>
            {
                Operation.ReadFile(validFileAccess ? outputA : outputB, doNotInfer: true),
                Operation.WriteFile(CreateOutputFileArtifact())
            });
            builder.AddInputDirectory(composedOpaque);
            var pipC = SchedulePipBuilder(builder);

            if (validFileAccess)
            {
                RunScheduler().AssertSuccess();
            }
            else
            {
                IgnoreWarnings();
                RunScheduler().AssertFailure();
                AssertErrorEventLogged(LogEventId.FileMonitoringError);
            }
        }

        /// <summary>
        /// Pip consumes a filtered SOD (fileA, fileB) and reads both files
        /// Filter is changed -> SOD (fileA)
        /// Pip should be a miss and should emit DFA
        /// </summary>
        public void FilterChangesShouldAffectCachingIfPipReadsFilteredFile()
        {
            var includeKind = global::BuildXL.Pips.Operations.SealDirectoryContentFilter.ContentFilterKind.Include;

            var sharedOpaqueDir = Path.Combine(ObjectRoot, "sod");
            var sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            var sharedOpaqueDirArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(sharedOpaqueDirPath);

            var fileA = CreateOutputFileArtifact(sharedOpaqueDir);
            var fileB = CreateOutputFileArtifact(sharedOpaqueDir);

            var builderA = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(fileA, doNotInfer: true),
                Operation.WriteFile(fileB, doNotInfer: true)                
            });
            builderA.AddOutputDirectory(sharedOpaqueDirArtifact, SealDirectoryKind.SharedOpaque);
            var pipA = SchedulePipBuilder(builderA);

            var producedSod = pipA.ProcessOutputs.GetOpaqueDirectory(sharedOpaqueDirPath);           
            var success = PipConstructionHelper.TryComposeSharedOpaqueDirectory(sharedOpaqueDirPath, new[] { producedSod }, contentFilter: new SealDirectoryContentFilter(includeKind, ".*"), description: null, tags: new string[] { }, out var filteredOpaque);
            XAssert.IsTrue(success);

            var builderB = CreateOpaqueDirectoryConsumer(CreateOutputFileArtifact(), null, filteredOpaque, fileA, fileB);
            var pipB = SchedulePipBuilder(builderB);

            // first run - populate cache
            RunScheduler().AssertSuccess();

            // reset the graph and this time specify a filter that would exclude fileB
            ResetPipGraphBuilder();

            pipA = SchedulePipBuilder(builderA);

            producedSod = pipA.ProcessOutputs.GetOpaqueDirectory(sharedOpaqueDirPath);
            var filter = $".*{fileA.Path.GetName(Context.PathTable).ToString(Context.StringTable)}$";
            success = PipConstructionHelper.TryComposeSharedOpaqueDirectory(sharedOpaqueDirPath, new[] { producedSod }, contentFilter: new SealDirectoryContentFilter(includeKind, filter), description: null, tags: new string[] { }, out filteredOpaque);
            XAssert.IsTrue(success);
            
            builderB = CreateOpaqueDirectoryConsumer(CreateOutputFileArtifact(), null, filteredOpaque, fileA, fileB);
            pipB = SchedulePipBuilder(builderB);

            // since filteredOpaque does not contain fileB, this run should result in a DFA
            IgnoreWarnings();
            var result = RunScheduler().AssertFailure();
            AssertErrorEventLogged(LogEventId.FileMonitoringError);

            result
                // pipA should be a hit
                .AssertCacheHitWithoutAssertingSuccess(pipA.Process.PipId)
                // pipB should be a miss (access to fileB is no longer valid)
                .AssertCacheMissWithoutAssertingSuccess(pipB.Process.PipId)
                // pipB should have a weak FP hit (file accesses are checked during strong FP computation)
                .AssertNumWeakFingerprintMisses(0);            
        }


        [Fact]
        public void DuplicateContentIsAllowedInCompositeSharedOpaqueDirectory()
        {
            CreatePip(
                @"root\pipA",
                out FileArtifact _,
                out FileArtifact outputA,
                out ProcessWithOutputs _,
                out DirectoryArtifact soA);

            var root = AbsolutePath.Create(Context.PathTable, Path.Combine(ObjectRoot, "root"));

            // We construct a composite shared opaque that constains soA twice
            var result = PipConstructionHelper.TryComposeSharedOpaqueDirectory(root, new[] { soA, soA }, contentFilter: null, description: null, tags: new string[] { }, out var composedOpaque);
            XAssert.IsTrue(result);

            // PipB consumes the composed shared opaque and reads from outputA
            var builder = CreatePipBuilder(new List<Operation>
                             {
                                 Operation.ReadFile(outputA, doNotInfer: true),
                                 Operation.WriteFile(CreateOutputFileArtifact())
                             });
            builder.AddInputDirectory(composedOpaque);
            var pipB = SchedulePipBuilder(builder);

            IgnoreWarnings();
            
            RunScheduler().AssertSuccess();
        }

        [Fact]
        public void CompositeContentIsProperlyMaterialized()
        {
            // This test verifies that BXL properly materializes composite shared opaques
            // (i.e., the files are materialized because we need to materialize the composite opaque
            // and not because we somehow materialized the original shared opaque directory).

            var root = Path.Combine(ObjectRoot, "root");
            var rootPath = AbsolutePath.Create(Context.PathTable, root);
            var sharedOpaqueDir = Path.Combine(ObjectRoot, "root", "CreateOutputFileArtifact");
            var sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            var inputB = CreateSourceFile();
            var outputA = CreateOutputFileArtifact(sharedOpaqueDir);

            // PipA - produces a dynamic file under /root/CreateOutputFileArtifact
            var builderA = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(outputA, doNotInfer: true)
            });
            
            builderA.AddOutputDirectory(DirectoryArtifact.CreateWithZeroPartialSealId(sharedOpaqueDirPath), SealDirectoryKind.SharedOpaque);
            builderA.AddTags(Context.StringTable, "pipA");

            var pipA = SchedulePipBuilder(builderA);

            // composite shared opaque - consists of a single shared opaque produced by PipA
            var success = PipConstructionHelper.TryComposeSharedOpaqueDirectory(rootPath, new[] { pipA.ProcessOutputs.GetOpaqueDirectory(sharedOpaqueDirPath) }, contentFilter: null, description: null, tags: new string[] { }, out var composedOpaque);
            XAssert.IsTrue(success);

            // PipB - consumes composite shared opaque directory
            // note: there is no direct dependency on PipA
            var builderB = CreatePipBuilder(new Operation[]
            {
                Operation.ReadRequiredFile(outputA, doNotInfer: true),  // dynamic output of PipA
                Operation.ReadFile(inputB),                             // dummy input, so we can force pipB to re-run
                Operation.WriteFile(CreateOutputFileArtifact())
            });

            builderB.AddInputDirectory(composedOpaque);
            builderB.AddTags(Context.StringTable, "pipB");
            
            var pipB = SchedulePipBuilder(builderB);

            // bring content into the cache
            RunScheduler().AssertCacheMiss(pipA.Process.PipId, pipB.Process.PipId);

            // Lazy materialization and tags are used here to prevent materialization of opaque directory produced by pipA,
            // so the only place where 'outputA' might come from is materialization of 'composedOpaque'.
            Configuration.Schedule.EnableLazyOutputMaterialization = true;
            Configuration.Filter = "tag='pipB'";

            // make sure that we start with no files materialized
            Directory.Delete(root, recursive: true);

            // force pipB to re-run
            File.AppendAllText(ArtifactToString(inputB), "foo");
            
            RunScheduler().AssertSuccess().AssertCacheHit(pipA.Process.PipId).AssertCacheMiss(pipB.Process.PipId);
        }

        /// <summary>
        /// Creates a <paramref name="pip"/> that reads from <paramref name="input"/> and writes to <paramref name="output"/>.
        /// The output id written in a <paramref name="sharedOpaque"/> directory with root <paramref name="root"/>.
        /// </summary>
        private void CreatePip(string root, out FileArtifact input, out FileArtifact output, out ProcessWithOutputs pip, out DirectoryArtifact sharedOpaque)
        {
            string sharedOpaqueDir = Path.Combine(ObjectRoot, root);
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            output = CreateOutputFileArtifact(sharedOpaqueDir);
            input = CreateSourceFile();
            pip = CreateAndScheduleSharedOpaqueProducer(sharedOpaqueDir, fileToProduceStatically: FileArtifact.Invalid, input, new KeyValuePair<FileArtifact, string>(output, null));
            sharedOpaque = pip.ProcessOutputs.GetOutputDirectories().Single().Root;
        }
    }
}
