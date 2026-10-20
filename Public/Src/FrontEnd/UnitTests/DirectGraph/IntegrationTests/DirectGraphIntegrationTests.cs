// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Linq;
using System.Text.Json;
using BuildXL.Engine;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Core;
using Test.BuildXL.Processes;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Test.DScript.Ast;
using Xunit;

namespace Test.BuildXL.FrontEnd.DirectGraph
{
    /// <summary>
    /// Runs the engine end to end, with the mock emitter streaming the graph to the DirectGraph resolver.
    /// </summary>
    public sealed class DirectGraphIntegrationTests : DsTestWithCacheBase
    {
        // CODESYNC: Public/Src/FrontEnd/UnitTests/DirectGraph/Test.BuildXL.FrontEnd.DirectGraph.dsc
        private string PathToEmitter => Path.Combine(TestDeploymentDir, "tools", "GraphEmitter", OperatingSystemHelper.IsWindowsOS ? "GraphEmitter.exe" : "GraphEmitter");

        public DirectGraphIntegrationTests(ITestOutputHelper output)
            : base(output, usePassThroughFileSystem: true)
        {
            RegisterEventSource(global::BuildXL.Processes.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Scheduler.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Pips.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.FrontEnd.Core.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.FrontEnd.DirectGraph.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Engine.DirectGraph.ETWLogger.Log);
        }

        protected override bool DisableDefaultSourceResolver => true;

        [Fact]
        public void EmittedPipsRunAndProduceTheirOutputs()
        {
            string[] command = OperatingSystemHelper.IsWindowsOS
                ? new[] { CmdHelper.CmdX64, "/d", "/c", @"type out\input.txt > out\opaque\result.txt" }
                : new[] { CmdHelper.Bash, "-c", "cat out/input.txt > out/opaque/result.txt" };

            // Appends to the file the first operation wrote, in place.
            string[] rewriteCommand = OperatingSystemHelper.IsWindowsOS
                ? new[] { CmdHelper.CmdX64, "/d", "/c", @"echo world>> out\input.txt" }
                : new[] { CmdHelper.Bash, "-c", "echo world >> out/input.txt" };

            var config = Build()
                .Configuration($$"""
                    config({
                        resolvers: [
                            {
                                kind: "DirectGraph",
                                moduleNames: [a`producer`, a`bystander`],
                                workingDirectory: d`.`,
                                tool: f`{{PathToEmitter.Replace('\\', '/')}}`,
                                arguments: ["graph.json"],
                                // More than the timer behind it takes, so the resolver has to cap it.
                                idleTimeoutMin: 100000,
                            },
                        ],
                        mounts: [{ name: a`out`, path: p`out`, trackSourceFileChanges: true, isReadable: true, isWritable: true }],
                        // Module evaluation runs under this qualifier, which the emitted pips must not depend on.
                        qualifiers: { defaultQualifier: { configuration: "debug" } },
                    });
                    """)
                .AddFile("graph.json", $$"""
                    { "modules": [ { "name": "producer", "operations": [
                        { "name": "write", "kind": "writeFile", "destination": "out/input.txt", "content": "hello\n" },
                        { "name": "rewrite", "kind": "process", "executable": {{JsonSerializer.Serialize(rewriteCommand[0])}}, "workingDirectory": ".",
                          "arguments": {{JsonSerializer.Serialize(rewriteCommand[1..])}}, "options": [ "dependsOnCurrentOs" ],
                          "rewrittenFiles": [ { "producedBy": "write", "path": "out/input.txt" } ] },
                        { "name": "run", "kind": "process", "executable": {{JsonSerializer.Serialize(command[0])}}, "workingDirectory": ".",
                          "arguments": {{JsonSerializer.Serialize(command[1..])}}, "options": [ "dependsOnCurrentOs" ],
                          "inputFiles": [ { "producedBy": "rewrite", "path": "out/input.txt" } ], "producesDirectories": [ "out/opaque" ] }
                    ] }, { "name": "bystander", "operations": [
                        { "name": "bystanderWrite", "kind": "writeFile", "destination": "out/bystander.txt", "content": "not requested" }
                    ] } ] }
                    """)
                .PersistSpecsAndGetConfiguration();

            var mutableConfig = (CommandLineConfiguration)config;
            mutableConfig.Engine.Phase = EnginePhases.Execute;
            mutableConfig.Sandbox.FileSystemMode = FileSystemMode.RealAndMinimalPipGraph;
            mutableConfig.DisableInBoxSdkSourceResolver = true;

            // The emitter produces the pips of both modules, whatever the filter selects.
            mutableConfig.Filter = "module='producer'";

            using var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TestOutputDirectory);
            var result = CreateAndRunEngine(config, CreateAppDeployment(tempFiles), testRootDirectory: null, rememberAllChangedTrackedInputs: true, engine: out var engine);

