// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Utilities;
using BuildXL.Utilities.Qualifier;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.FrontEnd.Core;
using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.Mutable;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Script
{
    /// <summary>
    /// A workspace resolver that can interpret DScript default source module configuration
    /// </summary>
    /// <remarks>
    /// Default source configurations are artificially introduced by the controller to represent the build extent that is specified outside a resolver.
    /// </remarks>
    public sealed class WorkspaceDefaultSourceModuleResolver : WorkspaceSourceModuleResolver
    {
        private IInternalDefaultDScriptResolverSettings m_defaultDScriptResolverSettings;

        /// <inheritdoc/>
        /// <remarks>
        ///   - build extent consists of 'projects' and 'packages' explicitly specified in the config file;
        ///   - those projects/packages are resolved by DefaultSourceResolver;
        ///   - when <see cref="IConfiguration.DisableDefaultSourceResolver"/> is set to true, the current
        ///     (arguably convoluted) semantics is that 'projects' and 'packages' are excluded from evaluation,
        ///     but the build extent becomes all other modules (regardless of how they were resolved);
        ///   - to compute the build extent at runtime, all modules are traversed and those that are
        ///     <see cref="ModuleDescriptor.ResolverKind"/> a resolver whose kind is
        ///     <see cref="KnownResolverKind.DefaultSourceResolverKind"/> are selected.  If any such modules
        ///     are found, they become the build extent; otherwise, the build extent is becomes all the modules;
        ///   - given the algorithm above, when 'DisableDefaultSourceResolver' is true, we don't want any modules
        ///     to appear to be resolved by 'DefaultSourceResolver'.
        /// </remarks>
        public override string Kind => Configuration.DisableDefaultSourceResolver()
            ? KnownResolverKind.DScriptResolverKind
            : KnownResolverKind.DefaultSourceResolverKind;

        /// <nodoc/>
        public WorkspaceDefaultSourceModuleResolver(
            GlobalConstants constants,
            ModuleRegistry sharedModuleRegistry,
            IFrontEndStatistics statistics,
            Logger logger = null)
            : base(constants, sharedModuleRegistry, statistics, logger)
        {
            Name = nameof(WorkspaceDefaultSourceModuleResolver);
        }

        /// <inheritdoc/>
        public override bool TryInitialize(FrontEndHost host, FrontEndContext context, IConfiguration configuration, IResolverSettings resolverSettings, QualifierId[] requestedQualifiers)
        {
            Contract.Requires(context != null);
            Contract.Requires(host != null);
            Contract.Requires(configuration != null);
            Contract.Requires(resolverSettings != null);
            Contract.Requires(requestedQualifiers?.Length > 0);

            if (!base.TryInitialize(host, context, configuration, resolverSettings, requestedQualifiers))
            {
                return false;
            }

            var defaultSourceResolverSettings = resolverSettings as IInternalDefaultDScriptResolverSettings;

            Contract.Assert(defaultSourceResolverSettings != null);

            m_defaultDScriptResolverSettings = defaultSourceResolverSettings;

            return true;
        }

        /// <inheritdoc/>
        protected override async Task<bool> DoResolveModuleAsync()
        {
            var packagePathsBuilder = new List<AbsolutePath>();
            var configDirPath = m_defaultDScriptResolverSettings.ConfigFile.GetParent(Context.PathTable);
            var shouldCollectPackages = true;
            var shouldCollectOrphanProjects = true;

            IReadOnlyList<AbsolutePath> orphanProjectPaths = null;

            // TODO: In the future we may want users to specify the packages/projects explicitly, or via explicit glob.
            // TODO: Thus, we can avoid implicit directory enumerations on collecting packages/projects.
            // TODO: Implicit directory enumeration turns out to be bad for spinning disk.
            if (!Configuration.DisableDefaultSourceResolver())
            {
                if (m_defaultDScriptResolverSettings.Projects != null)
                {
                    orphanProjectPaths = m_defaultDScriptResolverSettings.Projects;
                    shouldCollectOrphanProjects = false;
                }

                // If list of packages are explicitly specified by users, then they are the packages owned by the configuration.
                if (!await CheckUserExplicitlySpecifiedPackagesAsync(m_defaultDScriptResolverSettings.Modules, m_defaultDScriptResolverSettings.Packages,
                    m_defaultDScriptResolverSettings.ConfigFile, m_defaultDScriptResolverSettings.ConfigFile))
                {
                    // Error has been reported.
                    return false;
                }

                // Both cannot be present, already validated
                var modules = m_defaultDScriptResolverSettings.Modules ?? m_defaultDScriptResolverSettings.Packages;

                if (modules != null)
                {
                    packagePathsBuilder.AddRange(modules);
                    shouldCollectPackages = false;
                }
            }
            else
            {
                // If default source resolver is disabled, then it shouldn't own anything.
                shouldCollectPackages = false;
                shouldCollectOrphanProjects = false;

                // It is very important to use an empty list for projects
                // when the default source resolver is disabled.
                orphanProjectPaths = new List<AbsolutePath>();
            }

            if (shouldCollectPackages || shouldCollectOrphanProjects)
            {
                // Collect all packages and orphan projects.
                var orphanProjectPathsBuilder = new List<AbsolutePath>();

                if (
                    !await
                        CollectPackagesAndProjects(
                            new SourceResolverSettings() { Root = configDirPath }, // no need to set provenance since we know that configDirPath exists
                            shouldCollectPackages,
                            shouldCollectOrphanProjects,
                            packagePathsBuilder,
                            orphanProjectPathsBuilder,
                            Configuration.Layout.OutputDirectory,
                            skipConfigFile: false))
                {
                    // Error has been reported.
                    return false;
                }

                if (shouldCollectOrphanProjects)
                {
                    orphanProjectPaths = orphanProjectPathsBuilder;
                }
            }

            if (!await InitPackagesAsync(packagePathsBuilder, m_defaultDScriptResolverSettings.ConfigFile))
            {
                // Error has been reported.
                return false;
            }

            m_configAsPackage = CreateConfigAsPackage(
                Configuration,
                m_defaultDScriptResolverSettings.ConfigFile,
                orphanProjectPaths);

            UpdatePackageMap(m_configAsPackage);

            return true;
        }

        private Package CreateConfigAsPackage(
           IConfiguration config,
           AbsolutePath configPath,
           IReadOnlyList<AbsolutePath> projects)
        {
            string name = Names.ConfigAsPackageName;

            var descriptor = new PackageDescriptor
            {
                Name = name,
                // Instead of displaying cryptic '__Config__' we use a foler name where config.dsc is located
                DisplayName = configPath.GetParent(Context.PathTable).GetName(Context.PathTable).ToString(Context.StringTable),
                Projects = projects,
                NameResolutionSemantics = config.FrontEnd.NameResolutionSemantics(),
            };

            var id = PackageId.Create(StringId.Create(Context.StringTable, name));

            // This makes the default package the only one whose main file is its own configuration file!
            // TODO: make this consistent (nevertheless, 'main' field should go away when we move completely to V2...)
            return Package.Create(id, configPath, descriptor);
        }

        /// <inheritdoc/>
        public override string DescribeExtent()
        {
            var maybeModules = GetAllKnownModuleDescriptorsAsync().GetAwaiter().GetResult();

            if (!maybeModules.Succeeded)
            {
                return I($"Module extent could not be computed. {maybeModules.Failure.Describe()}");
            }

            // We report all modules but the 'fake' config as package
            return string.Join(
                ", ",
                maybeModules.Result.Select(module => module.Name)
                    .Where(moduleName => !moduleName.Equals(Names.ConfigAsPackageName)));
        }
    }
}
