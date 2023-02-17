// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.FrontEnd.MsBuild.Serialization;
using BuildXL.FrontEnd.Utilities;
using BuildXL.FrontEnd.Utilities.GenericProjectGraphResolver;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Native.IO;
using BuildXL.Processes;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Newtonsoft.Json;
using TypeScript.Net.Types;
using static BuildXL.Utilities.Core.FormattableStringEx;

namespace BuildXL.FrontEnd.MsBuild
{

    /// <summary>
    /// Workspace resolver using the MsBuild static graph API
    /// </summary>
    public class MsBuildWorkspaceResolver : ProjectGraphWorkspaceResolverBase<ProjectGraphResult, MsBuildResolverSettings>
    {
        internal const string MsBuildResolverName = "MsBuild";

        /// <summary>
        /// Set of well known locations that are used to identify a candidate entry point to parse, if a specific one is not provided
        /// </summary>
        private static readonly HashSet<string> s_wellKnownEntryPointExtensions = new(StringComparer.OrdinalIgnoreCase) { "proj", "sln" };

        /// <summary>
        /// Keep in sync with the BuildXL deployment spec that places the tool
        /// </summary>
        private RelativePath RelativePathToGraphConstructionTool =>
            RelativePath.Create(Context.StringTable, ResolverSettings.ShouldRunDotNetCoreMSBuild()
                ? @"tools\MsBuildGraphBuilder\dotnetcore\ProjectGraphBuilder.dll"
                : @"tools\MsBuildGraphBuilder\net472\ProjectGraphBuilder.exe");

        /// <inheritdoc/>
        public MsBuildWorkspaceResolver() => Name = MsBuildResolverName;

        /// <inheritdoc/>
        public override string Kind => KnownResolverKind.MsBuildResolverKind;

        /// <summary>
        /// Exposes a collection with all output directories as a value for other resolvers to consume
        /// </summary>
        protected override SourceFile DoCreateSourceFile(AbsolutePath path)
        {
            var sourceFile = SourceFile.Create(path.ToString(Context.PathTable));

            string projectIdentifier = GetIdentifierForProject(path);

            // TODO: factor out common logic into utility functions, we are duplicating
            // code that can be found on the download resolver

            // A value representing all output directories of the project
            // TODO: No MSBuild-specific logic
            var outputDeclaration = new VariableDeclaration(projectIdentifier, Identifier.CreateUndefined(), new ArrayTypeNode { ElementType = new TypeReferenceNode("SharedOpaqueDirectory") });
            outputDeclaration.Flags |= NodeFlags.Export | NodeFlags.Public | NodeFlags.ScriptPublic;
            outputDeclaration.Pos = 1;
            outputDeclaration.End = 2;

            // Final source file looks like
            //   @@public export outputs: SharedOpaqueDirectory[] = undefined;
            // The 'undefined' part is not really important here. The value at runtime has its own special handling in the resolver.
            sourceFile.Statements.Add(new VariableStatement()
            {
                DeclarationList = new VariableDeclarationList(
                        NodeFlags.Const,
                        outputDeclaration)
            });

            // Needed for the binder to recurse.
            sourceFile.ExternalModuleIndicator = sourceFile;
            sourceFile.SetLineMap(new[] { 0, 2 });
            sourceFile.OverrideIsScriptFile = true;

            return sourceFile;
        }

        /// <summary>
        /// Returns a DScript identifier to be used to represent the outputs of a project
        /// </summary>
        /// <remarks>
        /// TODO: for now we just return an underscore flattened name that is built using the relative path of the project
        /// from the root. Consider returning a more human-readable name that avoids duplicates.
        /// </remarks>
        internal string GetIdentifierForProject(AbsolutePath projectPath)
        {
            var success = ResolverSettings.RootTraversal.TryGetRelative(Context.PathTable, projectPath, out RelativePath path);
            Contract.Assert(success);

            string shortName = path.RemoveExtension(Context.StringTable).ToString(Context.StringTable);
            
            return shortName
                .Replace('.', '_')
                .Replace('/', '_')
                .Replace('\\', '_')
                .Replace('-', '_');
        }

