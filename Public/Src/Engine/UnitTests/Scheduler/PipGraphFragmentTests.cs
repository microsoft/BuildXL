// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Ipc.Interfaces;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using Process = BuildXL.Pips.Operations.Process;
using ProcessOutputs = BuildXL.Pips.Builders.ProcessOutputs;

namespace Test.BuildXL.Scheduler
{
    public class PipGraphFragmentTests : PipTestBase
    {
        public PipGraphFragmentTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestBasicCreation()
        {
            var fragment = CreatePipGraphFragmentTest(nameof(TestBasicCreation));
            var processBuilder = fragment.GetProcessBuilder();
            var argumentsBuilder = new ArgumentsBuilder(processBuilder);
            AbsolutePath outputPathToVerify;
            argumentsBuilder
                .AddInputFileOption("/input:", fragment.CreateSourceFile("f"))
                .AddOutputFileOption("/output:", outputPathToVerify = fragment.CreateOutputFile("g").Path)
                .Finish();
            (Process process, ProcessOutputs _) = fragment.ScheduleProcessBuilder(processBuilder);

            var graph = SerializeAndDeserializeFragments(fragment);

            VerifyGraphSuccessfullyConstructed(graph);
            VerifyProducerExists(graph, fragment, outputPathToVerify);
            VerifyMatchingArguments(graph, fragment, process);
        }

        [Fact]
        public void TestBasicDependencyBetweenFragments()
        {
            var fragment1 = CreatePipGraphFragmentTest(nameof(TestBasicDependencyBetweenFragments) + "1");
            var processBuilder1 = fragment1.GetProcessBuilder();
            var argumentsBuilder1 = new ArgumentsBuilder(processBuilder1);
            AbsolutePath outputPathToVerify1;
            argumentsBuilder1
                .AddInputFileOption("/input:", fragment1.CreateSourceFile("f"))
                .AddOutputFileOption("/output:", outputPathToVerify1 = fragment1.CreateOutputFile("g").Path)
                .Finish();
            (Process process1, ProcessOutputs _) = fragment1.ScheduleProcessBuilder(processBuilder1);

            // Fragment2 depends on fragment1 on output file g produced by fragment1.
            var fragment2 = CreatePipGraphFragmentTest(nameof(TestBasicDependencyBetweenFragments) + "2");
            var processBuilder2 = fragment2.GetProcessBuilder();
            var argumentsBuilder2 = new ArgumentsBuilder(processBuilder2);
            AbsolutePath outputPathToVerify2;
            argumentsBuilder2
                .AddInputFileOption("/input:", fragment2.CreateOutputFile("g")) // fragment2 depends on g without any producer within the fragment.
                .AddOutputFileOption("/output:", outputPathToVerify2 = fragment2.CreateOutputFile("h").Path)
                .Finish();
            (Process process2, ProcessOutputs _) = fragment2.ScheduleProcessBuilder(processBuilder2);

            var graph = SerializeAndDeserializeFragments(fragment1, fragment2);

            VerifyGraphSuccessfullyConstructed(graph);
            VerifyProducerExists(graph, fragment1, outputPathToVerify1);
            VerifyProducerExists(graph, fragment2, outputPathToVerify2);
            VerifyMatchingArguments(graph, fragment1, process1);
            VerifyMatchingArguments(graph, fragment2, process2);
        }

        [Fact]
        public void TestBasicAddIndependentFragments()
        {
            var fragment1 = CreatePipGraphFragmentTest(nameof(TestBasicAddIndependentFragments) + "1");
            var processBuilder1 = fragment1.GetProcessBuilder();
            var argumentsBuilder1 = new ArgumentsBuilder(processBuilder1);
            AbsolutePath outputPathToVerify1;
            argumentsBuilder1
                .AddInputFileOption("/input:", fragment1.CreateSourceFile("f"))
                .AddOutputFileOption("/output:", outputPathToVerify1 = fragment1.CreateOutputFile("g").Path)
                .Finish();
            (Process process1, ProcessOutputs _) = fragment1.ScheduleProcessBuilder(processBuilder1);

            // Fragment2 is independent of fragment1.
            var fragment2 = CreatePipGraphFragmentTest(nameof(TestBasicAddIndependentFragments) + "2");
            var processBuilder2 = fragment2.GetProcessBuilder();
            var argumentsBuilder2 = new ArgumentsBuilder(processBuilder2);
            AbsolutePath outputPathToVerify2;
            argumentsBuilder2
                .AddInputFileOption("/input:", fragment2.CreateSourceFile("i"))
                .AddOutputFileOption("/output:", outputPathToVerify2 = fragment2.CreateOutputFile("h").Path)
                .Finish();
            (Process process2, ProcessOutputs _) = fragment2.ScheduleProcessBuilder(processBuilder2);

            var graph = SerializeAndDeserializeIndependentFragments(fragment1, fragment2);

            VerifyGraphSuccessfullyConstructed(graph);
            VerifyProducerExists(graph, fragment1, outputPathToVerify1);
            VerifyProducerExists(graph, fragment2, outputPathToVerify2);
            VerifyMatchingArguments(graph, fragment1, process1);
            VerifyMatchingArguments(graph, fragment2, process2);
        }

