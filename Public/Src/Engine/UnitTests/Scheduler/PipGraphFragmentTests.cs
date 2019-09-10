// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Ipc.Interfaces;
using BuildXL.Pips;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using Process = global::BuildXL.Pips.Operations.Process;
using ProcessOutputs = global::BuildXL.Pips.Builders.ProcessOutputs;

namespace Test.BuildXL.Scheduler
{
    public sealed class PipGraphFragmentTests : PipTestBase
    {
        public PipGraphFragmentTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestBasicCreation()
        {
            var fragment = CreatePipGraphFragment(nameof(TestBasicCreation));
            var processBuilder = fragment.GetProcessBuilder();
            var argumentsBuilder = new ArgumentsBuilder(processBuilder);
            AbsolutePath outputPathToVerify;
            argumentsBuilder
                .AddInputOption("input", fragment.CreateSourceFile("f"))
                .AddOutputOption("output", outputPathToVerify = fragment.CreateOutputFile("g").Path);
            (Process process, ProcessOutputs _) = fragment.ScheduleProcessBuilder(processBuilder);

            var graph = SerializeDeserializeFragmentsSynchronously(fragment);

            VerifyGraphSuccessfullyConstructed(graph);
            VerifyProducerExists(graph, fragment, outputPathToVerify);
            VerifyMatchingArguments(graph, fragment, process);
        }

        [Fact]
        public void TestBasicDependencyBetweenFragments()
        {
            var fragment1 = CreatePipGraphFragment(nameof(TestBasicDependencyBetweenFragments) + "1");
            var processBuilder1 = fragment1.GetProcessBuilder();
            var argumentsBuilder1 = new ArgumentsBuilder(processBuilder1);
            AbsolutePath outputPathToVerify1;
            argumentsBuilder1
                .AddInputOption("input", fragment1.CreateSourceFile("f"))
                .AddOutputOption("output", outputPathToVerify1 = fragment1.CreateOutputFile("g").Path);
            (Process process1, ProcessOutputs _) = fragment1.ScheduleProcessBuilder(processBuilder1);

            // Fragment2 depends on fragment1 on output file g produced by fragment1.
            var fragment2 = CreatePipGraphFragment(nameof(TestBasicDependencyBetweenFragments) + "2");
            var processBuilder2 = fragment2.GetProcessBuilder();
            var argumentsBuilder2 = new ArgumentsBuilder(processBuilder2);
            AbsolutePath outputPathToVerify2;
            argumentsBuilder2
                .AddInputOption("input", fragment2.CreateOutputFile("g")) // fragment2 depends on g without any producer within the fragment.
                .AddOutputOption("output", outputPathToVerify2 = fragment2.CreateOutputFile("h").Path);
            (Process process2, ProcessOutputs _) = fragment2.ScheduleProcessBuilder(processBuilder2);

            var graph = SerializeDeserializeFragmentsSynchronously(fragment1, fragment2);

            VerifyGraphSuccessfullyConstructed(graph);
            VerifyProducerExists(graph, fragment1, outputPathToVerify1);
            VerifyProducerExists(graph, fragment2, outputPathToVerify2);
            VerifyMatchingArguments(graph, fragment1, process1);
            VerifyMatchingArguments(graph, fragment2, process2);
        }