        /// <inheritdoc/>
        protected override async Task<Possible<ProjectGraphResult>> TryComputeBuildGraphAsync()
        {
            // Get the locations where the MsBuild assemblies should be searched
            // We no longer search for the MsBuild location using %PATH% because the MsBuild graph builder will use Microsoft build locator to find MsBuild location
            // if none is specified explicitly.
            IEnumerable<AbsolutePath> msBuildSearchLocations = ResolverSettings.MsBuildSearchLocations != null && ResolverSettings.MsBuildSearchLocations.Count > 0
                ? ResolverSettings.MsBuildSearchLocations.Select(d => d.Path)
                : Enumerable.Empty<AbsolutePath>();

            if (!TryRetrieveParsingEntryPoint(out IEnumerable<AbsolutePath> parsingEntryPoints))
            {
                // Errors should have been logged
                return new MsBuildGraphConstructionFailure(ResolverSettings, Context.PathTable);
            }

            // If we should run the dotnet core version of MSBuild, let's retrieve the locations where dotnet.exe
            // should be found
            IEnumerable<AbsolutePath> dotNetSearchLocations = null;
            if (ResolverSettings.ShouldRunDotNetCoreMSBuild())
            {
                if (!TryRetrieveDotNetSearchLocations(out dotNetSearchLocations))
                {
                    // Errors should have been logged
                    return new MsBuildGraphConstructionFailure(ResolverSettings, Context.PathTable);
                }
            }

            BuildParameters.IBuildParameters buildParameters = RetrieveBuildParameters();

            return await TryComputeBuildGraphAsync(msBuildSearchLocations, dotNetSearchLocations, parsingEntryPoints, buildParameters);
        }

        private bool TryRetrieveParsingEntryPoint(out IEnumerable<AbsolutePath> parsingEntryPoints)
        {
            if (ResolverSettings.FileNameEntryPoints?.Count > 0)
            {
                parsingEntryPoints = ResolverSettings.FileNameEntryPoints.Select(entryPoint => ResolverSettings.RootTraversal.Combine(Context.PathTable, entryPoint));
                return true;
            }

            // Retrieve all files directly under the root traversal whose extensions end with any of the well known entry point extensions
            List<AbsolutePath> filesInRootTraversal = Host
                .Engine
                .EnumerateFiles(ResolverSettings.RootTraversal, recursive: false)
                .Where(file => s_wellKnownEntryPointExtensions
                                    .Any(extension => file
                                        .GetName(Context.PathTable)
                                        .GetExtension(Context.StringTable)
                                        .ToString(Context.StringTable)
                                        .EndsWith(extension, StringComparison.OrdinalIgnoreCase))).ToList();

            // If there is a single element, that's the one
            if (filesInRootTraversal.Count == 1)
            {
                parsingEntryPoints = filesInRootTraversal;
                return true;
            }

            // Otherwise, we don't really know where to start, and the user should specify that more precisely

            if (filesInRootTraversal.Count == 0)
            {
                Tracing.Logger.Log.CannotFindParsingEntryPoint(Context.LoggingContext, ResolverSettings.Location(Context.PathTable), ResolverSettings.RootTraversal.ToString(Context.PathTable));
            }
            else
            {
                Tracing.Logger.Log.TooManyParsingEntryPointCandidates(Context.LoggingContext, ResolverSettings.Location(Context.PathTable), ResolverSettings.RootTraversal.ToString(Context.PathTable));
            }

            parsingEntryPoints = null;
            return false;

        }

