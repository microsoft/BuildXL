// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using JetBrains.Annotations;
using TypeScript.Net.Types;
using SymbolTable = BuildXL.Utilities.SymbolTable;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.FrontEnd.Workspaces.Core
{
    /// <nodoc />
    public interface IWorkspaceProvider
    {
        /// <summary>
        /// Returns configuration object for the workspace computation.
        /// </summary>
        WorkspaceConfiguration Configuration { get; }

        // Following methods are used only by the incremental mode.

        /// <summary>
        /// Creates workspace definition using all known modules.
        /// </summary>
        Task<Possible<WorkspaceDefinition>> GetWorkspaceDefinitionForAllResolversAsync();

        /// <summary>
        /// Creates workspace definition using all known modules from scratch.
        /// </summary>
        Task<Possible<WorkspaceDefinition>> RecomputeWorkspaceDefinitionForAllResolversAsync();

        /// <summary>
        /// Creates workspace using non-parsed workspace (i.e. using <see cref="WorkspaceDefinition"/>).
        /// </summary>
        Task<Workspace> CreateWorkspaceAsync(WorkspaceDefinition workspaceDefinition, bool userFilterWasApplied);

        /// <summary>
        /// Parses and binds a given list of specs.
        /// </summary>
        /// <remarks>
        /// This method is used only in the incremental mode.
        /// </remarks>
        Task<Possible<ISourceFile>[]> ParseAndBindSpecsAsync(SpecWithOwningModule[] specs);

        /// <summary>
        /// Creates a workspace using a given spec as the starting point.
        /// </summary>
        Task<Workspace> CreateWorkspaceFromSpecAsync(AbsolutePath pathToSpec);

        /// <summary>
        /// Creates a workspace using a module as the starting point
        /// </summary>
        Task<Workspace> CreateWorkspaceFromModuleAsync(ModuleDescriptor moduleDescriptor);

        /// <summary>
        /// Creates a workspace using all known modules (to this provider) as the starting point
        /// </summary>
        Task<Workspace> CreateWorkspaceFromAllKnownModulesAsync();

        /// <summary>
        /// Creates a workspace using all known modules (to this provider) as the starting point
        /// </summary>
        /// <remarks>
        /// The workspace is not computed from start but from a collection of already parsed modules.
        /// This is useful for IDE purposes
        /// </remarks>
        Task<Workspace> CreateIncrementalWorkspaceForAllKnownModulesAsync(
            IEnumerable<ParsedModule> parsedModules,
            ModuleUnderConstruction moduleUnderConstruction,
            IEnumerable<Failure> failures,
            ParsedModule preludeModule);

        /// <summary>
        /// Returns parsed module with all the specs for all configuration files in the build.
        /// </summary>
        ParsedModule GetConfigurationModule();
    }

    /// <summary>
    /// Coordinates resolvers to build a <see cref="Workspace"/> from a set of goals.
    /// </summary>
    public sealed class WorkspaceProvider : IWorkspaceProvider
    {
        /// <summary>
        /// Workspace that was computed during configuration processing.
        /// </summary>
        private Workspace m_mainConfigurationWorkspace;

        private IWorkspaceModuleResolver m_configurationResolver;

        private ParsedModule m_configurationModule;

        private readonly IModuleReferenceResolver m_moduleReferenceResolver;
        private readonly List<IWorkspaceModuleResolver> m_resolvers;

        /// <nodoc/>
        public WorkspaceConfiguration Configuration { get; }

        internal IWorkspaceStatistics Statistics { get; }

        internal SymbolTable SymbolTable { get; }

        internal PathTable PathTable { get; }

        /// <summary>
        /// Creates a new WorkspaceProvider using a <see cref="ModuleReferenceResolver"/> to identify DScript module references
        /// </summary>
        public WorkspaceProvider(
            IWorkspaceStatistics workspaceStatistics,
            List<IWorkspaceModuleResolver> resolvers,
            WorkspaceConfiguration configuration,
            PathTable pathTable,
            SymbolTable symbolTable)
            : this(workspaceStatistics, resolvers, new ModuleReferenceResolver(pathTable), configuration, pathTable, symbolTable)
        {
        }

        /// <nodoc/>
        public WorkspaceProvider(
            IWorkspaceStatistics workspaceStatistics,
            List<IWorkspaceModuleResolver> resolvers,
            IModuleReferenceResolver moduleReferenceResolver,
            WorkspaceConfiguration configuration,
            PathTable pathTable,
            SymbolTable symbolTable)
        {
            Contract.Requires(workspaceStatistics != null);
            Contract.Requires(configuration != null);
            Contract.Requires(moduleReferenceResolver != null);
            Contract.Requires(pathTable != null);

            Statistics = workspaceStatistics;
            m_moduleReferenceResolver = moduleReferenceResolver;
            PathTable = pathTable;
            Configuration = configuration;
            SymbolTable = symbolTable;
            m_resolvers = resolvers;
        }

        /// <nodoc/>
        public static bool TryCreate<T>(
            [CanBeNull]Workspace mainConfigurationWorkspace,
            IWorkspaceStatistics workspaceStatistics,
            IWorkspaceResolverFactory<T> workspaceResolverFactory,
            WorkspaceConfiguration configuration,
            PathTable pathTable,
            SymbolTable symbolTable,
            bool useDecorator,
            bool addBuiltInPreludeResolver,
            out IWorkspaceProvider workspaceProvider,
            out IEnumerable<Failure> failures)
        {
            // mainConfigurationWorkspace can be null for some tests
            var mainFile = mainConfigurationWorkspace != null ? 
                mainConfigurationWorkspace.ConfigurationModule.Definition.MainFile : 
                AbsolutePath.Invalid;

            if (!TryCreateResolvers(
                workspaceResolverFactory, 
                configuration,
                mainFile, 
                pathTable, 
                addBuiltInPreludeResolver,
                out var resolvers, 
                out failures))
            {
                workspaceProvider = default(IWorkspaceProvider);
                return false;
            }

            var provider = new WorkspaceProvider(workspaceStatistics, resolvers, configuration, pathTable, symbolTable);
            provider.m_mainConfigurationWorkspace = mainConfigurationWorkspace;

            workspaceProvider = useDecorator
                ? (IWorkspaceProvider)new WorkspaceProviderStatisticsDecorator(workspaceStatistics, provider)
                : provider;
            return true;
        }

        /// <inheritdoc />
        public async Task<Possible<WorkspaceDefinition>> GetWorkspaceDefinitionForAllResolversAsync()
        {
            var moduleDefinitions = await GetModuleDefinitionsForAllResolversAsync();
            return await moduleDefinitions.ThenAsync(
                async md =>
                {
                    var maybePrelude = await FindUniqueModuleDefinitionWithName(ModuleReferenceWithProvenance.FromName(Configuration.PreludeName));
                    return maybePrelude.Then(p => new WorkspaceDefinition(md, p));
                });
        }

        /// <inheritdoc />
        public async Task<Possible<WorkspaceDefinition>> RecomputeWorkspaceDefinitionForAllResolversAsync()
        {
            var reinitializationTasks = m_resolvers.Select(r => r.ReinitializeResolver()).ToArray();
            await Task.WhenAll(reinitializationTasks);
            return await GetWorkspaceDefinitionForAllResolversAsync();
        }

        /// <inheritdoc />
        public async Task<Workspace> CreateWorkspaceAsync(WorkspaceDefinition workspaceDefinition, bool userFilterWasApplied)
        {
            var result = await CreateWorkspaceFromModuleDefinitionsAsync(
                new HashSet<ModuleDefinition>(workspaceDefinition.Modules),
                computeBindingFingerprint: !userFilterWasApplied && Configuration.ConstructFingerprintDuringParsing);
            result.FilterWasApplied = userFilterWasApplied;

            return result;
        }

        /// <inheritdoc />
        public Task<Possible<ISourceFile>[]> ParseAndBindSpecsAsync(SpecWithOwningModule[] specs)
        {
            var queue = ModuleParsingQueue.CraeteFingerprintComputationQueue(this, Configuration, m_moduleReferenceResolver);

            return Task.FromResult(queue.ParseAndBindSpecs(specs));
        }

        /// <summary>
        /// Creates a workspace using a given spec as the starting point.
        /// </summary>
        public async Task<Workspace> CreateWorkspaceFromSpecAsync(AbsolutePath pathToSpec)
        {
            // Try to find a resolver that knows about this spec
            var moduleResolver = await FindResolverAsync(pathToSpec);
            if (!moduleResolver.Succeeded)
            {
                return Failure(moduleResolver.Failure);
            }

            // Try to get the module definition that owns this spec
            var moduleDefinition = await moduleResolver.Result.TryGetOwningModuleDefinitionAsync(pathToSpec);

            return await CreateWorkspaceFromModuleDefinitionAsync(moduleDefinition);
        }

        /// <summary>
        /// Creates a workspace using a module as the starting point
        /// </summary>
        public async Task<Workspace> CreateWorkspaceFromModuleAsync(ModuleDescriptor moduleDescriptor)
        {
            // Try to find a resolver that knows about this module name
            var moduleResolver = await FindResolverAsync(moduleDescriptor);

            if (!moduleResolver.Succeeded)
            {
                return Failure(moduleResolver.Failure);
            }

            // Try to get the module definition that owns this spec
            var moduleDefinition = await moduleResolver.Result.TryGetModuleDefinitionAsync(moduleDescriptor);

            return await CreateWorkspaceFromModuleDefinitionAsync(moduleDefinition);
        }

        /// <summary>
        /// Creates a workspace using all known modules (to this provider) as the starting point
        /// </summary>
        public async Task<Workspace> CreateWorkspaceFromAllKnownModulesAsync()
        {
            Possible<HashSet<ModuleDefinition>> moduleDefinitions = await GetModuleDefinitionsForAllResolversAsync();
            if (!moduleDefinitions.Succeeded)
            {
                return Failure(moduleDefinitions.Failure);
            }

            return await CreateWorkspaceFromModuleDefinitionsAsync(moduleDefinitions.Result, Configuration.ConstructFingerprintDuringParsing);
        }

        /// <summary>
        /// Creates a workspace from all known modules in an incremental way
        /// </summary>
        /// <remarks>
        /// It reuses already parsed modules so they don't get recomputed again
        /// </remarks>
        public async Task<Workspace> CreateIncrementalWorkspaceForAllKnownModulesAsync(
            IEnumerable<ParsedModule> parsedModules,
            ModuleUnderConstruction moduleUnderConstruction,
            IEnumerable<Failure> failures,
            [CanBeNull]ParsedModule preludeModule)
        {
            var maybeModuleDefinitions = await GetModuleDefinitionsForAllResolversAsync();
            if (!maybeModuleDefinitions.Succeeded)
            {
                return Failure(maybeModuleDefinitions.Failure);
            }

            var moduleDefinitions = maybeModuleDefinitions.Result;

            ModuleDefinition preludeDefinition;
            if (preludeModule != null)
            {
                // Need to add prelude to a list of parsed modules if awailable
                var parsedModuleList = parsedModules.ToList();
                parsedModuleList.Add(preludeModule);
                parsedModules = parsedModuleList;
                preludeDefinition = preludeModule.Definition;
            }
            else
            {
                var possiblePreludeDefinition = await TryGetPreludeModuleDefinitionAsync();
                if (!possiblePreludeDefinition.Succeeded)
                {
                    return Failure(possiblePreludeDefinition.Failure);
                }

                preludeDefinition = possiblePreludeDefinition.Result;
                moduleDefinitions.Add(preludeDefinition);
            }

            if (!WorkspaceValidator.ValidateModuleDefinitions(moduleDefinitions, PathTable, out var validationFailures))
            {
                return Failure(validationFailures);
            }

            var queue = ModuleParsingQueue.CreateIncrementalQueue(
                this,
                Configuration,
                m_moduleReferenceResolver,
                preludeDefinition,
                GetConfigurationModule(),
                parsedModules,
                failures);

            return await queue.ProcessIncrementalAsync(moduleUnderConstruction);
        }

        /// <inheritdoc />
        public ParsedModule GetConfigurationModule()
        {
            // In some scenarios (like tests) main config workspace is unavailable.
            if (m_mainConfigurationWorkspace == null)
            {
                return null;
            }

            if (m_configurationModule == null)
            {
                m_configurationModule = CreateConfigurationModule();
            }

            return m_configurationModule;

            ParsedModule CreateConfigurationModule()
            {
                // Module with all configuration files includes all the files obtained during main config processing
                // as well as other module configuration files that each resolver used to compute their modules.
                var moduleFiles = GetModuleConfigurationFiles();
                var moduleFileNames = new HashSet<AbsolutePath>(moduleFiles.Keys);

                var mainConfigurationModule = m_mainConfigurationWorkspace.ConfigurationModule;
                Contract.Assert(mainConfigurationModule != null);

                if (moduleFileNames.Count == 0)
                {
                    // If there is no module configuration files, we can use main configuration as a result.
                    return m_mainConfigurationWorkspace.ConfigurationModule;
                }

                // For the full configuration module using the main configuration file.
                // Main config should be part of the module definition as well.
                moduleFileNames.AddRange(mainConfigurationModule.PathToSpecs);
                var module = CreateConfigModuleDefinition(mainConfigurationModule.Definition.MainFile, moduleFileNames, m_configurationResolver);

                // Need to add files from the main configuration to the final set of files.
                moduleFiles.AddRange(mainConfigurationModule.Specs);

                var parsedModule = new ParsedModule(module, moduleFiles);
                return parsedModule;
            }
        }

        /// <summary>
        /// Tries to find a resolver that knows of a module which contains the given <param name="specPath"/>
        /// Resolvers are traversed in order, and first one that claims ownership wins
        /// </summary>
        internal async Task<Possible<IWorkspaceModuleResolver>> FindResolverAsync(AbsolutePath specPath)
        {
            foreach (var resolver in m_resolvers)
            {
                var maybeModuleName = await resolver.TryGetOwningModuleDescriptorAsync(specPath);
                if (maybeModuleName.Succeeded)
                {
                    return new Possible<IWorkspaceModuleResolver>(resolver);
                }
            }

            return new ResolverNotFoundForPathFailure(m_resolvers, specPath.ToString(PathTable));
        }

        /// <summary>
        /// Tries to find a resolver that knows of <param name="moduleDescriptor"/>
        /// Resolvers are traversed in order, and first one that claims ownership wins
        /// </summary>
        internal async Task<Possible<IWorkspaceModuleResolver>> FindResolverAsync(ModuleDescriptor moduleDescriptor)
        {
            foreach (var resolver in m_resolvers)
            {
                var moduleDefinition = await resolver.TryGetModuleDefinitionAsync(moduleDescriptor);

                if (moduleDefinition.Succeeded)
                {
                    return new Possible<IWorkspaceModuleResolver>(resolver);
                }
            }

            return new ResolverNotFoundForModuleDescriptorFailure(m_resolvers, moduleDescriptor);
        }

        /// <summary>
        /// Tries to find a resolver that knows of <param name="moduleReference"/>
        /// Resolvers are traversed in order, and first one that claims ownership wins
        /// </summary>
        internal async Task<Possible<IWorkspaceModuleResolver>> FindResolverAsync(ModuleReferenceWithProvenance moduleReference)
        {
            foreach (var resolver in m_resolvers)
            {
                var descriptors = await resolver.TryGetModuleDescriptorsAsync(moduleReference);

                if (descriptors.Succeeded && descriptors.Result.Count > 0)
                {
                    return new Possible<IWorkspaceModuleResolver>(resolver);
                }
            }

            return new ResolverNotFoundForModuleNameFailure(m_resolvers, moduleReference);
        }

        /// <summary>
        /// Finds the first resolver that knows about <param name="moduleReference"/>, asserts it is the only module
        /// descriptor with that name and returns the corresponding module definition.
        /// </summary>
        internal async Task<Possible<ModuleDefinition>> FindUniqueModuleDefinitionWithName(ModuleReferenceWithProvenance moduleReference)
        {
            // Find a resolver that claims ownership of this module name
            var maybeResolver = await FindResolverAsync(moduleReference);
            if (!maybeResolver.Succeeded)
            {
                return maybeResolver.Failure;
            }

            var resolver = maybeResolver.Result;

            // Get the set of module descriptors from the module name. There must be only one.
            var maybeDescriptors = await resolver.TryGetModuleDescriptorsAsync(moduleReference);

            if (!maybeDescriptors.Succeeded)
            {
                return maybeDescriptors.Failure;
            }

            var descriptors = maybeDescriptors.Result;

            Contract.Assert(descriptors.Count == 1);

            // Get the module definition from the resolver and enqueue it
            return await resolver.TryGetModuleDefinitionAsync(descriptors.First());
        }

        private Dictionary<AbsolutePath, ISourceFile> GetModuleConfigurationFiles()
        {
            var result = new Dictionary<AbsolutePath, ISourceFile>();
            foreach (var resolver in m_resolvers)
            {
                foreach (var sf in resolver.GetAllModuleConfigurationFiles())
                {
                    result[sf.GetAbsolutePath(PathTable)] = sf;

                    // This is yet another indication of an overly complicated design of the front-end
                    // There is no simple way to know what resolver responsible for configuration processing.
                    // We can keep the map for each path to resolver that produced it,
                    // but this still leaves an issue with main configuration file.
                    // So for now, we just use one of the resolvers that produced the config
                    // as a "configuration resolver".
                    if (m_configurationResolver == null)
                    {
                        m_configurationResolver = resolver;
                    }
                }
            }

            return result;
        }

        private ModuleDefinition CreateConfigModuleDefinition(AbsolutePath mainConfig, HashSet<AbsolutePath> allSpecs, IWorkspaceModuleResolver configurationResolver)
        {
            var descriptorName = Names.ConfigModuleName;
            var mdsc = new ModuleDescriptor(
                id: ModuleId.Create(PathTable.StringTable, descriptorName),
                name: descriptorName,
                displayName: descriptorName,
                version: "0.0",
                resolverKind: configurationResolver.Kind,
                resolverName: configurationResolver.Name);
            return ModuleDefinition.CreateModuleDefinitionWithExplicitReferencesWithEmptyQualifierSpace(
                descriptor: mdsc,
                main: mainConfig,
                moduleConfigFile: AbsolutePath.Invalid,
                specs: allSpecs,
                pathTable: PathTable);
        }

        private static bool TryCreateResolvers<T>(
            IWorkspaceResolverFactory<T> workspaceResolverFactory, 
            WorkspaceConfiguration configuration,
            AbsolutePath mainConfigurationFile,
            PathTable pathTable,
            bool addBuiltInPreludeResolver,
            out List<IWorkspaceModuleResolver> resolvers,
            out IEnumerable<Failure> failures)
        {
            Contract.Ensures(Contract.Result<List<IWorkspaceModuleResolver>>() != null);
            Contract.EnsuresForAll(Contract.Result<List<IWorkspaceModuleResolver>>(), r => r != null);

            resolvers = new List<IWorkspaceModuleResolver>(configuration.ResolverSettings.Count + (addBuiltInPreludeResolver ? 1 : 0));

            var resolverFailures = new List<Failure>();

            var resolverSettings = new List<IResolverSettings>(configuration.ResolverSettings);

            // The built in resolver is generally not added only for some tests. Regular spec processing always adds it.
            if (addBuiltInPreludeResolver)
            {
                // We add a resolver that points to the built-in prelude at the end of the resolver collection
                // so the built-in prelude is used if no prelude is specified explicitly
                var builtInPreludeSettings = PreludeManager.GetResolverSettingsForBuiltInPrelude(mainConfigurationFile, pathTable);
                resolverSettings.Add(builtInPreludeSettings);
            }

            foreach (var resolverConfiguration in resolverSettings)
            {
                var maybeResolver = workspaceResolverFactory.TryGetResolver(resolverConfiguration);
                if (!maybeResolver.Succeeded)
                {
                    resolverFailures.Add(maybeResolver.Failure);
                }
                else
                {
                    var resolver = (IWorkspaceModuleResolver)maybeResolver.Result;
                    resolvers.Add(resolver);
                }
            }

            failures = resolverFailures;
            return resolverFailures.Count == 0;
        }

        private async Task<Workspace> CreateWorkspaceFromModuleDefinitionAsync(Possible<ModuleDefinition> moduleDefinition)
        {
            if (!moduleDefinition.Succeeded)
            {
                return Failure(moduleDefinition.Failure);
            }

            return
                await CreateWorkspaceFromModuleDefinitionsAsync(
                        new HashSet<ModuleDefinition> { moduleDefinition.Result },
                        Configuration.ConstructFingerprintDuringParsing);
        }

        private Workspace Failure(params Failure[] failures)
        {
            return Workspace.Failure(this, Configuration, failures);
        }

        /// <summary>
        /// From an initial set of requested <param name="moduleDefinitions"/> returns a workspace that contains the set
        /// of corresponding modules closed under dependencies (if A is in the workspace and A imports B,
        /// then B is in the workspace).
        /// </summary>
        /// <remarks>
        /// For each module definition, it parses all its specs and builds a module out of it. Only specs successfully parsed are
        /// added to the module. Any failures are reported as part of the returned workspace instance.
        /// If <param name="computeBindingFingerprint"/> is true, then the binding fingerprint for all file would be computed after the parsing phase.
        /// </remarks>
        private async Task<Workspace> CreateWorkspaceFromModuleDefinitionsAsync(HashSet<ModuleDefinition> moduleDefinitions, bool computeBindingFingerprint)
        {
            var possiblePreludeDefinition = await TryGetPreludeModuleDefinitionAsync();
            if (!possiblePreludeDefinition.Succeeded)
            {
                return Workspace.Failure(this, Configuration, possiblePreludeDefinition.Failure);
            }

            if (possiblePreludeDefinition.Result != null)
            {
                moduleDefinitions.Add(possiblePreludeDefinition.Result);
            }

            if (!WorkspaceValidator.ValidateModuleDefinitions(moduleDefinitions, PathTable, out var failures))
            {
                return Workspace.Failure(this, Configuration, failures);
            }

            var queue = ModuleParsingQueue.Create(
                this,
                Configuration.WithComputeBindingFingerprint(computeBindingFingerprint),
                m_moduleReferenceResolver,
                possiblePreludeDefinition.Result,
                GetConfigurationModule());

            return await queue.ProcessAsync(moduleDefinitions);
        }

        private async Task<Possible<ModuleDefinition>> TryGetPreludeModuleDefinitionAsync()
        {
            ModuleDefinition preludeDefinition = null;

            // Check if the prelude is required, and in that case, verify resolvers know about it and include it in the
            // workspace construction
            if (Configuration.ShouldIncludePrelude)
            {
                // TODO: switch to ValueTasks
                // Since the prelude is implicitly imported, there is no real provenance location.
                var maybePrelude = await FindUniqueModuleDefinitionWithName(ModuleReferenceWithProvenance.FromName(Configuration.PreludeName));
                if (!maybePrelude.Succeeded)
                {
                    return maybePrelude;
                }

                preludeDefinition = maybePrelude.Result;
            }

            return preludeDefinition;
        }

        /// <summary>
        /// Returns all known module definitions.
        /// </summary>
        /// <remarks>
        /// Even though each resolver returns a unique set of modules, all resolvers together may be resolving
        /// conceptually the same module. A <see cref="ModuleDefinitionWorkspaceComparer"/> is used for determining
        /// duplication, respecting the resolver order.
        /// </remarks>
        private async Task<Possible<HashSet<ModuleDefinition>>> GetModuleDefinitionsForAllResolversAsync()
        {
            var result = new HashSet<ModuleDefinition>(ModuleDefinitionWorkspaceComparer.Comparer);

            foreach (var resolver in m_resolvers)
            {
                var moduleDefinitions = await resolver.GetAllModuleDefinitionsAsync();
                if (!moduleDefinitions.Succeeded)
                {
                    return moduleDefinitions.Failure;
                }

                result.UnionWith(moduleDefinitions.Result);
            }

            return result;
        }
    }
}
