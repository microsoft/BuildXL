// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Engine.DirectGraph;
using BuildXL.Engine.DirectGraph.Tracing;
using BuildXL.Processes;
using BuildXL.Pips;
using BuildXL.Pips.Builders;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.Evaluation;
using BuildXL.FrontEnd.Sdk.Workspaces;
using BuildXL.FrontEnd.Script.Ambients.Map;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Utilities;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;

namespace BuildXL.FrontEnd.DirectGraph
{
    /// <summary>
    /// Resolver for tools that contribute pips directly to the BuildXL graph.
    /// </summary>
    public sealed class DirectGraphResolver : IResolver
    {
        internal const int DefaultIdleTimeoutMinutes = 10;
        internal const int MinIdleTimeoutMinutes = 1;

        // The longest delay CancellationTokenSource.CancelAfter accepts is uint.MaxValue - 1 milliseconds (about 49 days).
        // A longer idle timeout is shortened to that.
        private const int MaxTimerDelayMinutes = (int)((uint.MaxValue - 1) / 60000);

        private readonly FrontEndHost m_host;
        private readonly FrontEndContext m_context;
        private readonly string m_frontEndName;
        private readonly Lazy<Task<bool>> m_graphConstruction;
        private readonly CounterCollection<DirectGraphCounter> m_counters;
        private readonly ConcurrentDictionary<(string moduleName, FullSymbol symbol), PipConstructionHelper> m_pipConstructionHelpers = new();
        private readonly Dictionary<string, (OrderedMap Files, OrderedMap Directories)> m_exports = new(StringComparer.Ordinal);
        private DirectGraphWorkspaceResolver m_workspaceResolver;
        private IDirectGraphResolverSettings m_resolverSettings;

        /// <inheritdoc />
        public string Name { get; private set; }