        private async Task<Possible<ProjectGraphResult>> TryComputeBuildGraphAsync(
            IEnumerable<AbsolutePath> msBuildSearchLocations,
            IEnumerable<AbsolutePath> dotnetSearchLocations,
            IEnumerable<AbsolutePath> parsingEntryPoints,
            BuildParameters.IBuildParameters buildParameters)
        {
            // We create a unique output file on the obj folder associated with the current front end, and using a GUID as the file name
            AbsolutePath outputDirectory = Host.GetFolderForFrontEnd(Name);
            AbsolutePath outputFile = outputDirectory.Combine(Context.PathTable, Guid.NewGuid().ToString());
            
            // We create a unique response file that will contain the tool arguments
            AbsolutePath responseFile = outputDirectory.Combine(Context.PathTable, Guid.NewGuid().ToString());

            // Make sure the directories are there
            FileUtilities.CreateDirectory(outputDirectory.ToString(Context.PathTable));

            Possible<ProjectGraphWithPredictionsResult<AbsolutePath>> maybeProjectGraphResult = await ComputeBuildGraphAsync(
                responseFile,
                parsingEntryPoints,
                outputFile,
                msBuildSearchLocations,
                dotnetSearchLocations,
                buildParameters);

            if (!maybeProjectGraphResult.Succeeded)
            {
                // A more specific error has been logged already
                return maybeProjectGraphResult.Failure;
            }

            var projectGraphResult = maybeProjectGraphResult.Result;

            if (ResolverSettings.KeepProjectGraphFile != true)
            {
                DeleteGraphBuilderRelatedFiles(outputFile, responseFile);
            }
            else
            {
                // Graph-related files are requested to be left on disk. Let's print a message with their location.
                Tracing.Logger.Log.GraphBuilderFilesAreNotRemoved(Context.LoggingContext, outputFile.ToString(Context.PathTable), responseFile.ToString(Context.PathTable));
            }

            if (!projectGraphResult.Succeeded)
            {
                var failure = projectGraphResult.Failure;
                Tracing.Logger.Log.ProjectGraphConstructionError(Context.LoggingContext, failure.HasLocation ? failure.Location : ResolverSettings.Location(Context.PathTable), failure.Message);

                return new MsBuildGraphConstructionFailure(ResolverSettings, Context.PathTable);
            }

            ProjectGraphWithPredictions<AbsolutePath> projectGraph = projectGraphResult.Result;

            // The module contains all project files that are part of the graph
            var projectFiles = new HashSet<AbsolutePath>();
            foreach (ProjectWithPredictions<AbsolutePath> node in projectGraph.ProjectNodes)
            {
                projectFiles.Add(node.FullPath);
            }

            var moduleDescriptor = ModuleDescriptor.CreateWithUniqueId(Context.StringTable, ResolverSettings.ModuleName, this);
            var moduleDefinition = ModuleDefinition.CreateModuleDefinitionWithImplicitReferences(
                moduleDescriptor,
                ResolverSettings.RootTraversal,
                ResolverSettings.File,
                projectFiles,
                allowedModuleDependencies: null, // no module policies
                cyclicalFriendModules: null, // no allowlist of cycles
                mounts: null);

            return new ProjectGraphResult(projectGraph, moduleDefinition, projectGraphResult.PathToMsBuild, projectGraphResult.PathToDotNetExe);
        }

        private void DeleteGraphBuilderRelatedFiles(AbsolutePath outputFile, AbsolutePath responseFile)
        {
            // Remove the file with the serialized graph and the response file, so we leave no garbage behind
            // If there is a problem deleting these file, unlikely to happen (the process that created it should be gone by now), log as a warning and move on, this is not
            // a blocking problem

            try
            {
                FileUtilities.DeleteFile(outputFile.ToString(Context.PathTable));
            }
            catch (BuildXLException ex)
            {
                Tracing.Logger.Log.CannotDeleteSerializedGraphFile(Context.LoggingContext, ResolverSettings.Location(Context.PathTable), outputFile.ToString(Context.PathTable), ex.Message);
            }

            try
            {
                FileUtilities.DeleteFile(responseFile.ToString(Context.PathTable));
            }
            catch (BuildXLException ex)
            {
                Tracing.Logger.Log.CannotDeleteResponseFile(Context.LoggingContext, ResolverSettings.Location(Context.PathTable), responseFile.ToString(Context.PathTable), ex.Message);
            }
        }

        /// <summary>
        /// Retrieves a list of search locations for dotnet.exe
        /// </summary>
        /// <remarks>
        /// First inspects the resolver configuration to check if these are defined explicitly. Otherwise, uses PATH environment variable.
        /// </remarks>
        private bool TryRetrieveDotNetSearchLocations(out IEnumerable<AbsolutePath> searchLocations) =>
            FrontEndUtilities.TryRetrieveExecutableSearchLocations(
                Name,
                Context,
                Host.Engine,
                ResolverSettings.DotNetSearchLocations?.SelectList(directoryLocation => directoryLocation.Path),
                out searchLocations,
                () => Tracing.Logger.Log.NoSearchLocationsSpecified(Context.LoggingContext, ResolverSettings.Location(Context.PathTable), "dotnetSearchLocations"),
                paths => Tracing.Logger.Log.CannotParseBuildParameterPath(Context.LoggingContext, ResolverSettings.Location(Context.PathTable), paths));