        [Fact]
        public void TestAddingAndUnifyingIpcPip()
        {
            var fragment = CreatePipGraphFragmentTest(nameof(TestAddingAndUnifyingIpcPip));
            (IIpcMoniker moniker, PipId servicePipId) = TestPipGraphFragmentUtils.CreateService(fragment);

            var processBuilder = fragment.GetProcessBuilder();
            var argumentsBuilder = new ArgumentsBuilder(processBuilder);
            FileArtifact outputFileToVerify = fragment.CreateOutputFile("g");
            AbsolutePath outputDirectoryToVerify = fragment.CreateOutputDirectory("d").Path;
            argumentsBuilder
                .AddInputFileOption("/input:", fragment.CreateSourceFile("f"))
                .AddOutputFileOption("/output:", outputFileToVerify.Path)
                .AddOutputDirectoryOption("/outputDir:", outputDirectoryToVerify)
                .Finish();
            (Process process, ProcessOutputs processOutputs) = fragment.ScheduleProcessBuilder(processBuilder);

            XAssert.IsTrue(processOutputs.TryGetOutputDirectory(outputDirectoryToVerify, out var outputDirectory));

            var addFileProcessBuilder = fragment.GetIpcProcessBuilder();
            new ArgumentsBuilder(addFileProcessBuilder)
                .AddStringOption("--command ", "addFile")
                .AddIpcMonikerOption("--ipcMoniker ", moniker)
                .AddInputFileOption("--file ", outputFileToVerify)
                .AddInputDirectoryOption("--directory ", outputDirectory.Root)
                .AddFileIdOption("--fileId ", outputFileToVerify)
                .AddDirectoryIdOption("--directoryId ", outputDirectory.Root)
                .AddVsoHashOption("--vsoHash ", outputFileToVerify)
                .Finish();

            FileArtifact ipcOutputFileToVerify;

            IpcPip ipcPip = fragment.ScheduleIpcPip(
                moniker,
                servicePipId,
                addFileProcessBuilder,
                ipcOutputFileToVerify = fragment.CreateOutputFile("add"),
                false);

            var graph = SerializeAndDeserializeIndependentFragments(fragment, fragment);

            VerifyGraphSuccessfullyConstructed(graph);
            VerifyProducerExists(graph, fragment, outputFileToVerify.Path);
            VerifyProducerExists(graph, fragment, outputDirectoryToVerify);
            VerifyProducerExists(graph, fragment, ipcOutputFileToVerify);

            var remappedOutputFile = FileArtifact.CreateOutputFile(RemapFragmentPath(fragment, outputFileToVerify.Path));
            var remappedOutputDirectory = DirectoryArtifact.CreateWithZeroPartialSealId(RemapFragmentPath(fragment, outputDirectory.Root));

            PipData expectedArguments = new ArgumentsDataBuilder(Context.StringTable)
                .AddStringOption("--command ", "addFile")
                .AddIpcMonikerOption("--ipcMoniker ", moniker)
                .AddPathOption("--file ", remappedOutputFile.Path)
                .AddPathOption("--directory ", remappedOutputDirectory.Path)
                .AddFileIdOption("--fileId ", remappedOutputFile)
                .AddDirectoryIdOption("--directoryId ", remappedOutputDirectory)
                .AddVsoHashOption("--vsoHash ", remappedOutputFile)
                .Finish();

            VerifyResultingArguments(graph, fragment, ipcOutputFileToVerify, expectedArguments);
        }

