// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Core;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Scheduler.IncrementalSchedulingTests
{
    [Feature(Features.IncrementalScheduling)]
    [TestClassIfSupported(requiresJournalScan: true)]
    public class ReclassificationRulesTests_IncrementalScheduling : ReclassificationRulesTests
    {
        public ReclassificationRulesTests_IncrementalScheduling(ITestOutputHelper output) : base(output)
        {
            Configuration.Schedule.IncrementalScheduling = true;
            Configuration.Schedule.SkipHashSourceFile = false;
        }

        [Theory]
        [MemberData(nameof(TruthTable.GetTable), 2, MemberType = typeof(TruthTable))]
        public void NewlyAbsentPathProbeIsSkipped(bool isProbe, bool changeRule)
        {
            // Reclassify the read/probe as an absent path probe
            var rule = new ReclassificationRuleConfig()
            {
                PathRegex = "file.txt",
                Name = "FileIsAbsent",
                ResolvedObservationTypes = [ObservationType.ExistingFileProbe, ObservationType.FileContentRead],
                ReclassifyTo = Rt(ObservationType.AbsentPathProbe)
            };

            var pathStr = Path.Combine(SourceRoot, "file.txt");
            var path = AbsolutePath.Create(Context.PathTable, pathStr);
            File.WriteAllText(pathStr, "SomeContent");

            var operationsA = new List<Operation>
            {
                isProbe ? Operation.Probe(FileArtifact.CreateSourceFile(path), doNotInfer: true)
                        : Operation.ReadFile(FileArtifact.CreateSourceFile(path), doNotInfer: true),
                Operation.WriteFile(CreateOutputFileArtifact(), "stableContent")
            };

            var builderA = CreatePipBuilder(operationsA);
            builderA.ReclassificationRules = new[]
            {
                rule.GetRule()
            };

            builderA.Options |= global::BuildXL.Pips.Operations.Process.Options.AllowUndeclaredSourceReads;
            var pipA = SchedulePipBuilder(builderA);

            // Run once
            RunScheduler().AssertSuccess().AssertCacheMiss(pipA.Process.PipId);

            // Delete the file
            File.Delete(pathStr);
            XAssert.IsFalse(File.Exists(pathStr));

            ResetPipGraphBuilder();
            builderA = CreatePipBuilder(operationsA);

            if (changeRule)
            {
                // change the rule so the pip is scheduled (but the rule will still apply).
                rule.PathRegex = "ile.txt";
            }

            builderA.ReclassificationRules = new[] { rule.GetRule() };
            builderA.Options |= global::BuildXL.Pips.Operations.Process.Options.AllowUndeclaredSourceReads;
            pipA = SchedulePipBuilder(builderA);

            if (!changeRule)
            {
                // The pip had an absent probe so the pip is not scheduled
                RunScheduler().AssertSuccess().AssertNotScheduled(pipA.Process.PipId);
            }
            else
            {
                // The rule was changed so the pip is scheduled and is a miss due to WF mismatch
                RunScheduler().AssertSuccess().AssertCacheMiss(pipA.Process.PipId);
            }
        }
    }
}