            Assert.True(result.IsSuccess);
            Assert.Equal(100000, engine.Configuration.Resolvers.OfType<IDirectGraphResolverSettings>().Single().IdleTimeoutMin);
            // The consumer reads the rewritten version, which still has what the first operation wrote.
            Assert.Equal(new[] { "hello", "world" }, File.ReadAllLines(Path.Combine(TestRoot, "out", "opaque", "result.txt")));
            Assert.False(File.Exists(Path.Combine(TestRoot, "out", "bystander.txt")));
        }

        [Fact]
        public void TwoResolversEmitTheirOwnPips()
        {
            var result = RunWithTwoResolvers("first", "second");

            Assert.True(result.IsSuccess);
            Assert.Equal("first", File.ReadAllText(Path.Combine(TestRoot, "out", "first.txt")));
            Assert.Equal("second", File.ReadAllText(Path.Combine(TestRoot, "out", "second.txt")));
        }

        [Fact]
        public void ModuleNameDeclaredByTwoResolversBelongsToTheFirst()
        {
            // As for every resolver kind, the engine keeps the first module of a name, so the second resolver owns no
            // module and none of its pips is built.
            var result = RunWithTwoResolvers("shared", "shared");

            Assert.True(result.IsSuccess);
            Assert.Equal("first", File.ReadAllText(Path.Combine(TestRoot, "out", "first.txt")));
            Assert.False(File.Exists(Path.Combine(TestRoot, "out", "second.txt")));
        }

        private string Resolver(string module, string graphFile) => $$"""
            {
                kind: "DirectGraph",
                moduleNames: [a`{{module}}`],
                workingDirectory: d`.`,
                tool: f`{{PathToEmitter.Replace('\\', '/')}}`,
                arguments: ["{{graphFile}}"],
            }
            """;

        // The same operation name in both graphs: operation ids belong to a resolver's own stream.
        private static string Graph(string module, string output) => $$"""
            { "modules": [ { "name": "{{module}}", "operations": [
                { "name": "write", "kind": "writeFile", "destination": "out/{{output}}.txt", "content": "{{output}}" }
            ] } ] }
            """;

        private BuildXLEngineResult RunWithTwoResolvers(string firstModule, string secondModule)
        {
            var config = Build()
                .Configuration($$"""
                    config({
                        resolvers: [
                            {{Resolver(firstModule, "first.json")}},
                            {{Resolver(secondModule, "second.json")}},
                        ],
                        mounts: [{ name: a`out`, path: p`out`, trackSourceFileChanges: true, isReadable: true, isWritable: true }],
                    });
                    """)
                .AddFile("first.json", Graph(firstModule, "first"))
                .AddFile("second.json", Graph(secondModule, "second"))
                .PersistSpecsAndGetConfiguration();

            var mutableConfig = (CommandLineConfiguration)config;
            mutableConfig.Engine.Phase = EnginePhases.Execute;
            mutableConfig.Sandbox.FileSystemMode = FileSystemMode.RealAndMinimalPipGraph;
            mutableConfig.DisableInBoxSdkSourceResolver = true;

            using var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TestOutputDirectory);
            return CreateAndRunEngine(config, CreateAppDeployment(tempFiles), testRootDirectory: null, rememberAllChangedTrackedInputs: true, engine: out _);
        }
    }
}
