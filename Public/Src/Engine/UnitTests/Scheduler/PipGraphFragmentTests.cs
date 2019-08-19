// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
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

        private PipGraph SerializeDeserializeFragment(TestPipGraphFragment fragment)
        {
            var fragmentManager = new PipGraphFragmentManager(LoggingContext, Context, PipGraphBuilder);
            using (var ms = new MemoryStream())
            {
                fragment.Serialize(ms);
                ms.Seek(0, SeekOrigin.Begin);
                fragmentManager.AddFragmentFileToGraph(ms, fragment.ModuleName);
                return PipGraphBuilder.Build();
            }
        }

        private AbsolutePath RemapFragmentPath(TestPipGraphFragment fragment, AbsolutePath path) =>
            AbsolutePath.Create(Context.PathTable, path.ToString(fragment.Context.PathTable));
    }
}