        [Fact]
        public void TestUnifyProcessPip()
        {
            var fragment = CreatePipGraphFragmentTest(nameof(TestUnifyProcessPip));
            var processBuilder = fragment.GetProcessBuilder();
            var argumentsBuilder = new ArgumentsBuilder(processBuilder);
            AbsolutePath outputPathToVerify;
            argumentsBuilder
                .AddInputFileOption("/input:", fragment.CreateSourceFile("f"))
                .AddOutputFileOption("/output:", outputPathToVerify = fragment.CreateOutputFile("g").Path)
                .Finish();
            (Process process, ProcessOutputs _) = fragment.ScheduleProcessBuilder(processBuilder);

            var graph = SerializeAndDeserializeIndependentFragments(fragment, fragment);

            VerifyGraphSuccessfullyConstructed(graph);
            VerifyProducerExists(graph, fragment, outputPathToVerify);
            VerifyMatchingArguments(graph, fragment, process);
        }

        [Fact]
        public void TestIpcPipConsumptionFromOtherFragment()
        {
            // Create fragment 1:
            //
            // h -> R -> g -> Q -> f -> P -> e
            
            var fragment1 = CreatePipGraphFragmentTest(nameof(TestIpcPipConsumptionFromOtherFragment) + "1");
            var processBuilderP = fragment1.GetProcessBuilder();
            var argumentsBuilderP = new ArgumentsBuilder(processBuilderP);
            var f = fragment1.CreateOutputFile("f");
            argumentsBuilderP
                .AddInputFileOption("/input:", fragment1.CreateSourceFile("e"))
                .AddOutputFileOption("/output:", f.Path)
                .Finish();
            (Process processP, ProcessOutputs outputsP)  = fragment1.ScheduleProcessBuilder(processBuilderP);

            var processBuilderQ = fragment1.GetProcessBuilder();
            var argumentsBuilderQ = new ArgumentsBuilder(processBuilderQ);
            var g = fragment1.CreateOutputFile("g");
            argumentsBuilderQ
                .AddInputFileOption("/input:", outputsP.TryGetOutputFile(f.Path, out var fAsOutput) ? fAsOutput : FileArtifact.Invalid)
                .AddOutputFileOption("/output:", g.Path)
                .Finish();
            (Process processQ, ProcessOutputs outputsQ) = fragment1.ScheduleProcessBuilder(processBuilderQ);

            var processBuilderR = fragment1.GetProcessBuilder();
            var argumentsBuilderR = new ArgumentsBuilder(processBuilderR);
            var h = fragment1.CreateOutputFile("h");
            argumentsBuilderR
                .AddInputFileOption("/input:", outputsQ.TryGetOutputFile(g.Path, out var gAsOutput) ? gAsOutput : FileArtifact.Invalid)
                .AddOutputFileOption("/output:", h.Path)
                .Finish();
            (Process processR, ProcessOutputs outputsR) = fragment1.ScheduleProcessBuilder(processBuilderR);

            // Create fragment 2:
            //
            // z -> C -> x -> A -> w
            //      |
            //      + -> y -> B -> v

            var fragment2 = CreatePipGraphFragmentTest(nameof(TestIpcPipConsumptionFromOtherFragment) + "2");
            var processBuilderA = fragment2.GetProcessBuilder();
            var argumentsBuilderA = new ArgumentsBuilder(processBuilderA);
            var x = fragment2.CreateOutputFile("x");
            argumentsBuilderA
                .AddInputFileOption("/input:", fragment2.CreateSourceFile("w"))
                .AddOutputFileOption("/output:", x.Path)
                .Finish();
            (Process processA, ProcessOutputs outputsA) = fragment2.ScheduleProcessBuilder(processBuilderA);

            var processBuilderB = fragment2.GetProcessBuilder();
            var argumentsBuilderB = new ArgumentsBuilder(processBuilderB);
            var y = fragment2.CreateOutputFile("y");
            argumentsBuilderB
                .AddInputFileOption("/input:", fragment2.CreateSourceFile("v"))
                .AddOutputFileOption("/output:", y.Path)
                .Finish();
            (Process processB, ProcessOutputs outputsB) = fragment2.ScheduleProcessBuilder(processBuilderB);

            var processBuilderC = fragment2.GetProcessBuilder();
            var argumentsBuilderC = new ArgumentsBuilder(processBuilderC);
            var z = fragment2.CreateOutputFile("z");
            argumentsBuilderC
                .AddInputFileOption("/input:", outputsB.TryGetOutputFile(y.Path, out var yAsOutput) ? yAsOutput : FileArtifact.Invalid)
                .AddInputFileOption("/input:", outputsA.TryGetOutputFile(x.Path, out var xAsOutput) ? xAsOutput : FileArtifact.Invalid)
                .AddOutputFileOption("/output:", z.Path)
                .Finish();
            (Process processC, ProcessOutputs outputsC) = fragment2.ScheduleProcessBuilder(processBuilderC);

            // Drop z and h in fragment 2.
            (IIpcMoniker moniker, PipId servicePipId) = TestPipGraphFragmentUtils.CreateService(fragment2);

            var addZBuilder = fragment2.GetIpcProcessBuilder();
            new ArgumentsBuilder(addZBuilder)
                .AddStringOption("--command ", "addFile")
                .AddIpcMonikerOption("--ipcMoniker ", moniker)
                .AddInputFileOption("--file ", z)
                .Finish();

            IpcPip ipcPipZ = fragment2.ScheduleIpcPip(
                moniker,
                servicePipId,
                addZBuilder,
                fragment2.CreateOutputFile("addZ"),
                false);

            var addHBuilder = fragment2.GetIpcProcessBuilder();
            new ArgumentsBuilder(addHBuilder)
                .AddStringOption("--command ", "addFile")
                .AddIpcMonikerOption("--ipcMoniker ", moniker)
                .AddInputFileOption("--file ", fragment2.CreateOutputFile("h")) // h is created by fragment 1.
                .Finish();

            IpcPip ipcPipH = fragment2.ScheduleIpcPip(
                moniker,
                servicePipId,
                addHBuilder,
                fragment2.CreateOutputFile("addH"),
                false);

            var graph = SerializeAndDeserializeFragments(fragment1, fragment2);

            VerifyGraphSuccessfullyConstructed(graph);
        }

