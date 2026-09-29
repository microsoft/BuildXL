// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Core;
using Test.BuildXL.Engine;
using Test.BuildXL.EngineTestUtilities;
using Test.BuildXL.Processes;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using ProcessEventId = BuildXL.Processes.Tracing.LogEventId;
using SchedulerEventId = BuildXL.Scheduler.Tracing.LogEventId;
using EngineEventId = BuildXL.Engine.Tracing.LogEventId;
using PipEventId = BuildXL.Pips.Tracing.LogEventId;

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

        [Theory]
        [InlineData(FileSystemMode.RealAndPipGraph)]
        [InlineData(FileSystemMode.AlwaysMinimalWithAlienFilesGraph)]
        public void UnsupportedFileSystemModesAreRejected(FileSystemMode fileSystemMode)
        {
            ConfigureDynamicGraph();
            Configuration.Sandbox.FileSystemMode = fileSystemMode;

            SetConfig();
            global::BuildXL.Engine.BuildXLEngine.PopulateLoggingAndLayoutConfiguration(
                Configuration,
                Context.PathTable,
                bxlExeLocation: null,
                inTestMode: true);

            Assert.False(global::BuildXL.Engine.BuildXLEngine.PopulateAndValidateConfiguration(
                Configuration,
                Configuration,
                Context.PathTable,
                LoggingContext));
            AssertErrorEventLogged(EngineEventId.DynamicGraphConfigurationIncompatible);
        }

        [Fact]
        public void UndeclaredSourceReadsAreRejected()
        {
            ConfigureDynamicGraph();

            string spec = CreateSpec($@"
    const outputPath = p`obj/output.txt`;
{CreateProcess("process", "outputPath", "'echo', 'output', '>', Artifact.output(outputPath)", allowUndeclaredSourceReads: true)}
    return [process];
");

            AddModule("DynamicGraphUndeclaredSourceReads", ("spec.dsc", spec), placeInRoot: true);

            RunEngine(expectSuccess: false);
            AssertErrorEventLogged(PipEventId.ScheduleFailAddPipDynamicGraphUnsupportedFeature);
        }

        [Fact]
        public void SharedOpaqueOutputsAreRejected()
        {
            ConfigureDynamicGraph();

            string spec = $@"
import {{Artifact, Cmd, Transformer}} from 'Sdk.Transformers';

const tool = {GetOsShellCmdToolDefinition()};

{GetExecuteFunction()}

export const outputs = (() => {{
    const objectRoot = Context.getMount('ObjectRoot').path;
    const process = execute({{
        tool: tool,
        workingDirectory: d`.`,
        arguments: [
            Cmd.argument('/d'),
            Cmd.argument('/c'),
            Cmd.rawArgument('""'),
            Cmd.args(['echo', 'output', '>', p`${{objectRoot}}/shared/output.txt`]),
            Cmd.rawArgument('""'),
        ],
        outputs: [{{ kind: 'shared', directory: d`${{objectRoot}}/shared` }}],
    }});
    return [process];
}})();
";

            AddModule("DynamicGraphSharedOpaqueOutputs", ("spec.dsc", spec), placeInRoot: true);

            RunEngine(expectSuccess: false);
            AssertErrorEventLogged(PipEventId.ScheduleFailAddPipDynamicGraphUnsupportedFeature);
        }

        [Fact]
        public void ChildExecutesBeforeGraphConstructionCompletes()
        {
            ConfigureDynamicGraph();

            string spec = CreateSpec($@"
const parentPath = p`obj/parent.txt`;
const child1Path = p`obj/child1.txt`;
const child2Path = p`obj/child2.txt`;
const child1Probe = f`obj/child1.txt`;

{CreateProcess("parent", "parentPath", "'echo', 'parent', '>', Artifact.output(parentPath)")}
{CreateProcess("child1", "child1Path", "'type', Artifact.input(parent), '>', Artifact.output(child1Path)")}

    // Graph construction cannot proceed to child 2 until child 1 has executed.
{WaitForFile("child1Probe")}

    Contract.assert(File.exists(child1Probe), 'Child 1 did not execute while graph construction was in progress.');

{CreateProcess("child2", "child2Path", "'type', Artifact.input(parent), '>', Artifact.output(child2Path)")}

    return [parent, child1, child2];
");

            AddModule("DynamicGraphMiniBuild", ("spec.dsc", spec), placeInRoot: true);
            ConfigureInMemoryCache(new TestCache());

            RunEngine();
            AssertInformationalEventLogged(SchedulerEventId.ProcessPipCacheMiss, count: 3);

            string objectDirectory = Configuration.Layout.ObjectDirectory.ToString(Context.PathTable);
            Assert.Equal("parent", File.ReadAllText(Path.Combine(objectDirectory, "parent.txt")).Trim());
            Assert.Equal("parent", File.ReadAllText(Path.Combine(objectDirectory, "child1.txt")).Trim());
            Assert.Equal("parent", File.ReadAllText(Path.Combine(objectDirectory, "child2.txt")).Trim());
        }

        [Fact]
        public void LateChildrenOfFailedParentAreSkippedTransitively()
        {
            ConfigureDynamicGraph();
            Configuration.Schedule.StopOnFirstError = false;

            string spec = CreateSpec($@"
const parentPath = p`obj/failed-parent.txt`;
const childPath = p`obj/skipped-child.txt`;
const grandchildPath = p`obj/skipped-grandchild.txt`;
const parentProbe = f`obj/failed-parent.txt`;

{CreateProcess(
    "parent",
    "parentPath",
    "'echo', 'failed-parent', '>', Artifact.output(parentPath)",
    successExitCodes: "1")}

    // Admit the children only after the parent has run and produced its output.
{WaitForFile("parentProbe")}

    // The direct child must be skipped because its parent failed; the grandchild must then be skipped transitively.
{CreateProcess("child", "childPath", "'echo', 'child-ran', '>', Artifact.output(childPath)", dependencies: "parent")}
{CreateProcess(
    "grandchild",
    "grandchildPath",
    "'echo', 'grandchild-ran', '>', Artifact.output(grandchildPath)",
    dependencies: "child")}

    return [parent, child, grandchild];
");

            AddModule("DynamicGraphFailedParent", ("spec.dsc", spec), placeInRoot: true);
            ConfigureInMemoryCache(new TestCache());

            RunEngine(expectSuccess: false);
            AssertErrorEventLogged(ProcessEventId.PipProcessError, count: 1);

            string objectDirectory = Configuration.Layout.ObjectDirectory.ToString(Context.PathTable);
            Assert.True(File.Exists(Path.Combine(objectDirectory, "failed-parent.txt")));
            Assert.False(File.Exists(Path.Combine(objectDirectory, "skipped-child.txt")));
            Assert.False(File.Exists(Path.Combine(objectDirectory, "skipped-grandchild.txt")));
        }

        [Fact]
        public void LateChildOfCompletedWriteFilePipExecutes()
        {
            ConfigureDynamicGraph();

            string spec = CreateSpec($@"
const parentPath = p`obj/write-file-parent.txt`;
const childPath = p`obj/write-file-child.txt`;
const parentProbe = f`obj/write-file-parent.txt`;

    const parent = Transformer.writeFile(parentPath, 'non-process-parent');

    // Ensure the non-process parent completes before its child is admitted.
{WaitForFile("parentProbe")}

{CreateProcess("child", "childPath", "'echo', 'child', '>', Artifact.output(childPath)", dependencies: "parent")}

    return [parent, child];
");

            AddModule("DynamicGraphWriteFileParent", ("spec.dsc", spec), placeInRoot: true);
            ConfigureInMemoryCache(new TestCache());

            RunEngine();
            AssertInformationalEventLogged(SchedulerEventId.ProcessPipCacheMiss, count: 1);

            string objectDirectory = Configuration.Layout.ObjectDirectory.ToString(Context.PathTable);
            Assert.Equal("non-process-parent", File.ReadAllText(Path.Combine(objectDirectory, "write-file-parent.txt")).Trim());
            Assert.Equal("child", File.ReadAllText(Path.Combine(objectDirectory, "write-file-child.txt")).Trim());
        }

        [Fact]
        public void HighFanInLateChildExecutesExactlyOnce()
        {
            const int ParentCount = 32;

            ConfigureDynamicGraph();

            string parentDeclarations = string.Join(
                Environment.NewLine,
                Enumerable.Range(0, ParentCount).Select(
                    i => $@"
const parent{i}Path = p`obj/fan-in-parent-{i}.txt`;
{CreateProcess($"parent{i}", $"parent{i}Path", $"'echo', 'parent-{i}', '>', Artifact.output(parent{i}Path)")}
"));
            string parentDependencies = string.Join(", ", Enumerable.Range(0, ParentCount).Select(i => $"parent{i}"));

            string spec = CreateSpec($@"
{parentDeclarations}

    const firstParentProbe = f`obj/fan-in-parent-0.txt`;
{WaitForFile("firstParentProbe")}

    const childPath = p`obj/fan-in-child.txt`;
{CreateProcess(
    "child",
    "childPath",
    "'echo', 'child', '>', Artifact.output(childPath)",
    dependencies: parentDependencies)}

    return [child];
");

            AddModule("DynamicGraphHighFanIn", ("spec.dsc", spec), placeInRoot: true);
            ConfigureInMemoryCache(new TestCache());

            RunEngine();
            AssertInformationalEventLogged(SchedulerEventId.ProcessPipCacheMiss, count: ParentCount + 1);

            string objectDirectory = Configuration.Layout.ObjectDirectory.ToString(Context.PathTable);
            Assert.Equal("child", File.ReadAllText(Path.Combine(objectDirectory, "fan-in-child.txt")).Trim());
        }

        private string CreateSpec(string body)
        {
            return $@"
import {{Artifact, Cmd, Transformer}} from 'Sdk.Transformers';

const tool = {GetOsShellCmdToolDefinition()};

export const outputs = (() => {{
{body}
}})();
";
        }

        private static string CreateProcess(
            string pipName,
            string outputPath,
            string commandArguments,
            string dependencies = null,
            string successExitCodes = null,
            bool allowUndeclaredSourceReads = false)
        {
            string dependenciesProperty = dependencies == null
                ? string.Empty
                : $"        dependencies: [{dependencies}],{Environment.NewLine}";
            string successExitCodesProperty = successExitCodes == null
                ? string.Empty
                : $"        successExitCodes: [{successExitCodes}],{Environment.NewLine}";
            string allowUndeclaredSourceReadsProperty = allowUndeclaredSourceReads
                ? $"        allowUndeclaredSourceReads: true,{Environment.NewLine}"
                : string.Empty;

            return $@"    const {pipName} = Transformer.execute({{
        tool: tool,
        workingDirectory: d`.`,
        arguments: [
            Cmd.argument('/d'),
            Cmd.argument('/c'),
            Cmd.rawArgument('""'),
            Cmd.args([{commandArguments}]),
            Cmd.rawArgument('""'),
        ],
{dependenciesProperty}{successExitCodesProperty}{allowUndeclaredSourceReadsProperty}    }}).getOutputFile({outputPath});
";
        }

        private static string WaitForFile(string path)
        {
            return $@"    while (!File.exists({path})) {{
        Debug.sleep(100);
    }}";
        }

        private void ConfigureDynamicGraph()
        {
            IgnoreWarnings();

            Configuration.Engine.UnsafeEnableDynamicGraph = true;
            Configuration.Schedule.IncrementalScheduling = false;
            Configuration.Cache.CacheGraph = false;
            EnableTestSleepAmbient();
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
