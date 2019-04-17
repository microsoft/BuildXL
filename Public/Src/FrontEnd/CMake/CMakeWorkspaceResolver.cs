// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.FrontEnd.CMake.Failures;
using BuildXL.FrontEnd.CMake.Serialization;
using BuildXL.FrontEnd.Ninja;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.Workspaces;
using BuildXL.FrontEnd.Utilities;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Native.IO;
using BuildXL.Processes;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Tasks;
using JetBrains.Annotations;
using Newtonsoft.Json;
using TypeScript.Net.DScript;
using static BuildXL.Utilities.FormattableStringEx;
using ISourceFile = TypeScript.Net.Types.ISourceFile;
using SourceFile = TypeScript.Net.Types.SourceFile;

namespace BuildXL.FrontEnd.CMake
{
    /// <summary>
    /// Workspace resolver using a custom JSON generator from CMake specs
    /// </summary>
    public class CMakeWorkspaceResolver : DScriptInterpreterBase, IDScriptWorkspaceModuleResolver, Workspaces.Core.IWorkspaceModuleResolver
    {
        internal const string CMakeResolverName = "CMake";

        private ICMakeResolverSettings m_resolverSettings;

        private AbsolutePath ProjectRoot => m_resolverSettings.ProjectRoot;
        private AbsolutePath m_buildDirectory;
        private QualifierId[] m_requestedQualifiers;
        private const string DefaultBuildTarget = "all";

        // AsyncLazy graph
        private readonly ConcurrentDictionary<AbsolutePath, SourceFile> m_createdSourceFiles =
            new ConcurrentDictionary<AbsolutePath, SourceFile>();

        /// <summary>
        /// Keep in sync with the BuildXL deployment spec that places the tool (\Public\Src\Deployment\buildXL.dsc)
        /// </summary>
        private const string CMakeRunnerRelativePath = @"tools\CMakeRunner\CMakeRunner.exe";
        private readonly RelativePath m_relativePathToCMakeRunner;

        internal readonly NinjaWorkspaceResolver EmbeddedNinjaWorkspaceResolver;
        private readonly Lazy<NinjaResolverSettings> m_embeddedResolverSettings;
        private bool m_ninjaWorkspaceResolverInitialized;

        internal Possible<NinjaGraphWithModuleDefinition> ComputedGraph => EmbeddedNinjaWorkspaceResolver.ComputedGraph;

