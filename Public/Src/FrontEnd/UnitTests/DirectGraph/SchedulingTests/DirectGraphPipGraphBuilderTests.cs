// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using BuildXL.Engine.DirectGraph;
using BuildXL.Pips;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Instrumentation.Common;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using RewritePolicy = BuildXL.Utilities.Configuration.RewritePolicy;

namespace Test.BuildXL.FrontEnd.DirectGraph
{
    /// <summary>
    /// Tests for the translation from DirectGraph protocol operations to pips.
    /// </summary>
    /// <remarks>
    /// These drive <see cref="DirectGraphPipGraphBuilder"/> directly. No gRPC and no emitter process is involved, so
    /// what is under test is the translation, not the transport.
    /// </remarks>
    public sealed class DirectGraphPipGraphBuilderTests : XunitBuildXLTest
    {
        private const string TestModule = "testModule";

        private readonly PipExecutionContext m_context;
        private readonly TestEnv.TestPipGraph m_pipGraph;
        private readonly DirectGraphPipGraphBuilder m_graphBuilder;
        private readonly CounterCollection<DirectGraphCounter> m_counters = new();
        private readonly Dictionary<(string module, string symbol), PipConstructionHelper> m_helpers = new();
        private readonly AbsolutePath m_root;
        private readonly global::BuildXL.Utilities.Configuration.Mutable.SandboxConfiguration m_sandboxConfiguration = new();

        // The values requests use, numbered in the order first used, and how many of them each builder has been sent.
        private readonly List<string> m_paths = new();
        private readonly List<string> m_strings = new();
        private readonly Dictionary<DirectGraphPipGraphBuilder, (int Paths, int Strings)> m_registered = new();

        public DirectGraphPipGraphBuilderTests(ITestOutputHelper output)
            : base(output)
        {
            m_context = BuildXLContext.CreateInstanceForTesting();
            m_pipGraph = new TestEnv.TestPipGraph();
            m_root = AbsolutePath.Create(m_context.PathTable, A("c", "test"));

            m_graphBuilder = CreateBuilder();
        }

        /// <summary>
        /// A builder refuses every operation after the first one it rejects, so a test that exercises several
        /// distinct rejections needs a fresh builder for each.
        /// </summary>
        private DirectGraphPipGraphBuilder CreateBuilder(TryRegisterExport registerExport = null) =>
            new DirectGraphPipGraphBuilder(
                m_context.PathTable,
                m_pipGraph,
                m_sandboxConfiguration,
                () => ProcessBuilder.CreateForTesting(m_context.PathTable, new NoOpCredentialScanner(), new LoggingContext("Test")),
                TryGetPipConstructionHelper,
                registerExport ?? ((string moduleName, FileOrDirectoryArtifact artifact, out string error) =>
                {
                    error = null;
                    return true;
                }),
                new LoggingContext("Test"),
                traceOperation: false,
                counters: m_counters);

        [Fact]
        public void EveryPipKindIsConstructedAndCarriesItsOwnProvenance()
        {
            var write = Send(new GraphOperation
            {
                OperationId = 1,
                AddWriteFile = new AddWriteFileRequest
                {
                    Module = TestModule,
                    Symbol = "writeTarget",
                    Destination = Path("written.txt"),
                    Content = Text("content"),
                    Tags = { Text("writeTag") },
                },
            });

            var process = Send(new GraphOperation
            {
                OperationId = 2,
                AddProcess = NewProcess("processTarget", output: "processed.txt"),
            });

            var copy = Send(new GraphOperation
            {
                OperationId = 3,
                AddCopyFile = new AddCopyFileRequest
                {
                    Module = TestModule,
                    Symbol = "copyTarget",
                    Source = new FileArtifactReference { SourcePath = Path("source.txt") },
                    Destination = Path("copied.txt"),
                    OutputsMustRemainWritable = true,
                },
            });

            var seal = Send(new GraphOperation
            {
                OperationId = 4,
                AddSealDirectory = new AddSealDirectoryRequest
                {
                    Module = TestModule,
                    Symbol = "sealTarget",
                    DirectoryRoot = Path("sealed"),
                    Kind = global::BuildXL.Engine.DirectGraph.SealDirectoryKind.Partial,
                },
            });

            XAssert.AreEqual(GraphOperationResponseStatus.Success, write.Status, write.Error);
            XAssert.AreEqual(GraphOperationResponseStatus.Success, process.Status, process.Error);
            XAssert.AreEqual(GraphOperationResponseStatus.Success, copy.Status, copy.Error);
            XAssert.AreEqual(GraphOperationResponseStatus.Success, seal.Status, seal.Error);

            // Every target gets its own value symbol, which is what keeps semi-stable hashes independent of arrival order.
            var hashBySymbol = m_pipGraph.RetrieveScheduledPips()
                .Where(pip => pip.Provenance != null)
                .ToDictionary(pip => pip.Provenance.OutputValueSymbol.ToString(m_context.SymbolTable), pip => pip.SemiStableHash);

            XAssert.IsTrue(hashBySymbol.ContainsKey("writeTarget"), "write-file pip is missing its own value symbol");
            XAssert.IsTrue(hashBySymbol.ContainsKey("processTarget"), "process pip is missing its own value symbol");
            XAssert.IsTrue(hashBySymbol.ContainsKey("copyTarget"), "copy-file pip is missing its own value symbol");
            XAssert.IsTrue(hashBySymbol.ContainsKey("sealTarget"), "seal-directory pip is missing its own value symbol");

            // Whatever the kind, the response carries the semi-stable hash of the pip that operation added.
            foreach (var (response, symbol) in new[] { (write, "writeTarget"), (process, "processTarget"), (copy, "copyTarget"), (seal, "sealTarget") })
            {
                XAssert.IsTrue(
                    response.SemiStableHash != 0 && response.SemiStableHash == hashBySymbol[symbol],
                    $"the response to operation {response.OperationId} does not carry the hash of the pip of '{symbol}'");
            }

            // All of them belong to the same module, which is what module affinity keys off.
            foreach (var pip in m_pipGraph.RetrieveScheduledPips().Where(p => p.Provenance != null))
            {
                XAssert.AreEqual(TestModule, pip.Provenance.ModuleName.ToString(m_context.StringTable));
            }

            var writeFilePip = m_pipGraph.RetrieveScheduledPips().OfType<WriteFile>().Single();
            XAssert.Contains(writeFilePip.Tags.Select(tag => tag.ToString(m_context.StringTable)), "writeTag");

            // Without this the destination is hardlinked read-only out of the cache and a later writer fails at
            // execution time rather than here.
            var copyFilePip = m_pipGraph.RetrieveScheduledPips().OfType<CopyFile>().Single();
            XAssert.IsTrue(copyFilePip.OutputsMustRemainWritable);

            XAssert.AreEqual(1, m_counters.GetCounterValue(DirectGraphCounter.AddWriteFileCount));
            XAssert.AreEqual(1, m_counters.GetCounterValue(DirectGraphCounter.AddProcessCount));
            XAssert.AreEqual(1, m_counters.GetCounterValue(DirectGraphCounter.AddCopyFileCount));
            XAssert.AreEqual(1, m_counters.GetCounterValue(DirectGraphCounter.AddSealDirectoryCount));

            // A front end shares one counter collection across its resolver instances.
            var anotherBuilder = CreateBuilder();
            var anotherWrite = Send(anotherBuilder, new GraphOperation
            {
                OperationId = 1,
                AddWriteFile = new AddWriteFileRequest
                {
                    Module = TestModule,
                    Symbol = "otherWriteTarget",
                    Destination = Path("other-written.txt"),
                    Content = Text("other content"),
                },
            });
            XAssert.AreEqual(GraphOperationResponseStatus.Success, anotherWrite.Status, anotherWrite.Error);
            XAssert.AreEqual(2, m_counters.GetCounterValue(DirectGraphCounter.AddWriteFileCount));
        }

