// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.RuntimeModel;
using BuildXL.FrontEnd.Script.RuntimeModel.AstBridge;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.Mutable;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Processes.Containers;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;
using JetBrains.Annotations;
using TypeScript.Net.Binding;
using TypeScript.Net.BuildXLScript;
using TypeScript.Net.Parsing;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Utilities
{
    /// <summary>
    /// Static methods with common logic for the FrontEnd resolvers
    /// </summary>
    public sealed class FrontEndUtilities
    {
        /// <summary>
        /// Retrieves a list of search locations for an executable, inspecting a list of explicit candidates first or using PATH.
        /// The onEmptyResult action will be invoked if there are no search locations available
        /// The onPathParseFailure action will be invoked with the PATH as an argument if the PATH is malformed
        /// </summary>
        public static bool TryRetrieveExecutableSearchLocations(
            string frontEnd,
            FrontEndContext context,
            FrontEndEngineAbstraction engine,
            IReadOnlyCollection<AbsolutePath> explicitCandidates,
            out IEnumerable<AbsolutePath> searchLocations,
            Action onEmptyResult = null,
            Action<string> onPathParseFailure = null)
        {
            // If there are explicit search locations specified, use those
            if (explicitCandidates?.Count > 0)
            {
                searchLocations = explicitCandidates;
                return true;
            }

            // Otherwise use %PATH%
            if (!engine.TryGetBuildParameter("PATH", frontEnd, out string paths))
            {
                onEmptyResult?.Invoke();
                searchLocations = null;
                return false;
            }

            var locations = new List<AbsolutePath>();
            foreach (string path in paths.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
            {
                var nonEscapedPath = path.Trim('"');
                if (AbsolutePath.TryCreate(context.PathTable, nonEscapedPath, out var absolutePath))
                {
                    locations.Add(absolutePath);
                }
            }

            if (locations.Count == 0)
            {
                onPathParseFailure?.Invoke(paths);
                searchLocations = null;
                return false;
            }

            searchLocations = locations;
            return true;
        }

        /// <summary>
        /// Runs the a tool in a sandboxed process and returns the result.
        /// These optional callback Actions can be provided:
        ///     beforeLaunch is invoked right before the process is launched
        ///     onResult is invoked after getting a successful result
        /// </summary>>
        public static async Task<SandboxedProcessResult> RunSandboxedToolAsync(FrontEndContext context,
            string pathToTool,
            string buildStorageDirectory,
            FileAccessManifest fileAccessManifest,
            string arguments,
            string workingDirectory,
            string description,
            BuildParameters.IBuildParameters buildParameters,
            Action beforeLaunch = null,   // Invoked right before the process starts
            Action onResult = null      // Action to be taken after getting a successful result
            )
        {
            var toolBuildStorage = new ToolBuildStorage(buildStorageDirectory);

            // If the pipId is 0 (unset) set some roughly unique value for it. For the linux case, the sandbox connection assumes the pip id
            // is set. We don't really need a true id here since we are only running a single pip in the lifetime of the connection, so in fact any non-zero
            // value should do
            if (fileAccessManifest.PipId == 0)
            {
                fileAccessManifest.PipId = HashCodeHelper.Combine(pathToTool.GetHashCode(), arguments.GetHashCode());
            }

            var info =
                new SandboxedProcessInfo(
                    context.PathTable,
                    toolBuildStorage,
                    pathToTool,
                    fileAccessManifest,
                    disableConHostSharing: false,
                    ContainerConfiguration.DisabledIsolation,
                    loggingContext: context.LoggingContext)
                {
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    PipSemiStableHash = 0,
                    PipDescription = description,
                    EnvironmentVariables = buildParameters,
                };

            // We don't expect many failures (none for the typical case). A concurrent bag should be fine.
            var sandboxFailures = new ConcurrentBag<(int, string)>();
            void sandboxConnectionFailureCallback(int status, string description)
            {
                sandboxFailures.Add((status, description));
            }

            SandboxedProcessResult sandboxedProcessResult;
            try
            {
                if (OperatingSystemHelper.IsLinuxOS)
                {
                    info.SandboxConnection = new SandboxConnectionLinuxDetours(sandboxConnectionFailureCallback);
                }
                else if (OperatingSystemHelper.IsMacOS)
                {
                    info.SandboxConnection = new SandboxConnectionKext(
                        new SandboxConnectionKext.Config
                        {
                            FailureCallback = sandboxConnectionFailureCallback,
                            KextConfig = new Interop.Unix.Sandbox.KextConfig
                            {
                                ReportQueueSizeMB = 1024,
#if PLATFORM_OSX
                            EnableCatalinaDataPartitionFiltering = OperatingSystemHelperExtension.IsMacWithoutKernelExtensionSupport
#endif
                            }
                        });
                }

                var process = await SandboxedProcessFactory.StartAsync(info, forceSandboxing: true);

                var registration = context.CancellationToken.Register(
                    () =>
                    {
                        try
                        {
                            process.KillAsync().GetAwaiter().GetResult();
                        }
                        catch (TaskCanceledException)
                        {
                            // If the process has already terminated or doesn't exist, an TaskCanceledException is raised.
                            // In either case, we swallow the exception, cancellation is already requested by the user
                        }
                    });


                beforeLaunch?.Invoke();
                var result = process.GetResultAsync().ContinueWith(
                    r =>
                    {
                        // Dispose the registration for the cancellation token once the process is done
                        registration.Dispose();


                        //
                        onResult?.Invoke();

                        return r.GetAwaiter().GetResult();
                    });

                sandboxedProcessResult = await result;
            }
            finally 
            { 
                info.SandboxConnection?.Dispose(); 
            }

            // No sandboxed process failures, so we return the result
            if (sandboxFailures.Count == 0)
            {
                return sandboxedProcessResult;
            }

            // Inject the sandboxed failures into the result, so downstream consumers can fail appropriately 
            var error = string.Join(Environment.NewLine, sandboxFailures.Select((status, description) => $"[{status}]{description}"));
            return new SandboxedProcessResult() {
                ExitCode = -1,
                StandardError = new SandboxedProcessOutput(
                    error.Length, 
                    error, 
                    fileName: null, 
                    Console.OutputEncoding, 
                    toolBuildStorage, 
                    SandboxedProcessFile.StandardError, 
                    exception: null)
            };
        }

        /// <summary>
        /// Get all the environment exposed to the process, with the values overriden by the engine
        /// </summary>
        public static IDictionary<string, string> GetEngineEnvironment(FrontEndEngineAbstraction engine, string frontEndName)
        {
            var engineEnvironment = new Dictionary<string, string>();
            IDictionary environment = Environment.GetEnvironmentVariables();

            foreach (string environmentVariable in environment.Keys)
            {
                // Expose as much of the environment as we can -- use the ones overriden by the Engine
                if (engine.TryGetBuildParameter(environmentVariable, frontEndName, out var value))
                {
                    engineEnvironment[environmentVariable] = value;
                }
            }

            return engineEnvironment;
        }

        /// <summary>
        /// Generate a basic file access manifest for front end tools
        /// </summary>
        public static FileAccessManifest GenerateToolFileAccessManifest(FrontEndContext context, AbsolutePath toolDirectory)
        {
            var pathTable = context.PathTable;
            // We make no attempt at understanding what the tool is going to do
            // We just configure the manifest to not fail on unexpected accesses, so they can be collected
            // later if needed
            var fileAccessManifest = new FileAccessManifest(pathTable)
                                     {
                                         FailUnexpectedFileAccesses = false,
                                         ReportFileAccesses = true,
                                         MonitorNtCreateFile = true,
                                         MonitorZwCreateOpenQueryFile = true,
                                         MonitorChildProcesses = true,
                                     };

            OsDefaults osDefaults = OperatingSystemHelper.IsWindowsOS
                ? new BuildXL.Pips.Graph.PipGraph.WindowsOsDefaults(pathTable)
                : new BuildXL.Pips.Graph.PipGraph.UnixDefaults(pathTable, pipGraph: null);

            foreach (var untrackedDirectory in osDefaults.UntrackedDirectories)
            {
                fileAccessManifest.AddScope(
                    untrackedDirectory.Path,
                    ~FileAccessPolicy.ReportAccess,
                    FileAccessPolicy.AllowAll | FileAccessPolicy.AllowRealInputTimestamps);
            }
            
            foreach (var untrackedFile in osDefaults.UntrackedFiles)
            {
                fileAccessManifest.AddPath(
                    untrackedFile.Path,
                    ~FileAccessPolicy.ReportAccess,
                    FileAccessPolicy.AllowAll | FileAccessPolicy.AllowRealInputTimestamps);
            }

            fileAccessManifest.AddScope(toolDirectory, FileAccessPolicy.MaskAll, FileAccessPolicy.AllowReadAlways);

            return fileAccessManifest;
        }

        /// <summary>
        /// The FrontEnds that use an out-of-proc tool should sandbox that process and call this method
        /// in order to tack the tool's file accesses, enumerations, etc. in order to make graph caching sound
        /// </summary>
        public static void TrackToolFileAccesses(FrontEndEngineAbstraction engine, FrontEndContext context, string frontEndName, ISet<ReportedFileAccess> fileAccesses, AbsolutePath frontEndFolder)
        {
            // Compute all parseable paths
            // TODO: does it make sense to consider enumerations, or as a result the graph will be too unstable? Does it matter for MsBuild graph construction?
            foreach (var access in fileAccesses)
            {
                string accessPath = access.GetPath(context.PathTable);
                if (AbsolutePath.TryCreate(context.PathTable, accessPath, out AbsolutePath path))
                {
                    // Ignore accesses under the frontend folder: these are files used for internal communication between
                    // BuildXL and the graph builder tool, and they are never files that MSBuild itself interacted with
                    if (path.IsWithin(context.PathTable, frontEndFolder))
                    {
                        continue;
                    }

                    if ((access.RequestedAccess & RequestedAccess.Enumerate) != 0)
                    {
                        engine.TrackDirectory(path.ToString(context.PathTable));
                    }
                    if ((access.RequestedAccess & RequestedAccess.Probe) != 0)
                    {
                        engine.FileExists(path);
                    }
                    if ((access.RequestedAccess & RequestedAccess.Read) != 0)
                    {
                        // Two things are happening here: we want to register if the file is present or absent. Engine.FileExists takes
                        // care of that. And in the case the file exists, record the content.
                        // There are apparently some repos that create and delete files during graph construction :(
                        // So we cannot trust detours and check for IsNonexistent on the access itself. Even though there were read/write accesses on a given file,
                        // the file may not exist at this point
                        if (engine.FileExists(path))
                        {
                            engine.RecordFrontEndFile(path, frontEndName);
                        }
                    }
                }
            }
        }

        private sealed class ToolBuildStorage : ISandboxedProcessFileStorage
        {
            private readonly string m_directory;

            /// <nodoc />
            public ToolBuildStorage(string directory) => m_directory = directory;

            /// <inheritdoc />
            public string GetFileName(SandboxedProcessFile file) => Path.Combine(m_directory, file.DefaultFileName());
        }

        /// <summary>
        /// Configure the environment for a process
        /// </summary>
        public static void SetProcessEnvironmentVariables(
            IReadOnlyDictionary<string, string> userDefinedEnvironment,
            [CanBeNull] IEnumerable<string> userDefinedPassthroughVariables,
            ProcessBuilder processBuilder,
            PathTable pathTable)
        {
            Contract.RequiresNotNull(userDefinedEnvironment);
            Contract.RequiresNotNull(processBuilder);

            foreach (KeyValuePair<string, string> kvp in userDefinedEnvironment)
            {
                if (kvp.Value != null)
                {
                    var envPipData = new PipDataBuilder(pathTable.StringTable);

                    // Casing for paths is not stable as reported by BuildPrediction. So here we try to guess if the value
                    // represents a path, and normalize it
                    string value = kvp.Value;
                    if (!string.IsNullOrEmpty(value) && AbsolutePath.TryCreate(pathTable, value, out var absolutePath))
                    {
                        envPipData.Add(absolutePath);
                    }
                    else
                    {
                        envPipData.Add(value);
                    }

                    processBuilder.SetEnvironmentVariable(
                        StringId.Create(pathTable.StringTable, kvp.Key),
                        envPipData.ToPipData(string.Empty, PipDataFragmentEscaping.NoEscaping),
                        isPassThrough: false);
                }
            }

            if (userDefinedPassthroughVariables != null)
            {
                foreach (string passThroughVariable in userDefinedPassthroughVariables)
                {
                    processBuilder.SetPassthroughEnvironmentVariable(StringId.Create(pathTable.StringTable, passThroughVariable));
                }
            }
        }

        /// <summary>
        /// Exposes a declaration of the form '@@public export identifier : type = undefined' at the specified (pos, end) location
        /// </summary>
        /// <remarks>
        /// Line map of the source file is not set
        /// </remarks>
        public static void AddExportToSourceFile(TypeScript.Net.Types.SourceFile sourceFile, string identifier, ITypeNode type, int pos, int end)
        {
            // A value representing all output directories of the project
            var outputDeclaration = new VariableDeclaration(identifier, Identifier.CreateUndefined(), type);
            outputDeclaration.Flags |= NodeFlags.Export | NodeFlags.Public | NodeFlags.ScriptPublic;
            outputDeclaration.Pos = pos;
            outputDeclaration.End = end;

            // Final source file looks like
            //   @@public export outputs: type[] = undefined;
            // The 'undefined' part is not really important here. The value at runtime has its own special handling in the resolver.
            sourceFile.Statements.Add(new VariableStatement()
            {
                DeclarationList = new VariableDeclarationList(
                        NodeFlags.Const,
                        outputDeclaration)
            });
        }

        /// <summary>
        /// Adds a callback for a particular symbol that will be called
        /// at evaluation time when that symbol is evaluated.
        /// </summary>
        /// <remarks>
        /// Useful for programmatically executing customized evaluation for non-DScript
        /// resolvers
        /// </remarks>
        public static void AddEvaluationCallbackToFileModule(
            FileModuleLiteral fileModule,
            Func<Context, ModuleLiteral, EvaluationStackFrame,Task<EvaluationResult>> evaluationCallback,
            FullSymbol symbol,
            int position)
        {
            var sourceFilePath = fileModule.Path;
            
            var outputResolvedEntry = new ResolvedEntry(
                symbol,
                (Context context, ModuleLiteral env, EvaluationStackFrame args) => evaluationCallback(context, env, args),
                // The following position is a contract right now with he generated ast in the workspace resolver
                // we have to find a nicer way to handle and register these.
                TypeScript.Net.Utilities.LineInfo.FromLineAndPosition(0, position)
            );

            fileModule.AddResolvedEntry(symbol, outputResolvedEntry);
            fileModule.AddResolvedEntry(new FilePosition(position, sourceFilePath), outputResolvedEntry);
        }

        /// <summary>
        /// Runs AST conversion on the given target path.
        /// </summary>
        /// <remarks>
        /// The target path is assumed to already be part of the workspace contained by the given host
        /// </remarks>
        public static Task<SourceFileParseResult> RunAstConversionAsync(FrontEndHost host, FrontEndContext context, Script.Tracing.Logger logger, IFrontEndStatistics stats, Package package, AbsolutePath conversionTarget)
        {
            Contract.RequiresNotNull(host);
            Contract.RequiresNotNull(context);
            Contract.RequiresNotNull(logger);
            Contract.RequiresNotNull(stats);
            Contract.RequiresNotNull(package);
            Contract.Requires(conversionTarget.IsValid);

            var configuration = AstConversionConfiguration.FromConfiguration(host.Configuration.FrontEnd);
            var linter = DiagnosticAnalyzer.Create(
                logger,
                context.LoggingContext,
                new HashSet<string>(configuration.PolicyRules),
                configuration.DisableLanguagePolicies);

            var workspace = (Workspace) host.Workspace;
            var factory = new RuntimeModelFactory(stats, configuration, linter, workspace);
            var parserContext = new RuntimeModelContext(
                host,
                frontEndContext: context,
                logger,
                package: package,
                origin: default(LocationData));

            var sourceFile = workspace.GetSourceFile(conversionTarget);

            return factory.ConvertSourceFileAsync(parserContext, sourceFile);
        }

        /// <summary>
        /// Parses and binds an arbitrary string returning a corresponding ISourceFile.
        /// </summary>
        /// <returns>Whether parsing and binding succeded</returns>
        public static bool TryParseAndBindSourceFile(FrontEndHost host, FrontEndContext context, AbsolutePath sourceFilePath, string sourceFileContent, out TypeScript.Net.Types.SourceFile sourceFile)
        {
            if (!TryParseSourceFile(context, sourceFilePath, sourceFileContent, out sourceFile))
            {
                return false;
            }
            
            Binder binder = new Binder();
            binder.BindSourceFile(sourceFile, CompilerOptions.Empty);

            if (sourceFile.BindDiagnostics.Count != 0)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Parses an arbitrary string returning a corresponding ISourceFile.
        /// </summary>
        /// <returns>Whether parsing succeded</returns>
        public static bool TryParseSourceFile(FrontEndContext context, AbsolutePath sourceFilePath, string sourceFileContent, out TypeScript.Net.Types.SourceFile sourceFile)
        {
            var parser = new DScriptParser(context.PathTable);
            sourceFile = (TypeScript.Net.Types.SourceFile)parser.ParseSourceFileContent(sourceFilePath.ToString(context.PathTable), sourceFileContent, ParsingOptions.DefaultParsingOptions);

            return sourceFile.ParseDiagnostics.Count != 0;
        }

        /// <summary>
        /// Creates a package out of a module
        /// </summary>
        /// <remarks>
        /// TODO: This is DSCript V1 behavior we couldn't get rid of
        /// </remarks>
        public static Package CreatePackage(ModuleDefinition moduleDefinition, StringTable stringTable)
        {
            var moduleDescriptor = moduleDefinition.Descriptor;

            var packageId = PackageId.Create(StringId.Create(stringTable, moduleDescriptor.Name));
            var packageDescriptor = new PackageDescriptor
            {
                Name = moduleDescriptor.Name,
                Main = moduleDefinition.MainFile,
                NameResolutionSemantics = NameResolutionSemantics.ImplicitProjectReferences,
                Publisher = null,
                Version = moduleDescriptor.Version,
                Projects = new List<AbsolutePath>(moduleDefinition.Specs),
            };

            return Package.Create(packageId, moduleDefinition.ModuleConfigFile, packageDescriptor, moduleId: moduleDescriptor.Id);
        }

        /// <summary>
        /// <see cref="TryFindToolInPath(FrontEndContext, FrontEndHost, string, IEnumerable{string}, out AbsolutePath)"/>
        /// </summary>
        public static bool TryFindToolInPath(
            FrontEndContext context,
            FrontEndHost host,
            string paths,
            IEnumerable<string> toolNamesToFind,
            out AbsolutePath location)
        {
            Contract.RequiresNotNull(context);
            Contract.RequiresNotNull(host);
            Contract.RequiresNotNull(toolNamesToFind);
            Contract.RequiresNotNullOrEmpty(paths);

            var pathCollection = paths.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);

            var absolutePathCollection = pathCollection
                .Select(path => AbsolutePath.TryCreate(context.PathTable, path.Trim('"'), out var absolutePath) ? absolutePath : AbsolutePath.Invalid)
                .Where(path => path.IsValid);

            return TryFindToolInPath(context, host, absolutePathCollection, toolNamesToFind, out location);
        }

        /// <summary>
        /// Tries to find if any of the tool names provided can be found under a collection of paths
        /// </summary>
        public static bool TryFindToolInPath(
            FrontEndContext context, 
            FrontEndHost host, 
            IEnumerable<AbsolutePath> pathCollection, 
            IEnumerable<string> toolNamesToFind, 
            out AbsolutePath location)
        {
            Contract.RequiresNotNull(context);
            Contract.RequiresNotNull(host);
            Contract.RequiresNotNull(pathCollection);
            Contract.RequiresNotNull(toolNamesToFind);

            location = AbsolutePath.Invalid;

            AbsolutePath foundPath = AbsolutePath.Invalid;
            foreach (AbsolutePath absolutePath in pathCollection)
            {
                if (absolutePath.IsValid)
                {
                    foreach (var toolName in toolNamesToFind)
                    {
                        AbsolutePath pathToTool = absolutePath.Combine(context.PathTable, toolName);
                        if (host.Engine.FileExists(pathToTool))
                        {
                            foundPath = pathToTool;
                            break;
                        }
                    }
                }
            }

            if (!foundPath.IsValid)
            {
                return false;
            }

            location = foundPath;
            return true;
        }
    }
}
