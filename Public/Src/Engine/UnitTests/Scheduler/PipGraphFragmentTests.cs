// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Threading.Tasks;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

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
            fragment.ScheduleProcessBuilder(processBuilder);

            var graph = SerializeDeserializeFragment(fragment);
            XAssert.IsNotNull(graph);

            var pipId = graph.TryGetProducer(FileArtifact.CreateOutputFile(RemapFragmentPath(fragment, outputPathToVerify)));
            XAssert.IsTrue(pipId.IsValid);
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
            fragment1.ScheduleProcessBuilder(processBuilder1);

            // Fragment2 depends on fragment1 on output file g produced by fragment1.
            var fragment2 = CreatePipGraphFragment(nameof(TestBasicDependencyBetweenFragments) + "2");
            var processBuilder2 = fragment2.GetProcessBuilder();
            var argumentsBuilder2 = new ArgumentsBuilder(processBuilder2);
            AbsolutePath outputPathToVerify2;
            argumentsBuilder2
                .AddInputOption("input", fragment2.CreateOutputFile("g")) // fragment2 depends on g without any producer within the fragment.
                .AddOutputOption("output", outputPathToVerify2 = fragment2.CreateOutputFile("h").Path);
            fragment2.ScheduleProcessBuilder(processBuilder2);

            var graph = SerializeDeserializeFragmentsSynchronously(fragment1, fragment2);
            XAssert.IsNotNull(graph);

            var pipIdG = graph.TryGetProducer(FileArtifact.CreateOutputFile(RemapFragmentPath(fragment1, outputPathToVerify1)));
            XAssert.IsTrue(pipIdG.IsValid);

            var pipIdH = graph.TryGetProducer(FileArtifact.CreateOutputFile(RemapFragmentPath(fragment2, outputPathToVerify2)));
            XAssert.IsTrue(pipIdH.IsValid);
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
            fragment1.ScheduleProcessBuilder(processBuilder1);

            // Fragment2 is independent of fragment1.
            var fragment2 = CreatePipGraphFragment(nameof(TestBasicAddIndependentFragments) + "2");
            var processBuilder2 = fragment2.GetProcessBuilder();
            var argumentsBuilder2 = new ArgumentsBuilder(processBuilder2);
            AbsolutePath outputPathToVerify2;
            argumentsBuilder2
                .AddInputOption("input", fragment2.CreateSourceFile("i"))
                .AddOutputOption("output", outputPathToVerify2 = fragment2.CreateOutputFile("h").Path);
            fragment2.ScheduleProcessBuilder(processBuilder2);

            var graph = SerializeDeserializeFragmentsInParallel(fragment1, fragment2);
            XAssert.IsNotNull(graph);

            var pipIdG = graph.TryGetProducer(FileArtifact.CreateOutputFile(RemapFragmentPath(fragment1, outputPathToVerify1)));
            XAssert.IsTrue(pipIdG.IsValid);

            var pipIdH = graph.TryGetProducer(FileArtifact.CreateOutputFile(RemapFragmentPath(fragment2, outputPathToVerify2)));
            XAssert.IsTrue(pipIdH.IsValid);
        }

        private PipGraph SerializeDeserializeFragment(TestPipGraphFragment fragment)
        {
            var fragmentManager = new PipGraphFragmentManager(LoggingContext, Context, PipGraphBuilder);
            using (var ms = new MemoryStream())
            {
                fragment.Serialize(ms);
                ms.Seek(0, SeekOrigin.Begin);
                XAssert.IsTrue(fragmentManager.AddFragmentFileToGraph(ms, fragment.ModuleName));
                return PipGraphBuilder.Build();
            }
        }

        private PipGraph SerializeDeserializeFragmentsSynchronously(params TestPipGraphFragment[] fragments)
        {
            var streams = SerializeFragments(fragments);

            var fragmentManager = new PipGraphFragmentManager(LoggingContext, Context, PipGraphBuilder);

            for (int i = 0; i < streams.Length; ++i)
            {
                XAssert.IsTrue(fragmentManager.AddFragmentFileToGraph(streams[i], fragments[i].ModuleName));
            }

            DisposeStreams(streams);

            return PipGraphBuilder.Build();
        }

        private PipGraph SerializeDeserializeFragmentsInParallel(params TestPipGraphFragment[] fragments)
        {
            var streams = SerializeFragments(fragments);

            var fragmentManager = new PipGraphFragmentManager(LoggingContext, Context, PipGraphBuilder);

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

        private Stream[] SerializeFragments(params TestPipGraphFragment[] fragments)
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

        private void DisposeStreams(Stream[] streams)
        {
            for (int i = 0; i < streams.Length; ++i)
            {
                streams[i].Dispose();
            }
        }

        private AbsolutePath RemapFragmentPath(TestPipGraphFragment fragment, AbsolutePath path) =>
            AbsolutePath.Create(Context.PathTable, path.ToString(fragment.Context.PathTable));
    }
}