        /// <nodoc />
        public DirectGraphResolver(
            FrontEndHost host,
            FrontEndContext context,
            string frontEndName,
            CounterCollection<DirectGraphCounter> counters)
        {
            Contract.Requires(host != null);
            Contract.Requires(context != null);
            Contract.Requires(!string.IsNullOrEmpty(frontEndName));
            Contract.Requires(counters != null);

            m_host = host;
            m_context = context;
            m_frontEndName = frontEndName;
            m_counters = counters;

            // One run of the tool builds the pips of every module this resolver owns, and they do not depend on the
            // qualifier, so every module evaluation shares it whatever qualifier it runs under.
            m_graphConstruction = new Lazy<Task<bool>>(ConstructGraphAsync, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <inheritdoc />
        public Task<bool> InitResolverAsync(IResolverSettings resolverSettings, object workspaceResolver)
        {
            Contract.Requires(resolverSettings != null);

            Name = resolverSettings.Name;
            m_resolverSettings = resolverSettings as IDirectGraphResolverSettings;
            m_workspaceResolver = workspaceResolver as DirectGraphWorkspaceResolver;
            Contract.Assert(m_resolverSettings != null);
            Contract.Assert(m_workspaceResolver != null);

            foreach (var module in m_workspaceResolver.ModuleDefinitions.Keys)
            {
                m_exports.Add(module.Name, (OrderedMap.Empty, OrderedMap.Empty));
            }

            return Task.FromResult(true);
        }

        /// <inheritdoc />
        public void LogStatistics()
        {
            // Statistics are aggregated and logged by DirectGraphFrontEnd.
        }

        /// <inheritdoc />
        public Task<bool?> TryConvertModuleToEvaluationAsync(IModuleRegistry moduleRegistry, ParsedModule module, IWorkspace workspace)
        {
            if (!m_workspaceResolver.ModuleDefinitions.ContainsKey(module.Descriptor))
            {
                return Task.FromResult<bool?>(null);
            }

            ConvertExports(moduleRegistry, module);
            return Task.FromResult<bool?>(true);
        }

        private void ConvertExports(IModuleRegistry moduleRegistry, ParsedModule module)
        {
            var specPath = module.Definition.Specs.Single();
            var fileModule = ModuleLiteral.CreateFileModule(
                specPath,
                moduleRegistry,
                FrontEndUtilities.CreatePackage(module.Definition, m_context.StringTable),
                module.Specs[specPath].LineMap);

            int pos = 1;
            foreach (string name in new[] { DirectGraphWorkspaceResolver.FilesExportName, DirectGraphWorkspaceResolver.DirectoriesExportName })
            {
                FrontEndUtilities.AddEvaluationCallbackToFileModule(
                    fileModule,
                    (context, moduleLiteral, evaluationStackFrame) => ResolveExportAsync(module.Descriptor.Name, name),
                    FullSymbol.Create(m_context.SymbolTable, name),
                    pos);
                pos += 2;
            }

            moduleRegistry.AddUninstantiatedModuleInfo(
                new global::BuildXL.FrontEnd.Script.UninstantiatedModuleInfo(
                    new global::BuildXL.FrontEnd.Script.SourceFile(specPath, Array.Empty<global::BuildXL.FrontEnd.Script.Declarations.Declaration>()),
                    fileModule,
                    m_context.QualifierTable.EmptyQualifierSpaceId));
        }

        private async Task<EvaluationResult> ResolveExportAsync(string moduleName, string name)
        {
            // An importing module can reach this before this resolver's own modules are evaluated.
            if (!await m_graphConstruction.Value)
            {
                return EvaluationResult.Error;
            }

            var exports = m_exports[moduleName];
            return EvaluationResult.Create(name == DirectGraphWorkspaceResolver.FilesExportName ? exports.Files : exports.Directories);
        }

        private bool TryRegisterExport(string moduleName, FileOrDirectoryArtifact artifact, out string error)
        {
            if (!m_exports.TryGetValue(moduleName, out var exports))
            {
                error = $"The module '{moduleName}' is not owned by resolver '{Name}'. Owned modules: {m_workspaceResolver.DescribeExtent()}.";
                return false;
            }

            var key = EvaluationResult.Create(artifact.Path);
            if (exports.Files.ContainsKey(key) || exports.Directories.ContainsKey(key))
            {
                error = $"Module '{moduleName}' already exports '{artifact.Path.ToString(m_context.PathTable)}'. A path can be exported only once, across both files and directories.";
                return false;
            }

            if (artifact.IsFile)
            {
                exports.Files = exports.Files.Add(key, EvaluationResult.Create(artifact.FileArtifact));
            }
            else
            {
                var directory = artifact.DirectoryArtifact;
                if (!m_host.PipGraph.TryGetSealDirectoryKind(directory, out var kind))
                {
                    error = $"The exported directory '{directory.Path.ToString(m_context.PathTable)}' has not been added to the graph.";
                    return false;
                }

                var contents = kind.IsDynamicKind()
                    ? PipConstructionHelper.EmptyStaticDirContents
                    : SortedReadOnlyArray<FileArtifact, OrdinalPathOnlyFileArtifactComparer>.SortUnsafe(
                        m_host.PipGraph.ListSealedDirectoryContents(directory).ToArray(),
                        OrdinalPathOnlyFileArtifactComparer.Instance);
                exports.Directories = exports.Directories.Add(key, EvaluationResult.Create(new StaticDirectory(directory, kind, contents)));
            }

            m_exports[moduleName] = exports;
            error = null;
            return true;
        }

        /// <inheritdoc />
        public async Task<bool?> TryEvaluateModuleAsync(IEvaluationScheduler scheduler, ModuleDefinition module, QualifierId qualifierId)
        {
            if (!m_workspaceResolver.ModuleDefinitions.ContainsKey(module.Descriptor))
            {
                return null;
            }

            // The first of this resolver's modules to be evaluated runs the tool; the others wait for it.
            return await m_graphConstruction.Value;
        }

        private async Task<bool> ConstructGraphAsync()
        {
            var toolPath = m_resolverSettings.Tool.Path;
            var toolPathString = toolPath.ToString(m_context.PathTable);
            if (!m_host.Engine.FileExists(toolPath))
            {
                Logger.Log.GraphBuilderToolNotFound(m_context.LoggingContext, toolPathString);
                return false;
            }

            // Cancelling this kills the emitter. Armed by the idle timeout below.
            using var emitterCancellation = new CancellationTokenSource();

            RegisterModulesTheHostLeftOut();

            var graphBuilder = new DirectGraphPipGraphBuilder(
                m_context.PathTable,
                m_host.PipGraph,
                m_host.Configuration.Sandbox,
                () => ProcessBuilder.Create(
                    m_context.PathTable,
                    m_context.GetPipDataBuilder(),
                    m_context.CredentialScanner,
                    m_context.LoggingContext),
                TryGetPipConstructionHelper,
                TryRegisterExport,
                m_context.LoggingContext,
                // Tracing runs the protobuf text formatter over every request and every response and logs the result,
                // so it cannot be on unconditionally.
                traceOperation: m_resolverSettings.EnableProjectGraphVerboseLogging ?? false,
                counters: m_counters);

            int idleTimeoutMinutes = Math.Min(m_resolverSettings.IdleTimeoutMin ?? DefaultIdleTimeoutMinutes, MaxTimerDelayMinutes);
            var idleTimeout = TimeSpan.FromMinutes(idleTimeoutMinutes);

            // The server calls back on every operation, and CancelAfter replaces its own pending timer, so this
            // measures the time since the last one. Startup is deliberately not bounded: the first arming happens
            // on the first operation, so the emitter may take arbitrarily long to produce it. Neither is the time
            // after Complete, once the graph is finished.
            using var server = new DirectGraphServer(
                graphBuilder,
                onOperationReceived: operation => emitterCancellation.CancelAfter(
                    operation.OperationCase == GraphOperation.OperationOneofCase.Complete ? Timeout.InfiniteTimeSpan : idleTimeout));
            server.Start();

            var frontEndFolder = m_host.GetFolderForFrontEnd(Name);
            Directory.CreateDirectory(frontEndFolder.ToString(m_context.PathTable));

            m_host.Engine.RecordFrontEndFile(toolPath, Name);

            var manifest = FrontEndUtilities.GenerateToolFileAccessManifest(m_context, frontEndFolder);
            var arguments = string.Join(
                " ",
                (m_resolverSettings.Arguments ?? Array.Empty<string>()).Select(CommandLineEscaping.EscapeAsCommandLineWord));
            var buildParameters = m_workspaceResolver.CreateBuildParameters(server.Endpoint);

            var result = await FrontEndUtilities.RunSandboxedToolAsync(
                m_context,
                toolPathString,
                buildStorageDirectory: frontEndFolder.ToString(m_context.PathTable),
                fileAccessManifest: manifest,
                arguments,
                workingDirectory: m_resolverSettings.WorkingDirectory.ToString(m_context.PathTable),
                description: $"{Name} graph builder",
                buildParameters,
                useEBPFLinuxSandbox: m_host.Configuration.Sandbox.EnableEBPFLinuxSandbox,
                ebpfRingBufferSizeMultiplier: m_host.Configuration.Sandbox.EBPFRingBufferSizeMultiplier,
                enableLogging: m_resolverSettings.EnableProjectGraphVerboseLogging ?? false,
                cancellationToken: emitterCancellation.Token);

            // Settle the idle outcome the moment the emitter is gone, and stop the clock. Everything below can take
            // longer than the timeout - TrackToolFileAccesses walks every access the emitter made - and a build whose
            // graph is already built must not be failed by a timer that only ever applied to the emitter.
            emitterCancellation.CancelAfter(Timeout.InfiniteTimeSpan);
            bool emitterTimedOut = result.Killed && emitterCancellation.IsCancellationRequested;

            IEnumerable<ReportedFileAccess> reportedAccesses =
                result.AllUnexpectedFileAccesses ?? Enumerable.Empty<ReportedFileAccess>();
            reportedAccesses = reportedAccesses.Union(
                result.ExplicitlyReportedFileAccesses ?? Enumerable.Empty<ReportedFileAccess>());

            // Not optional, and not merely hygiene: graph caching correctness depends on it.
            //
            // The emitter discovers its inputs by reading and enumerating the filesystem - a generated graph typically
            // globs a source directory per pip. Only because the tool runs sandboxed do those reads and enumerations
            // become part of the graph fingerprint (TrackToolFileAccesses calls FrontEndEngineAbstraction.TrackDirectory
            // for enumerations), so adding a file to a globbed directory invalidates the cached graph and re-runs the
            // emitter. Running the tool unsandboxed would not fail loudly; it would silently serve stale graphs.
            FrontEndUtilities.TrackToolFileAccessesAndEnvironment(
                m_host.Engine,
                m_context,
                frontEndName: m_frontEndName,
                resolverName: Name,
                environmentVariables: m_workspaceResolver.InheritedEnvironmentVariables,
                reportedAccesses,
                frontEndFolder);

            // Reported ahead of the idle timeout and the exit code. A rejection is BuildXL's own record and names the
            // operation and the reason, whereas both of the others are at best symptoms of it: an emitter that stops
            // sending after a rejection and waits for the call to end will be killed by the timeout.
            if (graphBuilder.TryGetFirstRejection(out ulong rejectedOperationId, out string rejectionError))
            {
                Logger.Log.GraphOperationRejected(m_context.LoggingContext, toolPathString, rejectedOperationId, rejectionError);
                return false;
            }

            if (emitterTimedOut)
            {
                Logger.Log.GraphBuilderToolTimedOut(m_context.LoggingContext, toolPathString, idleTimeoutMinutes);
                return false;
            }

            // A cancelled build kills the emitter, so how it exited says nothing about it. Throwing lets the host report
            // the cancellation itself: returning false would require an error to have been logged already.
            m_context.CancellationToken.ThrowIfCancellationRequested();

            string standardError = result.StandardError == null
                ? string.Empty
                : await result.StandardError.ReadValueAsync();
            if (result.ExitCode != 0)
            {
                Logger.Log.GraphBuilderToolFailed(m_context.LoggingContext, toolPathString, result.ExitCode, standardError);
                return false;
            }

            if (!string.IsNullOrEmpty(standardError))
            {
                Logger.Log.GraphBuilderToolWarning(m_context.LoggingContext, toolPathString, standardError);
            }

            if (!graphBuilder.IsComplete)
            {
                Logger.Log.GraphBuilderToolExitedWithoutCompletion(m_context.LoggingContext, toolPathString);
                return false;
            }

            return true;
        }

        /// <inheritdoc />
        public void NotifyEvaluationFinished()
        {
        }

        /// <summary>
        /// The host registers the module and spec file pips of the modules the build's filter keeps in the workspace,
        /// but the emitter produces pips for every module of this resolver, and a pip needs the spec file pip of its
        /// module. Registers those of the others, the way the host does. A module that is registered already is left as it
        /// is.
        /// </summary>
        private void RegisterModulesTheHostLeftOut()
        {
            foreach (var definition in m_workspaceResolver.ModuleDefinitions.Values)
            {
                var descriptor = definition.Descriptor;
                var location = new LocationData(definition.ModuleConfigFile, 0, 0);

                var modulePip = new BuildXL.Pips.Operations.ModulePip(
                    module: descriptor.Id,
                    identity: StringId.Create(m_context.StringTable, descriptor.Name),
                    version: StringId.Create(m_context.StringTable, descriptor.Version),
                    location: location,
                    resolverKind: StringId.Create(m_context.StringTable, descriptor.ResolverKind),
                    resolverName: StringId.Create(m_context.StringTable, descriptor.ResolverName),
                    scrubDirectories: new List<AbsolutePath>(definition.ScrubDirectories));

                var specFilePips = definition.Specs
                    .Select(spec => new BuildXL.Pips.Operations.SpecFilePip(FileArtifact.CreateSourceFile(spec), location, descriptor.Id))
                    .ToList();

                // Through the fragment manager when there is one: it unifies these pips with the ones pip graph fragments add.
                if (m_host.PipGraphFragmentManager != null)
                {
                    m_host.PipGraphFragmentManager.AddModulePip(modulePip);
                    specFilePips.ForEach(specFilePip => m_host.PipGraphFragmentManager.AddSpecFilePip(specFilePip));
                }
                else
                {
                    m_host.PipGraph.AddModule(modulePip);
                    specFilePips.ForEach(specFilePip => m_host.PipGraph.AddSpecFile(specFilePip));
                }
            }
        }

        private PipConstructionHelper CreatePipConstructionHelper(ModuleDefinition moduleDefinition, FullSymbol symbol)
        {
            Contract.Assert(moduleDefinition.Specs.Count == 1);

            var specPath = moduleDefinition.Specs.First();
            var specRelativePath = moduleDefinition.Root.TryGetRelative(m_context.PathTable, specPath, out var relativePath)
                ? relativePath
                : RelativePath.Invalid;

            return PipConstructionHelper.Create(
                m_context,
                m_host.Engine.Layout.ObjectDirectory,
                m_host.Engine.Layout.RedirectedDirectory,
                m_host.Engine.Layout.TempDirectory,
                m_host.PipGraph,
                moduleDefinition.Descriptor.Id,
                moduleDefinition.Descriptor.Name,
                specRelativePath,
                symbol,
                // The value has to be located in the module's virtual spec: FrontEndHostController registered a spec
                // file pip for exactly that path, and PipGraph.Builder.AddOutputValue requires the owning spec pip to
                // exist. All values of a module share it, which is fine since values are keyed by (symbol, qualifier, path).
                new LocationData(specPath, 0, 0),
                QualifierId.Unqualified);
        }

        /// <summary>
        /// Resolves, creating it on first use, the pip construction helper that owns pips for a module and target symbol.
        /// </summary>
        /// <remarks>
        /// One helper per target rather than per module. A helper hands out semi-stable hashes by incrementing a counter
        /// seeded from (module, spec, symbol, qualifier), so sharing one helper across a module would make every pip's
        /// hash depend on how many pips arrived before it.
        /// </remarks>
        private bool TryGetPipConstructionHelper(
            string moduleName,
            string symbol,
            out PipConstructionHelper pipConstructionHelper,
            out string error)
        {
            pipConstructionHelper = null;

            if (!m_workspaceResolver.TryGetModuleDefinition(moduleName, out var moduleDefinition))
            {
                error = $"The module '{moduleName}' is not owned by resolver '{Name}'. Owned modules: {m_workspaceResolver.DescribeExtent()}.";
                return false;
            }

            // Names that sanitize to the same symbol have to share a helper. Two helpers carrying the same symbol would
            // be seeded identically and hand out colliding semi-stable hashes.
            string sanitizedSymbol = PipConstructionUtilities.SanitizeStringForSymbol(symbol);

            if (FullSymbol.TryCreate(m_context.SymbolTable, (StringSegment)sanitizedSymbol, out var fullSymbol, out _) != FullSymbol.ParseResult.Success)
            {
                error = $"Symbol '{symbol}' cannot be used to name a value. It contains no characters that are valid in an identifier.";
                return false;
            }

            pipConstructionHelper = m_pipConstructionHelpers.GetOrAdd(
                (moduleName, fullSymbol),
                key => CreatePipConstructionHelper(moduleDefinition, key.symbol));

            error = null;
            return true;
        }
    }
}