        private async Task<Possible<ProjectGraphWithPredictionsResult<AbsolutePath>>> ComputeBuildGraphAsync(
            AbsolutePath responseFile,
            IEnumerable<AbsolutePath> projectEntryPoints,
            AbsolutePath outputFile,
            IEnumerable<AbsolutePath> msBuidSearchLocations,
            IEnumerable<AbsolutePath> dotnetSearchLocations,
            BuildParameters.IBuildParameters buildParameters)
        {
            AbsolutePath dotnetExeLocation = AbsolutePath.Invalid;
            if (ResolverSettings.ShouldRunDotNetCoreMSBuild())
            {
                if (!TryFindDotNetExe(dotnetSearchLocations, out dotnetExeLocation, out string failure))
                {
                    return ProjectGraphWithPredictionsResult<AbsolutePath>.CreateFailure(
                        GraphConstructionError.CreateFailureWithoutLocation(failure),
                        CollectionUtilities.EmptyDictionary<string, AbsolutePath>(), AbsolutePath.Invalid);
                }
            }

            SandboxedProcessResult result = await RunMsBuildGraphBuilderAsync(responseFile, projectEntryPoints, outputFile, msBuidSearchLocations, dotnetExeLocation, buildParameters);

            string standardError = result.StandardError.CreateReader().ReadToEndAsync().GetAwaiter().GetResult();

            if (result.ExitCode != 0)
            {
                // In case of a cancellation, the tool may have exited with a non-zero
                // code, but that's expected
                if (!Context.CancellationToken.IsCancellationRequested)
                {
                    // This should never happen! Report the standard error and exit gracefully
                    Tracing.Logger.Log.GraphConstructionInternalError(
                        Context.LoggingContext,
                        ResolverSettings.Location(Context.PathTable),
                        standardError);
                }

                return new MsBuildGraphConstructionFailure(ResolverSettings, Context.PathTable);
            }

            // If the tool exited gracefully, but standard error is not empty, that
            // is interpreted as a warning. We propagate that to the BuildXL log
            if (!string.IsNullOrEmpty(standardError))
            {
                Tracing.Logger.Log.GraphConstructionFinishedSuccessfullyButWithWarnings(
                    Context.LoggingContext,
                    ResolverSettings.Location(Context.PathTable),
                    standardError);
            }

            TrackFilesAndEnvironment(result.AllUnexpectedFileAccesses, outputFile.GetParent(Context.PathTable));
            JsonSerializer serializer = ConstructProjectGraphSerializer(ProjectGraphSerializationSettings.Settings);

            using (var sr = new StreamReader(outputFile.ToString(Context.PathTable)))
            using (var reader = new JsonTextReader(sr))
            {
                var projectGraphWithPredictionsResult = serializer.Deserialize<ProjectGraphWithPredictionsResult<AbsolutePath>>(reader);

                // A successfully constructed graph should always have a valid path to MsBuild
                Contract.Assert(!projectGraphWithPredictionsResult.Succeeded || projectGraphWithPredictionsResult.PathToMsBuild.IsValid);
                // A successfully constructed graph should always have at least one project node
                Contract.Assert(!projectGraphWithPredictionsResult.Succeeded || projectGraphWithPredictionsResult.Result.ProjectNodes.Length > 0);
                // A failed construction should always have a failure set
                Contract.Assert(projectGraphWithPredictionsResult.Succeeded || projectGraphWithPredictionsResult.Failure != null);

                // Let's log the paths to the used MsBuild assemblies, just for debugging purposes
                Tracing.Logger.Log.GraphConstructionToolCompleted(
                    Context.LoggingContext, ResolverSettings.Location(Context.PathTable),
                    string.Join(",\n", projectGraphWithPredictionsResult.MsBuildAssemblyPaths.Select(kvp => I($"[{kvp.Key}]:{kvp.Value.ToString(Context.PathTable)}"))),
                    projectGraphWithPredictionsResult.PathToMsBuild.ToString(Context.PathTable));

                return ResolverSettings.ShouldRunDotNetCoreMSBuild() ? projectGraphWithPredictionsResult.WithPathToDotNetExe(dotnetExeLocation) : projectGraphWithPredictionsResult;
            }
        }