        [Fact]
        public void PipsOfOneTargetShareAHelperAndGetDistinctSemiStableHashes()
        {
            var first = Send(new GraphOperation { OperationId = 1, AddProcess = NewProcess("sameTarget", output: "first.txt") });
            var second = Send(new GraphOperation { OperationId = 2, AddProcess = NewProcess("sameTarget", output: "second.txt") });
            var other = Send(new GraphOperation { OperationId = 3, AddProcess = NewProcess("otherTarget", output: "third.txt") });

            XAssert.AreEqual(GraphOperationResponseStatus.Success, first.Status, first.Error);
            XAssert.AreEqual(GraphOperationResponseStatus.Success, second.Status, second.Error);
            XAssert.AreEqual(GraphOperationResponseStatus.Success, other.Status, other.Error);

            XAssert.AreEqual(2, m_helpers.Count);
            XAssert.AreNotEqual(first.SemiStableHash, second.SemiStableHash);
            XAssert.AreNotEqual(first.SemiStableHash, other.SemiStableHash);
        }

        [Fact]
        public void AProducedFileIsResolvedFromWhatItsProducerProduced()
        {
            var producer = Send(new GraphOperation { OperationId = 1, AddProcess = NewProcess("producer", output: "produced.txt") });
            XAssert.AreEqual(GraphOperationResponseStatus.Success, producer.Status, producer.Error);

            var consumer = Send(new GraphOperation
            {
                OperationId = 2,
                AddProcess = NewProcess("consumer", output: "consumed.txt", input: ProducedFile(1, "produced.txt")),
            });

            XAssert.AreEqual(GraphOperationResponseStatus.Success, consumer.Status, consumer.Error);

            // The executable is resolved the same way. Without this, a tool the build produces enters the pip as a
            // source file, which leaves no edge to its producer and BuildXL rejects the graph.
            var producedExecutable = NewProcess("runsProducedTool", output: "fromTool.txt");
            producedExecutable.Executable = ProducedFile(1, "produced.txt");

            var runsProducedTool = Send(new GraphOperation { OperationId = 3, AddProcess = producedExecutable });
            XAssert.AreEqual(GraphOperationResponseStatus.Success, runsProducedTool.Status, runsProducedTool.Error);

            var toolConsumer = m_pipGraph.RetrieveScheduledPips().OfType<Process>()
                .Single(p => p.Provenance.OutputValueSymbol.ToString(m_context.SymbolTable) == "runsProducedTool");
            XAssert.IsTrue(toolConsumer.Executable.IsOutputFile, "the executable did not resolve to the produced artifact");

            // Any file an operation declares can be referenced: a write-file or copy-file destination, and a process's
            // standard output, which is one of its output files as well.
            var written = Send(new GraphOperation
            {
                OperationId = 4,
                AddWriteFile = new AddWriteFileRequest { Module = TestModule, Symbol = "writer", Destination = Path("written.txt"), Content = Text("content") },
            });

            var copied = Send(new GraphOperation
            {
                OperationId = 5,
                AddCopyFile = new AddCopyFileRequest { Module = TestModule, Symbol = "copier", Source = ProducedFile(1, "produced.txt"), Destination = Path("copied.txt") },
            });

            // A process's files are found by path however it declared them: these are declared in the reverse of the
            // order their paths were registered in.
            var names = new[] { "a.txt", "b.txt", "c.txt", "d.txt", "e.txt" };
            foreach (var name in names)
            {
                Path(name);
            }

            var withStandardOutput = NewProcess("withStandardOutput", output: "e.txt");
            foreach (var name in new[] { "d.txt", "c.txt", "b.txt", "a.txt" })
            {
                withStandardOutput.OutputFiles.Add(new OutputFile { Path = Path(name), Existence = FileExistenceKind.Required });
            }

            withStandardOutput.StandardOutput = Path("stdout.txt");
            var standardOutput = Send(new GraphOperation { OperationId = 6, AddProcess = withStandardOutput });

            var reader = NewProcess("reader", output: "read.txt", input: ProducedFile(4, "written.txt"));
            reader.InputFiles.Add(ProducedFile(5, "copied.txt"));
            reader.InputFiles.Add(ProducedFile(6, "stdout.txt"));
            foreach (var name in names)
            {
                reader.InputFiles.Add(ProducedFile(6, name));
            }

            var read = Send(new GraphOperation { OperationId = 7, AddProcess = reader });

            foreach (var response in new[] { written, copied, standardOutput, read })
            {
                XAssert.AreEqual(GraphOperationResponseStatus.Success, response.Status, response.Error);
            }

            var readerPip = m_pipGraph.RetrieveScheduledPips().OfType<Process>()
                .Single(p => p.Provenance.OutputValueSymbol.ToString(m_context.SymbolTable) == "reader");
            XAssert.AreEqual(3 + names.Length, readerPip.Dependencies.Count(file => file.IsOutputFile), "an input did not resolve to the artifact its producer wrote");

            // A file the named operation did not produce is rejected, even though another operation did.
            var wrongProducer = Send(new GraphOperation
            {
                OperationId = 8,
                AddProcess = NewProcess("wrongProducer", output: "other.txt", input: ProducedFile(2, "produced.txt")),
            });

            XAssert.AreEqual(GraphOperationResponseStatus.InvalidRequest, wrongProducer.Status);
            XAssert.Contains(wrongProducer.Error, "produced no such file");

            // So is naming an operation BuildXL has not received. Each further rejection goes to its own builder, since a
            // builder stops processing after the first one.
            var unknownProducer = Send(CreateBuilder(), new GraphOperation
            {
                OperationId = 1,
                AddProcess = NewProcess("unknownProducer", output: "yetAnother.txt", input: ProducedFile(42, "produced.txt")),
            });

            XAssert.AreEqual(GraphOperationResponseStatus.InvalidRequest, unknownProducer.Status);
            XAssert.Contains(unknownProducer.Error, "has not received");

            // An operation is recorded once it succeeds, so one that names itself as the producer is rejected as well.
            var selfProducer = Send(CreateBuilder(), new GraphOperation
            {
                OperationId = 1,
                AddProcess = NewProcess("selfProducer", output: "self.txt", input: ProducedFile(1, "self.txt")),
            });

            XAssert.AreEqual(GraphOperationResponseStatus.InvalidRequest, selfProducer.Status);
            XAssert.Contains(selfProducer.Error, "has not received");
        }

