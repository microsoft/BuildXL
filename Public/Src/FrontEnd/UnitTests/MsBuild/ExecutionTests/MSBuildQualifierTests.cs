// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Configuration;
using TypeScript.Net.Extensions;
using Xunit;
using Xunit.Abstractions;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Engine;
using BuildXL.Scheduler.Graph;

namespace Test.BuildXL.FrontEnd.MsBuild
{
    public sealed class MsBuildQualifierTests : MsBuildPipExecutionTestBase
    {
        public MsBuildQualifierTests(ITestOutputHelper output)
            : base(output)
        {
        }

        /// <summary>
        /// We just need the engine to schedule pips
        /// </summary>
        protected override EnginePhases Phase => EnginePhases.Schedule;

        [Fact]
        public void EachRequestedQualifierIsHonored()
        {
            var config = Build()
                .AddSpec(R("A.proj"), CreateHelloWorldProject())
                .PersistSpecsAndGetConfiguration();

            var result = RunEngineWithConfigAndRequestedQualifiers(config, new List<string> { "configuration=debug", "configuration=release" });

            // We should find two scheduled processes, one for debug the other one for release
            var processes = result.EngineState.PipGraph.RetrievePipsOfType(PipType.Process);
            Assert.Equal(2, processes.Count());

            Assert.Equal(1, processes.Where(process => ProcessContainsArguments((Process)process, "/p:configuration=\"debug\"")).Count());
            Assert.Equal(1, processes.Where(process => ProcessContainsArguments((Process)process, "/p:configuration=\"release\"")).Count());
        }

        [Fact]
        public void DependenciesHonorRequestedQualifiers()
        {
            var config = Build("fileNameEntryPoints: [r`B.proj`]")
                .AddSpec(R("A.proj"), CreateHelloWorldProject())
                .AddSpec(R("B.proj"), CreateWriteFileTestProject("out.txt", "A.proj"))
                .PersistSpecsAndGetConfiguration();

            var result = RunEngineWithConfigAndRequestedQualifiers(config, new List<string> { "configuration=debug", "configuration=release" });

            // We should find four scheduled processes: A and B with debug and release versions
            var processes = result.EngineState.PipGraph.RetrievePipsOfType(PipType.Process);
            Assert.Equal(4, processes.Count());

            var pathToProjA = config.Layout.SourceDirectory.Combine(PathTable, "A.proj").ToString(PathTable);
            var pathToProjB = config.Layout.SourceDirectory.Combine(PathTable, "B.proj").ToString(PathTable);

            var projADebug = processes.Single(process => ProcessContainsArguments((Process)process, "/p:configuration=\"debug\"", pathToProjA));
            var projARelease = processes.Single(process => ProcessContainsArguments((Process)process, "/p:configuration=\"release\"", pathToProjA));
            var projBDebug = processes.Single(process => ProcessContainsArguments((Process)process, "/p:configuration=\"debug\"", pathToProjB));
            var projBRelease = processes.Single(process => ProcessContainsArguments((Process)process, "/p:configuration=\"release\"", pathToProjB));

            // The B -> A relationship should be reflected on each version of the qualifier, such that BDebug -> ADebug and BRelease -> ARelease
            Assert.True(result.EngineState.PipGraph.IsReachableFrom(projADebug.PipId.ToNodeId(), projBDebug.PipId.ToNodeId()));
            Assert.True(result.EngineState.PipGraph.IsReachableFrom(projARelease.PipId.ToNodeId(), projBRelease.PipId.ToNodeId()));
        }

        #region Helper

        private BuildXLEngineResult RunEngineWithConfigAndRequestedQualifiers(ICommandLineConfiguration config, List<string> requestedQualifiers)
        {
            ((StartupConfiguration)config.Startup).QualifierIdentifiers = requestedQualifiers;
            return RunEngineWithConfig(config);
        }

        private bool ProcessContainsArguments(Process process, params string[] arguments)
        {
            var processArguments = RetrieveProcessArguments(process);
            return arguments.All(contained => processArguments.Contains(contained));
        }

        #endregion

    }
}