        private bool TryFindDotNetExe(IEnumerable<AbsolutePath> dotnetSearchLocations, out AbsolutePath dotnetExeLocation, out string failure)
        {
            dotnetExeLocation = AbsolutePath.Invalid;
            failure = string.Empty;

            foreach (AbsolutePath location in dotnetSearchLocations)
            {
                AbsolutePath dotnetExeCandidate = location.Combine(Context.PathTable, "dotnet.exe");
                if (Host.Engine.FileExists(dotnetExeCandidate))
                {
                    dotnetExeLocation = dotnetExeCandidate;
                    return true;
                }
            }

            string searchLocationsStr = string.Join(", ", dotnetSearchLocations.Select(location => location.ToString(Context.PathTable)));
            failure = $"Cannot find dotnet.exe. This is required because the dotnet core version of MSBuild was specified to run. Searched locations: [{searchLocationsStr}]";
            return false;
        }

        private Task<SandboxedProcessResult> RunMsBuildGraphBuilderAsync(
            AbsolutePath responseFile,
            IEnumerable<AbsolutePath> projectEntryPoints,
            AbsolutePath outputFile,
            IEnumerable<AbsolutePath> msBuildSearchLocations,
            AbsolutePath dotnetExeLocation,
            BuildParameters.IBuildParameters buildParameters)
        {
            Contract.Assert(!ResolverSettings.ShouldRunDotNetCoreMSBuild() || dotnetExeLocation.IsValid);

            AbsolutePath toolDirectory = Configuration.Layout.BuildEngineDirectory.Combine(Context.PathTable, RelativePathToGraphConstructionTool).GetParent(Context.PathTable);
            string outputDirectory = outputFile.GetParent(Context.PathTable).ToString(Context.PathTable);
            string outputFileString = outputFile.ToString(Context.PathTable);
            IReadOnlyCollection<string> entryPointTargets = ResolverSettings.InitialTargets ?? CollectionUtilities.EmptyArray<string>();

            var requestedQualifiers = Host.QualifiersToEvaluate.Select(qualifierId => MsBuildResolverUtils.CreateQualifierAsGlobalProperties(qualifierId, Context)).ToList();

            var arguments = new MSBuildGraphBuilderArguments(
                projectEntryPoints.Select(entryPoint => entryPoint.ToString(Context.PathTable)).ToList(),
                outputFileString,
                new GlobalProperties(ResolverSettings.GlobalProperties ?? CollectionUtilities.EmptyDictionary<string, string>()),
                msBuildSearchLocations.Select(location => location.ToString(Context.PathTable)).ToList(),
                entryPointTargets,
                requestedQualifiers,
                ResolverSettings.AllowProjectsToNotSpecifyTargetProtocol == true,
                ResolverSettings.ShouldRunDotNetCoreMSBuild(),
                ResolverSettings.UseLegacyProjectIsolation == true);

            var responseFilePath = responseFile.ToString(Context.PathTable);
            SerializeResponseFile(responseFilePath, arguments);

            string graphConstructionToolPath = Configuration.Layout.BuildEngineDirectory.Combine(Context.PathTable, RelativePathToGraphConstructionTool).ToString(Context.PathTable);
            string pathToTool;
            string toolArguments;
            // if we should call the dotnet core version of MSBuild, we need to actually call dotnet.exe and pass the tool itself as its first argument
            if (ResolverSettings.ShouldRunDotNetCoreMSBuild())
            {
                pathToTool = dotnetExeLocation.ToString(Context.PathTable);
                toolArguments = I($"\"{graphConstructionToolPath}\" \"{responseFilePath}\"");
            }
            else
            {
                pathToTool = graphConstructionToolPath;
                toolArguments = I($"\"{responseFilePath}\"");
            }

            Tracing.Logger.Log.LaunchingGraphConstructionTool(Context.LoggingContext, ResolverSettings.Location(Context.PathTable), arguments.ToString(), pathToTool);

            // Just being defensive, make sure there is not an old output file lingering around
            File.Delete(outputFileString);

            return FrontEndUtilities.RunSandboxedToolAsync(
                Context,
                pathToTool,
                buildStorageDirectory: outputDirectory,
                fileAccessManifest: GenerateFileAccessManifest(toolDirectory, outputFile),
                arguments: toolArguments,
                workingDirectory: Configuration.Layout.SourceDirectory.ToString(Context.PathTable),
                description: "MsBuild graph builder",
                buildParameters,
                beforeLaunch: () => ConnectToServerPipeAndLogProgress(outputFileString));
        }