        /// <inheritdoc/>
        public CMakeWorkspaceResolver(
            StringTable stringTable,
            GlobalConstants constants,
            ModuleRegistry sharedModuleRegistry,
            IFrontEndStatistics statistics)
            : base(constants, sharedModuleRegistry, statistics, logger: null)
        {
            Name = nameof(CMakeWorkspaceResolver);
            m_relativePathToCMakeRunner = RelativePath.Create(stringTable, CMakeRunnerRelativePath);
            EmbeddedNinjaWorkspaceResolver = new NinjaWorkspaceResolver(stringTable, constants, sharedModuleRegistry, statistics);
            m_embeddedResolverSettings = new Lazy<NinjaResolverSettings>(CreateEmbeddedResolverSettings);
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
        public virtual string Kind => KnownResolverKind.CMakeResolverKind;

        /// <inheritdoc/>
        public async ValueTask<Possible<HashSet<ModuleDescriptor>>> GetAllKnownModuleDescriptorsAsync()
        {
            Contract.Assert(EmbeddedNinjaWorkspaceResolver != null);

            Possible<Unit> maybeSuccess = await GenerateBuildDirectoryAsync();
            if (!maybeSuccess.Succeeded)
            {
                return maybeSuccess.Failure;
            }


            if (!TryInitializeNinjaWorkspaceResolverIfNeeded())
            {
                return new NinjaWorkspaceResolverInitializationFailure();
            }

            var maybeResult = await EmbeddedNinjaWorkspaceResolver.GetAllKnownModuleDescriptorsAsync();
            if (!maybeResult.Succeeded)
            {
                return new InnerNinjaFailure(maybeResult.Failure);
            }

            return maybeResult;
        }

        private bool TryInitializeNinjaWorkspaceResolverIfNeeded()
        {
            if (m_ninjaWorkspaceResolverInitialized)
            {
                return true;
            }

            return (m_ninjaWorkspaceResolverInitialized = EmbeddedNinjaWorkspaceResolver.TryInitialize(
                FrontEndHost,
                Context,
                Configuration,
                m_embeddedResolverSettings.Value,
                m_requestedQualifiers));
        }

        private async Task<Possible<Unit>> GenerateBuildDirectoryAsync()
        {
            Contract.Assert(m_buildDirectory.IsValid);
            AbsolutePath outputDirectory = FrontEndHost.GetFolderForFrontEnd(CMakeFrontEnd.Name);
            AbsolutePath argumentsFile = outputDirectory.Combine(Context.PathTable, Guid.NewGuid().ToString());
            if (!TryRetrieveCMakeSearchLocations(out IEnumerable<AbsolutePath> searchLocations))
            {
                return new CMakeGenerationError(m_resolverSettings.ModuleName, m_buildDirectory.ToString(Context.PathTable));
            }

            SandboxedProcessResult result = await ExecuteCMakeRunner(argumentsFile, searchLocations);

            string standardError = result.StandardError.CreateReader().ReadToEndAsync().GetAwaiter().GetResult();
            if (result.ExitCode != 0)
            {
                if (!Context.CancellationToken.IsCancellationRequested)
                {

                    Tracing.Logger.Log.CMakeRunnerInternalError(
                        Context.LoggingContext,
                        m_resolverSettings.Location(Context.PathTable),
                        standardError);
                }

                return new CMakeGenerationError(m_resolverSettings.ModuleName, m_buildDirectory.ToString(Context.PathTable));
            }

            FrontEndUtilities.TrackToolFileAccesses(Engine, Context, CMakeFrontEnd.Name, result.AllUnexpectedFileAccesses, outputDirectory);
            return Possible.Create(Unit.Void);
        }

        private Task<SandboxedProcessResult> ExecuteCMakeRunner(AbsolutePath argumentsFile, IEnumerable<AbsolutePath> searchLocations)
        {
            AbsolutePath pathToTool = Configuration.Layout.BuildEngineDirectory.Combine(Context.PathTable, m_relativePathToCMakeRunner);
            string rootString = ProjectRoot.ToString(Context.PathTable);

            AbsolutePath outputDirectory = argumentsFile.GetParent(Context.PathTable);
            FileUtilities.CreateDirectory(outputDirectory.ToString(Context.PathTable)); // Ensure it exists
            SerializeToolArguments(argumentsFile, searchLocations);

            void CleanUpOnResult()
            {
                try
                {
                    FileUtilities.DeleteFile(argumentsFile.ToString(Context.PathTable));
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

            var environment = FrontEndUtilities.GetEngineEnvironment(Engine, CMakeFrontEnd.Name);

            // TODO: This manual configuration is temporary. Remove after the cloud builders have the correct configuration
            var pathToManuallyDroppedTools = Configuration.Layout.BuildEngineDirectory.Combine(Context.PathTable, RelativePath.Create(Context.StringTable, @"tools\CmakeNinjaPipEnvironment"));
            if (FileUtilities.Exists(pathToManuallyDroppedTools.ToString(Context.PathTable)))
            {
                environment = SpecialCloudConfiguration.OverrideEnvironmentForCloud(environment, pathToManuallyDroppedTools, Context);
            }

            var buildParameters = BuildParameters.GetFactory().PopulateFromDictionary(new ReadOnlyDictionary<string, string>(environment));

            return FrontEndUtilities.RunSandboxedToolAsync(
                Context,
                pathToTool.ToString(Context.PathTable),
                buildStorageDirectory: outputDirectory.ToString(Context.PathTable),
                fileAccessManifest: GenerateFileAccessManifest(pathToTool.GetParent(Context.PathTable)),
                arguments: I($@"""{argumentsFile.ToString(Context.PathTable)}"""),
                workingDirectory: rootString,
                description: "CMakeRunner",
                buildParameters,
                onResult: CleanUpOnResult);
        }

        private void SerializeToolArguments(in AbsolutePath argumentsFile, IEnumerable<AbsolutePath> searchLocations)
        {
            var arguments = new CMakeRunnerArguments()
            {
                ProjectRoot = ProjectRoot.ToString(Context.PathTable),
                BuildDirectory = m_buildDirectory.ToString(Context.PathTable),
                CMakeSearchLocations = searchLocations.Select(l => l.ToString(Context.PathTable)),
                CacheEntries = m_resolverSettings.CacheEntries
                // TODO: Output file
            };


            var serializer = JsonSerializer.Create(new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Include,
            });

            using (var sw = new StreamWriter(argumentsFile.ToString(Context.PathTable)))
            using (var writer = new JsonTextWriter(sw))
            {
                serializer.Serialize(writer, arguments);
            }
        }


        /// <summary>
        /// Retrieves a list of search locations for CMake.exe using the resolver settings or, the PATH
        /// </summary>
        private bool TryRetrieveCMakeSearchLocations(out IEnumerable<AbsolutePath> searchLocations)
        {
            // TODO: This manual configuration is temporary. Remove after the cloud builders have the correct configuration
            var pathTable = Context.PathTable;
            IReadOnlyList<AbsolutePath> cmakeSearchLocations = m_resolverSettings.CMakeSearchLocations?.SelectList(directoryLocation => directoryLocation.Path);
            var pathToManuallyDroppedTools = Configuration.Layout.BuildEngineDirectory.Combine(pathTable, RelativePath.Create(Context.StringTable, @"tools\CmakeNinjaPipEnvironment"));
            if (FileUtilities.Exists(pathToManuallyDroppedTools.ToString(pathTable)))
            {
                var cloudCmakeSearchLocations = new[] { pathToManuallyDroppedTools.Combine(pathTable, "cmake").Combine(pathTable, "bin") };
                if (cmakeSearchLocations == null)
                {
                    cmakeSearchLocations = cloudCmakeSearchLocations;
                }
                else
                {
                    cmakeSearchLocations = cmakeSearchLocations.Union(cloudCmakeSearchLocations).ToList();
                }
            }


            return FrontEndUtilities.TryRetrieveExecutableSearchLocations(
                CMakeFrontEnd.Name,
                Context,
                Engine,
                cmakeSearchLocations,
                out searchLocations,
                () => Tracing.Logger.Log.NoSearchLocationsSpecified(Context.LoggingContext, m_resolverSettings.Location(Context.PathTable)),
                paths => Tracing.Logger.Log.CannotParseBuildParameterPath(Context.LoggingContext, m_resolverSettings.Location(Context.PathTable), paths)
            );
        }

        private NinjaResolverSettings CreateEmbeddedResolverSettings()
        {
            Contract.Assert(m_resolverSettings != null);
            Contract.Assert(m_buildDirectory.IsValid);
            var settings = new NinjaResolverSettings
            {
                ModuleName = m_resolverSettings.ModuleName,
                ProjectRoot = m_buildDirectory,

                // The "file in which this resolver was configured"
                File = m_resolverSettings.File,

                // TODO: Different targets
                Targets = new[] { DefaultBuildTarget }
            };

            return settings;
        }

        /// <inheritdoc/>
        public ValueTask<Possible<ModuleDefinition>> TryGetModuleDefinitionAsync(ModuleDescriptor moduleDescriptor)
        {
            return EmbeddedNinjaWorkspaceResolver.TryGetModuleDefinitionAsync(moduleDescriptor);
        }


        /// <inheritdoc/>
        [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic")]
        public ValueTask<Possible<IReadOnlyCollection<ModuleDescriptor>>> TryGetModuleDescriptorsAsync(ModuleReferenceWithProvenance moduleReference)
        {
            return EmbeddedNinjaWorkspaceResolver.TryGetModuleDescriptorsAsync(moduleReference);
        }

        /// <inheritdoc/>
        public ValueTask<Possible<ModuleDescriptor>> TryGetOwningModuleDescriptorAsync(AbsolutePath specPath)
        {
            return EmbeddedNinjaWorkspaceResolver.TryGetOwningModuleDescriptorAsync(specPath);
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
            m_resolverSettings = resolverSettings as ICMakeResolverSettings;
            Contract.Assert(m_resolverSettings != null);
            m_buildDirectory = Configuration.Layout.OutputDirectory.Combine(Context.PathTable, m_resolverSettings.BuildDirectory);
            m_requestedQualifiers = requestedQualifiers;

            return true;
        }


        private FileAccessManifest GenerateFileAccessManifest(AbsolutePath toolDirectory)
        {
            // Get base FileAccessManifest
            var fileAccessManifest = FrontEndUtilities.GenerateToolFileAccessManifest(Context, toolDirectory);

            fileAccessManifest.AddScope(
                AbsolutePath.Create(Context.PathTable, SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.UserProfile)),
                FileAccessPolicy.MaskAll,
                FileAccessPolicy.AllowAllButSymlinkCreation);

            fileAccessManifest.AddScope(
                m_resolverSettings.ProjectRoot.Combine(Context.PathTable, ".git"),
                FileAccessPolicy.MaskAll,
                FileAccessPolicy.AllowAllButSymlinkCreation);
            
            return fileAccessManifest;
        }
    }
}
