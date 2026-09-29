// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using IntegrationTest.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Scheduler.DynamicGraph
{
    // These variants exercise the dynamic scheduler through the scheduler integration test harness.
    // Mixed classes are included below by overriding incompatible methods or theory rows with explicit skips.
    // Excluded scheduler integration test classes:
    // - AllowedFileRewriteTests: all scenarios require shared opaques and undeclared source reads.
    // - AllowedUndeclaredReadsTests: undeclared reads are not supported in dynamic graph mode.
    // - CasePreservingTests: its only scenario produces a shared opaque directory.
    // - CompositeSharedOpaqueDirectoryTests, SharedOpaqueDirectoryTests, UnsafeSharedOpaqueDirectoryTests,
    //   and LazySharedOpaqueOutputDeletionTests: shared opaque directories are not supported.
    // - DirectorySymlinkTests: its consumers enable undeclared source reads when running with the eBPF sandbox.
    // - FileChangeTrackerTests: exercises incremental scheduling state and is also sealed.
    // - FilesystemModeTests: covers RealAndPipGraph and undeclared real-filesystem observations, which are unsupported.
    // - MinimalGraphWithAlienFileTests: depends on undeclared alien-file reads.
    // - LazyMaterializationTests: its scenarios select pips using Configuration.Filter.
    // - PreserveOutputsTests and PreserveOutputsReuseOutputsTests: preserve-output state and reuse are not supported.
    // - TraceFileBuilderTests: trace generation requires whole-graph enumeration.
    // - IncrementalSchedulingTests, GraphChangesTests, and every *_IncrementalScheduling class: incremental
    //   scheduling is explicitly disabled in dynamic graph mode.
    //
    public class DynamicAllowlistTests : AllowlistTests
    {
        public DynamicAllowlistTests(ITestOutputHelper output) : base(output) => EnableDynamicGraphScheduler();
    }

    public class DynamicBaselineTests : BaselineTests
    {
        public DynamicBaselineTests(ITestOutputHelper output) : base(output) => EnableDynamicGraphScheduler();

        [Theory(Skip = "The inherited theory includes a shared opaque scenario, which is unsupported by the dynamic scheduler.")]
        public override void DirectoryWritesNotReportedAsObservations(bool underOpaque)
        {
            base.DirectoryWritesNotReportedAsObservations(underOpaque);
        }

        [Fact]
        public void DirectoryWritesNotReportedAsObservationsForRegularOutput()
        {
            base.DirectoryWritesNotReportedAsObservations(underOpaque: false);
        }

        [Theory(Skip = "The synthetic machine-performance hook accepts only the static Scheduler type.")]
        [InlineData(true)]
        [InlineData(false)]
        public override void StopSchedulerDueToLowPhysicalMemory(bool enableMemoryConservation)
        {
            base.StopSchedulerDueToLowPhysicalMemory(enableMemoryConservation);
        }

        [Fact(Skip = "The synthetic machine-performance hook accepts only the static Scheduler type.")]
        public override void StopSchedulerDueToLowCommitMemory()
        {
            base.StopSchedulerDueToLowCommitMemory();
        }

        [Theory(Skip = "The synthetic machine-performance hook accepts only the static Scheduler type.")]
        [InlineData(true)]
        [InlineData(false)]
        public override void RetryPipOnHighMemoryUsage(bool allowLowMemoryRetry)
        {
            base.RetryPipOnHighMemoryUsage(allowLowMemoryRetry);
        }

        [FactIfSupported(
            requiresWindowsBasedOperatingSystem: true,
            Skip = "The synthetic machine-performance hook accepts only the static Scheduler type.")]
        public override void SuspendResumePipOnHighMemoryUsage()
        {
            base.SuspendResumePipOnHighMemoryUsage();
        }

        [FactIfSupported(
            requiresWindowsBasedOperatingSystem: true,
            Skip = "The synthetic machine-performance hook accepts only the static Scheduler type.")]
        public override void SingleSuspendedPipIsCancelledUnderContinuousMemoryPressure()
        {
            base.SingleSuspendedPipIsCancelledUnderContinuousMemoryPressure();
        }

        [Fact(Skip = "The synthetic machine-performance hook accepts only the static Scheduler type.")]
        public override void SurvivingChildProcessesNotReportedOnCancelation()
        {
            base.SurvivingChildProcessesNotReportedOnCancelation();
        }

        [Theory(Skip = "The inherited theory includes RealAndPipGraph, which is unsupported by the dynamic scheduler.")]
        public override void FullGraphDirectoryEnumerationsExcludeUntrackedScopes(
            global::BuildXL.Utilities.Configuration.FileSystemMode fileSystemMode)
        {
            base.FullGraphDirectoryEnumerationsExcludeUntrackedScopes(fileSystemMode);
        }

        [Fact]
        public void DirectoryEnumerationsExcludeUntrackedScopesWithMinimalGraph()
        {
            base.FullGraphDirectoryEnumerationsExcludeUntrackedScopes(
                global::BuildXL.Utilities.Configuration.FileSystemMode.RealAndMinimalPipGraph);
        }
    }

    public class DynamicChangeAffectedInputTests : ChangeAffectedInputTests
    {
        public DynamicChangeAffectedInputTests(ITestOutputHelper output) : base(output) => EnableDynamicGraphScheduler();
    }

    public class DynamicDependencyViolationTests : DependencyViolationTests
    {
        public DynamicDependencyViolationTests(ITestOutputHelper output) : base(output) => EnableDynamicGraphScheduler();
    }

    public class DynamicDirectoryRenameTests : DirectoryRenameTests
    {
        public DynamicDirectoryRenameTests(ITestOutputHelper output) : base(output) => EnableDynamicGraphScheduler();

        [Fact(Skip = "Shared opaque directories are unsupported by the dynamic scheduler.")]
        public override void TestRenameDirectoryInsideSharedOpaqueDirectory()
        {
            base.TestRenameDirectoryInsideSharedOpaqueDirectory();
        }

        [Theory(Skip = "The inherited theory includes a shared opaque scenario, which is unsupported by the dynamic scheduler.")]
        public override void TestRenameUntrackedDirectoryToOpaqueDirectory(
            global::BuildXL.Pips.Operations.SealDirectoryKind dirKind)
        {
            base.TestRenameUntrackedDirectoryToOpaqueDirectory(dirKind);
        }

        [Fact]
        public void TestRenameUntrackedDirectoryToExclusiveOpaqueDirectory()
        {
            base.TestRenameUntrackedDirectoryToOpaqueDirectory(
                global::BuildXL.Pips.Operations.SealDirectoryKind.Opaque);
        }
    }

    public class DynamicDirectoryTranslationJunctionSubstTests : DirectoryTranslationJunctionSubstTests
    {
        public DynamicDirectoryTranslationJunctionSubstTests(ITestOutputHelper output) : base(output) => EnableDynamicGraphScheduler();
    }

    public class DynamicErrorRegexTests : ErrorRegexTests
    {
        public DynamicErrorRegexTests(ITestOutputHelper output) : base(output) => EnableDynamicGraphScheduler();
    }

    public class DynamicFileAccessPolicyTests : FileAccessPolicyTests
    {
        public DynamicFileAccessPolicyTests(ITestOutputHelper output) : base(output) => EnableDynamicGraphScheduler();
    }

    public class DynamicLinuxPermissionTests : LinuxPermissionTests
    {
        public DynamicLinuxPermissionTests(ITestOutputHelper output) : base(output) => EnableDynamicGraphScheduler();
    }

    public class DynamicMustRunOnOrchestratorTests : MustRunOnOrchestratorTests
    {
        public DynamicMustRunOnOrchestratorTests(ITestOutputHelper output) : base(output) => EnableDynamicGraphScheduler();
    }

    public class DynamicNonStandardOptionsTests : NonStandardOptionsTests
    {
        public DynamicNonStandardOptionsTests(ITestOutputHelper output) : base(output) => EnableDynamicGraphScheduler();

        [Fact(Skip = "Dynamic graph mode does not support pip filtering or force-skip-dependencies selection.")]
        public override void AllowEmptyFilterWithUnsafeForceSkipDeps_Bug1102785()
        {
            base.AllowEmptyFilterWithUnsafeForceSkipDeps_Bug1102785();
        }

        [Fact(Skip = "Dynamic graph mode does not support pip filtering or force-skip-dependencies selection.")]
        public override void ValidateUnsafeForceSkipDeps()
        {
            base.ValidateUnsafeForceSkipDeps();
        }
    }

    public class DynamicOpaqueDirectoryTests : OpaqueDirectoryTests
    {
        public DynamicOpaqueDirectoryTests(ITestOutputHelper output) : base(output) => EnableDynamicGraphScheduler();

        [Fact(Skip = "RealAndPipGraph requires a complete graph and is unsupported by the dynamic scheduler.")]
        public override void EnumerateOpaqueDirectory()
        {
            base.EnumerateOpaqueDirectory();
        }

        [Fact(Skip = "Dynamic graph mode does not support pip filtering.")]
        public override void ConsumeExplicitFileOutOfOpaque()
        {
            base.ConsumeExplicitFileOutOfOpaque();
        }

        [Theory(Skip = "The inherited theory includes shared opaque scenarios, which are unsupported by the dynamic scheduler.")]
        public override void AbsentPathProbeUnderOpaquesModeBehavior(
            global::BuildXL.Pips.Operations.Process.AbsentPathProbeInUndeclaredOpaquesMode absentPathProbeMode,
            global::BuildXL.Pips.Operations.SealDirectoryKind directoryKind)
        {
            base.AbsentPathProbeUnderOpaquesModeBehavior(absentPathProbeMode, directoryKind);
        }

        [Theory]
        [InlineData(global::BuildXL.Pips.Operations.Process.AbsentPathProbeInUndeclaredOpaquesMode.Strict)]
        [InlineData(global::BuildXL.Pips.Operations.Process.AbsentPathProbeInUndeclaredOpaquesMode.Relaxed)]
        [InlineData(global::BuildXL.Pips.Operations.Process.AbsentPathProbeInUndeclaredOpaquesMode.Unsafe)]
        public void AbsentPathProbeUnderExclusiveOpaqueModeBehavior(
            global::BuildXL.Pips.Operations.Process.AbsentPathProbeInUndeclaredOpaquesMode absentPathProbeMode)
        {
            base.AbsentPathProbeUnderOpaquesModeBehavior(
                absentPathProbeMode,
                global::BuildXL.Pips.Operations.SealDirectoryKind.Opaque);
        }

        [Theory(Skip = "The inherited theory includes shared opaque scenarios, which are unsupported by the dynamic scheduler.")]
        public override void AbsentFileProbeIsAllowedInsideDirectoryDependency(
            global::BuildXL.Pips.Operations.Process.AbsentPathProbeInUndeclaredOpaquesMode absentPathProbeMode,
            global::BuildXL.Pips.Operations.SealDirectoryKind directoryKind)
        {
            base.AbsentFileProbeIsAllowedInsideDirectoryDependency(absentPathProbeMode, directoryKind);
        }

        [Theory]
        [InlineData(global::BuildXL.Pips.Operations.Process.AbsentPathProbeInUndeclaredOpaquesMode.Strict)]
        [InlineData(global::BuildXL.Pips.Operations.Process.AbsentPathProbeInUndeclaredOpaquesMode.Relaxed)]
        [InlineData(global::BuildXL.Pips.Operations.Process.AbsentPathProbeInUndeclaredOpaquesMode.Unsafe)]
        public void AbsentFileProbeIsAllowedInsideExclusiveOpaqueDependency(
            global::BuildXL.Pips.Operations.Process.AbsentPathProbeInUndeclaredOpaquesMode absentPathProbeMode)
        {
            base.AbsentFileProbeIsAllowedInsideDirectoryDependency(
                absentPathProbeMode,
                global::BuildXL.Pips.Operations.SealDirectoryKind.Opaque);
        }

        [Theory(Skip = "The inherited theory includes a shared opaque scenario, which is unsupported by the dynamic scheduler.")]
        public override void OutputExistenceAssertionsUnderOpaqueConsumptionBehavior(
            global::BuildXL.Pips.Operations.SealDirectoryKind kind)
        {
            base.OutputExistenceAssertionsUnderOpaqueConsumptionBehavior(kind);
        }

        [Fact]
        public void OutputExistenceAssertionsUnderExclusiveOpaqueConsumptionBehavior()
        {
            base.OutputExistenceAssertionsUnderOpaqueConsumptionBehavior(
                global::BuildXL.Pips.Operations.SealDirectoryKind.Opaque);
        }

        [Theory(Skip = "The inherited theory includes a shared opaque scenario, which is unsupported by the dynamic scheduler.")]
        public override void OutputExistenceAssertionsUnderOpaqueIsValidated(
            global::BuildXL.Pips.Operations.SealDirectoryKind kind)
        {
            base.OutputExistenceAssertionsUnderOpaqueIsValidated(kind);
        }

        [Fact]
        public void OutputExistenceAssertionsUnderExclusiveOpaqueAreValidated()
        {
            base.OutputExistenceAssertionsUnderOpaqueIsValidated(
                global::BuildXL.Pips.Operations.SealDirectoryKind.Opaque);
        }

        [Theory(Skip = "The inherited theory includes a shared opaque scenario, which is unsupported by the dynamic scheduler.")]
        public override void OutputExistenceAssertionsUnderOpaqueCachingBehavior(
            global::BuildXL.Pips.Operations.SealDirectoryKind kind)
        {
            base.OutputExistenceAssertionsUnderOpaqueCachingBehavior(kind);
        }

        [Fact]
        public void OutputExistenceAssertionsUnderExclusiveOpaqueCachingBehavior()
        {
            base.OutputExistenceAssertionsUnderOpaqueCachingBehavior(
                global::BuildXL.Pips.Operations.SealDirectoryKind.Opaque);
        }
    }

    public class DynamicOutputReorderTests : OutputReorderTests
    {
        public DynamicOutputReorderTests(ITestOutputHelper output) : base(output) => EnableDynamicGraphScheduler();
    }

    public class DynamicOutputsRemainWriteableTests : OutputsRemainWriteableTests
    {
        public DynamicOutputsRemainWriteableTests(ITestOutputHelper output) : base(output) => EnableDynamicGraphScheduler();
    }

    public class DynamicProcessBreakawayTests : ProcessBreakawayTests
    {
        public DynamicProcessBreakawayTests(ITestOutputHelper output) : base(output) => EnableDynamicGraphScheduler();

        [FactIfSupported(
            requiresWindowsBasedOperatingSystem: true,
            Skip = "Shared opaque directories are unsupported by the dynamic scheduler.")]
        public override void BreakawayProcessCompensatesWithAugmentedAccesses()
        {
            base.BreakawayProcessCompensatesWithAugmentedAccesses();
        }

        [FactIfSupported(
            requiresWindowsBasedOperatingSystem: true,
            Skip = "Shared opaques and undeclared source reads are unsupported by the dynamic scheduler.")]
        public override void AllowedTrustedAccessesTrumpFileBasedExistenceDenials()
        {
            base.AllowedTrustedAccessesTrumpFileBasedExistenceDenials();
        }
    }

    public class DynamicProcessWeightTests : ProcessWeightTests
    {
        public DynamicProcessWeightTests(ITestOutputHelper output) : base(output) => EnableDynamicGraphScheduler();

        [Fact(Skip = "The synthetic machine-performance hook accepts only the static Scheduler type.")]
        public override void WeightTestInDispatcher()
        {
            base.WeightTestInDispatcher();
        }
    }

    public class DynamicReclassificationRulesTests : ReclassificationRulesTests
    {
        public DynamicReclassificationRulesTests(ITestOutputHelper output) : base(output) => EnableDynamicGraphScheduler();

        [Theory(Skip = "Undeclared source reads are unsupported by the dynamic scheduler.")]
        [InlineData(true)]
        [InlineData(false)]
        public override void BasicReclassificationTest(bool useAll)
        {
            base.BasicReclassificationTest(useAll);
        }

        [Theory(Skip = "Undeclared source reads are unsupported by the dynamic scheduler.")]
        [InlineData(true)]
        [InlineData(false)]
        public override void ReclassificationToUnitIgnoresAccess(bool ignore)
        {
            base.ReclassificationToUnitIgnoresAccess(ignore);
        }
    }

    public class DynamicRemoteCacheShortCircuitTests : RemoteCacheShortCircuitTests
    {
        public DynamicRemoteCacheShortCircuitTests(ITestOutputHelper output) : base(output) => EnableDynamicGraphScheduler();

        [Fact(Skip = "Shared opaque directories are unsupported by the dynamic scheduler.")]
        public override void RemoteCacheShortCircuitWithOpaqueHit()
        {
            base.RemoteCacheShortCircuitWithOpaqueHit();
        }
    }

    public class DynamicReparsePointTests : ReparsePointTests
    {
        public DynamicReparsePointTests(ITestOutputHelper output) : base(output) => EnableDynamicGraphScheduler();

        [FactIfSupported(
            requiresAdmin: true,
            requiresWindowsBasedOperatingSystem: true,
            Skip = "Shared opaque directories are unsupported by the dynamic scheduler.")]
        public override void ManifestOfResolvedAccessIsProperlyComputed()
        {
            base.ManifestOfResolvedAccessIsProperlyComputed();
        }

        [FactIfSupported(
            requiresAdmin: true,
            requiresWindowsBasedOperatingSystem: true,
            Skip = "Shared opaques and undeclared source reads are unsupported by the dynamic scheduler.")]
        public override void ReparsePointCreationInvalidatesTheCache()
        {
            base.ReparsePointCreationInvalidatesTheCache();
        }

        [TheoryIfSupported(
            requiresAdmin: true,
            requiresWindowsBasedOperatingSystem: true,
            Skip = "Shared opaques and undeclared source reads are unsupported by the dynamic scheduler.")]
        [InlineData(true)]
        [InlineData(false)]
        public override void IndividualPipsCanTurnOffReparsePointResolution(bool pipDisablesFullReparsePointResolution)
        {
            base.IndividualPipsCanTurnOffReparsePointResolution(pipDisablesFullReparsePointResolution);
        }
    }

    public class DynamicRetryTests : RetryTests
    {
        public DynamicRetryTests(ITestOutputHelper output) : base(output) => EnableDynamicGraphScheduler();
    }

    public class DynamicSchedulerCounterTests : SchedulerCounterTests
    {
        public DynamicSchedulerCounterTests(ITestOutputHelper output) : base(output) => EnableDynamicGraphScheduler();

        [Fact(Skip = "Dynamic graph mode does not support pip filtering.")]
        public override void ValidateProcessPipCountersByFilterForFailedPip()
        {
            base.ValidateProcessPipCountersByFilterForFailedPip();
        }

        [Fact(Skip = "Dynamic graph mode does not support pip filtering.")]
        public override void ValidateProcessPipCountersByFilter()
        {
            base.ValidateProcessPipCountersByFilter();
        }
    }

    public class DynamicSealFullDirectoryTests : SealFullDirectoryTests
    {
        public DynamicSealFullDirectoryTests(ITestOutputHelper output) : base(output) => EnableDynamicGraphScheduler();
    }

    public class DynamicSealedSourceDirectoryTests : SealedSourceDirectoryTests
    {
        public DynamicSealedSourceDirectoryTests(ITestOutputHelper output) : base(output) => EnableDynamicGraphScheduler();
    }

    public class DynamicStoreNoOutputsToCacheTests : StoreNoOutputsToCacheTests
    {
        public DynamicStoreNoOutputsToCacheTests(ITestOutputHelper output) : base(output) => EnableDynamicGraphScheduler();
    }

    public class DynamicSucceedFastTests : SucceedFastTests
    {
        public DynamicSucceedFastTests(ITestOutputHelper output) : base(output) => EnableDynamicGraphScheduler();
    }

    public class DynamicTempDirectoryTests : TempDirectoryTests
    {
        public DynamicTempDirectoryTests(ITestOutputHelper output) : base(output) => EnableDynamicGraphScheduler();

        [Theory(Skip = "The inherited theory includes shared opaque scenarios, which are unsupported by the dynamic scheduler.")]
        public override void FailWhenTempDirectoryContainsSharedOpaque(
            int tempArtifactType,
            global::BuildXL.Pips.Operations.SealDirectoryKind sealDirectoryKind)
        {
            base.FailWhenTempDirectoryContainsSharedOpaque(tempArtifactType, sealDirectoryKind);
        }

        [Theory]
        [InlineData(TempArtifactType.AdditionalTempDirectory)]
        [InlineData(TempArtifactType.TempDirectory)]
        public void FailWhenTempDirectoryContainsExclusiveOpaque(int tempArtifactType)
        {
            base.FailWhenTempDirectoryContainsSharedOpaque(
                tempArtifactType,
                global::BuildXL.Pips.Operations.SealDirectoryKind.Opaque);
        }
    }

    public class DynamicUnsafeDirectoryTests : UnsafeDirectoryTests
    {
        public DynamicUnsafeDirectoryTests(ITestOutputHelper output) : base(output) => EnableDynamicGraphScheduler();

        [Fact(Skip = "Shared opaque directories are unsupported by the dynamic scheduler.")]
        public override void AbsentFileProbeFollowedByDynamicWriteIsIgnored()
        {
            base.AbsentFileProbeFollowedByDynamicWriteIsIgnored();
        }
    }

    public class DynamicUnsafeGlobalUntrackedScopesTests : UnsafeGlobalUntrackedScopesTests
    {
        public DynamicUnsafeGlobalUntrackedScopesTests(ITestOutputHelper output) : base(output) => EnableDynamicGraphScheduler();
    }

    public class DynamicVsoHashDirectoryTests : VsoHashDirectoryTests
    {
        public DynamicVsoHashDirectoryTests(ITestOutputHelper output) : base(output) => EnableDynamicGraphScheduler();

        [Theory(Skip = "The inherited theory includes a shared opaque scenario, which is unsupported by the dynamic scheduler.")]
        public override void VsoHashOfOpaqueDirectorySucceeds(bool isSharedOpaque)
        {
            base.VsoHashOfOpaqueDirectorySucceeds(isSharedOpaque);
        }

        [Fact]
        public void VsoHashOfExclusiveOpaqueDirectorySucceeds()
        {
            base.VsoHashOfOpaqueDirectorySucceeds(isSharedOpaque: false);
        }
    }

    public class DynamicWeakFingerprintAugmentationTests : WeakFingerprintAugmentationTests
    {
        public DynamicWeakFingerprintAugmentationTests(ITestOutputHelper output) : base(output) => EnableDynamicGraphScheduler();

        [Fact(Skip = "The dynamic scheduler does not currently emit SuspiciousPathsInAugmentedPathSet.")]
        public override void AugmentedPathSetUsageTracking()
        {
            base.AugmentedPathSetUsageTracking();
        }
    }

}