        private void SerializeResponseFile(string responseFile, MSBuildGraphBuilderArguments arguments)
        {
            var serializer = JsonSerializer.Create(ProjectGraphSerializationSettings.Settings);
            using (var sw = new StreamWriter(responseFile))
            using (var writer = new JsonTextWriter(sw))
            {
                serializer.Serialize(sw, arguments);
            }
        }

        private void ConnectToServerPipeAndLogProgress(string outputFileString)
        {
            // We start a dedicated thread that listens to the graph construction progress process pipe and redirects all messages
            // to BuildXL logging. The thread terminates then the pip is closed or the user requests a cancellation.
            Analysis.IgnoreResult(
                Task.Factory.StartNew(
                        () =>
                        {
                            try
                            {
                                // The name of the pipe is the filename of the output file
                                using (var pipeClient = new NamedPipeClientStream(
                                    ".",
                                    Path.GetFileName(outputFileString),
                                    PipeDirection.In,
                                    PipeOptions.Asynchronous))
                                using (var reader = new StreamReader(pipeClient, Encoding.UTF8))
                                {
                                    // Let's give the client a 5 second timeout to connect to the graph construction process
                                    pipeClient.Connect(5000);
                                    // We try to read from the pipe while the stream is not flagged to be finished and there is
                                    // no user cancellation
                                    while (!Context.CancellationToken.IsCancellationRequested && !reader.EndOfStream)
                                    {
                                        var line = reader.ReadLine();
                                        if (line != null)
                                        {
                                            Tracing.Logger.Log.ReportGraphConstructionProgress(Context.LoggingContext, line);
                                        }
                                    }
                                }
                            }
                            // In case of a timeout or an unexpected exception, we just log warnings. This only prevents
                            // progress to be reported, but the graph construction process itself may continue to run
                            catch (TimeoutException)
                            {
                                Tracing.Logger.Log.CannotGetProgressFromGraphConstructionDueToTimeout(Context.LoggingContext);
                            }
                            catch (IOException ioException)
                            {
                                Tracing.Logger.Log.CannotGetProgressFromGraphConstructionDueToUnexpectedException(Context.LoggingContext, ioException.Message);
                            }
                        }
                    )
                );
        }

        private FileAccessManifest GenerateFileAccessManifest(AbsolutePath toolDirectory, AbsolutePath outputFile)
        {
            // We make no attempt at understanding what the graph generation process is going to do
            // We just configure the manifest to not fail on unexpected accesses, so they can be collected
            // later if needed
            var fileAccessManifest = new FileAccessManifest(Context.PathTable)
            {
                FailUnexpectedFileAccesses = false,
                ReportFileAccesses = true,
                MonitorNtCreateFile = true,
                MonitorZwCreateOpenQueryFile = true,
                MonitorChildProcesses = true,
            };

            fileAccessManifest.AddScope(
                AbsolutePath.Create(Context.PathTable, SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.Windows)),
                FileAccessPolicy.MaskAll,
                FileAccessPolicy.AllowAllButSymlinkCreation);

            fileAccessManifest.AddScope(
                AbsolutePath.Create(Context.PathTable, SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.InternetCache)),
                FileAccessPolicy.MaskAll,
                FileAccessPolicy.AllowAllButSymlinkCreation);

            fileAccessManifest.AddScope(
                AbsolutePath.Create(Context.PathTable, SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.History)),
                FileAccessPolicy.MaskAll,
                FileAccessPolicy.AllowAllButSymlinkCreation);

            fileAccessManifest.AddScope(toolDirectory, FileAccessPolicy.MaskAll, FileAccessPolicy.AllowReadAlways);
            fileAccessManifest.AddPath(outputFile, FileAccessPolicy.MaskAll, FileAccessPolicy.AllowWrite);

            return fileAccessManifest;
        }
    }
}
