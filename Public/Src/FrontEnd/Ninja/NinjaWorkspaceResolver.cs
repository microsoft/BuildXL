// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using JetBrains.Annotations;
using BuildXL.FrontEnd.Ninja.Serialization;
using BuildXL.FrontEnd.Utilities;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Sdk.Workspaces;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Core;
using ISourceFile = TypeScript.Net.Types.ISourceFile;
using SourceFile = TypeScript.Net.Types.SourceFile;
using TypeScript.Net.DScript;
using BuildXL.Utilities.Collections;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Native.IO;
using BuildXL.Processes;
using static BuildXL.Utilities.FormattableStringEx;
using System.IO;
using BuildXL.Processes.Containers;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Ninja
{
    /// <summary>
    /// Workspace resolver using a custom JSON generator from ninja specs
    /// </summary>
    public class NinjaWorkspaceResolver : DScriptInterpreterBase, IDScriptWorkspaceModuleResolver, Workspaces.Core.IWorkspaceModuleResolver
    {
        internal const string NinjaResolverName = "Ninja";

        private INinjaResolverSettings m_resolverSettings;
        private readonly NinjaFrontEnd m_frontEnd;

        internal AbsolutePath ProjectRoot;
        internal AbsolutePath SpecFile;


        // The build targets this workspace resolver knows about after initialization
        // For now this means we will build all of these
        private string[] m_targets; 
 
        // AsyncLazy graph
        private Lazy<Task<Possible<NinjaGraphWithModuleDefinition>>> m_graph;
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
        private const string NinjaGraphBuilderRelativePath = @"tools\NinjaGraphBuilder\NinjaGraphBuilder.exe";
        private readonly RelativePath m_relativePathToGraphConstructionTool;

        /// <inheritdoc/>
        public NinjaWorkspaceResolver(
            GlobalConstants constants,
            ModuleRegistry sharedModuleRegistry,
            IFrontEndStatistics statistics,
            NinjaFrontEnd frontEnd)
            : base(constants, sharedModuleRegistry, statistics, logger: null)
        {
            Name = nameof(NinjaWorkspaceResolver);
            m_frontEnd = frontEnd;
            m_relativePathToGraphConstructionTool = RelativePath.Create(frontEnd.Context.StringTable, NinjaGraphBuilderRelativePath);
            m_graph = new Lazy<Task<Possible<NinjaGraphWithModuleDefinition>>>(TryComputeBuildGraphAsync);
            SerializedGraphPath = new Lazy<AbsolutePath>(GetToolOutputPath);
        }

        /// <inheritdoc cref="DScriptInterpreterBase" />
        public override Task<Possible<ISourceFile>> TryParseAsync(
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
            sourceFile = SourceFile.Create(path.ToString(Context.PathTable));

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
                        return new SpecNotOwnedByResolverFailure(specPath.ToString(Context.PathTable));
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

        /// <inheritdoc cref="IDScriptWorkspaceModuleResolver" />
        public bool TryInitialize([NotNull] FrontEndHost host, [NotNull] FrontEndContext context, [NotNull] IConfiguration configuration, [NotNull] IResolverSettings resolverSettings, [NotNull] QualifierId[] requestedQualifiers)
        {
            InitializeInterpreter(host, context, configuration);
            m_resolverSettings = resolverSettings as INinjaResolverSettings;
            
            Contract.Assert(m_resolverSettings != null);

            return TryInitializeWorkspaceValues();
        }


        private bool TryInitializeWorkspaceValues()
        {
            if (!m_resolverSettings.ProjectRoot.IsValid)
            {
                if (!m_resolverSettings.SpecFile.IsValid)
                {
                    Tracing.Logger.Log.InvalidResolverSettings(Context.LoggingContext, m_resolverSettings.Location(Context.PathTable), "Either a project root or a spec file location (or both) must be specified.");
                    return false;
                }

                ProjectRoot = m_resolverSettings.SpecFile.GetParent(Context.PathTable);
                SpecFile = m_resolverSettings.SpecFile;
            }
            else
            {
                ProjectRoot = m_resolverSettings.ProjectRoot;
                SpecFile = m_resolverSettings.SpecFile;
                if (!m_resolverSettings.SpecFile.IsValid)
                {
                    SpecFile = ProjectRoot.Combine(Context.PathTable, "build.ninja");
                }
            }

            string path;
            if (!Directory.Exists(path = ProjectRoot.ToString(Context.PathTable)))
            {
                Tracing.Logger.Log.ProjectRootDirectoryDoesNotExist(Context.LoggingContext, m_resolverSettings.Location(Context.PathTable), path);
                return false;
            }

            if (!File.Exists(path = SpecFile.ToString(Context.PathTable)))
            {
                Tracing.Logger.Log.NinjaSpecFileDoesNotExist(Context.LoggingContext, m_resolverSettings.Location(Context.PathTable), path);
                return false;
            }
    
            m_targets = m_resolverSettings.Targets != null ? m_resolverSettings.Targets.AsArray() : CollectionUtilities.EmptyArray<string>();
            return true;
        }

        private async Task<Possible<NinjaGraphWithModuleDefinition>> TryComputeBuildGraphAsync()
        {
            Possible<NinjaGraphResult> maybeGraph = await ComputeBuildGraphAsync();
            
            var result = maybeGraph.Result;
            var specFileConfig = SpecFile.ChangeExtension(Context.PathTable, PathAtom.Create(Context.StringTable, ".ninja.dsc"));   // It needs to be a .dsc for the parsing to work

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
                if (!Context.CancellationToken.IsCancellationRequested)
                {

                    Tracing.Logger.Log.GraphConstructionInternalError(
                        Context.LoggingContext,
                        m_resolverSettings.Location(Context.PathTable),
                        standardError);
                }

                return new NinjaGraphConstructionFailure(m_resolverSettings.ModuleName, ProjectRoot.ToString(Context.PathTable));
            }

            // If the tool exited gracefully, but standard error is not empty, that is interpreted as a warning
            if (!string.IsNullOrEmpty(standardError))
            {
                Tracing.Logger.Log.GraphConstructionFinishedSuccessfullyButWithWarnings(
                    Context.LoggingContext,
                    m_resolverSettings.Location(Context.PathTable),
                    standardError);
            }
            
            FrontEndUtilities.TrackToolFileAccesses(Engine, Context, m_frontEnd.Name, result.AllUnexpectedFileAccesses, outputFile.GetParent(Context.PathTable));
            var serializer = JsonSerializer.Create(GraphSerializationSettings.Settings);
            
            // Add custom deserializer for converting string arrays to AbsolutePath ReadOnlySets
            serializer.Converters.Add(new RootAwareAbsolutePathConverter(Context.PathTable, SpecFile.GetParent(Context.PathTable)));
            serializer.Converters.Add(new ToReadOnlySetJsonConverter<AbsolutePath>());
            
            var outputFileString = outputFile.ToString(Context.PathTable);
            Tracing.Logger.Log.LeftGraphToolOutputAt(Context.LoggingContext, m_resolverSettings.Location(Context.PathTable), outputFileString);

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
            AbsolutePath pathToTool = Configuration.Layout.BuildEngineDirectory.Combine(Context.PathTable, m_relativePathToGraphConstructionTool);
            string rootString = ProjectRoot.ToString(Context.PathTable);

            AbsolutePath outputDirectory = outputFile.GetParent(Context.PathTable);
            FileUtilities.CreateDirectory(outputDirectory.ToString(Context.PathTable)); // Ensure it exists

            AbsolutePath argumentsFile = outputDirectory.Combine(Context.PathTable, Guid.NewGuid().ToString());
            SerializeToolArguments(outputFile, argumentsFile);

            // After running the tool we'd like to remove some files 
            void CleanUpOnResult()
            {
                try
                {
                    var shouldKeepArgs = m_resolverSettings.KeepToolFiles ?? false;
                    if (!shouldKeepArgs)
                    {
                        FileUtilities.DeleteFile(argumentsFile.ToString(Context.PathTable));
                    }
                }
                catch (BuildXLException e)
                {
                    Tracing.Logger.Log.CouldNotDeleteToolArgumentsFile(
                        Context.LoggingContext,
                        m_resolverSettings.Location(Context.PathTable),
                        argumentsFile.ToString(Context.PathTable),
                        e.Message);
                }
            }

            return FrontEndUtilities.RunSandboxedToolAsync(
                Context,
                pathToTool.ToString(Context.PathTable),
                buildStorageDirectory: outputDirectory.ToString(Context.PathTable),
                fileAccessManifest: GenerateFileAccessManifest(pathToTool.GetParent(Context.PathTable), outputFile),
                arguments: I($@"""{argumentsFile.ToString(Context.PathTable)}"""),
                workingDirectory: SpecFile.GetParent(Context.PathTable).ToString(Context.PathTable),
                description: "Ninja graph builder",
                BuildParameters.GetFactory().PopulateFromEnvironment(),
                onResult: CleanUpOnResult);
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
            AbsolutePath outputDirectory = FrontEndHost.GetFolderForFrontEnd(m_frontEnd.Name);
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