        [Fact]
        public void AnOutputCanRewriteAFileAnotherOperationProduced()
        {
            var producer = Send(new GraphOperation { OperationId = 1, AddProcess = NewProcess("producer", output: "rewritten.txt") });
            XAssert.AreEqual(GraphOperationResponseStatus.Success, producer.Status, producer.Error);

            // Naming the file is enough: it becomes an input of the process as well, and the process writes its next version.
            var rewriter = NewProcess("rewriter", output: "other.txt");
            rewriter.OutputFiles.Add(new OutputFile
            {
                Rewrites = new ProducedFileReference { ProducerOperationId = 1, Path = Path("rewritten.txt") },
                Existence = FileExistenceKind.Required,
            });

            var rewrite = Send(new GraphOperation { OperationId = 2, AddProcess = rewriter });
            XAssert.AreEqual(GraphOperationResponseStatus.Success, rewrite.Status, rewrite.Error);

            var rewrittenPath = m_root.Combine(m_context.PathTable, "rewritten.txt");
            var rewriterPip = ProcessOf("rewriter");
            XAssert.AreEqual(2, rewriterPip.FileOutputs.Single(output => output.Path == rewrittenPath).RewriteCount);
            XAssert.AreEqual(1, rewriterPip.Dependencies.Single(input => input.Path == rewrittenPath).RewriteCount, "the rewritten file is not an input of the process");

            // The new version is the one the rewriting operation wrote.
            var consumer = Send(new GraphOperation
            {
                OperationId = 3,
                AddProcess = NewProcess("consumer", output: "consumed.txt", input: ProducedFile(2, "rewritten.txt")),
            });

            XAssert.AreEqual(GraphOperationResponseStatus.Success, consumer.Status, consumer.Error);
            XAssert.AreEqual(2, ProcessOf("consumer").Dependencies.Single(input => input.Path == rewrittenPath).RewriteCount);

            // An output has to say which file it writes. This rejection goes to its own builder.
            var neitherSet = NewProcess("neitherSet", output: "y.txt");
            neitherSet.OutputFiles.Add(new OutputFile { Existence = FileExistenceKind.Required });
            var neither = Send(CreateBuilder(), new GraphOperation { OperationId = 1, AddProcess = neitherSet });
            XAssert.AreEqual(GraphOperationResponseStatus.InvalidRequest, neither.Status);
            XAssert.Contains(neither.Error, "neither 'path' nor 'rewrites'");

            // The file has to be one the named operation produced.
            var notProduced = NewProcess("notProduced", output: "z.txt");
            notProduced.OutputFiles.Add(new OutputFile
            {
                Rewrites = new ProducedFileReference { ProducerOperationId = 1, Path = Path("nobodyWrites.txt") },
                Existence = FileExistenceKind.Required,
            });

            var rejected = Send(new GraphOperation { OperationId = 4, AddProcess = notProduced });
            XAssert.AreEqual(GraphOperationResponseStatus.InvalidRequest, rejected.Status);
            XAssert.Contains(rejected.Error, "produced no such file");
        }

