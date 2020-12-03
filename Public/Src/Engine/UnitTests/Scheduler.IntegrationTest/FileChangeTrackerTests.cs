// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using BuildXL.Native.IO;
using BuildXL.Pips.Builders;
using BuildXL.Utilities;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Scheduler
{
    /// <summary>
    /// Tests related to the use of file change tracker.
    /// </summary>
    public sealed class FileChangeTrackerTests : SchedulerIntegrationTestBase
    {
        public FileChangeTrackerTests(ITestOutputHelper output) : base(output)
        {
            // Ensure scanning change journal.
            Configuration.Engine.ScanChangeJournal = true;
        }

        [TheoryIfSupported(requiresJournalScan: true)]
        [MemberData(nameof(TruthTable.GetTable), 1, MemberType = typeof(TruthTable))]
        public void TrackerLoadFailureShouldNotResultInUnderbuild(bool updateFileContentTableByScanningChangeJournal)
        {
            // This test is created due to underbuild resulted from updating file content table during change journal scan.
            // Here is the timeline of the scenario regarding source file 'srcA':
            //
            // Time         FileContentTable          FileChangeTracker        FileSystem           Description
            // ----------------------------------------------------------------------------------------------------------------------------------
            // T0                                                              srcA:#1,USN1         Initial.
            // T1                                                              srcA:#1,USN1         Build P where srcA is read.
            //                                                                                      No journal scan because no FileChangeTracker.
            // T2           FID(srcA)->#1,USN1        FID(srcA)->USN1          srcA:#1,USN1         After building P: cache miss.
            // T3           FID(srcA)->#1,USN1        FID(srcA)->USN1          srcA:#2,USN2         After srcA is modified on FileSystem.
            // T4           FID(srcA)->#1,USN1                                 srcA:#2,USN2         FileChangeTracker is destroyed.
            // T5           FID(srcA)->#1,USN1                                 srcA:#2,USN2         Build Q where srcA is only probed.
            //                                                                                      No journal scan because no FileChangeTracker.
            // T6           FID(srcA)->#1,USN1        FID(srcA)->USN2          srcA:#2,USN2         After building Q: cache miss.
            // T7           FID(srcA)->#1,USN1        FID(srcA)->USN2          srcA:#2,USN3         Modified timestamp of srcA.
            // T8           FID(srcA)->#1,USN1        FID(srcA)->USN2          srcA:#2,USN3         Build P again where srcA is read.
            // T9           FID(srcA)->#1,USN3        FID(srcA)->USN2          srcA:#2,USN3         After journal scan, but before executing P.
            // T10          FID(srcA)->#1,USN3        FID(srcA)->USN3          srcA:#2,USN3         After building P: cache hit.
            //
            // At T6, FileChangeTracker tracks srcA because srcA is probed.
            // At T9 FileContentTable is updated based on the change journal scan from the checkpoint set at T6. From T6 to T8, there
            // is only basic info change, i.e., timestamp change, that doesn't change the content hash of srcA. So, FID(srcA)'s entry
            // in FileContentTable is eligible for update. However, if we simply update it with USN3, i.e., the current USN, then there's
            // a mismatch between the content hash in FileContentTable and FileSystem that results in cache hit, i.e., underbuild.
            //
            // To handle the above case, we only update FileContentTable if the USN matches the one tracked by FileChangeTracker. In this case
            // since FileChangeTracker has USN2 for srcA, and FileContentTable has USN1, then we should not update FileContentTable.

            Configuration.Schedule.UpdateFileContentTableByScanningChangeJournal = updateFileContentTableByScanningChangeJournal;

            AbsolutePath path = CreateUniqueSourcePath().Combine(Context.PathTable, "srcA");
            FileArtifact srcA = CreateSourceFile(path);

            // Populate file content table with srcA's content via P.
            var pOperations = new[] { Operation.ReadFile(srcA), Operation.WriteFile(CreateOutputFileArtifact()) };
            var p = CreateAndSchedulePipBuilder(pOperations);

            var result = RunScheduler().AssertCacheMiss(p.Process.PipId);

            // Modify srcA.
            File.AppendAllText(ArtifactToString(srcA), Guid.NewGuid().ToString());

            // Destroy file change tracker.
            FileUtilities.DeleteFile(result.Config.Layout.SchedulerFileChangeTrackerFile.ToString(Context.PathTable), retryOnFailure: true);

            ResetPipGraphBuilder();

            // Make srcA tracked and probe via Q.
            var ssd = PipConstructionHelper.SealDirectorySource(path);

            var qOperations = new[] { Operation.Probe(srcA, doNotInfer: true), Operation.WriteFile(CreateOutputFileArtifact()) };
            var qBuilder = CreatePipBuilder(qOperations);
            qBuilder.AddInputDirectory(ssd);

            var q = SchedulePipBuilder(qBuilder);

            RunScheduler().AssertCacheMiss(q.Process.PipId);

            // Modify basic file info.
            var creationTime = File.GetCreationTime(ArtifactToString(srcA));
            File.SetCreationTime(ArtifactToString(srcA), creationTime.Add(TimeSpan.FromSeconds(10)));

            // Switch back to P.
            ResetPipGraphBuilder();

            p = CreateAndSchedulePipBuilder(pOperations);

            // P should be miss because the content of srcA changed.
            RunScheduler().AssertCacheMiss(p.Process.PipId);
        }
    }
}
