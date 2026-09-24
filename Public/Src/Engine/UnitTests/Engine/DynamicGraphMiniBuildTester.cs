// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Core;
using Test.BuildXL.Engine;
using Test.BuildXL.EngineTestUtilities;
using Test.BuildXL.Processes;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using SchedulerEventId = BuildXL.Scheduler.Tracing.LogEventId;

namespace Test.BuildXL.EngineTests
{
    [TestClassIfSupported(requiresWindowsBasedOperatingSystem: true)]
    [Trait("Category", "DynamicGraphMiniBuildTester")]
    public sealed class DynamicGraphMiniBuildTester : BaseEngineTest
    {
        public DynamicGraphMiniBuildTester(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void ChildExecutesBeforeGraphConstructionCompletes()
        {
            IgnoreWarnings();

            Configuration.Engine.UnsafeEnableDynamicGraph = true;
            Configuration.Schedule.IncrementalScheduling = false;
            Configuration.Cache.CacheGraph = false;
            EnableTestSleepAmbient();

            var tool = GetOsShellCmdToolDefinition();
            var spec = $@"
import {{Artifact, Cmd, Transformer}} from 'Sdk.Transformers';

const tool = {tool};
const parentPath = p`obj/parent.txt`;
const child1Path = p`obj/child1.txt`;
const child2Path = p`obj/child2.txt`;
const child1Probe = f`obj/child1.txt`;

export const outputs = (() => {{
    const parent = Transformer.execute({{
        tool: tool,
        workingDirectory: d`.`,
        arguments: [
            Cmd.argument('/d'),
            Cmd.argument('/c'),
            Cmd.rawArgument('""'),
            Cmd.args(['echo', 'parent', '>', Artifact.output(parentPath)]),
            Cmd.rawArgument('""'),
        ],
    }}).getOutputFile(parentPath);

    const child1 = Transformer.execute({{
        tool: tool,
        workingDirectory: d`.`,
        arguments: [
            Cmd.argument('/d'),
            Cmd.argument('/c'),
            Cmd.rawArgument('""'),
            Cmd.args(['type', Artifact.input(parent), '>', Artifact.output(child1Path)]),
            Cmd.rawArgument('""'),
        ],
    }}).getOutputFile(child1Path);

    // Graph construction cannot proceed to child 2 until child 1 has executed.
    while (!File.exists(child1Probe)) {{
        Debug.sleep(100);
    }}

    Contract.assert(File.exists(child1Probe), 'Child 1 did not execute while graph construction was in progress.');

    const child2 = Transformer.execute({{
        tool: tool,
        workingDirectory: d`.`,
        arguments: [
            Cmd.argument('/d'),
            Cmd.argument('/c'),
            Cmd.rawArgument('""'),
            Cmd.args(['type', Artifact.input(parent), '>', Artifact.output(child2Path)]),
            Cmd.rawArgument('""'),
        ],
    }}).getOutputFile(child2Path);

    return [parent, child1, child2];
}})();
";

            AddModule("DynamicGraphMiniBuild", ("spec.dsc", spec), placeInRoot: true);
            ConfigureInMemoryCache(new TestCache());

            RunEngine();
            AssertInformationalEventLogged(SchedulerEventId.ProcessPipCacheMiss, count: 3);

            string objectDirectory = Configuration.Layout.ObjectDirectory.ToString(Context.PathTable);
            Assert.Equal("parent", File.ReadAllText(Path.Combine(objectDirectory, "parent.txt")).Trim());
            Assert.Equal("parent", File.ReadAllText(Path.Combine(objectDirectory, "child1.txt")).Trim());
            Assert.Equal("parent", File.ReadAllText(Path.Combine(objectDirectory, "child2.txt")).Trim());
        }

        /// <summary>
        /// The 'sleep' ambient function is implemented but it is intentionally not public. This method enables the ambient for testing purposes.
        /// </summary>
        private void EnableTestSleepAmbient()
        {
            string sourcePrelude = Path.Combine(GetTestExecutionLocation(), "Sdk", "Prelude");
            string testPrelude = Path.Combine(TestRoot, "TestPrelude");
            Directory.CreateDirectory(testPrelude);

            foreach (string sourceFile in Directory.GetFiles(sourcePrelude, "*.dsc"))
            {
                File.Copy(sourceFile, Path.Combine(testPrelude, Path.GetFileName(sourceFile)));
            }

            File.AppendAllText(
                Path.Combine(testPrelude, "Prelude.Debug.dsc"),
                Environment.NewLine + "namespace Debug { export declare function sleep(milliseconds: number): void; }");

            var sdkResolver = (SourceResolverSettings)Configuration.Resolvers[1];
            var sdkModules = new List<DiscriminatingUnion<AbsolutePath, IInlineModuleDefinition>>(sdkResolver.Modules)
            {
                [0] = new DiscriminatingUnion<AbsolutePath, IInlineModuleDefinition>(
                    AbsolutePath.Create(Context.PathTable, Path.Combine(testPrelude, "package.config.dsc"))),
            };

            sdkResolver.Modules = sdkModules;
        }
    }
}