        [Fact]
        public void TestBasicAddIndependentFragments()
        {
            var fragment1 = CreatePipGraphFragment(nameof(TestBasicAddIndependentFragments) + "1");
            var processBuilder1 = fragment1.GetProcessBuilder();
            var argumentsBuilder1 = new ArgumentsBuilder(processBuilder1);
            AbsolutePath outputPathToVerify1;
            argumentsBuilder1
                .AddInputOption("input", fragment1.CreateSourceFile("f"))
                .AddOutputOption("output", outputPathToVerify1 = fragment1.CreateOutputFile("g").Path);
            (Process process1, ProcessOutputs _) = fragment1.ScheduleProcessBuilder(processBuilder1);

            // Fragment2 is independent of fragment1.
            var fragment2 = CreatePipGraphFragment(nameof(TestBasicAddIndependentFragments) + "2");
            var processBuilder2 = fragment2.GetProcessBuilder();
            var argumentsBuilder2 = new ArgumentsBuilder(processBuilder2);
            AbsolutePath outputPathToVerify2;
            argumentsBuilder2
                .AddInputOption("input", fragment2.CreateSourceFile("i"))
                .AddOutputOption("output", outputPathToVerify2 = fragment2.CreateOutputFile("h").Path);
            (Process process2, ProcessOutputs _) = fragment2.ScheduleProcessBuilder(processBuilder2);

            var graph = SerializeDeserializeFragmentsInParallel(fragment1, fragment2);

            VerifyGraphSuccessfullyConstructed(graph);
            VerifyProducerExists(graph, fragment1, outputPathToVerify1);
            VerifyProducerExists(graph, fragment2, outputPathToVerify2);
            VerifyMatchingArguments(graph, fragment1, process1);
            VerifyMatchingArguments(graph, fragment2, process2);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TestAddingTheSameFragments(bool addInParallel)
        {
            var fragment = CreatePipGraphFragment(nameof(TestAddingTheSameFragments) + "1");
            var processBuilder = fragment.GetProcessBuilder();
            var argumentsBuilder = new ArgumentsBuilder(processBuilder);
            AbsolutePath outputPathToVerify;
            argumentsBuilder
                .AddInputOption("input", fragment.CreateSourceFile("f"))
                .AddOutputOption("output", outputPathToVerify = fragment.CreateOutputFile("g").Path);
            (Process process, ProcessOutputs _) = fragment.ScheduleProcessBuilder(processBuilder);

            var graph = addInParallel
                ? SerializeDeserializeFragmentsInParallel(fragment, fragment)
                : SerializeDeserializeFragmentsSynchronously(fragment, fragment);

            VerifyGraphSuccessfullyConstructed(graph);
            VerifyProducerExists(graph, fragment, outputPathToVerify);
            VerifyMatchingArguments(graph, fragment, process);
        }

        [Fact]
        public void TestAddingIpcPip()
        {
            var fragment = CreatePipGraphFragment(nameof(TestAddingIpcPip));
            (IIpcMoniker moniker, PipId servicePipId) = CreateService(fragment);

            var processBuilder = fragment.GetProcessBuilder();
            var argumentsBuilder = new ArgumentsBuilder(processBuilder);
            AbsolutePath outputPathToVerify;
            argumentsBuilder
                .AddInputOption("input", fragment.CreateSourceFile("f"))
                .AddOutputOption("output", outputPathToVerify = fragment.CreateOutputFile("g").Path);
            (Process process, ProcessOutputs _) = fragment.ScheduleProcessBuilder(processBuilder);

            var addFileProcessBuilder = fragment.GetIpcProcessBuilder();
            new ArgumentsBuilder(addFileProcessBuilder)
                .AddOption("--command", "addFile")
                .AddIpcMonikerOption("--ipcMoniker", moniker)
                .AddInputOption("--file", fragment.CreateOutputFile("g"));

            fragment.ScheduleIpcPip(
                moniker,
                servicePipId,
                addFileProcessBuilder,
                fragment.CreateOutputFile("add"),
                false);

            var graph = SerializeDeserializeFragmentsSynchronously(fragment);
            VerifyGraphSuccessfullyConstructed(graph);
        }

        [Fact]
        public void TestUnifyIpcPips()
        {
            // TODO: Add unit test that unify Ipc pips.
            XAssert.IsTrue(true);
        }

        [Fact]
        public void TestIpcPipWithVsoHashAndFileId()
        {
            // TODO
            XAssert.IsTrue(true);
        }

        /// <summary>
        /// Serializes and deserializes graph fragments synchronously according to its dependency relation specified by their order in the array.
        /// </summary>
        /// <param name="fragments">Graph fragments with total order on their dependency relation.</param>
        /// <returns>Resulting pip graph.</returns>
        private PipGraph SerializeDeserializeFragmentsSynchronously(params TestPipGraphFragment[] fragments)
        {
            var streams = SerializeFragmentsSynchronously(fragments);

            var fragmentManager = new PipGraphFragmentManager(LoggingContext, Context, PipGraphBuilder, 2);

            for (int i = 0; i < streams.Length; ++i)
            {
                XAssert.IsTrue(fragmentManager.AddFragmentFileToGraph(streams[i], fragments[i].ModuleName));
            }

            DisposeStreams(streams);

            return PipGraphBuilder.Build();
        }

        /// <summary>
        /// Serializes and deserializes graph fragments in parallel.
        /// </summary>
        /// <param name="fragments">Graph fragments.</param>
        /// <returns>Resulting pip graph.</returns>
        /// <remarks>
        /// The graph fragments, <paramref name="fragments"/>, are serialized synchronously, but are deserialized in parallel.
        /// For correctness, the graph fragments in <paramref name="fragments"/> are assumed to be independent of each other.
        /// </remarks>
        private PipGraph SerializeDeserializeFragmentsInParallel(params TestPipGraphFragment[] fragments)
        {
            var streams = SerializeFragmentsSynchronously(fragments);

            var fragmentManager = new PipGraphFragmentManager(LoggingContext, Context, PipGraphBuilder, 2);

            Parallel.For(
                0,
                fragments.Length,
                i => 
                {
                    XAssert.IsTrue(fragmentManager.AddFragmentFileToGraph(streams[i], fragments[i].ModuleName));
                });

            DisposeStreams(streams);

            return PipGraphBuilder.Build();
        }

        /// <summary>
        /// Serializes fragments synchronously.
        /// </summary>
        private Stream[] SerializeFragmentsSynchronously(params TestPipGraphFragment[] fragments)
        {
            var streams = new MemoryStream[fragments.Length];

            for (int i = 0; i < streams.Length; ++i)
            {
                streams[i] = new MemoryStream();
                fragments[i].Serialize(streams[i]);
                streams[i].Seek(0, SeekOrigin.Begin);
            }

            return streams;
        }

        /// <summary>
        /// Disposes streams used for seralization tests.
        /// </summary>
        private void DisposeStreams(Stream[] streams)
        {
            for (int i = 0; i < streams.Length; ++i)
            {
                streams[i].Dispose();
            }
        }

        /// <summary>
        /// Verifies that the file output by a fragment exists in the resulting graph.
        /// </summary>
        /// <param name="graph">Resulting graph.</param>
        /// <param name="fragmentOrigin">Graph fragment where the output originates.</param>
        /// <param name="outputPath">Path to output file.</param>
        private void VerifyProducerExists(PipGraph graph, TestPipGraphFragment fragmentOrigin, AbsolutePath outputPath)
        {
            var pipId = graph.TryGetProducer(FileArtifact.CreateOutputFile(RemapFragmentPath(fragmentOrigin, outputPath)));
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

            Process processInGraph = graph.PipTable.HydratePip(pipId, global::BuildXL.Pips.PipQueryContext.PipGraphGetProducingPip) as Process;
            XAssert.IsNotNull(processInGraph);

            string argumentsInFragment = processInFragment.Arguments.ToString(fragmentOrigin.Context.PathTable).ToUpperInvariant();
            string argumentsInGraph = processInGraph.Arguments.ToString(Context.PathTable).ToUpperInvariant();

            XAssert.AreEqual(argumentsInFragment, argumentsInGraph);
        }

        /// <summary>
        /// Remaps a path produced by a fragment to path of the resulting graph.
        /// </summary>
        /// <param name="fragment">Fragment where the path originates.</param>
        /// <param name="path">A path.</param>
        /// <returns></returns>
        private AbsolutePath RemapFragmentPath(TestPipGraphFragment fragment, AbsolutePath path) =>
            AbsolutePath.Create(Context.PathTable, path.ToString(fragment.Context.PathTable));

        private (IIpcMoniker ipcMoniker, PipId servicePipId) CreateService(TestPipGraphFragment fragment)
        {
            var ipcMoniker = fragment.GetIpcMoniker();
            var apiServerMoniker = fragment.GetApiServerMoniker();

            var shutdownBuilder = fragment.GetProcessBuilder();
            new ArgumentsBuilder(shutdownBuilder)
                .AddIpcMonikerOption("--ipcMoniker", ipcMoniker)
                .AddIpcMonikerOption("--serverMoniker", apiServerMoniker)
                .AddOutputOption("--output", fragment.CreateOutputFile("shutdown.txt"));
            shutdownBuilder.ServiceKind = global::BuildXL.Pips.Operations.ServicePipKind.ServiceShutdown;
            (Process shutdownProcess, ProcessOutputs _) = fragment.ScheduleProcessBuilder(shutdownBuilder);

            var finalProcessBuilder = fragment.GetIpcProcessBuilder();
            new ArgumentsBuilder(finalProcessBuilder)
                .AddOption("--command", "final")
                .AddIpcMonikerOption("--ipcMoniker", ipcMoniker);
            var finalOutputFile = fragment.CreateOutputFile("final.txt");
            var finalizationPip = fragment.ScheduleIpcPip(
                ipcMoniker,
                null,
                finalProcessBuilder,
                finalOutputFile,
                true);
            XAssert.IsTrue(finalizationPip.IsValid);

            var serviceProcessBuilder = fragment.GetProcessBuilder();
            new ArgumentsBuilder(serviceProcessBuilder)
                .AddIpcMonikerOption("--ipcMoniker", ipcMoniker)
                .AddIpcMonikerOption("--serverMoniker", apiServerMoniker)
                .AddOutputOption("--output", fragment.CreateOutputFile("service.txt"));
            serviceProcessBuilder.ServiceKind = global::BuildXL.Pips.Operations.ServicePipKind.Service;
            serviceProcessBuilder.ShutDownProcessPipId = shutdownProcess.PipId;
            serviceProcessBuilder.FinalizationPipIds = ReadOnlyArray<PipId>.FromWithoutCopy(new[] { finalizationPip });
            (Process serviceProcess, ProcessOutputs _) = fragment.ScheduleProcessBuilder(serviceProcessBuilder);

            var createProcessBuilder = fragment.GetIpcProcessBuilder();
            new ArgumentsBuilder(createProcessBuilder)
                .AddOption("--command", "create")
                .AddIpcMonikerOption("--ipcMoniker", ipcMoniker);
            var createOutputFile = fragment.CreateOutputFile("create.txt");
            var createPip = fragment.ScheduleIpcPip(
                ipcMoniker,
                serviceProcess.PipId,
                createProcessBuilder,
                createOutputFile,
                false);
            XAssert.IsTrue(createPip.IsValid);

            return (ipcMoniker, serviceProcess.PipId);
        }
    }
}
