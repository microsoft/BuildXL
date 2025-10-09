﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Workspaces;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Processes;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using Newtonsoft.Json;
using TypeScript.Net.DScript;
using TypeScript.Net.Types;
using static BuildXL.Utilities.Core.FormattableStringEx;

namespace BuildXL.FrontEnd.Utilities.GenericProjectGraphResolver
{
    /// <summary>
    /// Base class for a workspace resolver that computes an environment-dependent project graph by exploring the file system from a given root
    /// </summary>
    public abstract class ProjectGraphWorkspaceResolverBase<TProjectGraphResult, TResolverSettings> : IWorkspaceModuleResolver 
        where TResolverSettings: class, IProjectGraphResolverSettings
        where TProjectGraphResult: ISingleModuleProjectGraphResult
    {
        /// <inheritdoc />
        public string Name { get; protected set; }

        /// <nodoc/>
        protected FrontEndContext Context;
        /// <nodoc/>
        protected FrontEndHost Host;
        /// <nodoc/>
        protected IConfiguration Configuration;
        /// <nodoc/>
        protected TResolverSettings ResolverSettings;

        // path-to-source-file to source file. Parsing requests may happen concurrently.
        private readonly ConcurrentDictionary<AbsolutePath, SourceFile> m_createdSourceFiles = new();

        private Possible<TProjectGraphResult>? m_projectGraph;

        private ICollection<string> m_passthroughEnvironmentVariables;

        private IDictionary<string, string> m_trackedEnvironmentVariables;

        private bool m_processEnvironmentUsed;

        /// <inheritdoc/>
        public virtual string Kind { get; }

        /// <summary>
        /// Collection of environment variables that are allowed to the graph construction process to see (in addition to the ones specified by the user)
        /// </summary>
        private static readonly string[] s_environmentVariableAllowlist = new[]
            {
                "ComSpec",
                "PATH",
                "PATHEXT",
                "NUMBER_OF_PROCESSORS",
                "OS",
                "PROCESSOR_ARCHITECTURE",
                "PROCESSOR_IDENTIFIER",
                "PROCESSOR_LEVEL",
                "PROCESSOR_REVISION",
                "SystemDrive",
                "SystemRoot",
                "SYSTEMTYPE"
            };

        /// <summary>
        /// The result of computing the build graph
        /// </summary>
        public Possible<TProjectGraphResult> ComputedProjectGraph
        {
            get
            {
                Contract.Assert(m_projectGraph.HasValue, "The computation of the build graph should have been triggered to be able to retrieve this value");
                return m_projectGraph.Value;
            }
        }

        /// <summary>
        /// Environment variables that are tracked by BuildXL and are exposed to the graph construction process and pip execution.
        /// </summary>
        /// <remarks>
        /// These environment variables can be defined by the user explicitly through the resolver configuration or can be populated from this process' environment variables.
        /// </remarks>
        public IEnumerable<KeyValuePair<string, string>> TrackedEnvironmentVariables
        {
            get
            {
                Contract.Assert(m_projectGraph.HasValue, "The computation of the build graph should have been triggered to be able to retrieve this value");
                return m_trackedEnvironmentVariables;
            }
        }

        /// <summary>
        /// Passthrough environment variables defined by the user that are exposed to the graph construction process and pip execution
        /// </summary>
        public IEnumerable<string> UserDefinedPassthroughEnvironmentVariables
        {
            get
            {
                Contract.Assert(m_projectGraph.HasValue, "The computation of the build graph should have been triggered to be able to retrieve this value");
                return m_passthroughEnvironmentVariables;
            }
        }

        /// <inheritdoc/>
        public Task<Possible<ISourceFile>> TryParseAsync(
            AbsolutePath pathToParse,
            AbsolutePath moduleOrConfigPathPromptingParse,
            ParsingOptions parsingOptions = null)
        {
            return Task.FromResult(Possible.Create((ISourceFile)GetOrCreateSourceFile(pathToParse)));
        }

        /// <inheritdoc/>
        public string DescribeExtent()
        {
            Possible<HashSet<ModuleDescriptor>> maybeModules = GetAllKnownModuleDescriptorsAsync().GetAwaiter().GetResult();
            return !maybeModules.Succeeded
                ? I($"Module extent could not be computed. {maybeModules.Failure.Describe()}")
                : string.Join(", ", maybeModules.Result.Select(module => module.Name));
        }

        /// <inheritdoc/>
        public virtual bool TryInitialize(
            FrontEndHost host,
            FrontEndContext context,
            IConfiguration configuration,
            IResolverSettings resolverSettings)
        {
            Host = host;
            Context = context;
            Configuration = configuration;

            ResolverSettings = resolverSettings as TResolverSettings;
            Contract.Assert(ResolverSettings != null);
            ResolverSettings.ComputeEnvironment(context.PathTable, out m_trackedEnvironmentVariables, out m_passthroughEnvironmentVariables, out m_processEnvironmentUsed);

            return true;
        }

        /// <inheritdoc/>
        public async ValueTask<Possible<HashSet<ModuleDescriptor>>> GetAllKnownModuleDescriptorsAsync()
        {
            var result = (await TryComputeBuildGraphIfNeededAsync())
                .Then(projectGraphResult => new HashSet<ModuleDescriptor> { projectGraphResult.ModuleDefinition.Descriptor });

            return result;
        }

        /// <inheritdoc/>
        public async ValueTask<Possible<ModuleDefinition>> TryGetModuleDefinitionAsync(ModuleDescriptor moduleDescriptor)
        {
            Possible<ModuleDefinition> result = (await TryComputeBuildGraphIfNeededAsync()).Then<ModuleDefinition>(
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

        /// <inheritdoc/>
        [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic")]
        public async ValueTask<Possible<IReadOnlyCollection<ModuleDescriptor>>> TryGetModuleDescriptorsAsync(ModuleReferenceWithProvenance moduleReference)
        {
            Possible<IReadOnlyCollection<ModuleDescriptor>> result = (await TryComputeBuildGraphIfNeededAsync()).Then(
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
            Possible<ModuleDescriptor> result = (await TryComputeBuildGraphIfNeededAsync()).Then<ModuleDescriptor>(
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
        public ISourceFile[] GetAllModuleConfigurationFiles() => CollectionUtilities.EmptyArray<ISourceFile>();

        private SourceFile GetOrCreateSourceFile(AbsolutePath path)
        {
            Contract.Assert(path.IsValid);

            if (m_createdSourceFiles.TryGetValue(path, out SourceFile sourceFile))
            {
                return sourceFile;
            }

            sourceFile = DoCreateSourceFile(path);

            m_createdSourceFiles.Add(path, sourceFile);

            return sourceFile;
        }

        /// <summary>
        /// Creates the source file that gets exposed to other resolvers
        /// </summary>
        protected abstract SourceFile DoCreateSourceFile(AbsolutePath path);

        private async Task<Possible<TProjectGraphResult>> TryComputeBuildGraphIfNeededAsync()
        {
            m_projectGraph ??= await TryComputeBuildGraphAsync();

            return m_projectGraph.Value;
        }

        /// <summary>
        /// Computes the project graph
        /// </summary>
        /// <returns></returns>
        protected abstract Task<Possible<TProjectGraphResult>> TryComputeBuildGraphAsync();

        /// <summary>
        /// Constructs the build parameters based on user defined variables and passthrough variables
        /// </summary>
        protected virtual BuildParameters.IBuildParameters RetrieveBuildParameters()
        {
            // The full environment is built with all user-defined env variables plus the passthrough variables with their current values
            var fullEnvironment = m_trackedEnvironmentVariables.Union(
                m_passthroughEnvironmentVariables.Select(variable =>
                    // Here we explicitly skip the front end engine for retrieving the passthrough values: we need these values for graph construction
                    // purposes but they shouldn't be tracked
                    new KeyValuePair<string, string>(variable, Environment.GetEnvironmentVariable(variable))));

            // User-configured environment
            var configuredEnvironment = BuildParameters.GetFactory().PopulateFromDictionary(fullEnvironment);

            // Combine the ones above with a set of OS-wide properties processes should see
            var buildParameters = BuildParameters
                .GetFactory()
                .PopulateFromEnvironment()
                .Select(s_environmentVariableAllowlist)
                .Override(configuredEnvironment.ToDictionary());

            return buildParameters;
        }

        /// <summary>
        /// Talks to the engine to register all accesses and build parameters
        /// </summary>
        protected void TrackFilesAndEnvironment(IEnumerable<ReportedFileAccess> fileAccesses, AbsolutePath frontEndFolder)
        {
            // Register all build parameters passed to the graph construction process if they were retrieved from the process environment
            // Otherwise, if build parameters were defined by the main config file, then there is nothing to register: if the definition
            // in the config file actually accessed the environment, that was already registered during config evaluation.
            // TODO: we actually need the build parameters *used* by the graph construction process, but for now this is a compromise to keep
            // graph caching sound. We need to modify this when MsBuild static graph API starts providing used env vars.
            if (m_processEnvironmentUsed)
            {
                foreach (string key in m_trackedEnvironmentVariables.Keys)
                {
                    Host.Engine.TryGetBuildParameter(key, Name, out _);
                }
            }

            FrontEndUtilities.TrackToolFileAccesses(Host.Engine, Context, Name, fileAccesses, frontEndFolder);
        }

        /// <summary>
        /// Creates a JSON serializer that can handle AbsolutePath and does profile redirection
        /// </summary>
        protected JsonSerializer ConstructProjectGraphSerializer(JsonSerializerSettings settings)
        {
            var serializer = JsonSerializer.Create(settings);

            // If the user profile has been redirected, we need to catch any path reported that falls under it
            // and relocate it to the redirected user profile.
            // This allows for cache hits across machines where the user profile is not uniformly located, and MSBuild
            // happens to read a spec under it (the typical case is a props/target file under the nuget cache)
            // Observe that the env variable UserProfile is already redirected in this case, and the engine abstraction exposes it.
            // However, tools like MSBuild very often manages to find the user profile by some other means
            AbsolutePathJsonConverter absolutePathConverter;
            if (Configuration.Layout.RedirectedUserProfileJunctionRoot.IsValid)
            {
                // Let's get the redirected and original user profile folder
                string redirectedUserProfile = SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string originalUserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                absolutePathConverter = new AbsolutePathJsonConverter(
                    Context.PathTable,
                    new[] {
                        (AbsolutePath.Create(Context.PathTable, originalUserProfile), AbsolutePath.Create(Context.PathTable, redirectedUserProfile))
                    });
            }
            else
            {
                absolutePathConverter = new AbsolutePathJsonConverter(Context.PathTable);
            }

            serializer.Converters.Add(absolutePathConverter);
            serializer.Converters.Add(new RelativePathJsonConverter(Context.StringTable));

            // Let's not add invalid absolute paths to any collection
            serializer.Converters.Add(ValidAbsolutePathEnumerationJsonConverter.Instance);

            return serializer;
        }
    }
}