        [Fact]
        public void OperationsAreRejectedAndNothingAfterTheFirstRejectionIsProcessed()
        {
            var noSymbol = Send(new GraphOperation { OperationId = 1, AddProcess = NewProcess(symbol: string.Empty, output: "a.txt") });
            XAssert.AreEqual(GraphOperationResponseStatus.InvalidRequest, noSymbol.Status);
            XAssert.Contains(noSymbol.Error, "symbol");

            // Each further case needs its own builder, since the one above has already stopped processing.
            var request = NewProcess("target", output: "b.txt");
            request.Module = "notOwned";
            var unknownModule = Send(CreateBuilder(), new GraphOperation { OperationId = 1, AddProcess = request });
            XAssert.AreEqual(GraphOperationResponseStatus.InvalidRequest, unknownModule.Status);
            XAssert.Contains(unknownModule.Error, "notOwned");

            // Version cases bypass Send, which stamps the current version.
            var skewed = SendRaw(CreateBuilder(), new GraphOperation
            {
                OperationId = 1,
                ProtocolVersion = DirectGraphProtocol.Version + 1,
                AddProcess = NewProcess("target", output: "c.txt"),
            });
            XAssert.AreEqual(GraphOperationResponseStatus.VersionMismatch, skewed.Status);
            XAssert.Contains(skewed.Error, (DirectGraphProtocol.Version + 1).ToString());

            // An emitter that never sets the version gets its own diagnosis rather than "version 0".
            var undeclared = SendRaw(CreateBuilder(), new GraphOperation { OperationId = 1, AddProcess = NewProcess("target", output: "d.txt") });
            XAssert.AreEqual(GraphOperationResponseStatus.VersionMismatch, undeclared.Status);
            XAssert.Contains(undeclared.Error, "GraphOperation.protocol_version");

            // Every response states what BuildXL speaks, so an emitter can diagnose skew itself.
            XAssert.AreEqual(DirectGraphProtocol.Version, skewed.ProtocolVersion);

            // Back on the first builder: a valid operation is refused, and the response names the one that failed.
            // The stream stays open so the emitter can see this and exit on its own terms.
            var afterRejection = Send(new GraphOperation { OperationId = 2, AddProcess = NewProcess("target", output: "e.txt") });
            XAssert.AreEqual(GraphOperationResponseStatus.PreviousOperationRejected, afterRejection.Status);
            XAssert.Contains(afterRejection.Error, "operation 1");
            XAssert.Contains(afterRejection.Error, "symbol");
            XAssert.AreEqual(0, m_pipGraph.RetrieveScheduledPips().OfType<Process>().Count());

            // Complete is refused too, so an emitter cannot close a failed graph as though it had succeeded.
            var complete = Send(new GraphOperation { OperationId = 3, Complete = new CompleteRequest() });
            XAssert.AreEqual(GraphOperationResponseStatus.PreviousOperationRejected, complete.Status);

            // The resolver asks the builder rather than trusting the tool's exit code, and gets the first rejection.
            XAssert.IsTrue(m_graphBuilder.TryGetFirstRejection(out ulong rejectedOperationId, out string rejectionError));
            XAssert.AreEqual(1ul, rejectedOperationId);
            XAssert.Contains(rejectionError, "symbol");

            // Operation ids name dependencies, so reusing one is rejected rather than letting it name two operations.
            var reusingIds = CreateBuilder();
            var first = Send(reusingIds, new GraphOperation
            {
                OperationId = 5,
                AddWriteFile = new AddWriteFileRequest { Module = TestModule, Symbol = "write", Destination = Path("f.txt"), Content = Text(string.Empty) },
            });
            XAssert.AreEqual(GraphOperationResponseStatus.Success, first.Status, first.Error);

            var reused = Send(reusingIds, new GraphOperation { OperationId = 5, AddProcess = NewProcess("target", output: "g.txt") });
            XAssert.AreEqual(GraphOperationResponseStatus.InvalidRequest, reused.Status);
            XAssert.Contains(reused.Error, "already been used");

            // Every registration carries its id, which must be the next one, so an emitter whose ids have drifted from
            // BuildXL's is stopped at the registration where they differ.
            var drifted = SendRaw(CreateBuilder(), new GraphOperation
            {
                OperationId = 1,
                ProtocolVersion = DirectGraphProtocol.Version,
                NewPaths = { new Registration { Id = 1, Value = A("c", "test", "drifted") } },
                Complete = new CompleteRequest(),
            });
            XAssert.AreEqual(GraphOperationResponseStatus.InvalidRequest, drifted.Status);
            XAssert.Contains(drifted.Error, "registers id 1, but the next id is 0");

            var unregisteredOutput = NewProcess("target", output: "h.txt");
            unregisteredOutput.OutputFiles.Add(new OutputFile { Path = 1000, Existence = FileExistenceKind.Required });
            var unregistered = Send(CreateBuilder(), new GraphOperation { OperationId = 1, AddProcess = unregisteredOutput });
            XAssert.AreEqual(GraphOperationResponseStatus.InvalidRequest, unregistered.Status);
            XAssert.Contains(unregistered.Error, "path id 1000");

            // A missing value is not read as 0: neither an id nor an enum.
            var missing = Send(CreateBuilder(), new GraphOperation
            {
                OperationId = 1,
                AddWriteFile = new AddWriteFileRequest { Module = TestModule, Symbol = "write" },
            });
            XAssert.AreEqual(GraphOperationResponseStatus.InvalidRequest, missing.Status);
            XAssert.Contains(missing.Error, "'destination' is required");

            var noKind = NewProcess("target", output: "i.txt");
            noKind.OutputDirectories.Add(new global::BuildXL.Engine.DirectGraph.OutputDirectory { Path = Path("opaque") });
            var missingKind = Send(CreateBuilder(), new GraphOperation { OperationId = 1, AddProcess = noKind });
            XAssert.AreEqual(GraphOperationResponseStatus.InvalidRequest, missingKind.Status);
            XAssert.Contains(missingKind.Error, "'output_directories.kind' is required");

            // An output file's existence is not defaulted to required either.
            var noExistence = NewProcess("target", output: "j.txt");
            noExistence.OutputFiles.Add(new OutputFile { Path = Path("k.txt") });
            var missingExistence = Send(CreateBuilder(), new GraphOperation { OperationId = 1, AddProcess = noExistence });
            XAssert.AreEqual(GraphOperationResponseStatus.InvalidRequest, missingExistence.Status);
            XAssert.Contains(missingExistence.Error, "'output_files.existence' is required");

            // A regular expression is checked when it arrives, as DScript checks it, rather than failing the scheduler later.
            var badRegex = Send(CreateBuilder(), new GraphOperation
            {
                OperationId = 1,
                AddCompositeSharedOpaqueDirectory = new AddCompositeSharedOpaqueDirectoryRequest
                {
                    Module = TestModule,
                    Symbol = "composite",
                    DirectoryRoot = Path("opaque"),
                    Action = CompositionActionKind.WidenDirectoryCone,
                    ContentFilter = new ContentFilter { Kind = ContentFilterKind.Exclude, Regex = "(" },
                },
            });
            XAssert.AreEqual(GraphOperationResponseStatus.InvalidRequest, badRegex.Status);
            XAssert.Contains(badRegex.Error, "not a valid regular expression");

            // Pip construction throws for some invalid inputs. That is a rejection too, and keeps the reason.
            var throwingBuilder = CreateBuilder();
            var patternsOnPartialSeal = Send(throwingBuilder, new GraphOperation
            {
                OperationId = 1,
                AddSealDirectory = new AddSealDirectoryRequest
                {
                    Module = TestModule,
                    Symbol = "seal",
                    DirectoryRoot = Path("sealed"),
                    Kind = global::BuildXL.Engine.DirectGraph.SealDirectoryKind.Partial,
                    Patterns = { Text("*.txt") },
                },
            });
            XAssert.AreEqual(GraphOperationResponseStatus.GraphError, patternsOnPartialSeal.Status);
            XAssert.Contains(patternsOnPartialSeal.Error, "source seal");
            XAssert.IsTrue(throwingBuilder.TryGetFirstRejection(out ulong throwingOperationId, out _));
            XAssert.AreEqual(1ul, throwingOperationId);
        }