        #region Serialization

        private string GetFragmentPath(TestPipGraphFragment fragment) => Path.Combine(TemporaryDirectory, fragment.ModuleName);

        private string GetIndexedFragmentPath(TestPipGraphFragment fragment, int index) => GetFragmentPath(fragment) + "-" + index;

        private PipGraph SerializeAndDeserializeFragments(params TestPipGraphFragment[] fragments)
        {
            SerializeFragments(fragments);
            return DeserializeFragments(true, fragments);
        }

        private PipGraph SerializeAndDeserializeIndependentFragments(params TestPipGraphFragment[] fragments)
        {
            SerializeFragments(fragments);
            return DeserializeFragments(false, fragments);
        }

        private void SerializeFragments(params TestPipGraphFragment[] fragments)
        {
            for (int i = 0; i < fragments.Length; ++i)
            {
                fragments[i].Serialize(GetIndexedFragmentPath(fragments[i], i));
            }
        }

        private PipGraph DeserializeFragments(bool dependent, params TestPipGraphFragment[] fragments)
        {
            var fragmentManager = new PipGraphFragmentManager(LoggingContext, Context, PipGraphBuilder, default);

            for (int i = 0; i < fragments.Length; ++i)
            {
                TestPipGraphFragment fragment = fragments[i];

                bool success = fragmentManager.AddFragmentFileToGraph(
                    AbsolutePath.Create(Context.PathTable, GetIndexedFragmentPath(fragment, i)),
                    fragment.ModuleName,
                    i > 0 && dependent
                    ? new[] { AbsolutePath.Create(Context.PathTable, GetIndexedFragmentPath(fragments[i - 1], i - 1)) }
                    : new AbsolutePath[0]);

                XAssert.IsTrue(success, $"Adding fragment {fragment.ModuleName} from file '{GetFragmentPath(fragment)}' to graph is unsuccessful");
            }

            Task.WaitAll(fragmentManager.GetAllFragmentTasks().Select(t => t.Item2).ToArray());

            XAssert.IsTrue(fragmentManager.GetAllFragmentTasks().All(t => t.Item2.Result), "Adding all fragments to graph is unsuccessful");

            return PipGraphBuilder.Build();
        }

        #endregion Serialization

        #region Verification

        /// <summary>
        /// Verifies that the file/directory output by a fragment exists in the resulting graph.
        /// </summary>
        /// <param name="graph">Resulting graph.</param>
        /// <param name="fragmentOrigin">Graph fragment where the output originates.</param>
        /// <param name="outputPath">Path to output file/directory.</param>
        private void VerifyProducerExists(PipGraph graph, TestPipGraphFragment fragmentOrigin, AbsolutePath outputPath)
        {
            AbsolutePath remappedOutputPath = RemapFragmentPath(fragmentOrigin, outputPath);
            var pipId = graph.TryGetProducer(FileArtifact.CreateOutputFile(remappedOutputPath));

            if (!pipId.IsValid)
            {
                pipId = graph.TryGetProducer(DirectoryArtifact.CreateWithZeroPartialSealId(remappedOutputPath));
            }

            XAssert.IsTrue(pipId.IsValid, $"Producer of '{outputPath.ToString(fragmentOrigin.Context.PathTable)}' from fragment '{fragmentOrigin.ModuleName}' could not be found in the resulting graph");
        }

