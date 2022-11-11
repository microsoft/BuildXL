// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BuildXL.FrontEnd.Ninja.Serialization;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Utilities;
using BuildXL.FrontEnd.Utilities.GenericProjectGraphResolver;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Native.IO;
using BuildXL.Processes;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using Newtonsoft.Json;
using TypeScript.Net.Types;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Ninja
{
    /// <summary>
    /// Workspace resolver using a custom JSON generator from ninja specs
    /// </summary>
    public sealed class NinjaWorkspaceResolver : ProjectGraphWorkspaceResolverBase<NinjaGraphWithModuleDefinition, INinjaResolverSettings>
    {
        internal const string NinjaResolverName = "Ninja";
        private AbsolutePath m_pathToTool;
        internal AbsolutePath ProjectRoot;
        internal AbsolutePath SpecFile;

        // The build targets this workspace resolver knows about after initialization
        // For now this means we will build all of these
        private string[] m_targets; 
 
        /// <summary>
        /// The path for the JSON graph associated with this resolver.
        /// This file exists only after the graph is successfully built.
        /// </summary>
        /// <remarks>
        /// Marked as internal - we may want to use this in the FrontEnd resolver for logging purposes
        /// </remarks>
        internal Lazy<AbsolutePath> SerializedGraphPath;

        /// <summary>
        /// Keep in sync with the BuildXL deployment spec that places the tool (\Public\Src\Deployment\buildXL.dsc)
        /// </summary>
        private const string NinjaGraphBuilderRelativePath = @"NinjaGraphBuilder.exe";

        /// <inheritdoc/>
        public NinjaWorkspaceResolver()
        {
            Name = "Ninja";
            SerializedGraphPath = new Lazy<AbsolutePath>(GetToolOutputPath);
        }

        /// <inheritdoc/>
        public override string Kind => KnownResolverKind.NinjaResolverKind;

        /// <inheritdoc />
        /// <summary>
        /// Initializes the workspace resolver
        /// </summary>
        public override bool TryInitialize(
            FrontEndHost host,
            FrontEndContext context,
            IConfiguration configuration,
            IResolverSettings resolverSettings)
        {
            if (!base.TryInitialize(host, context, configuration, resolverSettings))
            {
                return false;
            }

            var relativePathToGraphConstructionTool = RelativePath.Create(context.StringTable, NinjaGraphBuilderRelativePath);
            m_pathToTool = configuration.Layout.BuildEngineDirectory.Combine(Context.PathTable, relativePathToGraphConstructionTool);


            if (!ResolverSettings.Root.IsValid)
            {
                if (!ResolverSettings.SpecFile.IsValid)
                {
                    Tracing.Logger.Log.InvalidResolverSettings(Context.LoggingContext, ResolverSettings.Location(Context.PathTable), "Either a project root or a spec file location (or both) must be specified.");
                    return false;
                }

                ProjectRoot = ResolverSettings.SpecFile.GetParent(Context.PathTable);
                SpecFile = ResolverSettings.SpecFile;
            }
            else
            {
                ProjectRoot = ResolverSettings.Root;
                SpecFile = ResolverSettings.SpecFile;
                if (!ResolverSettings.SpecFile.IsValid)
                {
                    SpecFile = ProjectRoot.Combine(Context.PathTable, "build.ninja");
                }
            }

            string path;
            if (!Directory.Exists(path = ProjectRoot.ToString(Context.PathTable)))
            {
                Tracing.Logger.Log.ProjectRootDirectoryDoesNotExist(Context.LoggingContext, ResolverSettings.Location(Context.PathTable), path);
                return false;
            }

            if (!File.Exists(path = SpecFile.ToString(Context.PathTable)))
            {
                Tracing.Logger.Log.NinjaSpecFileDoesNotExist(Context.LoggingContext, ResolverSettings.Location(Context.PathTable), path);
                return false;
            }
    
            m_targets = ResolverSettings.Targets != null ? ResolverSettings.Targets.AsArray() : CollectionUtilities.EmptyArray<string>();
            return true;
        }

        /// <inheritdoc />
        protected override async Task<Possible<NinjaGraphWithModuleDefinition>> TryComputeBuildGraphAsync()
        {
            Possible<NinjaGraphResult> maybeGraph = await ComputeBuildGraphAsync();
            
            var result = maybeGraph.Result;
            var specFileConfig = SpecFile.ChangeExtension(Context.PathTable, PathAtom.Create(Context.StringTable, ".ninja.dsc")); // It needs to be a .dsc for the parsing to work

            var moduleDescriptor = ModuleDescriptor.CreateWithUniqueId(Context.StringTable, ResolverSettings.ModuleName, this);
            var moduleDefinition = ModuleDefinition.CreateModuleDefinitionWithImplicitReferences(
                moduleDescriptor,
                ProjectRoot,
                ResolverSettings.File,
                new List<AbsolutePath>() { specFileConfig } ,
                allowedModuleDependencies: null, // no module policies
                cyclicalFriendModules: null, // no allowlist of cycles
                mounts: null);

            return new NinjaGraphWithModuleDefinition(result.Graph, moduleDefinition);            
        }

        private async Task<Possible<NinjaGraphResult>> ComputeBuildGraphAsync()
        {
            AbsolutePath outputFile = SerializedGraphPath.Value;

            SandboxedProcessResult result = await RunNinjaGraphBuilderAsync(outputFile);

            string standardError = result.StandardError.CreateReader().ReadToEndAsync().GetAwaiter().GetResult();
            if (result.ExitCode != 0)
            {
                if (!Context.CancellationToken.IsCancellationRequested)
                {

                    Tracing.Logger.Log.GraphConstructionInternalError(
                        Context.LoggingContext,
                        ResolverSettings.Location(Context.PathTable),
                        standardError);
                }

                return new NinjaGraphConstructionFailure(ResolverSettings.ModuleName, ProjectRoot.ToString(Context.PathTable));
            }

            // If the tool exited gracefully, but standard error is not empty, that is interpreted as a warning
            if (!string.IsNullOrEmpty(standardError))
            {
                Tracing.Logger.Log.GraphConstructionFinishedSuccessfullyButWithWarnings(
                    Context.LoggingContext,
                    ResolverSettings.Location(Context.PathTable),
                    standardError);
            }
            
            TrackFilesAndEnvironment(result.AllUnexpectedFileAccesses, outputFile.GetParent(Context.PathTable));
            var serializer = JsonSerializer.Create(GraphSerializationSettings.Settings);
            
            // Add custom deserializer for converting string arrays to AbsolutePath ReadOnlySets
            serializer.Converters.Add(new RootAwareAbsolutePathConverter(Context.PathTable, SpecFile.GetParent(Context.PathTable)));
            serializer.Converters.Add(new ToReadOnlySetJsonConverter<AbsolutePath>());
            
            var outputFileString = outputFile.ToString(Context.PathTable);
            Tracing.Logger.Log.LeftGraphToolOutputAt(Context.LoggingContext, ResolverSettings.Location(Context.PathTable), outputFileString);

            NinjaGraphResult projectGraphWithPredictionResult;

            try
            {
                using (var sr = new StreamReader(outputFileString))
                using (var reader = new JsonTextReader(sr))
                {
                    projectGraphWithPredictionResult = serializer.Deserialize<NinjaGraphResult>(reader);
                }
            }
            catch (Exception ex)
            {
                Tracing.Logger.Log.GraphConstructionDeserializationError(
                    Context.LoggingContext,
                    ResolverSettings.Location(Context.PathTable),
                    ex.ToString());
                return new NinjaGraphConstructionFailure(ResolverSettings.ModuleName, ProjectRoot.ToString(Context.PathTable));
            }

            return projectGraphWithPredictionResult;
        }

        private Task<SandboxedProcessResult> RunNinjaGraphBuilderAsync(AbsolutePath outputFile)
        {
            AbsolutePath outputDirectory = outputFile.GetParent(Context.PathTable);
            FileUtilities.CreateDirectory(outputDirectory.ToString(Context.PathTable)); // Ensure it exists

            AbsolutePath argumentsFile = outputDirectory.Combine(Context.PathTable, Guid.NewGuid().ToString());
            SerializeToolArguments(outputFile, argumentsFile);

            // After running the tool we'd like to remove some files 
            void cleanUpOnResult()
            {
                try
                {
                    var shouldKeepArgs = ResolverSettings.KeepProjectGraphFile ?? false;
                    if (!shouldKeepArgs)
                    {
                        FileUtilities.DeleteFile(argumentsFile.ToString(Context.PathTable));
                    }
                }
                catch (BuildXLException e)
                {
                    Tracing.Logger.Log.CouldNotDeleteToolArgumentsFile(
                        Context.LoggingContext,
                        ResolverSettings.Location(Context.PathTable),
                        argumentsFile.ToString(Context.PathTable),
                        e.Message);
                }
            }


            return FrontEndUtilities.RunSandboxedToolAsync(
                Context,
                m_pathToTool.ToString(Context.PathTable),
                buildStorageDirectory: outputDirectory.ToString(Context.PathTable),
                fileAccessManifest: GenerateFileAccessManifest(m_pathToTool.GetParent(Context.PathTable), outputFile),
                arguments: I($@"""{argumentsFile.ToString(Context.PathTable)}"""),
                workingDirectory: SpecFile.GetParent(Context.PathTable).ToString(Context.PathTable),
                description: "Ninja graph builder",
                RetrieveBuildParameters(),
                onResult: cleanUpOnResult);
        }

        private void SerializeToolArguments(AbsolutePath outputFile, AbsolutePath argumentsFile)
        {
            var arguments = new NinjaGraphToolArguments()
                            {
                                BuildFileName = SpecFile.ToString(Context.PathTable),
                                ProjectRoot = SpecFile.GetParent(Context.PathTable).ToString(Context.PathTable),
                                OutputFile = outputFile.ToString(Context.PathTable),
                                Targets = m_targets
                            };


            var serializer = JsonSerializer.Create(GraphSerializationSettings.Settings);
            using (var sw = new StreamWriter(argumentsFile.ToString(Context.PathTable)))
            using (var writer = new JsonTextWriter(sw))
            {
                serializer.Serialize(writer, arguments);
            }
        }

        /// <summary>
        /// Get the output path for the JSON graph associated with this resolver
        /// </summary>
        private AbsolutePath GetToolOutputPath()
        {
            AbsolutePath outputDirectory = Host.GetFolderForFrontEnd(Name);
            var now = DateTime.UtcNow.ToString("yyyy-MM-dd-THH-mm-ss.SSS-Z");
            var uniqueName = $"ninja_graph_{now}.json";
            return outputDirectory.Combine(Context.PathTable, uniqueName);
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
                FileAccessPolicy.AllowAll);

            fileAccessManifest.AddScope(toolDirectory, FileAccessPolicy.MaskAll, FileAccessPolicy.AllowReadAlways);            
            return fileAccessManifest;
        }

        /// <inheritdoc />
        protected override SourceFile DoCreateSourceFile(AbsolutePath path) =>
            // This is the interop point to advertise values to other DScript specs
            // For now we just return an empty SourceFile
            SourceFile.Create(path.ToString(Context.PathTable));

        private sealed class NinjaGraphBuildStorage : ISandboxedProcessFileStorage
        {
            private readonly string m_directory;

            /// <nodoc />
            public NinjaGraphBuildStorage(string directory) => m_directory = directory;

            /// <inheritdoc />
            public string GetFileName(SandboxedProcessFile file) => Path.Combine(m_directory, file.DefaultFileName());
        }
    }
}