        [Fact]
        public void ProcessSandboxAndRuntimeSettingsComeFromTheRequest()
        {
            // The build allows same-content double writes and preserves outputs for incremental tools.
            m_sandboxConfiguration.UnsafeSandboxConfigurationMutable.DoubleWritePolicy = RewritePolicy.AllowSameContentDoubleWrites;
            m_sandboxConfiguration.PreserveOutputsForIncrementalTool = true;

            var withoutOptions = Send(new GraphOperation { OperationId = 1, AddProcess = NewProcess("plain", output: "plain.txt") });
            XAssert.AreEqual(GraphOperationResponseStatus.Success, withoutOptions.Status, withoutOptions.Error);

            var request = NewProcess("dependsOnOs", output: "os.txt");
            request.Options = new global::BuildXL.Engine.DirectGraph.ProcessOptions
            {
                DependsOnCurrentHostOsDirectories = true,
                UnsafeSkipGlobalDependencies = true,
                UnsafeAllowPreservedOutputs = true,
            };
            request.DoubleWritePolicy = global::BuildXL.Engine.DirectGraph.DoubleWritePolicy.DoubleWritesAreErrors;
            request.AbsentPathProbeInUndeclaredOpaqueMode = AbsentPathProbeMode.Strict;
            request.RetryAttemptEnvironmentVariable = Text("RETRY_ATTEMPT");
            request.WarningRegex = Text("WARN");
            request.ErrorRegex = Text("ERR");
            request.Description = Text("what the process does");
            request.ToolDescription = Text("the tool");
            request.SuccessExitCodes.Add(1);
            request.SucceedFastExitCodes.Add(2);
            request.UncacheableExitCodes.Add(3);
            request.RetryExitCodes.Add(4);
            request.OutputFiles.Add(new OutputFile { Path = Path("optional.txt"), Existence = FileExistenceKind.Optional });
            request.OutputFiles.Add(new OutputFile { Path = Path("temporary.txt"), Existence = FileExistenceKind.Temporary });
            request.BreakawayChildProcesses.Add(new global::BuildXL.Engine.DirectGraph.BreakawayChildProcess
            {
                ProcessName = Text("child.exe"),
                RequiredArguments = "/server",
                RequiredArgumentsIgnoreCase = true,
            });

            var withOptions = Send(new GraphOperation { OperationId = 2, AddProcess = request });
            XAssert.AreEqual(GraphOperationResponseStatus.Success, withOptions.Status, withOptions.Error);

            var processes = m_pipGraph.RetrieveScheduledPips().OfType<Process>().ToList();
            var plain = processes.Single(p => p.Provenance.OutputValueSymbol.ToString(m_context.SymbolTable) == "plain");
            var dependsOnOs = processes.Single(p => p.Provenance.OutputValueSymbol.ToString(m_context.SymbolTable) == "dependsOnOs");

            XAssert.IsFalse(plain.ProcessOptions.HasFlag(Process.Options.DependsOnCurrentOs));
            XAssert.IsTrue(dependsOnOs.ProcessOptions.HasFlag(Process.Options.DependsOnCurrentOs));

            // Unset options and policies get what a DScript process gets: global dependencies are required and the
            // build's double-write policy applies. A policy set on the process overrides it.
            XAssert.IsTrue(plain.ProcessOptions.HasFlag(Process.Options.RequireGlobalDependencies));
            XAssert.IsFalse(dependsOnOs.ProcessOptions.HasFlag(Process.Options.RequireGlobalDependencies));
            XAssert.AreEqual(RewritePolicy.AllowSameContentDoubleWrites | RewritePolicy.SourceRewritesAreErrors, plain.RewritePolicy);
            XAssert.AreEqual(RewritePolicy.DefaultStrict, dependsOnOs.RewritePolicy);
            XAssert.IsTrue(dependsOnOs.ProcessOptions.HasFlag(Process.Options.IncrementalTool));
            XAssert.AreEqual(Process.AbsentPathProbeInUndeclaredOpaquesMode.Unsafe, plain.ProcessAbsentPathProbeInUndeclaredOpaquesMode);
            XAssert.AreEqual(Process.AbsentPathProbeInUndeclaredOpaquesMode.Strict, dependsOnOs.ProcessAbsentPathProbeInUndeclaredOpaquesMode);

            XAssert.AreEqual(0, plain.ChildProcessesToBreakawayFromSandbox.Length);

            // Distinct from AllowedSurvivingChildProcessNames: a breakaway child is not tracked by the sandbox at all.
            var breakaway = dependsOnOs.ChildProcessesToBreakawayFromSandbox.Single();
            XAssert.AreEqual("child.exe", breakaway.ProcessName.ToString(m_context.StringTable));
            XAssert.AreEqual("/server", breakaway.RequiredArguments);
            XAssert.IsTrue(breakaway.RequiredArgumentsIgnoreCase);

            XAssert.AreEqual("RETRY_ATTEMPT", dependsOnOs.RetryAttemptEnvironmentVariable.ToString(m_context.StringTable));

            // As in DScript, the succeed-fast and uncacheable exit codes are also success codes.
            XAssert.AreEqual(0, plain.SuccessExitCodes.Length);
            XAssert.ArrayEqual(new[] { 1, 2, 3 }, dependsOnOs.SuccessExitCodes.ToArray());
            XAssert.ArrayEqual(new[] { 2 }, dependsOnOs.SucceedFastExitCodes.ToArray());
            XAssert.ArrayEqual(new[] { 3 }, dependsOnOs.UncacheableExitCodes.ToArray());
            XAssert.ArrayEqual(new[] { 4 }, dependsOnOs.RetryExitCodes.ToArray());

            // Each output file keeps the existence it was declared with.
            var existence = dependsOnOs.FileOutputs.ToDictionary(
                output => output.Path.GetName(m_context.PathTable).ToString(m_context.StringTable),
                output => output.FileExistence);
            XAssert.AreEqual(FileExistence.Required, existence["os.txt"]);
            XAssert.AreEqual(FileExistence.Optional, existence["optional.txt"]);
            XAssert.AreEqual(FileExistence.Temporary, existence["temporary.txt"]);

            // As in DScript, a supplied pattern is matched with no options; an omitted one keeps the default.
            XAssert.AreEqual("WARN", dependsOnOs.WarningRegex.Pattern.ToString(m_context.StringTable));
            XAssert.AreEqual(RegexOptions.None, dependsOnOs.WarningRegex.Options);
            XAssert.AreEqual("ERR", dependsOnOs.ErrorRegex.Pattern.ToString(m_context.StringTable));
            XAssert.AreEqual(RegexOptions.None, dependsOnOs.ErrorRegex.Options);
            XAssert.AreEqual(global::BuildXL.Pips.Operations.RegexDescriptor.CreateDefaultForWarnings(m_context.StringTable), plain.WarningRegex);
            XAssert.AreEqual(global::BuildXL.Pips.Operations.RegexDescriptor.CreateDefaultForErrors(m_context.StringTable), plain.ErrorRegex);

            // As in DScript, the description of the execute is the pip's usage and the tool has its own description.
            XAssert.AreEqual("what the process does", dependsOnOs.Provenance.Usage.ToString(m_context.PathTable));
            XAssert.AreEqual("the tool", dependsOnOs.ToolDescription.ToString(m_context.StringTable));
            XAssert.IsFalse(plain.Provenance.Usage.IsValid);
        }