        /// <summary>
        /// Verify that graph is successfully constructed.
        /// </summary>
        private void VerifyGraphSuccessfullyConstructed(PipGraph graph)
        {
            XAssert.IsNotNull(graph, "Failed in constructing graph");
        }

        /// <summary>
        /// Verifies that the arguments constructed in the fragment (string) matches with the one in the resulting graph.
        /// </summary>
        /// <param name="graph">Resulting graph.</param>
        /// <param name="fragmentOrigin">Graph fragment where the arguments are constructed.</param>
        /// <param name="processInFragment">Process in fragments whose arguments are to be verified.</param>
        private void VerifyMatchingArguments(PipGraph graph, TestPipGraphFragment fragmentOrigin, Process processInFragment)
        {
            var outputPath = processInFragment.FileOutputs.First().ToFileArtifact().Path;
            var pipId = graph.TryGetProducer(FileArtifact.CreateOutputFile(RemapFragmentPath(fragmentOrigin, outputPath)));
            XAssert.IsTrue(pipId.IsValid);

            Process processInGraph = graph.PipTable.HydratePip(pipId, PipQueryContext.PipGraphGetProducingPip) as Process;
            XAssert.IsNotNull(processInGraph);

            string argumentsInFragment = processInFragment.Arguments.ToString(fragmentOrigin.Context.PathTable).ToUpperInvariant();
            string argumentsInGraph = processInGraph.Arguments.ToString(Context.PathTable).ToUpperInvariant();

            XAssert.AreEqual(argumentsInFragment, argumentsInGraph);
        }

        /// <summary>
        /// Verifies the resulting arguments after full graph construction.
        /// </summary>
        /// <param name="graph">Resulting graph.</param>
        /// <param name="fragmentOrigin">Graph fragment where the arguments are constructed.</param>
        /// <param name="outputInFragmentOrigin">Output file to identify pip.</param>
        /// <param name="expectedArguments">Expected arguments.</param>
        private void VerifyResultingArguments(PipGraph graph, TestPipGraphFragment fragmentOrigin, FileArtifact outputInFragmentOrigin, PipData expectedArguments)
        {
            var pipId = graph.TryGetProducer(FileArtifact.CreateOutputFile(RemapFragmentPath(fragmentOrigin, outputInFragmentOrigin)));
            XAssert.IsTrue(pipId.IsValid);

            Pip pip = graph.PipTable.HydratePip(pipId, PipQueryContext.PipGraphGetProducingPip);
            XAssert.IsNotNull(pip);

            PipData actualArguments = PipData.Invalid;

            if (pip is Process process)
            {
                actualArguments = process.Arguments;
            }
            else if (pip is IpcPip ipcPip)
            {
                actualArguments = ipcPip.MessageBody;
            }
            else
            {
                XAssert.Fail("No arguments associated with pip");
            }

            string expected = expectedArguments.ToString(Context.PathTable).ToUpperInvariant();
            string actual = actualArguments.ToString(Context.PathTable).ToUpperInvariant();

            XAssert.AreEqual(expected, actual);
        }

        #endregion Verification

        /// <summary>
        /// Remaps a path produced by a fragment to path of the resulting graph.
        /// </summary>
        /// <param name="fragment">Fragment where the path originates.</param>
        /// <param name="path">A path.</param>
        /// <returns></returns>
        private AbsolutePath RemapFragmentPath(TestPipGraphFragment fragment, AbsolutePath path) =>
            AbsolutePath.Create(Context.PathTable, path.ToString(fragment.Context.PathTable));

        /// <summary>
        /// Creates an instance of <see cref="TestPipGraphFragment"/>
        /// </summary>
        /// <param name="moduleName">Module name.</param>
        /// <returns>An instance of <see cref="TestPipGraphFragment"/>.</returns>
        protected virtual TestPipGraphFragment CreatePipGraphFragmentTest(string moduleName)
        {
            return CreatePipGraphFragment(moduleName, useTopSort: false);
        }
    }
}
