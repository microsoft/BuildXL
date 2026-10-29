// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Engine.DirectGraph.Tracing;
using BuildXL.Pips;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Engine.DirectGraph
{
    /// <summary>
    /// Resolves the <see cref="PipConstructionHelper"/> that owns pips for a given module and target symbol.
    /// </summary>
    /// <remarks>
    /// Returns false and populates <paramref name="error"/> when the module is not owned by the resolver, or the
    /// symbol cannot be used to construct pip provenance.
    /// </remarks>
    public delegate bool PipConstructionHelperResolver(string moduleName, string symbol, out PipConstructionHelper pipConstructionHelper, out string error);

    /// <summary>
    /// Publishes an artifact at its own path in a module, or returns false with the reason it cannot be exported.
    /// </summary>
    public delegate bool TryRegisterExport(string moduleName, FileOrDirectoryArtifact artifact, out string error);

    /// <summary>
    /// Translates DirectGraph protocol operations into BuildXL pip construction calls.
    /// </summary>
    /// <remarks>
    /// Serves one stream: the interned values and the operation ids it records are that stream's, so another stream
    /// needs its own instance. Not thread safe: the server hands it one operation at a time, in stream order.
    /// </remarks>
    public sealed class DirectGraphPipGraphBuilder : IDirectGraphGraphBuilder
    {
        private const string DefaultToolDescription = "DirectGraph process";

        private readonly PathTable m_pathTable;
        private readonly IMutablePipGraph m_pipGraph;

        // Supplies the defaults a DScript process would get from the build's configuration.
        private readonly ISandboxConfiguration m_sandboxConfiguration;
        private readonly Func<ProcessBuilder> m_processBuilderFactory;
        private readonly PipConstructionHelperResolver m_pipConstructionHelperResolver;
        private readonly TryRegisterExport m_registerExport;
        private readonly LoggingContext m_loggingContext;
        private readonly bool m_traceOperation;
        private readonly CounterCollection<DirectGraphCounter> m_counters;
        private (ulong OperationId, string Error)? m_firstRejection;
        private bool m_complete;

        // The values the emitter registered, indexed by id in the order it registered them.
        private readonly List<AbsolutePath> m_paths = new();
        private readonly List<StringId> m_strings = new();

        // What each operation that has succeeded produced, by the emitter's operation id. A later operation names an
        // output by its producer and path, and is resolved from here rather than from the graph, whose lookup by path
        // returns only the latest artifact of the path. A directory's seal id is known only here as well: BuildXL
        // assigns it when it constructs the producer.
        private readonly Dictionary<ulong, ProducedArtifacts> m_producedArtifacts = new();

        /// <nodoc />
        public DirectGraphPipGraphBuilder(
            PathTable pathTable,
            IMutablePipGraph pipGraph,
            ISandboxConfiguration sandboxConfiguration,
            Func<ProcessBuilder> processBuilderFactory,
            PipConstructionHelperResolver pipConstructionHelperResolver,
            TryRegisterExport registerExport,
            LoggingContext loggingContext,
            bool traceOperation,
            CounterCollection<DirectGraphCounter> counters = null)
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(pipGraph != null);
            Contract.Requires(sandboxConfiguration != null);
            Contract.Requires(processBuilderFactory != null);
            Contract.Requires(pipConstructionHelperResolver != null);
            Contract.Requires(registerExport != null);
            Contract.Requires(loggingContext != null);

            m_pathTable = pathTable;
            m_pipGraph = pipGraph;
            m_sandboxConfiguration = sandboxConfiguration;
            m_processBuilderFactory = processBuilderFactory;
            m_pipConstructionHelperResolver = pipConstructionHelperResolver;
            m_registerExport = registerExport;
            m_loggingContext = loggingContext;
            m_traceOperation = traceOperation;
            m_counters = counters ?? new CounterCollection<DirectGraphCounter>();
        }

        /// <summary>
        /// The first operation this builder rejected, if any.
        /// </summary>
        /// <remarks>
        /// A rejected operation means the graph is not the one the emitter asked for, so the build has to fail. The
        /// builder cannot fail it directly - it runs on the server's stream loop while the resolver is blocked
        /// awaiting the emitter process - and no later check would catch it either, since
        /// <see cref="DirectGraphServer"/> ends the stream when Complete arrives whatever status Complete was
        /// answered with. The resolver reads this once the tool is gone.
        /// </remarks>
        public bool TryGetFirstRejection(out ulong operationId, out string error)
        {
            if (m_firstRejection == null)
            {
                operationId = 0;
                error = null;
                return false;
            }

            (operationId, error) = m_firstRejection.Value;
            return true;
        }

        /// <summary>
        /// Whether Complete has been handled: the emitter declared the graph finished.
        /// </summary>
        /// <remarks>
        /// Set before Complete's response is written, so an emitter that exits as soon as it reads the response cannot
        /// get ahead of it.
        /// </remarks>
        public bool IsComplete => Volatile.Read(ref m_complete);

        /// <inheritdoc />
        public Task<GraphOperationResponse> HandleOperationAsync(GraphOperation operation, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (m_traceOperation)
            {
                Logger.Log.GraphOperationReceived(m_loggingContext, operation.ToString());
            }

            GraphOperationResponse response;
            if (TryGetOperationCounters(operation.OperationCase, out var countCounter, out var durationCounter))
            {
                m_counters.IncrementCounter(countCounter);
                using (m_counters.StartStopwatch(durationCounter))
                {
                    response = HandleOperation(operation);
                }
            }
            else
            {
                response = HandleOperation(operation);
            }

            WithProtocolVersion(response);

            if (m_traceOperation)
            {
                Logger.Log.GraphOperationResponse(m_loggingContext, response.ToString());
            }

            return Task.FromResult(response);
        }

        private GraphOperationResponse HandleOperation(GraphOperation operation) =>
            // m_firstRejection is read before dispatching, because dispatching is what sets it.
            m_firstRejection != null ? AlreadyFailed(operation.OperationId) :
            operation.ProtocolVersion != DirectGraphProtocol.Version ? VersionMismatch(operation) :
            Dispatch(operation);

        private static bool TryGetOperationCounters(
            GraphOperation.OperationOneofCase operationCase,
            out DirectGraphCounter count,
            out DirectGraphCounter duration)
        {
            switch (operationCase)
            {
                case GraphOperation.OperationOneofCase.AddProcess:
                    (count, duration) = (DirectGraphCounter.AddProcessCount, DirectGraphCounter.AddProcessDuration);
                    return true;
                case GraphOperation.OperationOneofCase.AddWriteFile:
                    (count, duration) = (DirectGraphCounter.AddWriteFileCount, DirectGraphCounter.AddWriteFileDuration);
                    return true;
                case GraphOperation.OperationOneofCase.AddCopyFile:
                    (count, duration) = (DirectGraphCounter.AddCopyFileCount, DirectGraphCounter.AddCopyFileDuration);
                    return true;
                case GraphOperation.OperationOneofCase.AddSealDirectory:
                    (count, duration) = (DirectGraphCounter.AddSealDirectoryCount, DirectGraphCounter.AddSealDirectoryDuration);
                    return true;
                case GraphOperation.OperationOneofCase.AddCompositeSharedOpaqueDirectory:
                    (count, duration) = (DirectGraphCounter.AddCompositeSharedOpaqueDirectoryCount, DirectGraphCounter.AddCompositeSharedOpaqueDirectoryDuration);
                    return true;
                case GraphOperation.OperationOneofCase.AddExport:
                    (count, duration) = (DirectGraphCounter.AddExportCount, DirectGraphCounter.AddExportDuration);
                    return true;
                case GraphOperation.OperationOneofCase.Complete:
                    (count, duration) = (DirectGraphCounter.CompleteCount, DirectGraphCounter.CompleteDuration);
                    return true;
                default:
                    count = default;
                    duration = default;
                    return false;
            }
        }

        private GraphOperationResponse Dispatch(GraphOperation operation)
        {
            ulong operationId = operation.OperationId;

            try
            {
                Accept(operation);

                return operation.OperationCase switch
                {
                    GraphOperation.OperationOneofCase.AddProcess => AddProcess(operationId, operation.AddProcess),
                    GraphOperation.OperationOneofCase.AddWriteFile => AddWriteFile(operationId, operation.AddWriteFile),
                    GraphOperation.OperationOneofCase.AddCopyFile => AddCopyFile(operationId, operation.AddCopyFile),
                    GraphOperation.OperationOneofCase.AddSealDirectory => AddSealDirectory(operationId, operation.AddSealDirectory),
                    GraphOperation.OperationOneofCase.AddCompositeSharedOpaqueDirectory => AddCompositeSharedOpaqueDirectory(operationId, operation.AddCompositeSharedOpaqueDirectory),
                    GraphOperation.OperationOneofCase.AddExport => AddExport(operationId, operation.AddExport),
                    GraphOperation.OperationOneofCase.Complete => Complete(operationId),
                    _ => Failure(operationId, GraphOperationResponseStatus.InvalidRequest, "The graph operation is not specified."),
                };
            }
            catch (InvalidRequestException e)
            {
                return Failure(operationId, GraphOperationResponseStatus.InvalidRequest, e.Message);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Pip construction reports some invalid inputs by throwing. Nothing after a rejection is processed, so a
                // partly constructed pip is never built on.
                return Failure(operationId, GraphOperationResponseStatus.GraphError, $"BuildXL could not construct the pip: {e.GetType().Name}: {e.Message}");
            }
        }

        private GraphOperationResponse Complete(ulong operationId)
        {
            Volatile.Write(ref m_complete, true);
            return Success(operationId);
        }

        /// <summary>
        /// Checks that the operation's id is new and registers the values it introduces, before anything in it is resolved.
        /// </summary>
        private void Accept(GraphOperation operation)
        {
            if (operation.OperationId == 0)
            {
                throw new InvalidRequestException("The operation has no id. Operation ids must be non-zero.");
            }

            // An operation is recorded when it succeeds, and nothing is handled after a rejection or after Complete, so the
            // recorded ids are all the ids that have been used.
            if (m_producedArtifacts.ContainsKey(operation.OperationId))
            {
                throw new InvalidRequestException(
                    $"Operation id {operation.OperationId} has already been used. Operation ids must be unique within a stream.");
            }

            foreach (var registration in operation.NewPaths)
            {
                CheckNextId(registration, m_paths.Count, "new_paths");

                if (!AbsolutePath.TryCreate(m_pathTable, registration.Value, out var path))
                {
                    throw new InvalidRequestException($"'{registration.Value}', registered as path id {registration.Id}, is not a valid absolute path.");
                }

                m_paths.Add(path);
            }

            foreach (var registration in operation.NewStrings)
            {
                CheckNextId(registration, m_strings.Count, "new_strings");
                m_strings.Add(StringId.Create(m_pathTable.StringTable, registration.Value));
            }
        }

        private static void CheckNextId(Registration registration, int registered, string field)
        {
            if (registration.Id != (uint)registered)
            {
                throw new InvalidRequestException(
                    $"'{field}' registers id {registration.Id}, but the next id is {registered}. The emitter's ids no longer match BuildXL's.");
            }
        }

        private GraphOperationResponse AddProcess(ulong operationId, AddProcessRequest request)
        {
            if (!TryGetPipConstructionHelper(operationId, request.Module, request.Symbol, out var pipConstructionHelper, out var failure))
            {
                return failure;
            }

            if (request.Executable == null)
            {
                return Failure(operationId, GraphOperationResponseStatus.InvalidRequest, "The process operation does not specify an executable.");
            }

            var executable = ResolveFile(request.Executable, "executable");
            var workingDirectory = RequiredPath(request.HasWorkingDirectory, request.WorkingDirectory, "working_directory");

            using var processBuilder = m_processBuilderFactory();

            processBuilder.Executable = executable;
            processBuilder.WorkingDirectory = DirectoryArtifact.CreateWithZeroPartialSealId(workingDirectory);
            processBuilder.AddInputFile(executable);
            // As in DScript, the description of the execute is the pip's usage and the tool has a description of its own.
            processBuilder.ToolDescription = request.HasToolDescription
                ? ResolveString(request.ToolDescription, "tool_description")
                : StringId.Create(m_pathTable.StringTable, DefaultToolDescription);

            string description = OptionalStringValue(request.HasDescription, request.Description, "description");
            if (!string.IsNullOrEmpty(description))
            {
                processBuilder.Usage = PipDataBuilder.CreatePipData(m_pathTable.StringTable, string.Empty, PipDataFragmentEscaping.NoEscaping, description);
            }

            // Options and policies default as they do for a DScript process, so the emitter gets only what it asks for.
            processBuilder.Options = ToProcessOptions(request.Options ?? new ProcessOptions());
            processBuilder.RewritePolicy = ToRewritePolicy(request);
            if (request.HasAbsentPathProbeInUndeclaredOpaqueMode)
            {
                processBuilder.AbsentPathProbeUnderOpaquesMode = request.AbsentPathProbeInUndeclaredOpaqueMode switch
                {
                    AbsentPathProbeMode.Unsafe => Process.AbsentPathProbeInUndeclaredOpaquesMode.Unsafe,
                    AbsentPathProbeMode.Strict => Process.AbsentPathProbeInUndeclaredOpaquesMode.Strict,
                    AbsentPathProbeMode.Relaxed => Process.AbsentPathProbeInUndeclaredOpaquesMode.Relaxed,
                    _ => throw InvalidEnum((int)request.AbsentPathProbeInUndeclaredOpaqueMode, "absent_path_probe_in_undeclared_opaque_mode"),
                };
            }

            foreach (uint argument in request.Arguments)
            {
                processBuilder.ArgumentsBuilder.Add(PipDataAtom.FromStringId(ResolveString(argument, "arguments")));
            }

            foreach (var input in request.InputFiles)
            {
                processBuilder.AddInputFile(ResolveFile(input, "input_files"));
            }

            foreach (var inputDirectory in request.InputDirectories)
            {
                processBuilder.AddInputDirectory(ResolveDirectory(inputDirectory, "input_directories"));
            }

            foreach (var output in request.OutputFiles)
            {
                var existence = ToFileExistence(output.Existence);

                switch (output.TargetCase)
                {
                    case OutputFile.TargetOneofCase.Path:
                        processBuilder.AddOutputFile(ResolvePath(output.Path, "output_files"), existence);
                        break;

                    case OutputFile.TargetOneofCase.Rewrites:
                    {
                        // The process reads the file it rewrites, so BuildXL has to put it in place before the process runs.
                        var rewritten = ResolveProducedFile(output.Rewrites, "output_files");
                        processBuilder.AddInputFile(rewritten);
                        processBuilder.AddOutputFile(rewritten, existence);
                        break;
                    }

                    default:
                        throw new InvalidRequestException("'output_files' has an entry that sets neither 'path' nor 'rewrites'.");
                }
            }

            foreach (var outputDirectory in request.OutputDirectories)
            {
                var outputDirectoryPath = RequiredPath(outputDirectory.HasPath, outputDirectory.Path, "output_directories");
                bool isShared = outputDirectory.Kind switch
                {
                    OutputDirectoryKind.Shared => true,
                    OutputDirectoryKind.Exclusive => false,
                    _ => throw InvalidEnum((int)outputDirectory.Kind, "output_directories.kind"),
                };

                processBuilder.AddOutputDirectory(
                    isShared
                        ? m_pipGraph.ReserveSharedOpaqueDirectory(outputDirectoryPath)
                        : DirectoryArtifact.CreateWithZeroPartialSealId(outputDirectoryPath),
                    isShared
                        ? BuildXL.Pips.Operations.SealDirectoryKind.SharedOpaque
                        : BuildXL.Pips.Operations.SealDirectoryKind.Opaque);
            }

            foreach (uint exclusion in request.OutputDirectoryExclusions)
            {
                processBuilder.AddOutputDirectoryExclusion(ResolvePath(exclusion, "output_directory_exclusions"));
            }

            foreach (uint untracked in request.UntrackedPaths)
            {
                processBuilder.AddUntrackedFile(ResolvePath(untracked, "untracked_paths"));
            }

            foreach (uint untrackedScope in request.UntrackedScopes)
            {
                processBuilder.AddUntrackedDirectoryScope(DirectoryArtifact.CreateWithZeroPartialSealId(ResolvePath(untrackedScope, "untracked_scopes")));
            }

            foreach (var environmentVariable in request.EnvironmentVariables)
            {
                var name = RequiredString(environmentVariable.HasName, environmentVariable.Name, "environment_variables.name");

                if (environmentVariable.IsPassThrough)
                {
                    processBuilder.SetPassthroughEnvironmentVariable(name);
                }
                else
                {
                    var value = RequiredString(environmentVariable.HasValue, environmentVariable.Value, "environment_variables.value");
                    processBuilder.SetEnvironmentVariable(name, PipDataAtom.FromStringId(value), isPassThrough: false);
                }
            }

            foreach (var semaphore in request.Semaphores)
            {
                string name = StringValue(RequiredString(semaphore.HasName, semaphore.Name, "semaphores.name"));
                processBuilder.SetSemaphore(name, semaphore.Limit, semaphore.IncrementBy);
            }

            if (!TrySetProcessRuntimeBehavior(operationId, request, processBuilder, out failure))
            {
                return failure;
            }

            SetProcessRedirection(request, processBuilder);

            if (!pipConstructionHelper.TryAddProcess(processBuilder, out var processOutputs, out var process))
            {
                return Failure(operationId, GraphOperationResponseStatus.GraphError, "BuildXL rejected the process pip.");
            }

            var outputFiles = processOutputs.GetOutputFiles().ToArray();
            Array.Sort(outputFiles, OrdinalPathOnlyFileArtifactComparer.Instance);

            // Shared opaque output directories get their seal id assigned while the process is added, so this is the
            // only place a later reference to one can be resolved from.
            m_producedArtifacts[operationId] = new ProducedArtifacts(
                outputFiles,
                processOutputs.GetOutputDirectories().Select(outputDirectory => outputDirectory.Root).ToArray());

            return Success(operationId, process.SemiStableHash);
        }

        private GraphOperationResponse AddWriteFile(ulong operationId, AddWriteFileRequest request)
        {
            if (!TryGetPipConstructionHelper(operationId, request.Module, request.Symbol, out var pipConstructionHelper, out var failure))
            {
                return failure;
            }

            var destination = RequiredPath(request.HasDestination, request.Destination, "destination");

            var contentBuilder = new PipDataBuilder(m_pathTable.StringTable);
            contentBuilder.Add(RequiredString(request.HasContent, request.Content, "content"));

            if (!pipConstructionHelper.TryWriteFile(
                    destination,
                    contentBuilder.ToPipData(string.Empty, PipDataFragmentEscaping.NoEscaping),
                    WriteFileEncoding.Utf8,
                    ResolveStrings(request.Tags, "tags"),
                    description: OptionalStringValue(request.HasDescription, request.Description, "description"),
                    fileArtifact: out var destinationFile,
                    pip: out var writeFile))
            {
                return Failure(operationId, GraphOperationResponseStatus.GraphError, "BuildXL rejected the write-file pip.");
            }

            m_producedArtifacts[operationId] = new ProducedArtifacts(new[] { destinationFile }, Array.Empty<DirectoryArtifact>());
            return Success(operationId, writeFile.SemiStableHash);
        }

        private GraphOperationResponse AddCopyFile(ulong operationId, AddCopyFileRequest request)
        {
            if (!TryGetPipConstructionHelper(operationId, request.Module, request.Symbol, out var pipConstructionHelper, out var failure))
            {
                return failure;
            }

            if (request.Source == null)
            {
                return Failure(operationId, GraphOperationResponseStatus.InvalidRequest, "The copy-file operation does not specify a source.");
            }

            var source = ResolveFile(request.Source, "source");
            var destination = RequiredPath(request.HasDestination, request.Destination, "destination");

            if (!pipConstructionHelper.TryCopyFile(
                    source,
                    destination,
                    request.OutputsMustRemainWritable ? CopyFile.Options.OutputsMustRemainWritable : CopyFile.Options.None,
                    ResolveStrings(request.Tags, "tags"),
                    OptionalStringValue(request.HasDescription, request.Description, "description"),
                    out var destinationFile,
                    out var copyFile))
            {
                return Failure(operationId, GraphOperationResponseStatus.GraphError, "BuildXL rejected the copy-file pip.");
            }

            m_producedArtifacts[operationId] = new ProducedArtifacts(new[] { destinationFile }, Array.Empty<DirectoryArtifact>());
            return Success(operationId, copyFile.SemiStableHash);
        }

        private GraphOperationResponse AddSealDirectory(ulong operationId, AddSealDirectoryRequest request)
        {
            if (!TryGetPipConstructionHelper(operationId, request.Module, request.Symbol, out var pipConstructionHelper, out var failure))
            {
                return failure;
            }

            var directoryRoot = RequiredPath(request.HasDirectoryRoot, request.DirectoryRoot, "directory_root");

            var contents = new FileArtifact[request.Contents.Count];
            for (int i = 0; i < contents.Length; i++)
            {
                contents[i] = ResolveFile(request.Contents[i], "contents");
            }

            var outputDirectoryContents = new DirectoryArtifact[request.OutputDirectoryContents.Count];
            for (int i = 0; i < outputDirectoryContents.Length; i++)
            {
                outputDirectoryContents[i] = ResolveDirectory(request.OutputDirectoryContents[i], "output_directory_contents");
            }

            if (!pipConstructionHelper.TrySealDirectory(
                    directoryRoot,
                    SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>.SortUnsafe(
                        contents,
                        OrdinalFileArtifactComparer.Instance),
                    SortedReadOnlyArray<DirectoryArtifact, OrdinalDirectoryArtifactComparer>.SortUnsafe(
                        outputDirectoryContents,
                        OrdinalDirectoryArtifactComparer.Instance),
                    ToSealDirectoryKind(request.Kind),
                    ResolveStrings(request.Tags, "tags"),
                    OptionalStringValue(request.HasDescription, request.Description, "description"),
                    ResolveStrings(request.Patterns, "patterns"),
                    out var sealedDirectory,
                    out var sealDirectoryPip,
                    request.Scrub))
            {
                return Failure(operationId, GraphOperationResponseStatus.GraphError, "BuildXL rejected the seal-directory pip.");
            }

            m_producedArtifacts[operationId] = new ProducedArtifacts(Array.Empty<FileArtifact>(), new[] { sealedDirectory });
            return Success(operationId, sealDirectoryPip.SemiStableHash);
        }

        private GraphOperationResponse AddExport(ulong operationId, AddExportRequest request)
        {
            if (request.Files.Count == 0 && request.Directories.Count == 0)
            {
                return Failure(operationId, GraphOperationResponseStatus.InvalidRequest, "The export operation does not specify any files or directories.");
            }

            foreach (var file in request.Files)
            {
                if (!m_registerExport(request.Module, FileOrDirectoryArtifact.Create(ResolveFile(file, "files")), out string error))
                {
                    return Failure(operationId, GraphOperationResponseStatus.InvalidRequest, error);
                }
            }

            foreach (var directory in request.Directories)
            {
                if (!m_registerExport(request.Module, FileOrDirectoryArtifact.Create(ResolveDirectory(directory, "directories")), out string error))
                {
                    return Failure(operationId, GraphOperationResponseStatus.InvalidRequest, error);
                }
            }

            // Claim the id so it cannot be reused; exports produce no artifacts.
            m_producedArtifacts[operationId] = new ProducedArtifacts(Array.Empty<FileArtifact>(), Array.Empty<DirectoryArtifact>());
            return Success(operationId);
        }

        private GraphOperationResponse AddCompositeSharedOpaqueDirectory(ulong operationId, AddCompositeSharedOpaqueDirectoryRequest request)
        {
            if (!TryGetPipConstructionHelper(operationId, request.Module, request.Symbol, out var pipConstructionHelper, out var failure))
            {
                return failure;
            }

            var directoryRoot = RequiredPath(request.HasDirectoryRoot, request.DirectoryRoot, "directory_root");

            var contents = new DirectoryArtifact[request.Contents.Count];
            for (int i = 0; i < contents.Length; i++)
            {
                contents[i] = ResolveDirectory(request.Contents[i], "contents");

                if (!contents[i].IsSharedOpaque)
                {
                    return Failure(
                        operationId,
                        GraphOperationResponseStatus.InvalidRequest,
                        $"'{contents[i].Path.ToString(m_pathTable)}' cannot be composed because it is not a shared opaque directory.");
                }
            }

            SealDirectoryContentFilter? contentFilter = null;
            if (request.ContentFilter != null)
            {
                // Checked as DScript checks it, rather than failing the scheduler when the composite runs.
                try
                {
                    _ = new Regex(request.ContentFilter.Regex);
                }
                catch (ArgumentException e)
                {
                    return Failure(operationId, GraphOperationResponseStatus.InvalidRequest, $"'content_filter.regex' is not a valid regular expression: {e.Message}");
                }

                contentFilter = new SealDirectoryContentFilter(
                    request.ContentFilter.Kind switch
                    {
                        ContentFilterKind.Include => SealDirectoryContentFilter.ContentFilterKind.Include,
                        ContentFilterKind.Exclude => SealDirectoryContentFilter.ContentFilterKind.Exclude,
                        _ => throw InvalidEnum((int)request.ContentFilter.Kind, "content_filter.kind"),
                    },
                    request.ContentFilter.Regex);
            }

            var action = request.Action switch
            {
                CompositionActionKind.WidenDirectoryCone => SealDirectoryCompositionActionKind.WidenDirectoryCone,
                CompositionActionKind.NarrowDirectoryCone => SealDirectoryCompositionActionKind.NarrowDirectoryCone,
                _ => throw InvalidEnum((int)request.Action, "action"),
            };

            if (!pipConstructionHelper.TryComposeSharedOpaqueDirectory(
                    directoryRoot,
                    contents,
                    action,
                    contentFilter,
                    OptionalStringValue(request.HasDescription, request.Description, "description"),
                    ResolveStrings(request.Tags, "tags"),
                    out var composedDirectory,
                    out var composedPip))
            {
                return Failure(operationId, GraphOperationResponseStatus.GraphError, "BuildXL rejected the composed shared opaque directory.");
            }

            long semiStableHash = composedPip?.SemiStableHash ?? 0;
            m_producedArtifacts[operationId] = new ProducedArtifacts(Array.Empty<FileArtifact>(), new[] { composedDirectory });
            return Success(operationId, semiStableHash);
        }

        /// <summary>
        /// Resolves the pip construction helper that owns pips for a module and target symbol.
        /// </summary>
        private bool TryGetPipConstructionHelper(
            ulong operationId,
            string module,
            string symbol,
            out PipConstructionHelper pipConstructionHelper,
            out GraphOperationResponse failure)
        {
            pipConstructionHelper = null;
            failure = null;

            if (string.IsNullOrEmpty(symbol))
            {
                failure = Failure(
                    operationId,
                    GraphOperationResponseStatus.InvalidRequest,
                    "The operation does not specify a symbol. Every pip has to name the target it belongs to, so that its provenance is stable across builds.");
                return false;
            }

            if (!m_pipConstructionHelperResolver(module, symbol, out pipConstructionHelper, out string error))
            {
                failure = Failure(operationId, GraphOperationResponseStatus.InvalidRequest, error);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Resolves a file reference: a source path, or a file an earlier operation produced.
        /// </summary>
        private FileArtifact ResolveFile(FileArtifactReference reference, string field)
        {
            switch (reference?.ReferenceCase)
            {
                case FileArtifactReference.ReferenceOneofCase.SourcePath:
                    return FileArtifact.CreateSourceFile(ResolvePath(reference.SourcePath, field));

                case FileArtifactReference.ReferenceOneofCase.ProducedFile:
                    return ResolveProducedFile(reference.ProducedFile, field);

                default:
                    throw new InvalidRequestException($"A file reference in '{field}' is not specified.");
            }
        }

        /// <summary>
        /// Resolves a reference to a file an earlier operation produced: the artifact that operation wrote.
        /// </summary>
        private FileArtifact ResolveProducedFile(ProducedFileReference produced, string field)
        {
            var path = RequiredPath(produced.HasPath, produced.Path, field);
            var producer = Producer(produced.ProducerOperationId, field);

            int index = Array.BinarySearch(
                producer.Files,
                FileArtifact.CreateSourceFile(path),
                OrdinalPathOnlyFileArtifactComparer.Instance);

            if (index < 0)
            {
                throw new InvalidRequestException(
                    $"'{field}' refers to file '{path.ToString(m_pathTable)}' of operation {produced.ProducerOperationId}, which produced no such file.");
            }

            return producer.Files[index];
        }

        /// <summary>
        /// Resolves a reference to one of the directories an earlier operation produced.
        /// </summary>
        private DirectoryArtifact ResolveDirectory(DirectoryArtifactReference reference, string field)
        {
            if (reference == null)
            {
                throw new InvalidRequestException($"A directory reference in '{field}' is not specified.");
            }

            var path = RequiredPath(reference.HasPath, reference.Path, field);
            var producer = Producer(reference.ProducerOperationId, field);

            foreach (var directory in producer.Directories)
            {
                if (directory.Path == path)
                {
                    return directory;
                }
            }

            throw new InvalidRequestException(
                $"'{field}' refers to directory '{path.ToString(m_pathTable)}' of operation {reference.ProducerOperationId}, which produced no such directory.");
        }

        private ProducedArtifacts Producer(ulong operationId, string field)
        {
            if (!m_producedArtifacts.TryGetValue(operationId, out var produced))
            {
                throw new InvalidRequestException(
                    $"'{field}' refers to operation {operationId}, which BuildXL has not received. An operation must be sent after the operations it depends on.");
            }

            return produced;
        }

        private bool TrySetProcessRuntimeBehavior(
            ulong operationId,
            AddProcessRequest request,
            ProcessBuilder processBuilder,
            out GraphOperationResponse failure)
        {
            failure = null;

            processBuilder.Tags = ResolveStringIds(request.Tags, "tags");

            // As in DScript, a succeed-fast or uncacheable exit code is also a success.
            processBuilder.SucceedFastExitCodes = ReadOnlyArray<int>.From(request.SucceedFastExitCodes);
            processBuilder.UncacheableExitCodes = ReadOnlyArray<int>.From(request.UncacheableExitCodes);
            processBuilder.SuccessExitCodes = ReadOnlyArray<int>.FromWithoutCopy(
                request.SuccessExitCodes.Concat(request.SucceedFastExitCodes).Concat(request.UncacheableExitCodes).ToArray());
            processBuilder.RetryExitCodes = ReadOnlyArray<int>.From(request.RetryExitCodes);
            processBuilder.EnableMultiLineErrorScanning = request.EnableMultiLineErrorScanning;

            if (request.TimeoutMillis > 0)
            {
                processBuilder.Timeout = TimeSpan.FromMilliseconds(request.TimeoutMillis);
            }

            if (request.WarningTimeoutMillis > 0)
            {
                processBuilder.WarningTimeout = TimeSpan.FromMilliseconds(request.WarningTimeoutMillis);
            }

            if (request.Weight > 0)
            {
                processBuilder.Weight = request.Weight;
            }

            if (request.Priority > 0)
            {
                processBuilder.Priority = request.Priority;
            }

            if (request.ProcessRetries > 0)
            {
                processBuilder.SetProcessRetries(request.ProcessRetries);
            }

            // As in DScript, a supplied pattern is matched with no options; an omitted one keeps the builder's default.
            if (request.HasWarningRegex)
            {
                processBuilder.WarningRegex = new BuildXL.Pips.Operations.RegexDescriptor(
                    ResolveString(request.WarningRegex, "warning_regex"),
                    RegexOptions.None);
            }

            if (request.HasErrorRegex)
            {
                processBuilder.ErrorRegex = new BuildXL.Pips.Operations.RegexDescriptor(
                    ResolveString(request.ErrorRegex, "error_regex"),
                    RegexOptions.None);
            }

            processBuilder.AllowedUndeclareSourceReadScopes = ResolvePaths(request.AllowedUndeclaredSourceReadScopes, "allowed_undeclared_source_read_scopes");
            processBuilder.AllowedUndeclareSourceReadPaths = ResolvePaths(request.AllowedUndeclaredSourceReadPaths, "allowed_undeclared_source_read_paths");

            var survivingChildProcessNames = new PathAtom[request.AllowedSurvivingChildProcessNames.Count];
            for (int i = 0; i < survivingChildProcessNames.Length; i++)
            {
                string name = StringValue(ResolveString(request.AllowedSurvivingChildProcessNames[i], "allowed_surviving_child_process_names"));
                if (!PathAtom.TryCreate(m_pathTable.StringTable, name, out survivingChildProcessNames[i]))
                {
                    failure = Failure(
                        operationId,
                        GraphOperationResponseStatus.InvalidRequest,
                        $"'{name}' is not a valid process name for 'allowed_surviving_child_process_names'.");
                    return false;
                }
            }

            processBuilder.AllowedSurvivingChildProcessNames = ReadOnlyArray<PathAtom>.From(survivingChildProcessNames);

            var breakawayChildProcesses = new IBreakawayChildProcess[request.BreakawayChildProcesses.Count];
            for (int i = 0; i < breakawayChildProcesses.Length; i++)
            {
                var breakaway = request.BreakawayChildProcesses[i];
                string name = StringValue(RequiredString(breakaway.HasProcessName, breakaway.ProcessName, "breakaway_child_processes.process_name"));
                if (!PathAtom.TryCreate(m_pathTable.StringTable, name, out var breakawayName))
                {
                    failure = Failure(
                        operationId,
                        GraphOperationResponseStatus.InvalidRequest,
                        $"'{name}' is not a valid process name for 'breakaway_child_processes'.");
                    return false;
                }

                // The proto type of the same name wins inside this namespace.
                breakawayChildProcesses[i] = new global::BuildXL.Utilities.Configuration.Mutable.BreakawayChildProcess
                {
                    ProcessName = breakawayName,
                    RequiredArguments = breakaway.RequiredArguments ?? string.Empty,
                    RequiredArgumentsIgnoreCase = breakaway.RequiredArgumentsIgnoreCase,
                };
            }

            processBuilder.ChildProcessesToBreakawayFromSandbox = ReadOnlyArray<IBreakawayChildProcess>.From(breakawayChildProcesses);

            if (request.HasRetryAttemptEnvironmentVariable)
            {
                processBuilder.SetRetryAttemptEnvironmentVariable(ResolveString(request.RetryAttemptEnvironmentVariable, "retry_attempt_environment_variable"));
            }

            return true;
        }

        private void SetProcessRedirection(AddProcessRequest request, ProcessBuilder processBuilder)
        {
            if (request.HasStandardOutput)
            {
                processBuilder.SetStandardOutputFile(ResolvePath(request.StandardOutput, "standard_output"));
            }

            if (request.HasStandardError)
            {
                processBuilder.SetStandardErrorFile(ResolvePath(request.StandardError, "standard_error"));
            }

            if (request.HasTraceFile)
            {
                processBuilder.SetTraceFile(ResolvePath(request.TraceFile, "trace_file"));
            }

            if (request.HasTempDirectory)
            {
                processBuilder.SetTempDirectory(DirectoryArtifact.CreateWithZeroPartialSealId(ResolvePath(request.TempDirectory, "temp_directory")));
            }

            processBuilder.AdditionalTempDirectories = ResolvePaths(request.AdditionalTempDirectories, "additional_temp_directories");
        }

        private AbsolutePath ResolvePath(uint id, string field) =>
            id < (uint)m_paths.Count
                ? m_paths[(int)id]
                : throw new InvalidRequestException($"'{field}' refers to path id {id}, but only {m_paths.Count} paths have been registered.");

        private AbsolutePath RequiredPath(bool isSet, uint id, string field) =>
            isSet ? ResolvePath(id, field) : throw new InvalidRequestException($"'{field}' is required.");

        private ReadOnlyArray<AbsolutePath> ResolvePaths(IReadOnlyList<uint> ids, string field)
        {
            var paths = new AbsolutePath[ids.Count];
            for (int i = 0; i < paths.Length; i++)
            {
                paths[i] = ResolvePath(ids[i], field);
            }

            return ReadOnlyArray<AbsolutePath>.FromWithoutCopy(paths);
        }

        private StringId ResolveString(uint id, string field) =>
            id < (uint)m_strings.Count
                ? m_strings[(int)id]
                : throw new InvalidRequestException($"'{field}' refers to string id {id}, but only {m_strings.Count} strings have been registered.");

        private StringId RequiredString(bool isSet, uint id, string field) =>
            isSet ? ResolveString(id, field) : throw new InvalidRequestException($"'{field}' is required.");

        private ReadOnlyArray<StringId> ResolveStringIds(IReadOnlyList<uint> ids, string field)
        {
            if (ids.Count == 0)
            {
                return ReadOnlyArray<StringId>.Empty;
            }

            var values = new StringId[ids.Count];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = ResolveString(ids[i], field);
            }

            return ReadOnlyArray<StringId>.FromWithoutCopy(values);
        }

        private string StringValue(StringId value) => value.ToString(m_pathTable.StringTable);

        private string OptionalStringValue(bool isSet, uint id, string field) => isSet ? StringValue(ResolveString(id, field)) : null;

        /// <summary>
        /// Values for the pip construction APIs that take plain strings, where null means none.
        /// </summary>
        private string[] ResolveStrings(IReadOnlyList<uint> ids, string field)
        {
            if (ids.Count == 0)
            {
                return null;
            }

            var values = new string[ids.Count];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = StringValue(ResolveString(ids[i], field));
            }

            return values;
        }

        /// <summary>
        /// The flags DScript sets for the same execute arguments (AmbientTransformer.Process), defaults included.
        /// </summary>
        private Process.Options ToProcessOptions(ProcessOptions options)
        {
            var result = options.UnsafeSkipGlobalDependencies ? Process.Options.None : Process.Options.RequireGlobalDependencies;

            foreach (var (isSet, flag) in s_processOptionFlags)
            {
                if (isSet(options))
                {
                    result |= flag;
                }
            }

            if (options.UnsafeAllowPreservedOutputs && m_sandboxConfiguration.PreserveOutputsForIncrementalTool)
            {
                result |= Process.Options.IncrementalTool;
            }

            return result;
        }

        private static readonly (Func<ProcessOptions, bool> IsSet, Process.Options Flag)[] s_processOptionFlags =
        {
            (o => o.KeepOutputsWritable, Process.Options.OutputsMustRemainWritable),
            (o => o.RequiresAdmin, Process.Options.RequiresAdmin),
            (o => o.IsLight, Process.Options.IsLight),
            (o => o.ScanForBuildxlHints, Process.Options.EnableBuildXLHintScanning),
            (o => o.AllowUndeclaredSourceReads, Process.Options.AllowUndeclaredSourceReads),
            (o => o.PreservePathSetCasing, Process.Options.PreservePathSetCasing),
            (o => o.EnforceWeakFingerprintAugmentation, Process.Options.EnforceWeakFingerprintAugmentation),
            (o => o.DisableCacheLookup, Process.Options.DisableCacheLookup),
            (o => o.Uncancellable, Process.Options.Uncancellable),
            (o => o.MustRunOnOrchestrator, Process.Options.MustRunOnOrchestrator),
            (o => o.WritingToStandardErrorFailsExecution, Process.Options.WritingToStandardErrorFailsExecution),
            (o => o.DependsOnCurrentHostOsDirectories, Process.Options.DependsOnCurrentOs),
            (o => o.DependsOnWindowsAppData, Process.Options.DependsOnWindowsAppData),
            (o => o.UnsafeHasUntrackedChildProcesses, Process.Options.HasUntrackedChildProcesses),
            (o => o.UnsafeAllowPreservedOutputs, Process.Options.AllowPreserveOutputs),
            (o => o.UnsafeIncrementalTool, Process.Options.IncrementalTool),
            (o => o.UnsafeTrustStaticallyDeclaredAccesses, Process.Options.TrustStaticallyDeclaredAccesses),
            (o => o.UnsafeDisableFullReparsePointResolving, Process.Options.DisableFullReparsePointResolving),
            (o => o.UnsafeDisableSandboxing, Process.Options.DisableSandboxing),
        };

        /// <summary>
        /// The requested policies, each falling back to the build's configured one as it does for a DScript process.
        /// </summary>
        private RewritePolicy ToRewritePolicy(AddProcessRequest request)
        {
            var doubleWrite = request.HasDoubleWritePolicy
                ? request.DoubleWritePolicy switch
                {
                    DoubleWritePolicy.DoubleWritesAreErrors => RewritePolicy.DoubleWritesAreErrors,
                    DoubleWritePolicy.AllowSameContentDoubleWrites => RewritePolicy.AllowSameContentDoubleWrites,
                    DoubleWritePolicy.UnsafeFirstDoubleWriteWins => RewritePolicy.UnsafeFirstDoubleWriteWins,
                    _ => throw InvalidEnum((int)request.DoubleWritePolicy, "double_write_policy"),
                }
                : m_sandboxConfiguration.UnsafeSandboxConfiguration.DoubleWritePolicy();

            var sourceRewrite = request.HasSourceRewritePolicy
                ? request.SourceRewritePolicy switch
                {
                    SourceRewritePolicy.SourceRewritesAreErrors => RewritePolicy.SourceRewritesAreErrors,
                    SourceRewritePolicy.SafeSourceRewritesAreAllowed => RewritePolicy.SafeSourceRewritesAreAllowed,
                    _ => throw InvalidEnum((int)request.SourceRewritePolicy, "source_rewrite_policy"),
                }
                : m_sandboxConfiguration.UnsafeSandboxConfiguration.SourceWritePolicy();

            return doubleWrite | sourceRewrite;
        }

        private static FileExistence ToFileExistence(FileExistenceKind kind) => kind switch
        {
            FileExistenceKind.Required => FileExistence.Required,
            FileExistenceKind.Temporary => FileExistence.Temporary,
            FileExistenceKind.Optional => FileExistence.Optional,
            _ => throw InvalidEnum((int)kind, "output_files.existence"),
        };

        private static BuildXL.Pips.Operations.SealDirectoryKind ToSealDirectoryKind(SealDirectoryKind kind) => kind switch
        {
            SealDirectoryKind.Partial => BuildXL.Pips.Operations.SealDirectoryKind.Partial,
            SealDirectoryKind.Full => BuildXL.Pips.Operations.SealDirectoryKind.Full,
            SealDirectoryKind.SourceAllDirectories => BuildXL.Pips.Operations.SealDirectoryKind.SourceAllDirectories,
            SealDirectoryKind.SourceTopDirectoryOnly => BuildXL.Pips.Operations.SealDirectoryKind.SourceTopDirectoryOnly,
            _ => throw InvalidEnum((int)kind, "kind"),
        };

        /// <summary>
        /// An enum field's zero value means it was not set, which is rejected like any missing required value.
        /// </summary>
        private static InvalidRequestException InvalidEnum(int value, string field) =>
            new InvalidRequestException(value == 0
                ? $"'{field}' is required."
                : $"'{field}' has the value {value}, which this version of BuildXL does not know.");

        private GraphOperationResponse VersionMismatch(GraphOperation operation) =>
            Failure(
                operation.OperationId,
                GraphOperationResponseStatus.VersionMismatch,
                operation.ProtocolVersion == 0
                    ? $"The emitter did not set GraphOperation.protocol_version. This BuildXL speaks DirectGraph protocol version {DirectGraphProtocol.Version}."
                    : $"The emitter speaks DirectGraph protocol version {operation.ProtocolVersion}. This BuildXL speaks version {DirectGraphProtocol.Version}.");

        /// <summary>
        /// Response to an operation that arrived after graph construction had already failed.
        /// </summary>
        /// <remarks>
        /// The stream is deliberately left open rather than aborted. Aborting would replace BuildXL's reason with a
        /// transport error on the emitter's side; answering every subsequent operation with this status instead lets
        /// the emitter see what went wrong, stop, and exit on its own terms. <see cref="DirectGraphServer"/> still
        /// ends the stream when Complete arrives, whatever status Complete itself was answered with.
        /// </remarks>
        private GraphOperationResponse AlreadyFailed(ulong operationId)
        {
            var (rejectedOperationId, rejectedError) = m_firstRejection.Value;

            return Failure(
                operationId,
                GraphOperationResponseStatus.PreviousOperationRejected,
                $"Graph construction already failed at operation {rejectedOperationId}, so this operation was not processed. " +
                $"That operation was rejected because: {rejectedError}");
        }

        private static GraphOperationResponse WithProtocolVersion(GraphOperationResponse response)
        {
            response.ProtocolVersion = DirectGraphProtocol.Version;
            return response;
        }

        private static GraphOperationResponse Success(ulong operationId, long semiStableHash = 0) =>
            new GraphOperationResponse { OperationId = operationId, Status = GraphOperationResponseStatus.Success, SemiStableHash = semiStableHash };

        private GraphOperationResponse Failure(ulong operationId, GraphOperationResponseStatus status, string error)
        {
            m_firstRejection ??= (operationId, error);
            return new GraphOperationResponse { OperationId = operationId, Status = status, Error = error };
        }

        /// <summary>
        /// What an operation produced that a later operation can refer to. The files are sorted by path, so that a
        /// reference is found by binary search.
        /// </summary>
        private readonly record struct ProducedArtifacts(FileArtifact[] Files, DirectoryArtifact[] Directories);

        /// <summary>
        /// Rejects the operation being dispatched as an invalid request. Thrown and caught only within dispatching.
        /// </summary>
        private sealed class InvalidRequestException : Exception
        {
            public InvalidRequestException(string message)
                : base(message)
            {
            }
        }
    }
}