        [Fact]
        public void EveryProcessFlagIsSetByAWireOptionOrListedAsNotOnTheWire()
        {
            // The flags no wire option sets. A flag added to Process.Options fails this test until someone either puts
            // it on the wire or lists it here.
            const Process.Options notOnTheWire =
                Process.Options.ProducesPathIndependentOutputs // no frontend sets it
                | Process.Options.HasPreserveOutputAllowlist // DScript's unsafe preserveOutputAllowlist, not on the wire
                | Process.Options.DependsOnWindowsProgramData // DScript has no setting for it
                | Process.Options.BypassFingerprintSalt; // explicitly not public in DScript

            ulong operationId = 0;

            void SendProcess(string symbol, global::BuildXL.Engine.DirectGraph.ProcessOptions options)
            {
                var request = NewProcess(symbol, symbol + ".txt");
                request.Options = options;
                var response = Send(new GraphOperation { OperationId = ++operationId, AddProcess = request });
                XAssert.AreEqual(GraphOperationResponseStatus.Success, response.Status, "{0}: {1}", symbol, response.Error);
            }

            // A process without options, then one with each wire option alone.
            var optionFields = global::BuildXL.Engine.DirectGraph.ProcessOptions.Descriptor.Fields.InFieldNumberOrder();
            SendProcess("default", options: null);
            foreach (var field in optionFields)
            {
                var options = new global::BuildXL.Engine.DirectGraph.ProcessOptions();
                field.Accessor.SetValue(options, true);
                SendProcess(field.Name, options);
            }

            var flags = m_pipGraph.RetrieveScheduledPips().OfType<Process>()
                .ToDictionary(p => p.Provenance.OutputValueSymbol.ToString(m_context.SymbolTable), p => p.ProcessOptions);

            var reachable = flags["default"];
            foreach (var field in optionFields)
            {
                XAssert.AreNotEqual(flags["default"], flags[field.Name], "{0} changes no flag of the process", field.Name);
                reachable |= flags[field.Name];
            }

            foreach (var flag in Enum.GetValues(typeof(Process.Options)).Cast<Process.Options>().Where(flag => flag != Process.Options.None))
            {
                XAssert.AreEqual(!notOnTheWire.HasFlag(flag), reachable.HasFlag(flag), "{0} must be set by a wire option or listed as not on the wire, and not both", flag);
            }
        }

