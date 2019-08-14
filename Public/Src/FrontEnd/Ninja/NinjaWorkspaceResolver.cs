// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.FrontEnd.Ninja.Serialization;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Utilities;
using BuildXL.FrontEnd.Workspaces;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Native.IO;
using BuildXL.Processes;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using Newtonsoft.Json;
using TypeScript.Net.DScript;
using TypeScript.Net.Types;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Ninja
{
    /// <summary>
    /// Workspace resolver using a custom JSON generator from ninja specs
    /// </summary>
    public class NinjaWorkspaceResolver : IWorkspaceModuleResolver
    {
        internal const string NinjaResolverName = "Ninja";

        /// <inheritdoc />
        public string Name { get; }

        private FrontEndContext m_context;
        private FrontEndHost m_host;

        private INinjaResolverSettings m_resolverSettings;

        private AbsolutePath m_pathToTool;
        internal AbsolutePath ProjectRoot;
        internal AbsolutePath SpecFile;

        // The build targets this workspace resolver knows about after initialization
        // For now this means we will build all of these
        private string[] m_targets; 
 
        // AsyncLazy graph
        private readonly Lazy<Task<Possible<NinjaGraphWithModuleDefinition>>> m_graph;
        private readonly ConcurrentDictionary<AbsolutePath, SourceFile> m_createdSourceFiles =
            new ConcurrentDictionary<AbsolutePath, SourceFile>();

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
        private const string NinjaGraphBuilderRelativePath = @"tools\CMakeNinja\NinjaGraphBuilder.exe";

        /// <inheritdoc/>
        public NinjaWorkspaceResolver()
        {
            Name = nameof(NinjaWorkspaceResolver);
            m_graph = new Lazy<Task<Possible<NinjaGraphWithModuleDefinition>>>(TryComputeBuildGraphAsync);
            SerializedGraphPath = new Lazy<AbsolutePath>(GetToolOutputPath);
        }

        /// <inheritdoc />
        public Task<Possible<ISourceFile>> TryParseAsync(
            AbsolutePath pathToParse,
            AbsolutePath moduleOrConfigPathPromptingParse,
            ParsingOptions parsingOptions = null)
        {
            return Task.FromResult(Possible.Create((ISourceFile)GetOrCreateSourceFile(pathToParse)));
        }


        private SourceFile GetOrCreateSourceFile(AbsolutePath path)
        {
            Contract.Assert(path.IsValid);

            if (m_createdSourceFiles.TryGetValue(path, out SourceFile sourceFile))
            {
                return sourceFile;
            }

            // This is the interop point to advertise values to other DScript specs
            // For now we just return an empty SourceFile
            sourceFile = SourceFile.Create(path.ToString(m_context.PathTable));

            // We need the binder to recurse
            sourceFile.ExternalModuleIndicator = sourceFile;
            sourceFile.SetLineMap(new int[0] { });

            m_createdSourceFiles.Add(path, sourceFile);
            return sourceFile;
        }

        /// <inheritdoc/>
        public string DescribeExtent()
        {
            Possible<HashSet<ModuleDescriptor>> maybeModules = GetAllKnownModuleDescriptorsAsync().GetAwaiter().GetResult();

            if (!maybeModules.Succeeded)
            {
                return I($"Module extent could not be computed. {maybeModules.Failure.Describe()}");
            }

            return string.Join(", ", maybeModules.Result.Select(module => module.Name));
        }

        /// <inheritdoc/>
        public virtual string Kind => KnownResolverKind.NinjaResolverKind;

        /// <inheritdoc/>
        public async ValueTask<Possible<HashSet<ModuleDescriptor>>> GetAllKnownModuleDescriptorsAsync()
        {
            var result = (await m_graph.Value)
                .Then(projectGraphResult => new HashSet<ModuleDescriptor> { projectGraphResult.ModuleDefinition.Descriptor });

            return result;
        }

        /// <inheritdoc/>
        public async ValueTask<Possible<ModuleDefinition>> TryGetModuleDefinitionAsync(ModuleDescriptor moduleDescriptor)
        {
            // TODO: Maybe we don't need to wait on the graph
            Possible<ModuleDefinition> result = (await m_graph.Value).Then<ModuleDefinition>(
                parsedResult =>
                {
                    // There is a single module, so we check against that
                    if (parsedResult.ModuleDefinition.Descriptor != moduleDescriptor)
                    {
                        return new ModuleNotOwnedByThisResolver(moduleDescriptor);
                    }

                    return parsedResult.ModuleDefinition;
                });

            return result;
        }


        /// <summary>
        /// The result of computing the build graph
        /// </summary>
        public Possible<NinjaGraphWithModuleDefinition> ComputedGraph
        {
            get { 
                Contract.Assert(m_graph != null && m_graph.IsValueCreated, "The computation of the build graph should have been triggered to be able to retrieve this value");
                return m_graph.Value.Result;
            }
        }

        /// <inheritdoc/>
        [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic")]
        public async ValueTask<Possible<IReadOnlyCollection<ModuleDescriptor>>> TryGetModuleDescriptorsAsync(ModuleReferenceWithProvenance moduleReference)
        {
            Possible<IReadOnlyCollection<ModuleDescriptor>> result = (await m_graph.Value).Then(
                parsedResult =>
                    (IReadOnlyCollection<ModuleDescriptor>)(
                        parsedResult.ModuleDefinition.Descriptor.Name == moduleReference.Name ?
                            new[] { parsedResult.ModuleDefinition.Descriptor }
                            : CollectionUtilities.EmptyArray<ModuleDescriptor>()));

            return result;
        }

        /// <inheritdoc/>
        public async ValueTask<Possible<ModuleDescriptor>> TryGetOwningModuleDescriptorAsync(AbsolutePath specPath)
        {
            Possible<ModuleDescriptor> result = (await m_graph.Value).Then<ModuleDescriptor>(
                parsedResult =>
                {
                    if (!parsedResult.ModuleDefinition.Specs.Contains(specPath))
                    {
                        return new SpecNotOwnedByResolverFailure(specPath.ToString(m_context.PathTable));
                    }

                    return parsedResult.ModuleDefinition.Descriptor;
                });

            return result;
        }

        /// <inheritdoc/>
        public Task ReinitializeResolver() => Task.FromResult<object>(null);

        /// <inheritdoc />
        public ISourceFile[] GetAllModuleConfigurationFiles()
        {
            return CollectionUtilities.EmptyArray<ISourceFile>();
        }

        /// <inheritdoc />
        /// <summary>
        /// Initializes the workspace resolver
        /// </summary>
        public bool TryInitialize(
            FrontEndHost host,
            FrontEndContext context,
            IConfiguration configuration,
            IResolverSettings resolverSettings,
            QualifierId[] requestedQualifiers)
        {
            m_host = host;
            m_context = context;
            m_resolverSettings = resolverSettings as INinjaResolverSettings;
            Contract.Assert(m_resolverSettings != null);

            var relativePathToGraphConstructionTool = RelativePath.Create(context.StringTable, NinjaGraphBuilderRelativePath);
            m_pathToTool = configuration.Layout.BuildEngineDirectory.Combine(m_context.PathTable, relativePathToGraphConstructionTool);


            if (!m_resolverSettings.ProjectRoot.IsValid)
            {
                if (!m_resolverSettings.SpecFile.IsValid)
                {
                    Tracing.Logger.Log.InvalidResolverSettings(m_context.LoggingContext, m_resolverSettings.Location(m_context.PathTable), "Either a project root or a spec file location (or both) must be specified.");
                    return false;
                }

                ProjectRoot = m_resolverSettings.SpecFile.GetParent(m_context.PathTable);
                SpecFile = m_resolverSettings.SpecFile;
            }
            else
            {
                ProjectRoot = m_resolverSettings.ProjectRoot;
                SpecFile = m_resolverSettings.SpecFile;
                if (!m_resolverSettings.SpecFile.IsValid)
                {
                    SpecFile = ProjectRoot.Combine(m_context.PathTable, "build.ninja");
                }
            }

            string path;
            if (!Directory.Exists(path = ProjectRoot.ToString(m_context.PathTable)))
            {
                Tracing.Logger.Log.ProjectRootDirectoryDoesNotExist(m_context.LoggingContext, m_resolverSettings.Location(m_context.PathTable), path);
                return false;
            }

            if (!File.Exists(path = SpecFile.ToString(m_context.PathTable)))
            {
                Tracing.Logger.Log.NinjaSpecFileDoesNotExist(m_context.LoggingContext, m_resolverSettings.Location(m_context.PathTable), path);
                return false;
            }
    
            m_targets = m_resolverSettings.Targets != null ? m_resolverSettings.Targets.AsArray() : CollectionUtilities.EmptyArray<string>();
            return true;
        }

        private async Task<Possible<NinjaGraphWithModuleDefinition>> TryComputeBuildGraphAsync()
        {
            Possible<NinjaGraphResult> maybeGraph = await ComputeBuildGraphAsync();
            
            var result = maybeGraph.Result;
            var specFileConfig = SpecFile.ChangeExtension(m_context.PathTable, PathAtom.Create(m_context.StringTable, ".ninja.dsc"));   // It needs to be a .dsc for the parsing to work

            var moduleDescriptor = ModuleDescriptor.CreateWithUniqueId(m_resolverSettings.ModuleName, this);   
            var moduleDefinition = ModuleDefinition.CreateModuleDefinitionWithImplicitReferences(
                moduleDescriptor,
                ProjectRoot,
                m_resolverSettings.File,
                new List<AbsolutePath>() { specFileConfig } ,
                allowedModuleDependencies: null, // no module policies
                cyclicalFriendModules: null); // no whitelist of cycles

            return new NinjaGraphWithModuleDefinition(result.Graph, moduleDefinition);            
        }

        private async Task<Possible<NinjaGraphResult>> ComputeBuildGraphAsync()
        {
            AbsolutePath outputFile = SerializedGraphPath.Value;

            SandboxedProcessResult result = await RunNinjaGraphBuilderAsync(outputFile);

            string standardError = result.StandardError.CreateReader().ReadToEndAsync().GetAwaiter().GetResult();
            if (result.ExitCode != 0)
            {
                if (!m_context.CancellationToken.IsCancellationRequested)
                {

                    Tracing.Logger.Log.GraphConstructionInternalError(
                        m_context.LoggingContext,
                        m_resolverSettings.Location(m_context.PathTable),
                        standardError);
                }

                return new NinjaGraphConstructionFailure(m_resolverSettings.ModuleName, ProjectRoot.ToString(m_context.PathTable));
            }

            // If the tool exited gracefully, but standard error is not empty, that is interpreted as a warning
            if (!string.IsNullOrEmpty(standardError))
            {
                Tracing.Logger.Log.GraphConstructionFinishedSuccessfullyButWithWarnings(
                    m_context.LoggingContext,
                    m_resolverSettings.Location(m_context.PathTable),
                    standardError);
            }
            
            FrontEndUtilities.TrackToolFileAccesses(m_host.Engine, m_context, NinjaFrontEnd.Name, result.AllUnexpectedFileAccesses, outputFile.GetParent(m_context.PathTable));
            var serializer = JsonSerializer.Create(GraphSerializationSettings.Settings);
            
            // Add custom deserializer for converting string arrays to AbsolutePath ReadOnlySets
            serializer.Converters.Add(new RootAwareAbsolutePathConverter(m_context.PathTable, SpecFile.GetParent(m_context.PathTable)));
            serializer.Converters.Add(new ToReadOnlySetJsonConverter<AbsolutePath>());
            
            var outputFileString = outputFile.ToString(m_context.PathTable);
            Tracing.Logger.Log.LeftGraphToolOutputAt(m_context.LoggingContext, m_resolverSettings.Location(m_context.PathTable), outputFileString);

            NinjaGraphResult projectGraphWithPredictionResult;
            using (var sr = new StreamReader(outputFileString))
            using (var reader = new JsonTextReader(sr))
            {
                projectGraphWithPredictionResult = serializer.Deserialize<NinjaGraphResult>(reader);
            }

            return projectGraphWithPredictionResult;
        }

        private Task<SandboxedProcessResult> RunNinjaGraphBuilderAsync(AbsolutePath outputFile)
        {
            AbsolutePath outputDirectory = outputFile.GetParent(m_context.PathTable);
            FileUtilities.CreateDirectory(outputDirectory.ToString(m_context.PathTable)); // Ensure it exists

            AbsolutePath argumentsFile = outputDirectory.Combine(m_context.PathTable, Guid.NewGuid().ToString());
            SerializeToolArguments(outputFile, argumentsFile);

            // After running the tool we'd like to remove some files 
            void CleanUpOnResult()
            {
                try
                {
                    var shouldKeepArgs = m_resolverSettings.KeepToolFiles ?? false;
                    if (!shouldKeepArgs)
                    {
                        FileUtilities.DeleteFile(argumentsFile.ToString(m_context.PathTable));
                    }
                }
                catch (BuildXLException e)
                {
                    Tracing.Logger.Log.CouldNotDeleteToolArgumentsFile(
                        m_context.LoggingContext,
                        m_resolverSettings.Location(m_context.PathTable),
                        argumentsFile.ToString(m_context.PathTable),
                        e.Message);
                }
            }

            return FrontEndUtilities.RunSandboxedToolAsync(
                m_context,
                m_pathToTool.ToString(m_context.PathTable),
                buildStorageDirectory: outputDirectory.ToString(m_context.PathTable),
                fileAccessManifest: GenerateFileAccessManifest(m_pathToTool.GetParent(m_context.PathTable), outputFile),
                arguments: I($@"""{argumentsFile.ToString(m_context.PathTable)}"""),
                workingDirectory: SpecFile.GetParent(m_context.PathTable).ToString(m_context.PathTable),
                description: "Ninja graph builder",
                BuildParameters.GetFactory().PopulateFromEnvironment(),
                onResult: CleanUpOnResult);
        }

        private void SerializeToolArguments(AbsolutePath outputFile, AbsolutePath argumentsFile)
        {
            var arguments = new NinjaGraphToolArguments()
                            {
                                BuildFileName = SpecFile.ToString(m_context.PathTable),
                                ProjectRoot = SpecFile.GetParent(m_context.PathTable).ToString(m_context.PathTable),
                                OutputFile = outputFile.ToString(m_context.PathTable),
                                Targets = m_targets
                            };


            var serializer = JsonSerializer.Create(GraphSerializationSettings.Settings);
            using (var sw = new StreamWriter(argumentsFile.ToString(m_context.PathTable)))
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
            AbsolutePath outputDirectory = m_host.GetFolderForFrontEnd(NinjaFrontEnd.Name);
            var now = DateTime.UtcNow.ToString("yyyy-MM-dd-THH-mm-ss.SSS-Z");
            var uniqueName = $"ninja_graph_{now}.json";
            return outputDirectory.Combine(m_context.PathTable, uniqueName);
        }

        private FileAccessManifest GenerateFileAccessManifest(AbsolutePath toolDirectory, AbsolutePath outputFile)
        {
            // We make no attempt at understanding what the graph generation process is going to do
            // We just configure the manifest to not fail on unexpected accesses, so they can be collected
            // later if needed
            var fileAccessManifest = new FileAccessManifest(m_context.PathTable)
            {
                FailUnexpectedFileAccesses = false,
                ReportFileAccesses = true,
                MonitorNtCreateFile = true,
                MonitorZwCreateOpenQueryFile = true,
                MonitorChildProcesses = true,
            };

            fileAccessManifest.AddScope(
                AbsolutePath.Create(m_context.PathTable, SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.Windows)),
                FileAccessPolicy.MaskAll,
                FileAccessPolicy.AllowAllButSymlinkCreation);

            fileAccessManifest.AddScope(
                AbsolutePath.Create(m_context.PathTable, SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.InternetCache)),
                FileAccessPolicy.MaskAll,
                FileAccessPolicy.AllowAllButSymlinkCreation);

            fileAccessManifest.AddScope(
                AbsolutePath.Create(m_context.PathTable, SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.History)),
                FileAccessPolicy.MaskAll,
                FileAccessPolicy.AllowAll);

            fileAccessManifest.AddScope(toolDirectory, FileAccessPolicy.MaskAll, FileAccessPolicy.AllowReadAlways);            
            return fileAccessManifest;
        }

        private sealed class NinjaGraphBuildStorage : ISandboxedProcessFileStorage
        {
            private readonly string m_directory;

            /// <nodoc />
            public NinjaGraphBuildStorage(string directory)
            {
                m_directory = directory;
            }

            /// <inheritdoc />
            public string GetFileName(SandboxedProcessFile file)
            {
                return Path.Combine(m_directory, file.DefaultFileName());
            }
        }
    }
}
