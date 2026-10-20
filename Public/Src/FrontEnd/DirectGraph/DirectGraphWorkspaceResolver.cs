// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Utilities;
using BuildXL.FrontEnd.Workspaces;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Core;
using TypeScript.Net.DScript;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.DirectGraph
{
    /// <summary>
    /// Workspace resolver for DirectGraph modules.
    /// </summary>
    public sealed class DirectGraphWorkspaceResolver : IWorkspaceModuleResolver
    {
        private FrontEndContext m_context;
        private FrontEndHost m_host;
        private IDirectGraphResolverSettings m_resolverSettings;

        /// <summary>
        /// Module name to module descriptor mapping.
        /// </summary>
        private ReadOnlyDictionary<string, ModuleDescriptor> m_moduleDescriptors;

        /// <summary>
        /// Known specs to owning module mapping
        /// </summary>
        private ReadOnlyDictionary<AbsolutePath, ModuleDescriptor> m_specsPerModule;

        /// <summary>
        /// Module descriptor to module definition mapping.
        /// </summary>
        public ReadOnlyDictionary<ModuleDescriptor, ModuleDefinition> ModuleDefinitions { get; private set; }

        /// <inheritdoc/>
        public string Name { get; private set; }

        /// <nodoc />
        public DirectGraphWorkspaceResolver() => Name = KnownResolverKind.DirectGraphResolverKind;

        private ICollection<string> m_passthroughEnvironmentVariables;

        private IDictionary<string, string> m_trackedEnvironmentVariables;

        private bool m_processEnvironmentUsed;

        /// <inheritdoc/>
        public bool TryInitialize([NotNull] FrontEndHost host, [NotNull] FrontEndContext context, [NotNull] IConfiguration configuration, [NotNull] IResolverSettings resolverSettings)
        {
            m_host = host;
            m_context = context;

            m_resolverSettings = resolverSettings as IDirectGraphResolverSettings;
            Contract.Assert(m_resolverSettings != null);

            var result = ValidateSettings(m_resolverSettings, out var moduleNames);

            m_moduleDescriptors = new ReadOnlyDictionary<string, ModuleDescriptor>(moduleNames.ToDictionary(
                moduleName => moduleName,
                moduleName => new ModuleDescriptor(
                    ModuleId.Create(StringId.Create(m_context.StringTable, moduleName)), moduleName, moduleName, string.Empty, Kind, moduleName)));

            ModuleDefinitions = new ReadOnlyDictionary<ModuleDescriptor, ModuleDefinition>(m_moduleDescriptors.Select(
                kvp => new KeyValuePair<ModuleDescriptor, ModuleDefinition>(
                    kvp.Value,
                    ModuleDefinition.CreateModuleDefinitionWithImplicitReferences(
                        kvp.Value,
                        moduleRootDirectory: m_resolverSettings.File,
                        // We map all modules to the main config file, since there are no actual physical module files.
                        moduleConfigFile: m_resolverSettings.File,
                        // Each module defines a single (virtual) spec file, named after the module itself.
                        specs: new List<AbsolutePath>() { GetSpecFileForModule(kvp.Key) },
                        // We don't enforce any module-specific dependencies
                        allowedModuleDependencies: null,
                        // No cycles allowed
                        cyclicalFriendModules: null,
                        // No module-specific mounts
                        mounts: null,
                        // No specific directories to scrub
                        scrubDirectories: null)))
                .ToDictionary());

            m_specsPerModule = new ReadOnlyDictionary<AbsolutePath, ModuleDescriptor>(ModuleDefinitions.ToDictionary(kvp => kvp.Value.Specs.Single(), kvp => kvp.Value.Descriptor));

            ResolverSettings.ComputeEnvironment(m_resolverSettings.Environment, m_context.PathTable, out m_trackedEnvironmentVariables, out m_passthroughEnvironmentVariables, out m_processEnvironmentUsed);

            return result;
        }

        /// <summary>
        /// The variables of the process environment that are inputs of the graph: all of them when the configuration does
        /// not define the tool's environment, none when it does. Either way the tool also inherits
        /// <see cref="FrontEndUtilities.GraphConstructionEnvironmentVariableAllowlist"/>, which is tracked only in the first case.
        /// </summary>
        public IEnumerable<string> InheritedEnvironmentVariables =>
            m_processEnvironmentUsed ? m_trackedEnvironmentVariables.Keys : Enumerable.Empty<string>();

        /// <summary>
        /// Constructs the build parameters based on user defined variables and passthrough variables
        /// </summary>
        public BuildParameters.IBuildParameters CreateBuildParameters(string endpoint)
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
                .Select(FrontEndUtilities.GraphConstructionEnvironmentVariableAllowlist)
                .Override(configuredEnvironment.ToDictionary())
                // Expose the channel endpoint so the running tool can use it to communicate with the engine
                .Override(new[] { new KeyValuePair<string, string>(DirectGraphFrontEnd.ChannelEnvironmentVariable, endpoint) });

            return buildParameters;
        }

        /// <summary>
        /// Retrieves the definition of a module owned by this resolver.
        /// </summary>
        public bool TryGetModuleDefinition(string moduleName, out ModuleDefinition moduleDefinition)
        {
            if (m_moduleDescriptors.TryGetValue(moduleName, out var moduleDescriptor))
            {
                return ModuleDefinitions.TryGetValue(moduleDescriptor, out moduleDefinition);
            }

            moduleDefinition = null;
            return false;
        }

        /// <summary>
        /// Each module is represented by a virtual spec file, named after the module, sitting next to the main config file.
        /// </summary>
        /// <remarks>
        /// The file does not exist on disk. It is the spec the workspace advertises for the module, and therefore also the
        /// location <see cref="FrontEndHost.PipGraph"/> registers a spec file pip for. Pip provenance has to point at this
        /// same path, otherwise adding a value pip fails.
        /// </remarks>
        private AbsolutePath GetSpecFileForModule(string moduleName) =>
            m_resolverSettings.File.GetParent(m_context.PathTable).Combine(
                m_context.PathTable,
                PathAtom.Create(m_context.StringTable, moduleName + ".dsc"));

        private bool ValidateSettings(IDirectGraphResolverSettings resolverSettings, out IReadOnlySet<string> moduleNames)
        {
            moduleNames = CollectionUtilities.EmptySet<string>();

            if (resolverSettings.ModuleNames == null || resolverSettings.ModuleNames.Count == 0)
            {
                Tracing.Logger.Log.ReportResolverMissingModuleNames(m_context.LoggingContext);
                return false;
            }

            if (resolverSettings.ModuleNames.Any(name => !name.IsValid))
            {
                Tracing.Logger.Log.ReportResolverEmptyModuleName(m_context.LoggingContext);
                return false;
            }

            if (!resolverSettings.Tool.IsValid)
            {
                Tracing.Logger.Log.ReportResolverMissingTool(m_context.LoggingContext);
                return false;
            }

            if (resolverSettings.IdleTimeoutMin is int idleTimeoutMin && idleTimeoutMin < DirectGraphResolver.MinIdleTimeoutMinutes)
            {
                Tracing.Logger.Log.ReportResolverInvalidIdleTimeout(
                    m_context.LoggingContext,
                    idleTimeoutMin,
                    DirectGraphResolver.MinIdleTimeoutMinutes);
                return false;
            }

            // A module name also names its virtual spec file, and the path of that file ignores case on some platforms.
            var names = resolverSettings.ModuleNames.Select(name => name.ToString(m_context.StringTable)).ToList();
            var duplicates = names
                .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .ToList();

            if (duplicates.Count > 0)
            {
                string duplicatedModuleNames = string.Join(", ", duplicates.SelectMany(group => group).Distinct());
                Tracing.Logger.Log.ReportResolverDuplicateModuleNames(m_context.LoggingContext, duplicatedModuleNames);
                return false;
            }

            moduleNames = names.ToReadOnlySet();

            return true;
        }

        /// <inheritdoc/>
        public string Kind => KnownResolverKind.DirectGraphResolverKind;

        /// <inheritdoc/>
        public ValueTask<Possible<IReadOnlyCollection<ModuleDescriptor>>> TryGetModuleDescriptorsAsync(ModuleReferenceWithProvenance moduleReference)
        {
            if (m_moduleDescriptors.TryGetValue(moduleReference.Name, out var moduleDescriptor))
            {
                return new ValueTask<Possible<IReadOnlyCollection<ModuleDescriptor>>>(Possible.Create<IReadOnlyCollection<ModuleDescriptor>>(new[] { moduleDescriptor }));
            }

            return new ValueTask<Possible<IReadOnlyCollection<ModuleDescriptor>>>(Possible.Create<IReadOnlyCollection<ModuleDescriptor>>(CollectionUtilities.EmptyArray<ModuleDescriptor>()));
        }

        /// <inheritdoc/>
        public ValueTask<Possible<HashSet<ModuleDescriptor>>> GetAllKnownModuleDescriptorsAsync()
        {
            return ValueTask.FromResult(Possible.Create(m_moduleDescriptors.Values.ToHashSet()));
        }

        /// <inheritdoc/>
        public ValueTask<Possible<ModuleDefinition>> TryGetModuleDefinitionAsync(ModuleDescriptor moduleDescriptor)
        {
            if (!ModuleDefinitions.TryGetValue(moduleDescriptor, out var moduleDefinitionFromSettings) || !moduleDescriptor.Equals(moduleDefinitionFromSettings.Descriptor))
            {
                return new ValueTask<Possible<ModuleDefinition>>(new ModuleNotOwnedByThisResolver(moduleDescriptor));
            }

            return new ValueTask<Possible<ModuleDefinition>>(Possible.Create<ModuleDefinition>(moduleDefinitionFromSettings));
        }

        /// <inheritdoc/>
        public ValueTask<Possible<ModuleDescriptor>> TryGetOwningModuleDescriptorAsync(AbsolutePath specPath)
        {
            if (m_specsPerModule.TryGetValue(specPath, out var moduleDescriptor))
            {
                return new ValueTask<Possible<ModuleDescriptor>>(Possible.Create(moduleDescriptor));
            }

            return new ValueTask<Possible<ModuleDescriptor>>(new SpecNotOwnedByResolverFailure(specPath.ToString(m_context.PathTable)));
        }

        /// <inheritdoc/>
        public Task<Possible<ISourceFile>> TryParseAsync(AbsolutePath pathToParse, AbsolutePath moduleOrConfigPathPromptingParse, ParsingOptions parsingOptions = null)
        {
            if (!m_specsPerModule.TryGetValue(pathToParse, out _))
            {
                return Task.FromResult<Possible<ISourceFile>>(new SpecNotOwnedByResolverFailure(pathToParse.ToString(m_context.PathTable)));
            }

            var sourceFile = SourceFile.Create(pathToParse.ToString(m_context.PathTable));
            sourceFile.OverrideIsScriptFile = true;
            sourceFile.ExternalModuleIndicator = sourceFile;

            return Task.FromResult(Possible.Create<ISourceFile>(sourceFile));
        }

        /// <inheritdoc/>
        [return: NotNull]
        public string DescribeExtent()
        {
            Contract.Assume(m_moduleDescriptors != null, "Init must have been called");

            return string.Join(", ", m_moduleDescriptors.Values.Select(descriptor => descriptor.Name));
        }

        /// <inheritdoc/>
        public Task ReinitializeResolver() => Task.FromResult<object>(null);

        /// <inheritdoc />
        public ISourceFile[] GetAllModuleConfigurationFiles() => CollectionUtilities.EmptyArray<ISourceFile>();
    }
}