        [Fact]
        public void ProcessOutputDirectoriesCanBeReferencedAndComposed()
        {
            // A shared opaque output directory gets its seal id assigned by BuildXL while the process is added, so a later
            // operation names it by the operation that produced it.
            var producerA = Send(new GraphOperation
            {
                OperationId = 1,
                AddProcess = NewProcess("producerA", output: "a.txt", outputDirectory: "opaque"),
            });
            var producerB = Send(new GraphOperation
            {
                OperationId = 2,
                AddProcess = NewProcess("producerB", output: "b.txt", outputDirectory: "opaque"),
            });

            XAssert.AreEqual(GraphOperationResponseStatus.Success, producerA.Status, producerA.Error);
            XAssert.AreEqual(GraphOperationResponseStatus.Success, producerB.Status, producerB.Error);

            // Two pips writing to the same shared opaque root get distinct seal ids.
            var producedDirectories = m_pipGraph.RetrieveScheduledPips().OfType<Process>().Select(p => p.DirectoryOutputs.Single()).ToList();
            XAssert.AreEqual(2, producedDirectories.Count);
            XAssert.IsTrue(producedDirectories.All(directory => directory.IsSharedOpaque));
            XAssert.AreNotEqual(producedDirectories[0].PartialSealId, producedDirectories[1].PartialSealId);

            var compose = new AddCompositeSharedOpaqueDirectoryRequest
            {
                Module = TestModule,
                Symbol = "barrier",
                DirectoryRoot = Path("opaque"),
                Action = CompositionActionKind.WidenDirectoryCone,
                ContentFilter = new ContentFilter { Kind = ContentFilterKind.Exclude, Regex = "\\.tmp$" },
                Contents = { ProducedDirectory(1, "opaque"), ProducedDirectory(2, "opaque") },
            };

            var barrier = Send(new GraphOperation { OperationId = 3, AddCompositeSharedOpaqueDirectory = compose });
            XAssert.AreEqual(GraphOperationResponseStatus.Success, barrier.Status, barrier.Error);

            // The composite is addressable as an input like any other directory. It shares its path with the directories
            // it composes, so only the operation id tells them apart.
            var consumer = NewProcess("consumer", output: "consumed.txt");
            consumer.InputDirectories.Add(ProducedDirectory(3, "opaque"));
            var consumed = Send(new GraphOperation { OperationId = 4, AddProcess = consumer });
            XAssert.AreEqual(GraphOperationResponseStatus.Success, consumed.Status, consumed.Error);

            var dependency = m_pipGraph.RetrieveScheduledPips().OfType<Process>()
                .Single(p => p.Provenance.OutputValueSymbol.ToString(m_context.SymbolTable) == "consumer")
                .DirectoryDependencies.Single();
            XAssert.IsTrue(dependency.IsSharedOpaque);
            XAssert.IsFalse(producedDirectories.Contains(dependency), "the consumer depends on a composed directory rather than the composite");

            // Composing something that is not a shared opaque is rejected rather than silently producing a bad graph.
            var seal = Send(new GraphOperation
            {
                OperationId = 5,
                AddSealDirectory = new AddSealDirectoryRequest
                {
                    Module = TestModule,
                    Symbol = "sealTarget",
                    DirectoryRoot = Path("sealed"),
                    Kind = global::BuildXL.Engine.DirectGraph.SealDirectoryKind.Partial,
                },
            });
            XAssert.AreEqual(GraphOperationResponseStatus.Success, seal.Status, seal.Error);

            var badCompose = new AddCompositeSharedOpaqueDirectoryRequest
            {
                Module = TestModule,
                Symbol = "badBarrier",
                DirectoryRoot = Path("sealed"),
                Contents = { ProducedDirectory(5, "sealed") },
            };

            var rejected = Send(new GraphOperation { OperationId = 6, AddCompositeSharedOpaqueDirectory = badCompose });
            XAssert.AreEqual(GraphOperationResponseStatus.InvalidRequest, rejected.Status);
            XAssert.Contains(rejected.Error, "not a shared opaque");
        }

        [Fact]
        public void ExportsResolveTheirReferencesAndPropagateRegistrationFailures()
        {
            var exports = new List<FileOrDirectoryArtifact>();
            bool registerExport(string moduleName, FileOrDirectoryArtifact artifact, out string error)
            {
                if (moduleName != TestModule)
                {
                    error = $"The module '{moduleName}' is not owned by this resolver.";
                    return false;
                }

                exports.Add(artifact);
                error = null;
                return true;
            }

            var builder = CreateBuilder(registerExport);
            var producer = Send(builder, new GraphOperation { OperationId = 1, AddProcess = NewProcess("producer", "rewritten.txt") });
            XAssert.AreEqual(GraphOperationResponseStatus.Success, producer.Status, producer.Error);

            var rewriter = NewProcess("rewriter", "other.txt");
            rewriter.OutputFiles.Add(new OutputFile
            {
                Rewrites = new ProducedFileReference { ProducerOperationId = 1, Path = Path("rewritten.txt") },
                Existence = FileExistenceKind.Required,
            });
            var rewritten = Send(builder, new GraphOperation { OperationId = 2, AddProcess = rewriter });
            XAssert.AreEqual(GraphOperationResponseStatus.Success, rewritten.Status, rewritten.Error);

            var seal = Send(builder, new GraphOperation
            {
                OperationId = 3,
                AddSealDirectory = new AddSealDirectoryRequest
                {
                    Module = TestModule,
                    Symbol = "seal",
                    DirectoryRoot = Path("sealed"),
                    Kind = global::BuildXL.Engine.DirectGraph.SealDirectoryKind.Partial,
                },
            });
            var batch = Send(builder, new GraphOperation
            {
                OperationId = 4,
                AddExport = new AddExportRequest
                {
                    Module = TestModule,
                    Files = { ProducedFile(2, "rewritten.txt"), new FileArtifactReference { SourcePath = Path("source.txt") } },
                    Directories = { ProducedDirectory(3, "sealed") },
                },
            });

            foreach (var response in new[] { seal, batch })
            {
                XAssert.AreEqual(GraphOperationResponseStatus.Success, response.Status, response.Error);
            }

            XAssert.AreEqual(3, exports.Count);
            XAssert.AreEqual(2, exports[0].FileArtifact.RewriteCount);
            XAssert.AreEqual(m_root.Combine(m_context.PathTable, "rewritten.txt"), exports[0].Path);
            XAssert.IsFalse(exports[1].FileArtifact.IsOutputFile);
            XAssert.AreEqual(m_root.Combine(m_context.PathTable, "sealed"), exports[2].DirectoryArtifact.Path);
            XAssert.AreEqual(0L, batch.SemiStableHash, "exporting artifacts adds no pip");
            XAssert.AreEqual(1, m_counters.GetCounterValue(DirectGraphCounter.AddExportCount));

            // An export's id is claimed, but it produces nothing a later operation can reference.
            var reused = Send(builder, new GraphOperation
            {
                OperationId = 4,
                AddExport = new AddExportRequest { Module = TestModule, Files = { ProducedFile(2, "rewritten.txt") } },
            });
            XAssert.AreEqual(GraphOperationResponseStatus.InvalidRequest, reused.Status);
            XAssert.Contains(reused.Error, "already been used");

            foreach (var reference in new[]
            {
                new AddExportRequest { Module = TestModule, Files = { ProducedFile(1, "source.txt") } },
                new AddExportRequest { Module = TestModule, Directories = { ProducedDirectory(1, "source.txt") } },
            })
            {
                var referencingExport = CreateBuilder();
                var exported = Send(referencingExport, new GraphOperation
                {
                    OperationId = 1,
                    AddExport = new AddExportRequest { Module = TestModule, Files = { new FileArtifactReference { SourcePath = Path("source.txt") } } },
                });
                XAssert.AreEqual(GraphOperationResponseStatus.Success, exported.Status, exported.Error);

                var rejected = Send(referencingExport, new GraphOperation { OperationId = 2, AddExport = reference });
                XAssert.AreEqual(GraphOperationResponseStatus.InvalidRequest, rejected.Status);
                XAssert.Contains(rejected.Error, "produced no such");
            }

            foreach (var (request, expectedError) in new[]
            {
                (new AddExportRequest { Module = TestModule }, "does not specify any files or directories"),
                (new AddExportRequest { Module = TestModule, Files = { ProducedFile(42, "rewritten.txt") } }, "has not received"),
                (new AddExportRequest { Module = TestModule, Directories = { ProducedDirectory(42, "sealed") } }, "has not received"),
                (new AddExportRequest { Module = TestModule, Files = { new FileArtifactReference { SourcePath = Path("source.txt") }, ProducedFile(42, "rewritten.txt") } }, "has not received"),
                (new AddExportRequest { Module = "notOwned", Files = { new FileArtifactReference { SourcePath = Path("source.txt") } } }, "notOwned"),
            })
            {
                var rejectingBuilder = CreateBuilder(registerExport);
                var rejected = Send(rejectingBuilder, new GraphOperation { OperationId = 1, AddExport = request });
                XAssert.AreEqual(GraphOperationResponseStatus.InvalidRequest, rejected.Status);
                XAssert.Contains(rejected.Error, expectedError);

                var complete = Send(rejectingBuilder, new GraphOperation { OperationId = 2, Complete = new CompleteRequest() });
                XAssert.AreEqual(GraphOperationResponseStatus.PreviousOperationRejected, complete.Status);
                XAssert.IsFalse(rejectingBuilder.IsComplete);
            }
        }

