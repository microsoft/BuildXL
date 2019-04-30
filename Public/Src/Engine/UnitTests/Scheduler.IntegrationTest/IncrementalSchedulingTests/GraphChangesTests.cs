// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Graph;
using BuildXL.Scheduler.IncrementalScheduling;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Scheduler.IncrementalSchedulingTests
{
    /// <summary>
    /// Tests that validate functionality of graph-agnostic incremental scheduling.
    /// </summary>
    [Trait("Category", "IncrementalSchedulingTests")]
    [TestClassIfSupported(requiresJournalScan: true)]
    public class GraphChangesTests : IncrementalSchedulingTests
    {
        public GraphChangesTests(ITestOutputHelper output) : base(output)
        {
            Configuration.Schedule.IncrementalScheduling = true;
            Configuration.Schedule.SkipHashSourceFile = false;

            // Set this option to true if you want to debug the components of pip static fingerprints.
            Configuration.Schedule.LogPipStaticFingerprintTexts = false;

            // Reset pip graph builder so that it is configured properly.
            ResetPipGraphBuilder();
        }

        [Fact]
        public void SwitchSimplePipsBackAndForth()
        {
            // Graph G1: P
            // Graph G2: Q

            // Start with G1.
            // Build P.
            var pOperations = new Operation[] { Operation.ReadFile(CreateSourceFile()), Operation.WriteFile(CreateOutputFileArtifact()) };
            Process p = CreateAndSchedulePipBuilder(pOperations).Process;

            RunScheduler().AssertScheduled(p.PipId);

            // Switch to G2.
            ResetPipGraphBuilder();

            // Build Q.
            var qOperations = new Operation[] { Operation.ReadFile(CreateSourceFile()), Operation.WriteFile(CreateOutputFileArtifact()) };
            Process q = CreateAndSchedulePipBuilder(qOperations).Process;

            RunScheduler().AssertScheduled(q.PipId);

            // Switch to G1.
            ResetPipGraphBuilder();

            // Build P again.
            p = CreateAndSchedulePipBuilder(pOperations).Process;

            // P should not be affected.
            RunScheduler(
                new global::BuildXL.Scheduler.SchedulerTestHooks()
                {
                    IncrementalSchedulingStateAfterJournalScanAction = iss =>
                    {
                        XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(p.PipId.ToNodeId()));
                    }
                }).AssertNotScheduled(p.PipId);

            // Switch to G2.
            ResetPipGraphBuilder();

            // Build q again.
            q = CreateAndSchedulePipBuilder(qOperations).Process;

            // Q should not be affected.
            RunScheduler(
                new global::BuildXL.Scheduler.SchedulerTestHooks()
                {
                    IncrementalSchedulingStateAfterJournalScanAction = iss =>
                    {
                        XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(q.PipId.ToNodeId()));
                    }
                }).AssertNotScheduled(q.PipId);
        }

        [Fact]
        public void ModifySourceFileOfOtherGraph()
        {
            // Graph G1: P -> f
            // Graph G2: Q -> g

            // Start with G1.
            // Build P.
            FileArtifact f = CreateSourceFile();

            var pOperations = new Operation[] { Operation.ReadFile(f), Operation.WriteFile(CreateOutputFileArtifact()) };
            Process p = CreateAndSchedulePipBuilder(pOperations).Process;

            RunScheduler().AssertScheduled(p.PipId);

            // Switch to G2.
            ResetPipGraphBuilder();

            // Build Q.
            FileArtifact g = CreateSourceFile();

            var qOperations = new Operation[] { Operation.ReadFile(g), Operation.WriteFile(CreateOutputFileArtifact()) };
            Process q = CreateAndSchedulePipBuilder(qOperations).Process;

            RunScheduler().AssertScheduled(q.PipId);

            // Modify f, Q should not be affected.
            ModifyFile(f);

            RunScheduler().AssertNotScheduled(q.PipId);

            // Switch to G1.
            ResetPipGraphBuilder();

            // Build P again.
            p = CreateAndSchedulePipBuilder(pOperations).Process;

            RunScheduler(
                new global::BuildXL.Scheduler.SchedulerTestHooks()
                {
                    IncrementalSchedulingStateAfterJournalScanAction = iss =>
                    {
                        // f was modified before, so P should be dirty.
                        XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeDirty(p.PipId.ToNodeId()));
                    }
                }).AssertScheduled(p.PipId);
        }

        [Fact]
        public void ModifySharedSourceFile()
        {
            // Graph G1: P -> f
            // Graph G2: Q -> f

            // f is a shared source file.
            FileArtifact f = CreateSourceFile();

            // Start with G1.
            var pOperations = new Operation[] { Operation.ReadFile(f), Operation.WriteFile(CreateOutputFileArtifact()) };
            Process p = CreateAndSchedulePipBuilder(pOperations).Process;
            RunScheduler().AssertScheduled(p.PipId);

            // Switch to G2.
            ResetPipGraphBuilder();

            var qOperations = new Operation[] { Operation.ReadFile(f), Operation.WriteFile(CreateOutputFileArtifact()) };
            Process q = CreateAndSchedulePipBuilder(qOperations).Process;
            RunScheduler().AssertScheduled(q.PipId);

            // Switch to G1.
            ResetPipGraphBuilder();

            p = CreateAndSchedulePipBuilder(pOperations).Process;
            RunScheduler(new global::BuildXL.Scheduler.SchedulerTestHooks()
            {
                IncrementalSchedulingStateAfterJournalScanAction = iss =>
                {
                    // f was not modified, so P should be clean.
                    XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(p.PipId.ToNodeId()));
                }
            }).AssertNotScheduled(p.PipId);

            // Modify f.
            ModifyFile(f);

            // Switch to G2.
            ResetPipGraphBuilder();

            q = CreateAndSchedulePipBuilder(qOperations).Process;
            RunScheduler(new global::BuildXL.Scheduler.SchedulerTestHooks()
            {
                IncrementalSchedulingStateAfterJournalScanAction = iss =>
                {
                    // f was modified, so Q should be dirty.
                    XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeDirty(q.PipId.ToNodeId()));
                }
            }).AssertScheduled(q.PipId);

            // Switch to G1.
            ResetPipGraphBuilder();

            p = CreateAndSchedulePipBuilder(pOperations).Process;
            RunScheduler(new global::BuildXL.Scheduler.SchedulerTestHooks()
            {
                IncrementalSchedulingStateAfterJournalScanAction = iss =>
                {
                    // f was modified and so has a different version wrt. P's view, so P should be clean.
                    XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeDirty(p.PipId.ToNodeId()));
                }
            }).AssertScheduled(p.PipId);
        }

        [Fact]
        public void ModifySharedAndNonSharedSourceFile()
        {
            // Graph G1: P -> f, h
            // Graph G2: Q -> g, h

            // h is a shared source file.
            FileArtifact h = CreateSourceFile();

            // Start with G1.
            // Build P.
            FileArtifact f = CreateSourceFile();
            var pOperations = new Operation[]
            {
                Operation.ReadFile(f),
                Operation.ReadFile(h),
                Operation.WriteFile(CreateOutputFileArtifact())
            };

            Process p = CreateAndSchedulePipBuilder(pOperations).Process;

            RunScheduler().AssertScheduled(p.PipId);

            // Switch to G2.
            ResetPipGraphBuilder();

            // Build Q.
            FileArtifact g = CreateSourceFile();
            var qOperations = new Operation[]
            {
                Operation.ReadFile(g),
                Operation.ReadFile(h),
                Operation.WriteFile(CreateOutputFileArtifact())
            };

            Process q = CreateAndSchedulePipBuilder(qOperations).Process;

            RunScheduler().AssertScheduled(q.PipId);

            // Modify non shared source file g, P should not be affected.
            ModifyFile(g);

            RunScheduler().AssertScheduled(q.PipId);

            // Switch to G1.
            ResetPipGraphBuilder();

            // Build P again.
            p = CreateAndSchedulePipBuilder(pOperations).Process;

            RunScheduler(
                new global::BuildXL.Scheduler.SchedulerTestHooks()
                {
                    IncrementalSchedulingStateAfterJournalScanAction = iss =>
                    {
                        // g was modified before, but is only used by Q, so P should be clean.
                        XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(p.PipId.ToNodeId()));
                    }
                }).AssertNotScheduled(p.PipId);

            // Modify shared source file h, P and Q should be affected.
            ModifyFile(h);

            RunScheduler().AssertScheduled(p.PipId);

            // Switch to G2.
            ResetPipGraphBuilder();

            // Build Q again.
            q = CreateAndSchedulePipBuilder(qOperations).Process;

            RunScheduler(
                new global::BuildXL.Scheduler.SchedulerTestHooks()
                {
                    IncrementalSchedulingStateAfterJournalScanAction = iss =>
                    {
                        // h was modified before, so Q should be dirty.
                        XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeDirty(q.PipId.ToNodeId()));
                    }
                }).AssertScheduled(q.PipId);
        }

        [Fact]
        public void SourceFileBecomeOutputFileViceVersa()
        {
            // Graph G1: h -> P -> f
            // Graph G2: h -> P -> f -> Q -> g

            // Start with G1.
            // Build P.
            FileArtifact f = CreateSourceFile();
            FileArtifact h = CreateOutputFileArtifact();

            var pOperations = new Operation[] { Operation.ReadFile(f), Operation.WriteFile(h) };
            Process p = CreateAndSchedulePipBuilder(pOperations).Process;

            RunScheduler().AssertScheduled(p.PipId);

            // Switch to G2.
            ResetPipGraphBuilder();

            // Build P and Q.
            var qOperations = new Operation[] { Operation.ReadFile(CreateSourceFile()), Operation.WriteFile(f.CreateNextWrittenVersion()) };
            Process q = CreateAndSchedulePipBuilder(qOperations).Process;

            var pNewOperations = new Operation[] { Operation.ReadFile(f.CreateNextWrittenVersion()), Operation.WriteFile(h) };
            p = CreateAndSchedulePipBuilder(pNewOperations).Process;

            RunScheduler(
                new global::BuildXL.Scheduler.SchedulerTestHooks()
                {
                    IncrementalSchedulingStateAfterJournalScanAction = iss =>
                    {
                        // f is now produced by Q, hence P should be dirty.
                        XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeDirty(p.PipId.ToNodeId()));
                    }
                }).AssertScheduled(p.PipId, q.PipId);

            // Switch to G1.
            ResetPipGraphBuilder();

            // Build P.
            p = CreateAndSchedulePipBuilder(pOperations).Process;

            RunScheduler(
                new global::BuildXL.Scheduler.SchedulerTestHooks()
                {
                    IncrementalSchedulingStateAfterJournalScanAction = iss =>
                    {
                        // f is back as a source file consumed by P, hence P should be dirty.
                        XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeDirty(p.PipId.ToNodeId()));
                    }
                }).AssertScheduled(p.PipId);
        }

        [Fact]
        public void FileProducersChange()
        {
            // Graph G1: f -> P -> g
            // Graph G2: f -> Q -> h

            // Start with G1.
            FileArtifact f = CreateOutputFileArtifact();
            FileArtifact g = CreateSourceFile();

            var pOperations = new Operation[] { Operation.ReadFile(g), Operation.WriteFile(f) };
            Process p = CreateAndSchedulePipBuilder(pOperations).Process;

            RunScheduler().AssertScheduled(p.PipId);

            // Switch to G2.
            ResetPipGraphBuilder();

            FileArtifact h = CreateSourceFile();

            var qOperations = new Operation[] { Operation.ReadFile(h), Operation.WriteFile(f) };
            Process q = CreateAndSchedulePipBuilder(qOperations).Process;

            RunScheduler().AssertScheduled(q.PipId);

            // Switch to G1.
            ResetPipGraphBuilder();

            p = CreateAndSchedulePipBuilder(pOperations).Process;

            RunScheduler(
                new global::BuildXL.Scheduler.SchedulerTestHooks()
                {
                    IncrementalSchedulingStateAfterJournalScanAction = iss =>
                    {
                        // f is again produced by P, hence P should be dirty.
                        XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeDirty(p.PipId.ToNodeId()));
                    }
                }).AssertScheduled(p.PipId);
        }

        [Fact]
        public void FileProducersChangeButHaveEqualFingerprint()
        {
            // Graph G1: f -> P -> g
            // Graph G2: f -> Q -> g

            // Start with G1.
            FileArtifact f = CreateOutputFileArtifact();
            FileArtifact g = CreateSourceFile();

            var pOperations = new Operation[] { Operation.ReadFile(g), Operation.WriteFile(f) };
            Process p = CreateAndSchedulePipBuilder(pOperations).Process;

            RunScheduler().AssertScheduled(p.PipId);

            // Switch to G2.
            ResetPipGraphBuilder();

            var qOperations = new Operation[] { Operation.ReadFile(g), Operation.WriteFile(f) };
            Process q = CreateAndSchedulePipBuilder(qOperations).Process;

            RunScheduler(
                new global::BuildXL.Scheduler.SchedulerTestHooks()
                {
                    IncrementalSchedulingStateAfterJournalScanAction = iss =>
                    {
                        // f is now produced by Q, but P and Q have equal fingerprint, hence Q should be clean.
                        XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(q.PipId.ToNodeId()));
                    }
                }).AssertNotScheduled(q.PipId);
        }

        [Fact]
        public void DirtyStatusIsPropagatedOnGraphChange()
        {
            // Graph G1: P -> g -> Q -> f
            // Graph G2: R -> f

            // Start with G1.
            FileArtifact f = CreateSourceFile();
            FileArtifact g = CreateOutputFileArtifact();

            var qOperations = new Operation[] { Operation.ReadFile(f), Operation.WriteFile(g) };
            Process q = CreateAndSchedulePipBuilder(qOperations).Process;

            var pOperations = new Operation[] { Operation.ReadFile(g), Operation.WriteFile(CreateOutputFileArtifact()) };
            Process p = CreateAndSchedulePipBuilder(pOperations).Process;

            RunScheduler().AssertScheduled(p.PipId, q.PipId);

            // Switch to G2.
            ResetPipGraphBuilder();

            var rOperations = new Operation[] { Operation.ReadFile(f), Operation.WriteFile(CreateOutputFileArtifact()) };
            Process r = CreateAndSchedulePipBuilder(rOperations).Process;

            RunScheduler().AssertScheduled(r.PipId);

            ModifyFile(f);

            RunScheduler().AssertScheduled(r.PipId);
            RunScheduler().AssertNotScheduled(r.PipId);

            // Switch to G1.
            ResetPipGraphBuilder();

            q = CreateAndSchedulePipBuilder(qOperations).Process;
            p = CreateAndSchedulePipBuilder(pOperations).Process;

            RunScheduler(new global::BuildXL.Scheduler.SchedulerTestHooks()
            {
                IncrementalSchedulingStateAfterJournalScanAction = iss =>
                {
                    // f was modified, thus Q should be dirty.
                    XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeDirty(q.PipId.ToNodeId()));

                    // Q is dirty, and so is P.
                    XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeDirty(p.PipId.ToNodeId()));
                }
            }).AssertScheduled(p.PipId, q.PipId);
        }

        [Fact]
        public void CleanStatusIsPreservedOnGraphChangeIfArtifactUnchanged()
        {
            // Graph G1: P -> g -> Q -> f
            // Graph G2: R -> f

            // Start with G1.
            FileArtifact f = CreateSourceFile();
            FileArtifact g = CreateOutputFileArtifact();

            var qOperations = new Operation[] { Operation.ReadFile(f), Operation.WriteFile(g) };
            Process q = CreateAndSchedulePipBuilder(qOperations).Process;

            var pOperations = new Operation[] { Operation.ReadFile(g), Operation.WriteFile(CreateOutputFileArtifact()) };
            Process p = CreateAndSchedulePipBuilder(pOperations).Process;

            RunScheduler().AssertScheduled(p.PipId, q.PipId);

            // Switch to G2.
            ResetPipGraphBuilder();

            var rOperations = new Operation[] { Operation.ReadFile(f), Operation.WriteFile(CreateOutputFileArtifact()) };
            Process r = CreateAndSchedulePipBuilder(rOperations).Process;

            RunScheduler().AssertScheduled(r.PipId);
            RunScheduler().AssertNotScheduled(r.PipId);

            // Switch to G1.
            ResetPipGraphBuilder();

            q = CreateAndSchedulePipBuilder(qOperations).Process;
            p = CreateAndSchedulePipBuilder(pOperations).Process;

            RunScheduler(new global::BuildXL.Scheduler.SchedulerTestHooks()
            {
                IncrementalSchedulingStateAfterJournalScanAction = iss =>
                {
                    // Nothing has changed, so P and Q should be clean.
                    XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(q.PipId.ToNodeId()));
                    XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(p.PipId.ToNodeId()));
                }
            }).AssertNotScheduled(p.PipId, q.PipId);
        }

        [Fact]
        public void DirtyAndCleanStatusAreSetProperlyOnGraphChangeAndChangedSourceFile()
        {
            // Graph G1: P -> h1 -> Q -> f
            //           |
            //           + -> h3 -> R -> h2 -> S -> g
            //
            // Graph G2: T -> f

            // Start with G1.
            FileArtifact f = CreateSourceFile();
            FileArtifact h1 = CreateOutputFileArtifact();

            var qOperations = new Operation[] { Operation.ReadFile(f), Operation.WriteFile(h1) };
            Process q = CreateAndSchedulePipBuilder(qOperations).Process;

            FileArtifact g = CreateSourceFile();
            FileArtifact h2 = CreateOutputFileArtifact();

            var sOperations = new Operation[] { Operation.ReadFile(g), Operation.WriteFile(h2) };
            Process s = CreateAndSchedulePipBuilder(sOperations).Process;

            FileArtifact h3 = CreateOutputFileArtifact();

            var rOperations = new Operation[] { Operation.ReadFile(h2), Operation.WriteFile(h3) };
            Process r = CreateAndSchedulePipBuilder(rOperations).Process;

            var pOperations = new Operation[] { Operation.ReadFile(h3), Operation.ReadFile(h1), Operation.WriteFile(CreateOutputFileArtifact()) };
            Process p = CreateAndSchedulePipBuilder(pOperations).Process;

            RunScheduler().AssertScheduled(q.PipId, s.PipId, r.PipId, p.PipId);

            // Switch to G2.
            ResetPipGraphBuilder();

            var tOperations = new Operation[] { Operation.ReadFile(f), Operation.WriteFile(CreateOutputFileArtifact()) };
            Process t = CreateAndSchedulePipBuilder(tOperations).Process;

            RunScheduler().AssertScheduled(t.PipId);

            // Modify f.
            ModifyFile(f);

            RunScheduler().AssertScheduled(t.PipId);
            RunScheduler().AssertNotScheduled(t.PipId);

            // Switch to G1.
            ResetPipGraphBuilder();

            q = CreateAndSchedulePipBuilder(qOperations).Process;
            s = CreateAndSchedulePipBuilder(sOperations).Process;
            r = CreateAndSchedulePipBuilder(rOperations).Process;
            p = CreateAndSchedulePipBuilder(pOperations).Process;

            RunScheduler(
                new global::BuildXL.Scheduler.SchedulerTestHooks()
                {
                    IncrementalSchedulingStateAfterJournalScanAction = iss =>
                    {
                        // f was modified, thus Q should be dirty.
                        XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeDirty(q.PipId.ToNodeId()));

                        // Q is dirty, and so is P.
                        XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeDirty(p.PipId.ToNodeId()));

                        // R and S are unaffected by f modification.
                        XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(r.PipId.ToNodeId()));
                        XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(s.PipId.ToNodeId()));
                    }
                })
                .AssertScheduled(p.PipId, q.PipId, r.PipId /* R needs to be scheduled for hashing outputs, although it's clean and materialized.*/)
                .AssertNotScheduled(s.PipId);
        }

        [Fact]
        public void DirtyAndCleanStatusAreSetProperlyOnGraphChangeAndProducerChange()
        {
            // Graph G1: P -> h1 -> Q -> f
            //           |
            //           + -> h3 -> R -> h2 -> S -> g
            //
            // Graph G2: h1 -> T

            // Start with G1.
            FileArtifact f = CreateSourceFile();
            FileArtifact h1 = CreateOutputFileArtifact();

            var qOperations = new Operation[] { Operation.ReadFile(f), Operation.WriteFile(h1) };
            Process q = CreateAndSchedulePipBuilder(qOperations).Process;

            FileArtifact g = CreateSourceFile();
            FileArtifact h2 = CreateOutputFileArtifact();

            var sOperations = new Operation[] { Operation.ReadFile(g), Operation.WriteFile(h2) };
            Process s = CreateAndSchedulePipBuilder(sOperations).Process;

            FileArtifact h3 = CreateOutputFileArtifact();

            var rOperations = new Operation[] { Operation.ReadFile(h2), Operation.WriteFile(h3) };
            Process r = CreateAndSchedulePipBuilder(rOperations).Process;

            var pOperations = new Operation[] { Operation.ReadFile(h3), Operation.ReadFile(h1), Operation.WriteFile(CreateOutputFileArtifact()) };
            Process p = CreateAndSchedulePipBuilder(pOperations).Process;

            RunScheduler().AssertScheduled(q.PipId, s.PipId, r.PipId, p.PipId);

            // Switch to G2.
            ResetPipGraphBuilder();

            var tOperations = new Operation[] { Operation.ReadFile(CreateSourceFile()), Operation.WriteFile(h1) };
            Process t = CreateAndSchedulePipBuilder(tOperations).Process;

            RunScheduler().AssertScheduled(t.PipId);

            // Switch to G1.
            ResetPipGraphBuilder();

            q = CreateAndSchedulePipBuilder(qOperations).Process;
            s = CreateAndSchedulePipBuilder(sOperations).Process;
            r = CreateAndSchedulePipBuilder(rOperations).Process;
            p = CreateAndSchedulePipBuilder(pOperations).Process;

            RunScheduler(
                new global::BuildXL.Scheduler.SchedulerTestHooks()
                {
                    IncrementalSchedulingStateAfterJournalScanAction = iss =>
                    {
                        // h1's producer is now back to Q, thus Q should be dirty.
                        XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeDirty(q.PipId.ToNodeId()));

                        // Q is dirty, and so is P.
                        XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeDirty(p.PipId.ToNodeId()));

                        // R and S are unaffected by change in h1's producer.
                        XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(r.PipId.ToNodeId()));
                        XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(s.PipId.ToNodeId()));
                    }
                })
                .AssertScheduled(p.PipId, q.PipId, r.PipId /* R needs to be scheduled for hashing outputs, although it's clean and materialized.*/)
                .AssertNotScheduled(s.PipId);
        }

        [Fact]
        public void WriteFileChangeContent()
        {
            // Graph G1: f -> W(f), g -> W(g)
            // Graph G2: f -> W(f')
            // Graph G3: f -> W(f)

            // Start with G1.
            AbsolutePath f = CreateUniqueObjPath("write_f");
            AbsolutePath g = CreateUniqueObjPath("write_g");
            NodeId wF = GetProducerNode(WriteFile(f, "f"));
            NodeId wG = GetProducerNode(WriteFile(g, "g"));

            RunScheduler().AssertScheduled(wF.ToPipId(), wG.ToPipId());

            // Switch to G2.
            ResetPipGraphBuilder();

            // Content of f changes.
            wF = GetProducerNode(WriteFile(f, "f_modified"));

            RunScheduler().AssertScheduled(wF.ToPipId());

            // Switch to G3.
            ResetPipGraphBuilder();

            // Content of f is reverted.
            wF = GetProducerNode(WriteFile(f, "f"));

            RunScheduler().AssertScheduled(wF.ToPipId());

            // Switch to G1.
            ResetPipGraphBuilder();

            wF = GetProducerNode(WriteFile(f, "f"));
            wG = GetProducerNode(WriteFile(g, "g"));

            RunScheduler(
                new global::BuildXL.Scheduler.SchedulerTestHooks()
                {
                    IncrementalSchedulingStateAfterJournalScanAction = iss =>
                    {
                        XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(wF));
                        XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(wG));
                    }
                }).AssertNotScheduled(wF.ToPipId(), wG.ToPipId());
        }

        [Fact]
        public void CopyFileChangeDestination()
        {
            // Graph G1: i -> C1 -> f <- C2 <- h
            // Graph G2:            g <- C3 <- h

            // Start with G1.
            AbsolutePath i = CreateUniqueObjPath("copy_i");
            FileArtifact f = CreateSourceFile();
            AbsolutePath h = CreateUniqueObjPath("copy_h");

            NodeId c1 = GetProducerNode(CopyFile(f, i));
            NodeId c2 = GetProducerNode(CopyFile(f, h));

            RunScheduler().AssertScheduled(c1.ToPipId(), c2.ToPipId());

            // Switch to G2.
            ResetPipGraphBuilder();

            FileArtifact g = CreateSourceFile();
            NodeId c3 = GetProducerNode(CopyFile(g, h));

            RunScheduler().AssertScheduled(c3.ToPipId());

            // Switch to G1.
            ResetPipGraphBuilder();

            c1 = GetProducerNode(CopyFile(f, i));
            c2 = GetProducerNode(CopyFile(f, h));

            RunScheduler(
                new global::BuildXL.Scheduler.SchedulerTestHooks()
                {
                    IncrementalSchedulingStateAfterJournalScanAction = iss =>
                    {
                        XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeDirty(c2));
                        XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(c1));
                    }
                }).AssertScheduled(c2.ToPipId()).AssertNotScheduled(c1.ToPipId());
        }

        [Fact]
        public void SealDirectoryChangeMembership()
        {
            // NOTES: This test will fail if seal directories' fingerprints are not taken into account 
            //        when computing the fingerprints of processes.

            // Graph G1: h -> P -> SD -> f
            // Graph G2: h -> P -> SD -> f, g

            // Start with G1.
            AbsolutePath sdRoot = CreateUniqueDirectory();
            FileArtifact f = CreateSourceFile(sdRoot.ToString(Context.PathTable));
            FileArtifact h = CreateOutputFileArtifact();

            DirectoryArtifact sd = SealDirectory(sdRoot, SealDirectoryKind.Full, f);
            var pOperations = new Operation[] { Operation.ReadFile(f, doNotInfer: true), Operation.WriteFile(h) };
            ProcessBuilder pb = CreatePipBuilder(pOperations);
            pb.AddInputDirectory(sd);

            Process p = SchedulePipBuilder(pb).Process;

            RunScheduler().AssertScheduled(p.PipId);

            // Switch to G2.
            ResetPipGraphBuilder();

            FileArtifact g = CreateSourceFile(sdRoot.ToString(Context.PathTable));
            sd = SealDirectory(sdRoot, SealDirectoryKind.Full, f, g);

            pb = CreatePipBuilder(pOperations);
            pb.AddInputDirectory(sd);

            p = SchedulePipBuilder(pb).Process;

            // Although P doesn't consume g, but because the membership of seal directory changed,
            // P will have a new fingerprint.
            RunScheduler(
                new global::BuildXL.Scheduler.SchedulerTestHooks()
                {
                    IncrementalSchedulingStateAfterJournalScanAction = iss =>
                    {
                        XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeDirty(p.PipId.ToNodeId()));
                    }
                }).AssertScheduled(p.PipId);

            // Switch to G1.
            ResetPipGraphBuilder();

            sd = SealDirectory(sdRoot, SealDirectoryKind.Full, f);
            pb = CreatePipBuilder(pOperations);
            pb.AddInputDirectory(sd);

            p = SchedulePipBuilder(pb).Process;

            // Previous P can be considered as distinct pip that wrote to h. 
            // Thus, now P is dirty.
            RunScheduler(
                new global::BuildXL.Scheduler.SchedulerTestHooks()
                {
                    IncrementalSchedulingStateAfterJournalScanAction = iss =>
                    {
                        XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeDirty(p.PipId.ToNodeId()));
                    }
                }).AssertScheduled(p.PipId);
        }

        [Fact]
        public void OutputDirectoryBecomesOutputFileViceVersa()
        {
            // Graph G1: f -> P
            // Graph G2: f -> Q, where f is an output directory.

            // Start with G1.
            FileArtifact fAsOutputFile = CreateOutputFileArtifact();
            var pOperations = new Operation[] { Operation.ReadFile(CreateSourceFile()), Operation.WriteFile(fAsOutputFile) };
            Process p = CreateAndSchedulePipBuilder(pOperations).Process;

            RunScheduler().AssertScheduled(p.PipId);

            // Switch to G2.
            ResetPipGraphBuilder();

            string opaqueDir = fAsOutputFile.Path.ToString(Context.PathTable);
            AbsolutePath opaqueDirPath = AbsolutePath.Create(Context.PathTable, opaqueDir);
            FileArtifact outputInOpaque = CreateOutputFileArtifact(opaqueDir);

            var qOperations = new Operation[] { Operation.ReadFile(CreateSourceFile()), Operation.WriteFile(outputInOpaque) };
            ProcessBuilder qPb = CreatePipBuilder(qOperations);
            qPb.AddOutputDirectory(opaqueDirPath);

            Process q = SchedulePipBuilder(qPb).Process;

            RunScheduler().AssertScheduled(q.PipId);

            // Switch to G1.
            ResetPipGraphBuilder();

            p = CreateAndSchedulePipBuilder(pOperations).Process;

            RunScheduler(new global::BuildXL.Scheduler.SchedulerTestHooks()
            {
                IncrementalSchedulingStateAfterJournalScanAction = iss =>
                {
                    XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeDirty(p.PipId.ToNodeId()));
                }
            }).AssertScheduled(p.PipId);

            // Switch to G2.
            ResetPipGraphBuilder();

            qPb = CreatePipBuilder(qOperations);
            qPb.AddOutputDirectory(opaqueDirPath);

            q = SchedulePipBuilder(qPb).Process;

            RunScheduler(new global::BuildXL.Scheduler.SchedulerTestHooks()
            {
                IncrementalSchedulingStateAfterJournalScanAction = iss =>
                {
                    XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeDirty(q.PipId.ToNodeId()));
                }
            }).AssertScheduled(q.PipId);
        }

        [Fact]
        public void ModifyDynamicInput()
        {
            // Graph G1: P -> SD -> f, g, where P reads f
            // Graph G2: Q -> SD -> f, g, where Q reads g.

            // Start with G1.
            AbsolutePath sdRoot = CreateUniqueDirectory();
            FileArtifact f = CreateSourceFile(sdRoot.ToString(Context.PathTable));
            FileArtifact g = CreateSourceFile(sdRoot.ToString(Context.PathTable));

            DirectoryArtifact sd = SealDirectory(sdRoot, SealDirectoryKind.Partial, f, g);
            var pOperations = new Operation[] { Operation.ReadFile(f, doNotInfer: true), Operation.WriteFile(CreateOutputFileArtifact()) };
            ProcessBuilder pb = CreatePipBuilder(pOperations);
            pb.AddInputDirectory(sd);
            Process p = SchedulePipBuilder(pb).Process;

            RunScheduler().AssertScheduled(p.PipId);

            // Switch to G2.
            ResetPipGraphBuilder();

            sd = SealDirectory(sdRoot, SealDirectoryKind.Partial, f, g);
            var qOperations = new Operation[] { Operation.ReadFile(g, doNotInfer: true), Operation.WriteFile(CreateOutputFileArtifact()) };
            ProcessBuilder qb = CreatePipBuilder(qOperations);
            qb.AddInputDirectory(sd);
            Process q = SchedulePipBuilder(qb).Process;

            RunScheduler().AssertScheduled(q.PipId);

            // Modify f.
            ModifyFile(f);

            RunScheduler(new global::BuildXL.Scheduler.SchedulerTestHooks()
            {
                IncrementalSchedulingStateAfterJournalScanAction = iss =>
                {
                    // Q is dirty due to graph transitive dependency.
                    XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeDirty(q.PipId.ToNodeId()));
                }
            }).AssertScheduled(q.PipId).AssertCacheHit(q.PipId); // But Q should be cache hit because it doesn't consume f.

            // Switch to G1.
            ResetPipGraphBuilder();

            sd = SealDirectory(sdRoot, SealDirectoryKind.Partial, f, g);
            pb = CreatePipBuilder(pOperations);
            pb.AddInputDirectory(sd);
            p = SchedulePipBuilder(pb).Process;

            RunScheduler(new global::BuildXL.Scheduler.SchedulerTestHooks()
            {
                IncrementalSchedulingStateAfterJournalScanAction = iss =>
                {
                    // P should be affected by f modification.
                    // This assertion would fail if seal directories are not tracked in the graph-agnostic state.
                    XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeDirty(p.PipId.ToNodeId()));
                }
            }).AssertScheduled(p.PipId).AssertCacheMiss(p.PipId);
        }

        [Fact]
        public void CleanOnlySealDirectory()
        {
            // Graph G1: h -> P -> SD -> f, g
            // Graph G2: i -> Q -> SD -> f, g

            Configuration.Filter = "tag='T'";

            // Run first without incremental scheduling.
            Configuration.Schedule.IncrementalScheduling = false;
            Configuration.Schedule.GraphAgnosticIncrementalScheduling = false;

            // Start with G1.
            AbsolutePath sdRoot = CreateUniqueDirectory();
            FileArtifact f = CreateSourceFile(sdRoot.ToString(Context.PathTable));
            FileArtifact g = CreateSourceFile(sdRoot.ToString(Context.PathTable));
            FileArtifact h = CreateOutputFileArtifact();

            DirectoryArtifact sd = SealDirectory(sdRoot, SealDirectoryKind.Partial, f, g);
            var pOperations = new Operation[] { Operation.ReadFile(f, doNotInfer: true), Operation.WriteFile(h) };
            ProcessBuilder pb = CreatePipBuilder(pOperations);
            pb.AddInputDirectory(sd);
            pb.AddTags(Context.StringTable, "T");

            Process p = SchedulePipBuilder(pb).Process;

            RunScheduler().AssertScheduled(p.PipId).AssertCacheMiss(p.PipId);

            // Switch to G2.
            ResetPipGraphBuilder();

            sd = SealDirectory(sdRoot, SealDirectoryKind.Partial, f, g);
            FileArtifact i = CreateOutputFileArtifact();
            var qOperations = new Operation[] { Operation.ReadFile(g, doNotInfer: true), Operation.WriteFile(i) };
            ProcessBuilder qb = CreatePipBuilder(qOperations);
            qb.AddInputDirectory(sd);
            qb.AddTags(Context.StringTable, "T");

            Process q = SchedulePipBuilder(qb).Process;

            RunScheduler().AssertScheduled(q.PipId).AssertCacheMiss(q.PipId);

            // At this point, P's and Q's outputs are already in the cache.

            // Enable incremental scheduling.
            Configuration.Schedule.IncrementalScheduling = true;
            Configuration.Schedule.GraphAgnosticIncrementalScheduling = true;

            // Switch to G1.
            ResetPipGraphBuilder();

            sd = SealDirectory(sdRoot, SealDirectoryKind.Partial, f, g);
            pOperations = new Operation[] { Operation.ReadFile(f, doNotInfer: true), Operation.WriteFile(h) };
            pb = CreatePipBuilder(pOperations);
            pb.AddInputDirectory(sd);
            pb.AddTags(Context.StringTable, "T");

            p = SchedulePipBuilder(pb).Process;

            RunScheduler().AssertScheduled(p.PipId).AssertCacheHit(p.PipId);

            // Switch to G2.
            ResetPipGraphBuilder();

            sd = SealDirectory(sdRoot, SealDirectoryKind.Partial, f, g);
            qOperations = new Operation[] { Operation.ReadFile(g, doNotInfer: true), Operation.WriteFile(i) };
            qb = CreatePipBuilder(qOperations);
            qb.AddInputDirectory(sd);
            qb.AddTags(Context.StringTable, "T");

            q = SchedulePipBuilder(qb).Process;

            RunScheduler().AssertScheduled(q.PipId).AssertCacheHit(q.PipId);

            // Switch to G1.
            ResetPipGraphBuilder();

            sd = SealDirectory(sdRoot, SealDirectoryKind.Partial, f, g);
            NodeId sdNode = PipGraphBuilder.GetSealedDirectoryNode(sd);
            pOperations = new Operation[] { Operation.ReadFile(f, doNotInfer: true), Operation.WriteFile(h) };
            pb = CreatePipBuilder(pOperations);
            pb.AddInputDirectory(sd);
            pb.AddTags(Context.StringTable, "T");

            p = SchedulePipBuilder(pb).Process;

            RunScheduler(new global::BuildXL.Scheduler.SchedulerTestHooks()
            {
                IncrementalSchedulingStateAfterJournalScanAction = iss =>
                {
                    XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(p.PipId.ToNodeId()));

                    // Due to filter, seal directory should only be clean.
                    XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanButNotMaterialized(sdNode));
                }
            }).AssertNotScheduled(p.PipId);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void StaticSealDirectoriesAreIdentifiedUniquely(bool createSourceSeal)
        {
            // Graph G1: P -> SD{f}, Q -> SD{f}
            // Graph G2: R -> dummy

            // Start with G1.
            AbsolutePath sdRoot = CreateUniqueDirectory();
            FileArtifact f = CreateSourceFile(sdRoot);

            var sdP = createSourceSeal ? SealDirectory(sdRoot, SealDirectoryKind.SourceAllDirectories) : SealDirectory(sdRoot, SealDirectoryKind.Partial, f);
            var pOperations = new Operation[] { Operation.ReadFile(f, doNotInfer: true), Operation.WriteFile(CreateOutputFileArtifact()) };
            ProcessBuilder pBuilder = CreatePipBuilder(pOperations);
            pBuilder.AddInputDirectory(sdP);
            Process p = SchedulePipBuilder(pBuilder).Process;

            var sdQ = createSourceSeal ? SealDirectory(sdRoot, SealDirectoryKind.SourceAllDirectories) : SealDirectory(sdRoot, SealDirectoryKind.Partial, f);
            var qOperations = new Operation[] { Operation.ReadFile(f, doNotInfer: true), Operation.WriteFile(CreateOutputFileArtifact()) };
            ProcessBuilder qBuilder = CreatePipBuilder(qOperations);
            qBuilder.AddInputDirectory(sdQ);
            Process q = SchedulePipBuilder(qBuilder).Process;

            RunScheduler().AssertScheduled(p.PipId, q.PipId).AssertCacheMiss(p.PipId, q.PipId);

            // Switch to G2.
            ResetPipGraphBuilder();

            Process r = CreateAndSchedulePipBuilder(new Operation[] { Operation.ReadFile(CreateSourceFile()), Operation.WriteFile(CreateOutputFileArtifact()) }).Process;

            RunScheduler().AssertScheduled(r.PipId).AssertCacheMiss(r.PipId);

            // Switch to G1.
            ResetPipGraphBuilder();

            sdP = createSourceSeal ? SealDirectory(sdRoot, SealDirectoryKind.SourceAllDirectories) : SealDirectory(sdRoot, SealDirectoryKind.Partial, f);
            NodeId sdPNode = PipGraphBuilder.GetSealedDirectoryNode(sdP);
            pBuilder = CreatePipBuilder(pOperations);
            pBuilder.AddInputDirectory(sdP);
            p = SchedulePipBuilder(pBuilder).Process;

            sdQ = createSourceSeal ? SealDirectory(sdRoot, SealDirectoryKind.SourceAllDirectories) : SealDirectory(sdRoot, SealDirectoryKind.Partial, f);
            NodeId sdQNode = PipGraphBuilder.GetSealedDirectoryNode(sdQ);
            qBuilder = CreatePipBuilder(qOperations);
            qBuilder.AddInputDirectory(sdQ);
            q = SchedulePipBuilder(qBuilder).Process;

            RunScheduler(new global::BuildXL.Scheduler.SchedulerTestHooks()
            {
                IncrementalSchedulingStateAfterJournalScanAction = iss =>
                {
                    XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(p.PipId.ToNodeId()));
                    XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(q.PipId.ToNodeId()));
                    XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(sdPNode));
                    XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(sdQNode));
                }
            }).AssertNotScheduled(p.PipId, q.PipId);
        }

        [Fact]
        public void SourceFileBecomesOutputFileViaSealDirectory()
        {
            // Graph G1: P -> SD{f}
            // Graph G2: P -> SD -> f -> Q

            // Start with G1.
            AbsolutePath sdRoot = CreateUniqueDirectory();
            FileArtifact f = CreateSourceFile(sdRoot);
            FileArtifact dummy = CreateOutputFileArtifact();

            var sd = SealDirectory(sdRoot, SealDirectoryKind.Partial, f);
            var pOperations = new Operation[] { Operation.ReadFile(f, doNotInfer: true), Operation.WriteFile(dummy) };
            ProcessBuilder pBuilder = CreatePipBuilder(pOperations);
            pBuilder.AddInputDirectory(sd);
            Process p = SchedulePipBuilder(pBuilder).Process;

            RunScheduler().AssertScheduled(p.PipId).AssertCacheMiss(p.PipId);

            // Switch to G2.
            ResetPipGraphBuilder();

            FileArtifact fAsOutput = f.CreateNextWrittenVersion();
            var qOperations = new Operation[] { Operation.ReadFile(CreateSourceFile()), Operation.WriteFile(fAsOutput) };
            Process q = CreateAndSchedulePipBuilder(qOperations).Process;

            sd = SealDirectory(sdRoot, SealDirectoryKind.Partial, fAsOutput);
            pOperations = new Operation[] { Operation.ReadFile(fAsOutput, doNotInfer: true), Operation.WriteFile(dummy) };
            pBuilder = CreatePipBuilder(pOperations);
            pBuilder.AddInputDirectory(sd);
            p = SchedulePipBuilder(pBuilder).Process;

            RunScheduler().AssertScheduled(p.PipId, q.PipId).AssertCacheMiss(p.PipId, q.PipId);

            // Switch to G1.
            ResetPipGraphBuilder();

            sd = SealDirectory(sdRoot, SealDirectoryKind.Partial, f);
            NodeId sdNode = PipGraphBuilder.GetSealedDirectoryNode(sd);
            pOperations = new Operation[] { Operation.ReadFile(f, doNotInfer: true), Operation.WriteFile(dummy) };
            pBuilder = CreatePipBuilder(pOperations);
            pBuilder.AddInputDirectory(sd);
            p = SchedulePipBuilder(pBuilder).Process;

            RunScheduler(new global::BuildXL.Scheduler.SchedulerTestHooks()
            {
                IncrementalSchedulingStateAfterJournalScanAction = iss =>
                {
                    XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeDirty(p.PipId.ToNodeId()));
                    XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeDirty(sdNode));
                }
            }).AssertScheduled(p.PipId);
        }

        [Fact]
        public void ChangingRewriteCountMakesPipsDirty()
        {
            // Graph G1: P -> f -> Q -> g
            // Graph G2: P -> f -> Q -> f -> R -> h

            // Start with G1.
            FileArtifact f1 = CreateOutputFileArtifact();
            FileArtifact g = CreateSourceFile();
            FileArtifact dummy = CreateOutputFileArtifact();

            Process q = CreateAndSchedulePipBuilder(new[] { Operation.ReadFile(g), Operation.WriteFile(f1) }).Process;
            Process p = CreateAndSchedulePipBuilder(new[] { Operation.ReadFile(f1), Operation.WriteFile(dummy) }).Process;

            RunScheduler().AssertScheduled(p.PipId, q.PipId).AssertCacheMiss(p.PipId, q.PipId);

            // Switch to G2.
            ResetPipGraphBuilder();

            FileArtifact h = CreateSourceFile();
            FileArtifact f2 = f1.CreateNextWrittenVersion();
            Process r = CreateAndSchedulePipBuilder(new[] { Operation.ReadFile(h), Operation.WriteFile(f1) }).Process;
            q = CreateAndSchedulePipBuilder(new[] { Operation.ReadFile(f1), Operation.WriteFile(f2) }).Process;
            p = CreateAndSchedulePipBuilder(new[] { Operation.ReadFile(f2), Operation.WriteFile(dummy) }).Process;

            RunScheduler().AssertScheduled(p.PipId, q.PipId, r.PipId).AssertCacheMiss(p.PipId, q.PipId, r.PipId);

            // Switch to G1.
            ResetPipGraphBuilder();

            q = CreateAndSchedulePipBuilder(new[] { Operation.ReadFile(g), Operation.WriteFile(f1) }).Process;
            p = CreateAndSchedulePipBuilder(new[] { Operation.ReadFile(f1), Operation.WriteFile(dummy) }).Process;

            RunScheduler().AssertScheduled(p.PipId, q.PipId);
        }

        [Fact]
        public void TwoProcessesAccessTheSameSourceSealDirectoryInTurn()
        {
            // Graph G1: P -> SSD <- Q
            // Graph G2: P -> SSD
            // Graph G3:      SSD <- Q

            // Start with G1.
            AbsolutePath ssdRoot = CreateUniqueDirectory();
            FileArtifact f = CreateSourceFile(ssdRoot);
            FileArtifact pOut = CreateOutputFileArtifact();
            FileArtifact qOut = CreateOutputFileArtifact();

            DirectoryArtifact pWorkDir = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory());
            DirectoryArtifact qWorkDir = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory());

            var ssd = SealDirectory(ssdRoot, SealDirectoryKind.SourceAllDirectories);

            var pOperations = new Operation[] { Operation.ReadFile(f, doNotInfer: true), Operation.WriteFile(pOut) };
            ProcessBuilder pBuilder = CreatePipBuilder(pOperations);
            pBuilder.AddInputDirectory(ssd);
            pBuilder.WorkingDirectory = pWorkDir;
            Process p = SchedulePipBuilder(pBuilder).Process;

            var qOperations = new Operation[] { Operation.ReadFile(f, doNotInfer: true), Operation.WriteFile(qOut) };
            ProcessBuilder qBuilder = CreatePipBuilder(qOperations);
            qBuilder.AddInputDirectory(ssd);
            qBuilder.WorkingDirectory = qWorkDir;
            Process q = SchedulePipBuilder(qBuilder).Process;

            RunScheduler().AssertScheduled(p.PipId, q.PipId).AssertCacheMiss(p.PipId, q.PipId);

            // Modify f.
            ModifyFile(f);

            // Switch to G2.
            ResetPipGraphBuilder();

            ssd = SealDirectory(ssdRoot, SealDirectoryKind.SourceAllDirectories);

            pOperations = new Operation[] { Operation.ReadFile(f, doNotInfer: true), Operation.WriteFile(pOut) };
            pBuilder = CreatePipBuilder(pOperations);
            pBuilder.AddInputDirectory(ssd);
            pBuilder.WorkingDirectory = pWorkDir;
            p = SchedulePipBuilder(pBuilder).Process;

            RunScheduler(new global::BuildXL.Scheduler.SchedulerTestHooks()
            {
                IncrementalSchedulingStateAfterJournalScanAction = iss =>
                {
                    XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeDirty(p.PipId.ToNodeId()));
                }
            }).AssertScheduled(p.PipId).AssertCacheMiss(p.PipId);

            // Switch to G3.
            ResetPipGraphBuilder();

            ssd = SealDirectory(ssdRoot, SealDirectoryKind.SourceAllDirectories);

            qOperations = new Operation[] { Operation.ReadFile(f, doNotInfer: true), Operation.WriteFile(qOut) };
            qBuilder = CreatePipBuilder(qOperations);
            qBuilder.AddInputDirectory(ssd);
            qBuilder.WorkingDirectory = qWorkDir;
            q = SchedulePipBuilder(qBuilder).Process;

            RunScheduler(new global::BuildXL.Scheduler.SchedulerTestHooks()
            {
                IncrementalSchedulingStateAfterJournalScanAction = iss =>
                {
                    XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeDirty(q.PipId.ToNodeId()));
                }
            }).AssertScheduled(q.PipId).AssertCacheMiss(q.PipId);
        }

        [Fact]
        public void GAISSCannotBeReusedFromEngineStateWhenGraphChange()
        {
            // Graph G1: P
            // Graph G2: Q

            // Start with G1.
            // Build P.
            var pOperations = new Operation[] { Operation.ReadFile(CreateSourceFile()), Operation.WriteFile(CreateOutputFileArtifact()) };
            Process p = CreateAndSchedulePipBuilder(pOperations).Process;

            var result = RunScheduler().AssertScheduled(p.PipId);

            // Switch to G2.
            ResetPipGraphBuilder();

            // Build Q.
            var qOperations = new Operation[] { Operation.ReadFile(CreateSourceFile()), Operation.WriteFile(CreateOutputFileArtifact()) };
            Process q = CreateAndSchedulePipBuilder(qOperations).Process;

            result = RunScheduler(schedulerState: result.SchedulerState).AssertScheduled(q.PipId);

            AssertVerboseEventLogged(EventId.IncrementalSchedulingReuseState);
            AssertLogContains(false, "Attempt to reuse existing incremental scheduling state: " + ReuseKind.ChangedGraph);
        }

        [Fact]
        public void ChangeOutputDirectoryProducers()
        {
            // Graph G1: Q -> d -> P -> f
            // Graph G2: Q -> d -> R -> f

            // Start with G1.
            // Build P.
            FileArtifact f = CreateSourceFile();
            DirectoryArtifact d = CreateOutputDirectoryArtifact();
            FileArtifact dFile = CreateOutputFileArtifact(root: d.Path);

            var pOperations = new Operation[] { Operation.ReadFile(f), Operation.WriteFile(dFile, "P", doNotInfer: true) };
            ProcessBuilder pBuilder = CreatePipBuilder(pOperations);
            pBuilder.AddOutputDirectory(d.Path, SealDirectoryKind.Opaque);
            ProcessWithOutputs pWithOutputs = SchedulePipBuilder(pBuilder);

            // Build Q.
            FileArtifact g = CreateOutputFileArtifact();
            var qOperations = new Operation[] { Operation.CopyFile(dFile, g, doNotInfer: true) };
            ProcessBuilder qBuilder = CreatePipBuilder(qOperations);
            qBuilder.AddInputDirectory(pWithOutputs.ProcessOutputs.GetOpaqueDirectory(d.Path));
            qBuilder.AddOutputFile(g.Path);
            ProcessWithOutputs qWithOutputs = SchedulePipBuilder(qBuilder);

            RunScheduler().AssertScheduled(pWithOutputs.Process.PipId, qWithOutputs.Process.PipId);
            XAssert.AreEqual("P", ReadAllText(g));

            // Switch to G2.
            ResetPipGraphBuilder();

            // Build R.
            var rOperations = new Operation[] { Operation.ReadFile(f), Operation.WriteFile(dFile, "R", doNotInfer: true) };
            ProcessBuilder rBuilder = CreatePipBuilder(rOperations);
            rBuilder.AddOutputDirectory(d.Path, SealDirectoryKind.Opaque);
            ProcessWithOutputs rWithOutputs = SchedulePipBuilder(rBuilder);

            qBuilder = CreatePipBuilder(qOperations);
            qBuilder.AddInputDirectory(pWithOutputs.ProcessOutputs.GetOpaqueDirectory(d.Path));
            qBuilder.AddOutputFile(g.Path);
            qWithOutputs = SchedulePipBuilder(qBuilder);

            RunScheduler().AssertScheduled(rWithOutputs.Process.PipId, qWithOutputs.Process.PipId);
            XAssert.AreEqual("R", ReadAllText(g));
        }

        [Fact]
        public void ExtraSaltsInvalidatePip()
        {
            // Graph G1: P -- salt A
            // Graph G2: P -- salt B

            Configuration.Cache.CacheSalt = "A";

            // Start with G1.
            // Build P with salt A.
            FileArtifact source = CreateSourceFile();
            FileArtifact output = CreateOutputFileArtifact();

            var pOperations = new Operation[] { Operation.ReadFile(source), Operation.WriteFile(output) };
            Process p = CreateAndSchedulePipBuilder(pOperations).Process;

            RunScheduler().AssertScheduled(p.PipId);

            // Switch to G2.
            ResetPipGraphBuilder();

            Configuration.Cache.CacheSalt = "B";

            // Build P.
            p = CreateAndSchedulePipBuilder(pOperations).Process;

            RunScheduler().AssertScheduled(p.PipId).AssertCacheMiss(p.PipId);

            // Switch to G1.
            ResetPipGraphBuilder();

            Configuration.Cache.CacheSalt = "A";

            // Build P again.
            p = CreateAndSchedulePipBuilder(pOperations).Process;

            // P is scheduled because it gets dirty by salting to B.
            RunScheduler().AssertScheduled(p.PipId).AssertCacheHit(p.PipId);            
        }

        [Fact]
        public void DisappearingOutputDirectoryProducers()
        {
            // Graph G1: g -> Q -> OD/outFile -> P -> SSD
            // Graph G2: h -> R

            // Start with G1.
            // Build P.
            AbsolutePath sourceDirPath = CreateUniqueDirectory(SourceRoot, "SSD");
            FileArtifact f = CreateSourceFile(sourceDirPath);
            ModifyFile(f, "f0");
            DirectoryArtifact sourceDir = CreateAndScheduleSealDirectoryArtifact(sourceDirPath, SealDirectoryKind.SourceAllDirectories);
            
            
            DirectoryArtifact outputDir = CreateOutputDirectoryArtifact(ObjectRoot);
            FileArtifact outputFile = CreateOutputFileArtifact(root: outputDir.Path, "outFile");

            var pOperations = new Operation[] { Operation.CopyFile(f, outputFile, doNotInfer: true) };
            ProcessBuilder pBuilder = CreatePipBuilder(pOperations, description: "Pip-P");
            pBuilder.AddInputDirectory(sourceDir);
            pBuilder.AddOutputDirectory(outputDir.Path, SealDirectoryKind.Opaque);
            ProcessWithOutputs pWithOutputs = SchedulePipBuilder(pBuilder);

            // Build Q.
            FileArtifact g = CreateOutputFileArtifact();
            var qOperations = new Operation[] { Operation.CopyFile(outputFile, g, doNotInfer: true) };
            ProcessBuilder qBuilder = CreatePipBuilder(qOperations, description: "Pip-Q");
            qBuilder.AddInputDirectory(pWithOutputs.ProcessOutputs.GetOpaqueDirectory(outputDir.Path));
            qBuilder.AddOutputFile(g.Path);
            ProcessWithOutputs qWithOutputs = SchedulePipBuilder(qBuilder);

            RunScheduler().AssertScheduled(pWithOutputs.Process.PipId, qWithOutputs.Process.PipId);
            XAssert.AreEqual("f0", ReadAllText(g));

            // Switch to G2.
            ResetPipGraphBuilder();

            // Build R.
            FileArtifact h = CreateOutputFileArtifact();
            var rOperations = new Operation[] { Operation.WriteFile(h) };
            ProcessBuilder rBuilder = CreatePipBuilder(rOperations, description: "Pip-R");
            ProcessWithOutputs rWithOutputs = SchedulePipBuilder(rBuilder);

            // Modify f to invalidate P.
            ModifyFile(f, "f1");
            RunScheduler().AssertScheduled(rWithOutputs.Process.PipId);
            
            // Ensure that g has not changed.
            XAssert.AreEqual("f0", ReadAllText(g));

            // Switch to G1.
            ResetPipGraphBuilder();

            sourceDir = CreateAndScheduleSealDirectoryArtifact(sourceDirPath, SealDirectoryKind.SourceAllDirectories);

            pBuilder = CreatePipBuilder(pOperations, description: "Pip-P");
            pBuilder.AddInputDirectory(sourceDir);
            pBuilder.AddOutputDirectory(outputDir.Path, SealDirectoryKind.Opaque);
            pWithOutputs = SchedulePipBuilder(pBuilder);

            // Build Q.
            qBuilder = CreatePipBuilder(qOperations, description: "Pip-Q");
            qBuilder.AddInputDirectory(pWithOutputs.ProcessOutputs.GetOpaqueDirectory(outputDir.Path));
            qBuilder.AddOutputFile(g.Path);
            qWithOutputs = SchedulePipBuilder(qBuilder);

            // Modify f to invalidate P.
            ModifyFile(f, "f2");

            RunScheduler().AssertScheduled(pWithOutputs.Process.PipId, qWithOutputs.Process.PipId);
            XAssert.AreEqual("f2", ReadAllText(g));
        }
    }
}
