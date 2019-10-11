// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using BuildXL.Ipc.Interfaces;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using Process = BuildXL.Pips.Operations.Process;
using ProcessOutputs = BuildXL.Pips.Builders.ProcessOutputs;

namespace Test.BuildXL.Scheduler
{
    public sealed class PipGraphFragmentTopSortTests : PipTestBase
    {
        public PipGraphFragmentTopSortTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestTopSort()
        {
            // Create fragment :
            //
            // z -> C --------+----> x -> A -> w
            //      |         |
            //      |         |
            //      + -> y -> B -> v

            var fragment = CreatePipGraphFragment(nameof(TestTopSort), useTopSort: true);
            var processBuilderA = fragment.GetProcessBuilder();
            var argumentsBuilderA = new ArgumentsBuilder(processBuilderA);
            var x = fragment.CreateOutputFile("x");
            argumentsBuilderA
                .AddInputFileOption("/input:", fragment.CreateSourceFile("w"))
                .AddOutputFileOption("/output:", x.Path)
                .Finish();
            (Process processA, ProcessOutputs outputsA) = fragment.ScheduleProcessBuilder(processBuilderA);

            var processBuilderB = fragment.GetProcessBuilder();
            var argumentsBuilderB = new ArgumentsBuilder(processBuilderB);
            var y = fragment.CreateOutputFile("y");
            argumentsBuilderB
                .AddInputFileOption("/input:", fragment.CreateSourceFile("v"))
                .AddInputFileOption("/input:", outputsA.TryGetOutputFile(x.Path, out var xAsOutput) ? xAsOutput : FileArtifact.Invalid)
                .AddOutputFileOption("/output:", y.Path)
                .Finish();
            (Process processB, ProcessOutputs outputsB) = fragment.ScheduleProcessBuilder(processBuilderB);

            var processBuilderC = fragment.GetProcessBuilder();
            var argumentsBuilderC = new ArgumentsBuilder(processBuilderC);
            var z = fragment.CreateOutputFile("z");
            argumentsBuilderC
                .AddInputFileOption("/input:", outputsB.TryGetOutputFile(y.Path, out var yAsOutput) ? yAsOutput : FileArtifact.Invalid)
                .AddInputFileOption("/input:", outputsA.TryGetOutputFile(x.Path, out var xAsOutput2) ? xAsOutput2 : FileArtifact.Invalid)
                .AddOutputFileOption("/output:", z.Path)
                .Finish();
            (Process processC, ProcessOutputs outputsC) = fragment.ScheduleProcessBuilder(processBuilderC);

            // Drop z and y.
            var serviceRelatedPips = new TestPipGraphFragmentUtils.ServiceRelatedPips();
            (IIpcMoniker moniker, PipId servicePipId) = TestPipGraphFragmentUtils.CreateService(fragment, serviceRelatedPips);

            var addZBuilder = fragment.GetIpcProcessBuilder();
            new ArgumentsBuilder(addZBuilder)
                .AddStringOption("--command ", "addFile")
                .AddIpcMonikerOption("--ipcMoniker ", moniker)
                .AddInputFileOption("--file ", z)
                .Finish();

            IpcPip ipcPipZ = fragment.ScheduleIpcPip(
                moniker,
                servicePipId,
                addZBuilder,
                fragment.CreateOutputFile("addZ"),
                false);

            var addYBuilder = fragment.GetIpcProcessBuilder();
            new ArgumentsBuilder(addYBuilder)
                .AddStringOption("--command ", "addFile")
                .AddIpcMonikerOption("--ipcMoniker ", moniker)
                .AddInputFileOption("--file ", y)
                .Finish();

            IpcPip ipcPipY = fragment.ScheduleIpcPip(
                moniker,
                servicePipId,
                addYBuilder,
                fragment.CreateOutputFile("addY"),
                false);

            var sortedPips = new PipGraphFragmentTopSort(fragment.PipGraph).Sort();

            XAssert.AreEqual(10, sortedPips.Count); // There are ten layers.
            XAssert.IsTrue(sortedPips[0].All(p => p is ModulePip)); // 0th layer is module pips.
            XAssert.IsTrue(sortedPips[1].All(p => p is SpecFilePip)); // 1st layer is spec pips.
            XAssert.IsTrue(sortedPips[2].All(p => p is ValuePip)); // 2nd layer is value pips.

            for (int i = 3; i <= 5; ++i)
            {
                // 3rd, 4th, and 5th layer are service related pips.
                XAssert.AreEqual(1, sortedPips[i].Count);
                XAssert.IsTrue(ServicePipKindUtil.IsServiceStartShutdownOrFinalizationPip(sortedPips[i][0]));
            }

            // 6th layer contains pip A and create drop pip.
            XAssert.AreEqual(2, sortedPips[6].Count);
            XAssert.Contains(sortedPips[6], serviceRelatedPips.Create, processA);

            // 7th layer contains pip B.
            XAssert.AreEqual(1, sortedPips[7].Count);
            XAssert.Contains(sortedPips[7], processB);

            // 8th layer contains pip C and create drop pip y.
            XAssert.AreEqual(2, sortedPips[8].Count);
            XAssert.Contains(sortedPips[8], ipcPipY, processC);

            // 9th layer contains drop pip z.
            XAssert.AreEqual(1, sortedPips[9].Count);
            XAssert.Contains(sortedPips[9], ipcPipZ);
        }
    }
}
