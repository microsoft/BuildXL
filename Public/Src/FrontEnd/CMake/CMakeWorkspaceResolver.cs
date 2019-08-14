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
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Utilities;
using BuildXL.FrontEnd.Workspaces;
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
using TypeScript.Net.Types;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.CMake
{
    /// <summary>
    /// Workspace resolver using a custom JSON generator from CMake specs
    /// </summary>
    public class CMakeWorkspaceResolver : IWorkspaceModuleResolver
    {
        internal const string CMakeResolverName = "CMake";

        /// <inheritdoc />
        public string Name { get; }

        private FrontEndContext m_context;
        private FrontEndHost m_host;
        private IConfiguration m_configuration;

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
        private const string CMakeRunnerRelativePath = @"tools\CMakeNinja\CMakeRunner.exe";
        private AbsolutePath m_pathToTool;

        internal readonly NinjaWorkspaceResolver EmbeddedNinjaWorkspaceResolver;
        private readonly Lazy<NinjaResolverSettings> m_embeddedResolverSettings;
        private bool m_ninjaWorkspaceResolverInitialized;

        internal Possible<NinjaGraphWithModuleDefinition> ComputedGraph => EmbeddedNinjaWorkspaceResolver.ComputedGraph;

        /// <inheritdoc/>
        public CMakeWorkspaceResolver()
        {
            Name = nameof(CMakeWorkspaceResolver);
            EmbeddedNinjaWorkspaceResolver = new NinjaWorkspaceResolver();
            m_embeddedResolverSettings = new Lazy<NinjaResolverSettings>(CreateEmbeddedResolverSettings);
        }

        /// <inheritdoc cref="Script.DScriptInterpreterBase" />
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
                m_host,
                m_context,
                m_configuration,
                m_embeddedResolverSettings.Value,
                m_requestedQualifiers));
        }

        private async Task<Possible<Unit>> GenerateBuildDirectoryAsync()
        {
            Contract.Assert(m_buildDirectory.IsValid);
            AbsolutePath outputDirectory = m_host.GetFolderForFrontEnd(CMakeFrontEnd.Name);
            AbsolutePath argumentsFile = outputDirectory.Combine(m_context.PathTable, Guid.NewGuid().ToString());
            if (!TryRetrieveCMakeSearchLocations(out IEnumerable<AbsolutePath> searchLocations))
            {
                return new CMakeGenerationError(m_resolverSettings.ModuleName, m_buildDirectory.ToString(m_context.PathTable));
            }

            SandboxedProcessResult result = await ExecuteCMakeRunner(argumentsFile, searchLocations);

            string standardError = result.StandardError.CreateReader().ReadToEndAsync().GetAwaiter().GetResult();
            if (result.ExitCode != 0)
            {
                if (!m_context.CancellationToken.IsCancellationRequested)
                {

                    Tracing.Logger.Log.CMakeRunnerInternalError(
                        m_context.LoggingContext,
                        m_resolverSettings.Location(m_context.PathTable),
                        standardError);
                }

                return new CMakeGenerationError(m_resolverSettings.ModuleName, m_buildDirectory.ToString(m_context.PathTable));
            }

            FrontEndUtilities.TrackToolFileAccesses(m_host.Engine, m_context, CMakeFrontEnd.Name, result.AllUnexpectedFileAccesses, outputDirectory);
            return Possible.Create(Unit.Void);
        }

        private Task<SandboxedProcessResult> ExecuteCMakeRunner(AbsolutePath argumentsFile, IEnumerable<AbsolutePath> searchLocations)
        {
            string rootString = ProjectRoot.ToString(m_context.PathTable);

            AbsolutePath outputDirectory = argumentsFile.GetParent(m_context.PathTable);
            FileUtilities.CreateDirectory(outputDirectory.ToString(m_context.PathTable)); // Ensure it exists
            SerializeToolArguments(argumentsFile, searchLocations);

            void CleanUpOnResult()
            {
                try
                {
                    FileUtilities.DeleteFile(argumentsFile.ToString(m_context.PathTable));
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

            var environment = FrontEndUtilities.GetEngineEnvironment(m_host.Engine, CMakeFrontEnd.Name);

            // TODO: This manual configuration is temporary. Remove after the cloud builders have the correct configuration
            var pathToManuallyDroppedTools = m_configuration.Layout.BuildEngineDirectory.Combine(m_context.PathTable, RelativePath.Create(m_context.StringTable, @"tools\CmakeNinjaPipEnvironment"));
            if (FileUtilities.Exists(pathToManuallyDroppedTools.ToString(m_context.PathTable)))
            {
                environment = SpecialCloudConfiguration.OverrideEnvironmentForCloud(environment, pathToManuallyDroppedTools, m_context);
            }

            var buildParameters = BuildParameters.GetFactory().PopulateFromDictionary(new ReadOnlyDictionary<string, string>(environment));

            return FrontEndUtilities.RunSandboxedToolAsync(
                m_context,
                m_pathToTool.ToString(m_context.PathTable),
                buildStorageDirectory: outputDirectory.ToString(m_context.PathTable),
                fileAccessManifest: GenerateFileAccessManifest(m_pathToTool.GetParent(m_context.PathTable)),
                arguments: I($@"""{argumentsFile.ToString(m_context.PathTable)}"""),
                workingDirectory: rootString,
                description: "CMakeRunner",
                buildParameters,
                onResult: CleanUpOnResult);
        }

        private void SerializeToolArguments(in AbsolutePath argumentsFile, IEnumerable<AbsolutePath> searchLocations)
        {
            var arguments = new CMakeRunnerArguments()
            {
                ProjectRoot = ProjectRoot.ToString(m_context.PathTable),
                BuildDirectory = m_buildDirectory.ToString(m_context.PathTable),
                CMakeSearchLocations = searchLocations.Select(l => l.ToString(m_context.PathTable)),
                CacheEntries = m_resolverSettings.CacheEntries
                // TODO: Output file
            };


            var serializer = JsonSerializer.Create(new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Include,
            });

            using (var sw = new StreamWriter(argumentsFile.ToString(m_context.PathTable)))
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
            var pathTable = m_context.PathTable;
            IReadOnlyList<AbsolutePath> cmakeSearchLocations = m_resolverSettings.CMakeSearchLocations?.SelectList(directoryLocation => directoryLocation.Path);
            var pathToManuallyDroppedTools = m_configuration.Layout.BuildEngineDirectory.Combine(pathTable, RelativePath.Create(m_context.StringTable, @"tools\CmakeNinjaPipEnvironment"));
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
                m_context,
                m_host.Engine,
                cmakeSearchLocations,
                out searchLocations,
                () => Tracing.Logger.Log.NoSearchLocationsSpecified(m_context.LoggingContext, m_resolverSettings.Location(m_context.PathTable)),
                paths => Tracing.Logger.Log.CannotParseBuildParameterPath(m_context.LoggingContext, m_resolverSettings.Location(m_context.PathTable), paths)
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

        /// <inheritdoc />
        public bool TryInitialize([NotNull] FrontEndHost host, [NotNull] FrontEndContext context, [NotNull] IConfiguration configuration, [NotNull] IResolverSettings resolverSettings, [NotNull] QualifierId[] requestedQualifiers)
        {
            m_host = host;
            m_context = context;
            m_configuration = configuration;
            m_resolverSettings = resolverSettings as ICMakeResolverSettings;

            Contract.Assert(m_resolverSettings != null);

            var relativePathToCMakeRunner = RelativePath.Create(context.StringTable, CMakeRunnerRelativePath);
            m_pathToTool = configuration.Layout.BuildEngineDirectory.Combine(m_context.PathTable, relativePathToCMakeRunner);

            m_buildDirectory = m_configuration.Layout.OutputDirectory.Combine(m_context.PathTable, m_resolverSettings.BuildDirectory);
            m_requestedQualifiers = requestedQualifiers;

            return true;
        }


        private FileAccessManifest GenerateFileAccessManifest(AbsolutePath toolDirectory)
        {
            // Get base FileAccessManifest
            var fileAccessManifest = FrontEndUtilities.GenerateToolFileAccessManifest(m_context, toolDirectory);

            fileAccessManifest.AddScope(
                AbsolutePath.Create(m_context.PathTable, SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.UserProfile)),
                FileAccessPolicy.MaskAll,
                FileAccessPolicy.AllowAllButSymlinkCreation);

            fileAccessManifest.AddScope(
                m_resolverSettings.ProjectRoot.Combine(m_context.PathTable, ".git"),
                FileAccessPolicy.MaskAll,
                FileAccessPolicy.AllowAllButSymlinkCreation);
            
            return fileAccessManifest;
        }
    }
}
