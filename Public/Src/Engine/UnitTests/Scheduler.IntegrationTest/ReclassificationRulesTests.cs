// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using BuildXL.Pips.Reclassification;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Core;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Scheduler
{
    /// <nodoc />
    public class ReclassificationRulesTests : ReclassificationRulesTestsBase
    {
        public ReclassificationRulesTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void ChangesInRuleDefinitionsAffectPipFingerprints()
        {
            var operations = new List<Operation>
            {
                Operation.WriteFile(CreateOutputFileArtifact(), "stableContent")
            };
            var builder = CreatePipBuilder(operations);
            var pip = SchedulePipBuilder(builder);

            RunScheduler().AssertSuccess();
            RunScheduler().AssertSuccess().AssertCacheHit(pip.Process.PipId);

            var ruleA = new ReclassificationRuleConfig()
            {
                PathRegex = "probedpath",
                Name = "ExistingDirProbesAreAbsent",
                ResolvedObservationTypes = [ ObservationType.ExistingDirectoryProbe ],
                ReclassifyTo = Rt(ObservationType.AbsentPathProbe)
            };

            var ruleB = new ReclassificationRuleConfig()
            {
                PathRegex = "probedpath",
                Name = "Rule21",
                ResolvedObservationTypes = [ObservationType.ExistingDirectoryProbe],
                ReclassifyTo = Rt(ObservationType.AbsentPathProbe)
            };

            var ruleC = new ReclassificationRuleConfig()
            {
                PathRegex = "probedpath",
                Name = "Rule22",
                ResolvedObservationTypes = [ObservationType.AbsentPathProbe],
                ReclassifyTo = Rt(ObservationType.AbsentPathProbe)
            };


            // A change in the configuration that adds some rules should change the fingerprint, even if nothing else changes
            Configuration.GlobalReclassificationRules.Add(ruleA);
            RunScheduler().AssertSuccess().AssertCacheMiss(pip.Process.PipId);
            RunScheduler().AssertSuccess().AssertCacheHit(pip.Process.PipId);

            // A new rule changes the fingerprint
            Configuration.GlobalReclassificationRules.Add(ruleB);
            Configuration.GlobalReclassificationRules.Add(ruleC);
            RunScheduler().AssertSuccess().AssertCacheMiss(pip.Process.PipId);
            RunScheduler().AssertSuccess().AssertCacheHit(pip.Process.PipId);

            // A change in a rule name changes the fingerprint
            ((ReclassificationRuleConfig)ruleA).Name += "_Suffix";
            RunScheduler().AssertSuccess().AssertCacheMiss(pip.Process.PipId);
            RunScheduler().AssertSuccess().AssertCacheHit(pip.Process.PipId);

            // A change in a rule casing does not change the fingerprint
            ((ReclassificationRuleConfig)ruleA).Name = ((ReclassificationRuleConfig)ruleA).Name.ToUpper();
            RunScheduler().AssertSuccess().AssertCacheHit(pip.Process.PipId);

            // A change in the order of the rulesets changes the fingerprint, because they change priority
            Configuration.GlobalReclassificationRules.Clear();
            Configuration.GlobalReclassificationRules.Add(ruleB);
            Configuration.GlobalReclassificationRules.Add(ruleA);
            Configuration.GlobalReclassificationRules.Add(ruleC);
            RunScheduler().AssertSuccess().AssertCacheMiss(pip.Process.PipId);
            RunScheduler().AssertSuccess().AssertCacheHit(pip.Process.PipId);

            // A change in a regex changes the fingerprint
            ruleA.PathRegex = ruleA.PathRegex += ".*";
            RunScheduler().AssertSuccess().AssertCacheMiss(pip.Process.PipId);
            RunScheduler().AssertSuccess().AssertCacheHit(pip.Process.PipId);


            // Change the ruleA to map two different types
            ruleA.ResolvedObservationTypes = new List<ObservationType> { ObservationType.FileContentRead, ObservationType.ExistingDirectoryProbe };
            RunScheduler().AssertSuccess().AssertCacheMiss(pip.Process.PipId);
            RunScheduler().AssertSuccess().AssertCacheHit(pip.Process.PipId);

            // Changing the order of the 'ResolvedObservationTypes' collection should *not* affect the fingerprint
            // as it does not affect the mapping
            ruleA.ResolvedObservationTypes = new List<ObservationType> { ObservationType.ExistingDirectoryProbe, ObservationType.FileContentRead };
            RunScheduler().AssertSuccess().AssertCacheHit(pip.Process.PipId);

            // But changing the reclassification type should
            ruleA.ReclassifyTo = Rt(ObservationType.ExistingFileProbe);
            RunScheduler().AssertSuccess().AssertCacheMiss(pip.Process.PipId);
            RunScheduler().AssertSuccess().AssertCacheHit(pip.Process.PipId);
        }

        [Fact]
        public void ChangesInOptInRulesAffectPipFingerprints()
        {
            var operations = new List<Operation>
            {
                Operation.WriteFile(CreateOutputFileArtifact(), "stableContent")
            };
            var builder = CreatePipBuilder(operations);
            var pip = SchedulePipBuilder(builder);

            RunScheduler().AssertSuccess();
            RunScheduler().AssertSuccess().AssertCacheHit(pip.Process.PipId);

            ResetPipGraphBuilder();
            builder = CreatePipBuilder(operations);
            var rule = new ReclassificationRuleConfig()
            {
                PathRegex = "probedpath",
                Name = "ExistingDirProbesAreAbsent",
                ResolvedObservationTypes = [ObservationType.ExistingDirectoryProbe],
                ReclassifyTo = Rt(ObservationType.AbsentPathProbe)
            };
            builder.ReclassificationRules = new[] { new DScriptInternalReclassificationRule(0, rule.GetRule()) };
            pip = SchedulePipBuilder(builder);
            RunScheduler().AssertSuccess().AssertCacheMiss(pip.Process.PipId);
            RunScheduler().AssertSuccess().AssertCacheHit(pip.Process.PipId);

            // Change the rule a bit 
            ResetPipGraphBuilder();
            builder = CreatePipBuilder(operations);
            rule.ResolvedObservationTypes = [ObservationType.ExistingFileProbe];
            builder.ReclassificationRules = new[] { new DScriptInternalReclassificationRule(0, rule.GetRule()) };
            pip = SchedulePipBuilder(builder);
            RunScheduler().AssertSuccess().AssertCacheMiss(pip.Process.PipId);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void BasicReclassificationTest(bool useAll)
        {
            // Reclassify a file content read as a probe

            var pathStr = Path.Combine(SourceRoot, "file.txt");
            var path = AbsolutePath.Create(Context.PathTable, pathStr);
            File.WriteAllText(pathStr, "SomeContent");

            var operationsA = new List<Operation>
            {
                Operation.ReadFile(FileArtifact.CreateSourceFile(path), doNotInfer:true),
                Operation.WriteFile(CreateOutputFileArtifact(), "stableContent")
            };

            var builderA = CreatePipBuilder(operationsA);
            builderA.ReclassificationRules = new[] {
                new DScriptInternalReclassificationRule(0, new ReclassificationRule()
                {
                    PathRegex = "file.txt",
                    Name = "ReadsAreProbes",
                    ResolvedObservationTypes = [ ObservationType.AbsentPathProbe, useAll ? ObservationType.All : ObservationType.FileContentRead ],
                    ReclassifyTo = Rt(ObservationType.ExistingFileProbe)
                })
            };
            builderA.Options |= global::BuildXL.Pips.Operations.Process.Options.AllowUndeclaredSourceReads;
            var pipA = SchedulePipBuilder(builderA);

            // Run once
            RunScheduler().AssertSuccess().AssertCacheMiss(pipA.Process.PipId);
            RunScheduler().AssertSuccess().AssertCacheHit(pipA.Process.PipId);

            // Modify the file
            File.WriteAllText(pathStr, "OtherContent");

            // Still a cache hit
            RunScheduler().AssertSuccess().AssertCacheHit(pipA.Process.PipId);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ReclassificationToUnitIgnoresAccess(bool ignore)
        {
            var pathStr = Path.Combine(SourceRoot, "file.txt");
            var path = AbsolutePath.Create(Context.PathTable, pathStr);
            File.WriteAllText(pathStr, "SomeContent");

            var operationsA = new List<Operation>
            {
                Operation.ReadFile(FileArtifact.CreateSourceFile(path), doNotInfer:true),
                Operation.WriteFile(CreateOutputFileArtifact(), "stableContent")
            };

            // BuilderA reclassifies the read as a probe
            var builderA = CreatePipBuilder(operationsA);
            builderA.ReclassificationRules = new[] {
                new DScriptInternalReclassificationRule(0, new ReclassificationRule()
                {
                    PathRegex = "file.txt",
                    ResolvedObservationTypes = [ ObservationType.FileContentRead ],
                    // The 'ignore: true' test case uses 'Unit' so the observation is flat-out ignored
                    // The 'ignore: false' case is used as a baseline - the probe is classified as existing
                    ReclassifyTo = ignore ? Rt(null) : Rt(ObservationType.ExistingFileProbe)
                })
            };
            builderA.Options |= global::BuildXL.Pips.Operations.Process.Options.AllowUndeclaredSourceReads;
            var pipA = SchedulePipBuilder(builderA);

            // Run once
            RunScheduler().AssertSuccess().AssertCacheMiss(pipA.Process.PipId);
            RunScheduler().AssertSuccess().AssertCacheHit(pipA.Process.PipId);

            // Delete the file
            File.Delete(pathStr);
            XAssert.IsFalse(File.Exists(pathStr));

            // Run with the file absent - the observation is now an 'absent probe'
            var result = RunScheduler().AssertSuccess();
            
            if (ignore)
            {
                // The access was ignored, so this should be a hit
                result.AssertCacheHit(pipA.Process.PipId);
            }
            else
            {
                // Absent probe causes a miss
                result.AssertCacheMiss(pipA.Process.PipId);
            }
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(false, false)]
        public void DirectoryProbesReclassifiedAsAbsent(bool isExplicitDirectoryProbe, bool isDirectoryProducedOnPath)
        {
            // Let's avoid this test in the incremental scheduling tests 
            // There are cases where the first pip is skipped, which is expected,
            // but would complicate the test logic unnecessarily
            Configuration.Schedule.IncrementalScheduling = false;
            
            // First build has a single pip which probes a path. The path is absent so this is an absent probe.
            // Second build includes a downstream pip that produces the path. With our default behavior, we now mark this path as a present directory probe
            // because it is present in the full graph filesystem view.
            // But if we add a rule to reclassify the existing directory probe to an absent path probe, we should get cache hits.
            // This tests exercises a number of combinations w/r to the actual existence of the path to verify that the reclassification
            // only applies in the relevant cases.
            //
            // For the bug that motivated this test / behavior, see work item #2182113
            var existingDirProbesAreAbsentRule = new DScriptInternalReclassificationRule(0, new ReclassificationRule()
            {
                PathRegex = "probedpath",
                Name = "ExistingDirProbesAreAbsent",
                ResolvedObservationTypes = [ ObservationType.ExistingDirectoryProbe ],
                ReclassifyTo = Rt(ObservationType.AbsentPathProbe)
            });

            string dir = Path.Combine(SourceRoot, "probedpath");
            AbsolutePath dirPath = AbsolutePath.Create(Context.PathTable, dir);
            DirectoryArtifact dirToProbe = DirectoryArtifact.CreateWithZeroPartialSealId(dirPath);
            var outputUnderDirectory = CreateOutputFileArtifact(dirPath, prefix: "out_");

            var outputA = CreateOutputFileArtifact();
            var operationsA = new List<Operation>
            {
                isExplicitDirectoryProbe ? Operation.DirProbe(dirToProbe) : Operation.Probe(FileArtifact.CreateSourceFile(dirPath), doNotInfer:true),
                Operation.WriteFile(outputA, "stableContent")
            };

            var builderA = CreatePipBuilder(operationsA);
            builderA.ReclassificationRules = new[] { existingDirProbesAreAbsentRule };
            var pipA = SchedulePipBuilder(builderA);

            // Run once
            RunScheduler().AssertSuccess();

            // Sanity check:
            // Add a downstream pipB that writes in a random location: this shouldn't affect the pipA's fingerprint at all
            var operationsDownstream = new List<Operation>
            {
                Operation.ReadFile(outputA), // depend on pipA
                Operation.WriteFile(CreateOutputFileArtifact()) // write to arbitrary file outside of A's scope
            };

            ResetPipGraphBuilder();

            builderA = CreatePipBuilder(operationsA);
            builderA.ReclassificationRules = new[] { existingDirProbesAreAbsentRule };

            pipA = SchedulePipBuilder(builderA);
            var builderB = CreatePipBuilder(operationsDownstream);
            var pipB = SchedulePipBuilder(builderB);
            RunScheduler().AssertCacheHit(pipA.Process.PipId);

            // Now to the actual test: add a pipC that writes on the location probed by A
            //  pipA <--- pipB
            //     `-<--- pipC
            ResetPipGraphBuilder();
            builderA = CreatePipBuilder(operationsA);
            builderA.ReclassificationRules = new[] { existingDirProbesAreAbsentRule };

            pipA = SchedulePipBuilder(builderA);
            builderB = CreatePipBuilder(operationsDownstream);
            pipB = SchedulePipBuilder(builderB);

            // Add a downstream pipC that writes to the path pipA probes
            var operationsC = new List<Operation>
            {
                Operation.ReadFile(outputA), // depend on pipA
                isDirectoryProducedOnPath ?  Operation.WriteFile(outputUnderDirectory)                   // random outputA under directory
                                           : Operation.WriteFile(FileArtifact.CreateOutputFile(dirPath)) // actually write a file to the 'directory' location
            };
            var builderC = CreatePipBuilder(operationsC);
            var pipC = SchedulePipBuilder(builderC);
            var schedulerResult = RunScheduler();

            // "isFileProbe && isFileProduced" is the single case where the build will fail.
            // Because we 'know' in ObservedInputProcessor that this path will be a file (it is a file produced by the downstream pip,
            // so it exists as file in the Output filesystem) we **won't** reclassify, as it won't be an 'ExistingDirectoryProbe'
            // Hence, we will see an existing probe on the file that is later produced (this is a DFA).
            var nextBuildFails = !isExplicitDirectoryProbe && !isDirectoryProducedOnPath;

            if (OperatingSystemHelper.IsLinuxOS && !isDirectoryProducedOnPath)
            {
                // DirectoryLocation is not set correctly on Linux so we never see that flag
                // we will assume a file probe even in that case, so this turns into the case above. Skip it.
                // Bug here: https://dev.azure.com/mseng/1ES/_workitems/edit/2194350
                nextBuildFails = true;
            }

            if (nextBuildFails)
            {
                schedulerResult.AssertFailure();
                AllowWarningEventMaybeLogged(global::BuildXL.Scheduler.Tracing.LogEventId.ProcessNotStoredToCacheDueToFileMonitoringViolations);
                AssertErrorEventLogged(global::BuildXL.Scheduler.Tracing.LogEventId.FileMonitoringError);
            }
            else
            {
                // The presence of this new write shouldn't change the fingerprint of pipA!
                // If this is explicitly a directory probe, the reclassification applies, so the path is
                // marked as absent. 
                // If this is a file probe but the downstream pip actually produces a directory,
                // the engine infers that the probe is on an (absent, future) directory, so it is also reclassified.
                schedulerResult.AssertSuccess();
                schedulerResult.AssertCacheHit(pipA.Process.PipId, pipB.Process.PipId)
                               .AssertCacheMiss(pipC.Process.PipId);
            }

            if (!nextBuildFails) // Only run a final build if the last one succeeded
            {
                // Run a final build with the whole graph
                // Note that now, the new path is present in the file system, so the status of a true probe would be different for the pip,
                // but in fact we will reclassify as absent every time:
                // Case 1) explicitDirectoryProbe && isDirectoryProducedOnPath
                //         Explicit directory probe => reclassified => path is 'absent' => cache hit
                //
                // Case 2) explicitDirectoryProbe && !isDirectoryProducedOnPath
                //         Explicit directory probe => reclassified => path is 'absent' => cache hit
                //
                // Case 3) !explicitDirectoryProbe && isDirectoryProducedOnPath
                //         Probe is on 'file path' but there is actually a directory present so we assume 'directory probe'
                //              => reclassified => path is 'absent' => cache hit
                //         This case is the arguably the most controversial, but not all tools probe directories with 'directory paths' (e.g., cmd.exe)
                //         so we assume 'directory probe' in this case.
                RunScheduler().AssertSuccess()
                                   // Even though the path is now there, it is absent for the pip due to the reclassification
                                   // (that applies because this is explicitly a directory probe)
                                   .AssertCacheHit(pipA.Process.PipId)
                                   .AssertCacheHit(pipB.Process.PipId, pipC.Process.PipId);
            }
        }
    }
}
