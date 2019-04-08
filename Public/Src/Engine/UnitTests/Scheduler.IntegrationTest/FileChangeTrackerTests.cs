// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Native.IO;
using BuildXL.Pips.Builders;
using BuildXL.Utilities;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities.Xunit;
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

        [FactIfSupported(requiresJournalScan: true)]
        public void TrackerLoadFailureShouldNotResultInUnderbuild()
        {
            AbsolutePath path = CreateUniqueSourcePath().Combine(Context.PathTable, "MySrc");
            FileArtifact srcA = CreateSourceFile(path);

            // Populate file content table with srcA's content via P.
            var pOperations = new[] { Operation.ReadFile(srcA), Operation.WriteFile(CreateOutputFileArtifact()) };
            var p = CreateAndSchedulePipBuilder(pOperations);

            var result = RunScheduler().AssertCacheMiss(p.Process.PipId);

            // Modify srcA.
            File.AppendAllText(ArtifactToString(srcA), Guid.NewGuid().ToString());

            // Destroy file change tracker.
            FileUtilities.DeleteFile(result.Config.Layout.SchedulerFileChangeTrackerFile.ToString(Context.PathTable), waitUntilDeletionFinished: true);

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