        private AddProcessRequest NewProcess(string symbol, string output, FileArtifactReference input = null, string outputDirectory = null)
        {
            var request = new AddProcessRequest
            {
                Module = TestModule,
                Symbol = symbol,
                Executable = new FileArtifactReference { SourcePath = Path("tool.exe") },
                WorkingDirectory = Intern(m_paths, m_root.ToString(m_context.PathTable)),
            };

            request.OutputFiles.Add(new OutputFile { Path = Path(output), Existence = FileExistenceKind.Required });

            if (input != null)
            {
                request.InputFiles.Add(input);
            }

            if (outputDirectory != null)
            {
                request.OutputDirectories.Add(new global::BuildXL.Engine.DirectGraph.OutputDirectory
                {
                    Path = Path(outputDirectory),
                    Kind = OutputDirectoryKind.Shared,
                });
            }

            return request;
        }

        private Process ProcessOf(string symbol) =>
            m_pipGraph.RetrieveScheduledPips().OfType<Process>()
                .Single(p => p.Provenance.OutputValueSymbol.ToString(m_context.SymbolTable) == symbol);

        private FileArtifactReference ProducedFile(ulong producerOperationId, string relative) =>
            new() { ProducedFile = new ProducedFileReference { ProducerOperationId = producerOperationId, Path = Path(relative) } };

        private DirectoryArtifactReference ProducedDirectory(ulong producerOperationId, string relative) =>
            new() { ProducerOperationId = producerOperationId, Path = Path(relative) };

        private uint Path(string relative) => Intern(m_paths, m_root.Combine(m_context.PathTable, relative).ToString(m_context.PathTable));

        private uint Text(string value) => Intern(m_strings, value);

        private static uint Intern(List<string> values, string value)
        {
            int id = values.IndexOf(value);
            if (id < 0)
            {
                id = values.Count;
                values.Add(value);
            }

            return (uint)id;
        }

        private GraphOperationResponse Send(GraphOperation operation) => Send(m_graphBuilder, operation);

        /// <summary>
        /// Sends the operation with the current protocol version, registering with it every value the builder has not
        /// been sent yet. Ids are numbered once per test, so a request can go to any builder.
        /// </summary>
        private GraphOperationResponse Send(DirectGraphPipGraphBuilder builder, GraphOperation operation)
        {
            var (paths, strings) = m_registered.GetValueOrDefault(builder);
            for (int id = paths; id < m_paths.Count; id++)
            {
                operation.NewPaths.Add(new Registration { Id = (uint)id, Value = m_paths[id] });
            }

            for (int id = strings; id < m_strings.Count; id++)
            {
                operation.NewStrings.Add(new Registration { Id = (uint)id, Value = m_strings[id] });
            }

            m_registered[builder] = (m_paths.Count, m_strings.Count);

            operation.ProtocolVersion = DirectGraphProtocol.Version;
            return SendRaw(builder, operation);
        }

        private GraphOperationResponse SendRaw(DirectGraphPipGraphBuilder builder, GraphOperation operation) =>
            builder.HandleOperationAsync(operation, CancellationToken.None).GetAwaiter().GetResult();

        private bool TryGetPipConstructionHelper(string moduleName, string symbol, out PipConstructionHelper pipConstructionHelper, out string error)
        {
            pipConstructionHelper = null;

            if (moduleName != TestModule)
            {
                error = $"The module '{moduleName}' is not owned by this resolver.";
                return false;
            }

            if (!m_helpers.TryGetValue((moduleName, symbol), out pipConstructionHelper))
            {
                pipConstructionHelper = PipConstructionHelper.CreateForTesting(
                    m_context,
                    objectRoot: m_root,
                    redirectedRoot: m_root.Combine(m_context.PathTable, "redirected"),
                    pipGraph: m_pipGraph,
                    moduleName: moduleName,
                    symbol: symbol,
                    specPath: m_root.Combine(m_context.PathTable, "spec.dsc"));
                m_helpers.Add((moduleName, symbol), pipConstructionHelper);
            }

            error = null;
            return true;
        }
    }
}
