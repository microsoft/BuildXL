// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Failures;
using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Script.Util;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Workspaces;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.FrontEnd.Workspaces.Core.Failures;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.ParallelAlgorithms;
using BuildXL.Utilities.Tasks;
using JetBrains.Annotations;
using TypeScript.Net.Types;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Script
{
    /// <summary>
    /// Cases for the internal state of this module resolver. This is so we don't resolve a settings more than once.
    /// </summary>
    internal enum ModuleResolutionState
    {
        Unresolved = 0,
        Succeeded = 1,
        Failed = 2,
    }

    /// <summary>
    /// A workspace resolver that can interpret DScript modules
    /// </summary>
    /// TODO:
    /// - Replace generic Failure by proper failures. Error messages are not great.
    /// - Merge Package/PackageDescriptor and ParsedModule/ModuleDefinition so they share common properties. Now this class is two-faced:
    /// - It implements IDScriptWorkspaceModuleResolver, so it respects the structures defined in the Workspace project
    /// - It returns a ModuleResolutionResult when ResolveModuleAsyncIfNeeded() is called from the source front end.
    /// We should consider removing the package interpreting logic from IResolver, which will remove the dependency between a workspace resolver and an IResolver
    public class WorkspaceSourceModuleResolver : DScriptInterpreterBase, IWorkspaceModuleResolver
    {
        private IDScriptResolverSettings m_resolverSettings;

        /// <summary>
        /// Source files obtained during configuration processing.
        /// </summary>
        /// <remarks>
        /// Not null if <see cref="InitPackageFromDescriptorAsync"/> was called and was successful.
        /// </remarks>
        private ISourceFile[] m_configurationFiles;

        /// <summary>
        /// Configuration as a package, which is only created for the default source resolver. Null otherwise. Populated by DoResolveModuleAsync.
        /// </summary>
        protected Package m_configAsPackage;

        /// <summary>
        /// Mappings from package id's to package locations and descriptors. Populated by DoResolveModuleAsync.
        /// </summary>
        private readonly ConcurrentDictionary<PackageId, Package> m_packages = new ConcurrentDictionary<PackageId, Package>(PackageIdEqualityComparer.NameOnly);

        /// <summary>
        /// Mappings from the absolute path to a spec to an owning package.
        /// </summary>
        private Dictionary<AbsolutePath, Package> m_specPathToPackageMap;

        /// <summary>
        /// Mappings from package id's to module definition. Populated lazily.
        /// </summary>
        /// <remarks>
        /// Module definition computation involves file system probing. Using a cache significantly improves performance when one package is referenced multiple times.
        /// </remarks>
        private readonly ConcurrentDictionary<PackageId, ModuleDefinition> m_modules = new ConcurrentDictionary<PackageId, ModuleDefinition>(PackageIdEqualityComparer.NameOnly);

        /// <summary>
        /// Mappings package directories to lists of packages. Populated by DoResolveModuleAsync.
        /// </summary>
        /// <remarks>
        /// We allow multiple packages in a single directory, and hence the list of packages. Moreover, by construction, the packages in the same list
        /// must reside in the same directory.
        /// </remarks>
        private readonly ConcurrentDictionary<AbsolutePath, List<Package>> m_packageDirectories = new ConcurrentDictionary<AbsolutePath, List<Package>>();

        /// <summary>
        /// Helper for parsing and converting module config files.
        /// </summary>
        private ConfigurationConversionHelper m_configConversionHelper;

        /// <summary>
        /// A projection of m_packages with all module descriptors. It is cached here by DoResolveModuleAsync so we don't have to re-compute it every time.
        /// </summary>
        [CanBeNull]
        private HashSet<ModuleDescriptor> m_allModuleDescriptors;

        /// <summary>
        /// Map of module names to module descriptors. Populated by DoResolveModuleAsync.
        /// </summary>
        [CanBeNull]
        private MultiValueDictionary<string, ModuleDescriptor> m_moduleDescriptorByName;

        private ModuleResolutionState m_moduleResolutionState;

        private CachedTask<ModuleResolutionResult> m_resolveModuleAsyncIfNeededTask = CachedTask<ModuleResolutionResult>.Create();

        private PathTable PathTable => Context.PathTable;

        private StringTable StringTable => Context.StringTable;

        /// <inheritdoc/>
        public virtual string Kind => KnownResolverKind.DScriptResolverKind;
        
        private readonly PathAtom m_packageConfigDsc;
        private readonly PathAtom m_moduleConfigBm;
        private readonly PathAtom m_moduleConfigDsc;
        private readonly PathAtom m_configDsc;
        private readonly PathAtom m_configBc;
        private readonly PathAtom m_packageDsc;
        private readonly PathAtom m_dotConfigDotDscExtension;

        /// <nodoc/>
        public WorkspaceSourceModuleResolver(
            StringTable stringTable,
            IFrontEndStatistics statistics,
            Logger logger = null)
            : base(statistics, logger)
        {
            Name = nameof(WorkspaceSourceModuleResolver);
            m_moduleResolutionState = ModuleResolutionState.Unresolved;

            m_configDsc = PathAtom.Create(stringTable, Names.ConfigDsc);
            m_configBc = PathAtom.Create(stringTable, Names.ConfigBc);
            m_packageDsc = PathAtom.Create(stringTable, Names.PackageDsc);
            m_packageConfigDsc = PathAtom.Create(stringTable, Names.PackageConfigDsc);
            m_moduleConfigBm = PathAtom.Create(stringTable, Names.ModuleConfigBm);
            m_moduleConfigDsc = PathAtom.Create(stringTable, Names.ModuleConfigDsc);
            m_dotConfigDotDscExtension = PathAtom.Create(stringTable, Names.DotConfigDotDscExtension);
        }

        /// <nodoc/>
        public virtual bool TryInitialize(FrontEndHost host, FrontEndContext context, IConfiguration configuration, IResolverSettings resolverSettings, QualifierId[] requestedQualifiers)
        {
            Contract.Requires(context != null);
            Contract.Requires(host != null);
            Contract.Requires(configuration != null);
            Contract.Requires(resolverSettings != null);
            Contract.Requires(requestedQualifiers?.Length > 0);

            var sourceResolverSettings = resolverSettings as IDScriptResolverSettings;
            Contract.Assert(sourceResolverSettings != null);

            InitializeInterpreter(host, context, configuration);

            Name = resolverSettings.Name;
            m_resolverSettings = sourceResolverSettings;
            m_configConversionHelper = new ConfigurationConversionHelper(
                host.Engine,
                ConfigurationConversionHelper.ConfigurationKind.ModuleConfig,
                Logger,
                FrontEndHost,
                Context,
                Configuration,
                FrontEndStatistics);

            return true;
        }

        /// <inheritdoc/>
        public async ValueTask<Possible<ModuleDefinition>> TryGetModuleDefinitionAsync(ModuleDescriptor moduleDescriptor)
        {
            await ResolveModuleAsyncIfNeeded();

            var packageName = StringId(moduleDescriptor.Name);
            var packageId = PackageId.Create(packageName);

            if (!m_packages.TryGetValue(packageId, out Package package))
            {
                return new ModuleNotOwnedByThisResolver(moduleDescriptor);
            }

            return GetOrConvertModuleDefinitionForPackage(packageId, package);
        }

        private Possible<ModuleDefinition> GetOrConvertModuleDefinitionForPackage(PackageId packageId, Package package)
        {
            if (m_modules.TryGetValue(packageId, out ModuleDefinition moduleDefinition))
            {
                return moduleDefinition;
            }

            if (!FrontEndConfiguration.UseLegacyOfficeLogic())
            {
                var packageName = package.Descriptor.Name;
                if (package.Descriptor.NameResolutionSemantics() == NameResolutionSemantics.ExplicitProjectReferences &&
                    !packageName.Equals(FrontEndHost.PreludeModuleName) &&
                    !packageName.Equals(Script.Constants.Names.ConfigAsPackageName))
                {
                    Logger.WarnForDeprecatedV1Modules(
                        Context.LoggingContext,
                        packageName,
                        package.Path.ToString(PathTable));
                }
            }

            var maybeModuleDefinition = ConvertPackageToModuleDefinition(package);
            return maybeModuleDefinition.Then(
                (m_modules, packageId),
                (tpl, module) =>
                {
                    var modules = tpl.m_modules;
                    var pckgId = tpl.packageId;
                    modules.TryAdd(pckgId, module);
                    return module;
                });
        }

        /// <inheritdoc/>
        public async ValueTask<Possible<IReadOnlyCollection<ModuleDescriptor>>> TryGetModuleDescriptorsAsync(ModuleReferenceWithProvenance moduleReference)
        {
            await ResolveModuleAsyncIfNeeded();

            if (m_moduleResolutionState == ModuleResolutionState.Failed)
            {
                return new ModuleResolutionFailure();
            }

            Contract.Assert(m_allModuleDescriptors != null, "ResolveModuleAsyncIfNeeded should always populate this");

            if (!m_moduleDescriptorByName.TryGetValue(moduleReference.Name, out IReadOnlyList<ModuleDescriptor> moduleDescriptors))
            {
                return CollectionUtilities.EmptyArray<ModuleDescriptor>();
            }

            return new Possible<IReadOnlyCollection<ModuleDescriptor>>(moduleDescriptors);
        }

        /// <inheritdoc/>
        public async ValueTask<Possible<ModuleDescriptor>> TryGetOwningModuleDescriptorAsync(AbsolutePath specPath)
        {
            await ResolveModuleAsyncIfNeeded();

            var package = TryFindPackage(specPath);
            if (package == null)
            {
                return new SpecNotOwnedByResolverFailure(specPath.ToString(PathTable));
            }

            return ConvertPackageToModuleDescriptor(package);
        }

        /// <inheritdoc/>
        public async ValueTask<Possible<HashSet<ModuleDescriptor>>> GetAllKnownModuleDescriptorsAsync()
        {
            await ResolveModuleAsyncIfNeeded();

            if (m_moduleResolutionState == ModuleResolutionState.Failed)
            {
                return new ModuleResolutionFailure();
            }

            Contract.Assert(m_allModuleDescriptors != null, "ResolveModuleAsyncIfNeeded should always populate this");

            return m_allModuleDescriptors;
        }

        /// <inheritdoc/>
        public virtual string DescribeExtent()
        {
            var maybeModules = GetAllKnownModuleDescriptorsAsync().GetAwaiter().GetResult();

            if (!maybeModules.Succeeded)
            {
                return I($"Module extent could not be computed. {maybeModules.Failure.Describe()}");
            }

            return string.Join(", ", maybeModules.Result.Select(module => module.DisplayName));
        }

        /// <inheritdoc />
        public Task ReinitializeResolver()
        {
            // This operation is not fully thread-safe, but the only intented scenario is IDE. So we're good.
            m_resolveModuleAsyncIfNeededTask.Reset();

            m_moduleResolutionState = ModuleResolutionState.Unresolved;
            m_packages.Clear();
            m_specPathToPackageMap?.Clear();
            m_modules.Clear();
            m_packageDirectories.Clear();
            m_moduleDescriptorByName?.Clear();
            m_allModuleDescriptors?.Clear();

            return ResolveModuleAsyncIfNeeded();
        }

        /// <inheritdoc />
        public ISourceFile[] GetAllModuleConfigurationFiles()
        {
            var result = m_configurationFiles ?? CollectionUtilities.EmptyArray<ISourceFile>();
            m_configurationFiles = null;
            return result;
        }

        /// <summary>
        /// Interprets and resolves a module. This methods acts as a bridge for source resolvers, since the module interpretation logic
        /// is for now shared between workspace resolvers and IResolvers. TODO: This methods should be removed when IResolver package-interpretation
        /// logic is removed.
        /// </summary>
        internal Task<ModuleResolutionResult> ResolveModuleAsyncIfNeeded()
        {
            return m_resolveModuleAsyncIfNeededTask.GetOrCreate(this, @this => @this.DoResolveModuleAsyncIfNeededAsync());
        }

        private async Task<ModuleResolutionResult> DoResolveModuleAsyncIfNeededAsync()
        {
            if (m_moduleResolutionState == ModuleResolutionState.Unresolved)
            {
                var result = await DoResolveModuleAsync();

                // Compute the set of all known modules and name -> module here, so we only do this once
                m_allModuleDescriptors =
                    new HashSet<ModuleDescriptor>(m_packages.Values.Select(ConvertPackageToModuleDescriptor));

                m_moduleDescriptorByName =
                    m_allModuleDescriptors.ToMultiValueDictionary(descriptor => descriptor.Name, descriptor => descriptor);

                m_moduleResolutionState = !result || !ValidateFoundPackages()
                    ? ModuleResolutionState.Failed
                    : ModuleResolutionState.Succeeded;

                m_specPathToPackageMap = m_moduleResolutionState == ModuleResolutionState.Succeeded
                    ? GetSpecPathToPackageMap(m_packages)
                    : new Dictionary<AbsolutePath, Package>();
            }

            return m_moduleResolutionState == ModuleResolutionState.Succeeded
                ? ModuleResolutionResult.CreateModuleResolutionResult(m_packageDirectories, m_packages, m_configAsPackage)
                : ModuleResolutionResult.CreateFailure();
        }

        private static Dictionary<AbsolutePath, Package> GetSpecPathToPackageMap(ConcurrentDictionary<PackageId, Package> packages)
        {
            var result = new Dictionary<AbsolutePath, Package>();
            foreach (var kvp in packages)
            {
                if (kvp.Value.DescriptorProjects != null)
                {
                    foreach (var specPath in kvp.Value.DescriptorProjects)
                    {
                        result[specPath] = kvp.Value;
                    }
                }
            }

            return result;
        }

        /// <nodoc/>
        protected virtual async Task<bool> DoResolveModuleAsync()
        {
            var packagePaths = new List<AbsolutePath>();

            if (!await CheckUserExplicitlySpecifiedPackagesAsync(m_resolverSettings.Modules, m_resolverSettings.Packages, AbsolutePath.Invalid, m_resolverSettings.File))
            {
                // Error has been reported.
                return false;
            }

            // Both cannot be present, already validated
            var modules = m_resolverSettings.Modules ?? m_resolverSettings.Packages;

            if (modules != null)
            {
                packagePaths.AddRange(modules);
            }

            // TODO: In the future we may want users to specify the packages explicitly, or via explicit glob.
            // TODO: Thus, we can avoid implicit directory enumerations on collecting packages.
            // TODO: Implicit directory enumeration turns out to be bad for spinning disk.
            if (m_resolverSettings.Root.IsValid)
            {
                var dummyProjectPaths = new List<AbsolutePath>();

                if (
                    !await
                        CollectPackagesAndProjects(
                            resolverSettings: m_resolverSettings,
                            shouldCollectPackage: true,
                            shouldCollectOrphanProjects: false,
                            packagesBuilder: packagePaths,
                            projectsBuilder: dummyProjectPaths,
                            outputDirectory: Configuration.Layout.OutputDirectory,
                            skipConfigFile: true))
                {
                    // Error has been reported.
                    return false;
                }
            }

            // Specified packages can reside in different config cones (i.e., different workspaces).
            // Currently we use the config qualifier space as the default fall-back.
            return await InitPackagesAsync(packagePaths, FrontEndHost.PrimaryConfigFile);
        }

        /// <nodoc />
        public Package TryFindPackage(AbsolutePath path)
        {
            // First, trying to get the package from the map.
            if (m_specPathToPackageMap.TryGetValue(path, out var resultCandidate))
            {
                return resultCandidate;
            }

            // Only if the map does not have the package, using old v1-like logic to find the owner.
            // The following logic is highly suspicious and left for backward compatibilities reason.
            // The logic is relying on folder structure to find the first folder that has a module definition
            // assuming that this module owns it.
            // But it is possible that the spec is owned by the module in the upper folder.
            // In this case the resolution will fail.
            if (m_packageDirectories.Count > 0)
            {
                var searchPath = path;
                searchPath = searchPath.GetParent(PathTable);

                while (searchPath != AbsolutePath.Invalid)
                {
                    if (m_packageDirectories.TryGetValue(searchPath, out List<Package> packages))
                    {
                        // A package is found, don't go to the parent directory, but check if the package owns it.
                        return FindOwningPackageByPath(path, packages);
                    }

                    // Following check is only applicable for default source resolver.
                    if (m_configAsPackage?.Path == searchPath)
                    {
                        // Check that the config package owns the search folder.
                        // This prevents from file system probing in case of a miss.
                        if (IsRootConfigurationFileExists(searchPath, out var configFilePath))
                        {
                            // Don't go to the parent directory if a config file is found.
                            // But the path being question may point to an orphaned project, and thus, check if config owns it.
                            return FindOwningPackage(configFilePath, m_configAsPackage);
                        }
                    }

                    searchPath = searchPath.GetParent(PathTable);
                }
            }

            return null;
        }

        private Package FindOwningPackageByPath(AbsolutePath path, List<Package> packages)
        {
            return packages.Count == 1 ? FindOwningPackage(path, packages[0]) : FindOwningPackage(path, packages);
        }

        /// <nodoc/>
        protected static Package FindOwningPackage(AbsolutePath path, Package package)
        {
            Contract.Requires(package != null);

            if (path != package.Path && package.DescriptorProjects != null && !package.DescriptorProjects.Contains(path))
            {
                // Package is found.
                // Path does not coincide with the main file of the package.
                // Package specifies explicitly the projects that it owns.
                // But the path is not one of them.
                return null;
            }

            return package;
        }

        private Package FindOwningPackage(AbsolutePath path, IReadOnlyList<Package> packages)
        {
            Package foundPackage = null;

            foreach (var package in packages)
            {
                if (package == m_configAsPackage)
                {
                    // Config package is handled differently by TryFindPackage.
                    continue;
                }

                if (path == package.Path || package.DescriptorProjects == null || (package.DescriptorProjects != null && package.DescriptorProjects.Contains(path)))
                {
                    foundPackage = package;
                }
            }

            return foundPackage;
        }

        // TODO: A package has actually two identifiers: packageId and moduleId. PackageId is a stringId that gets created
        // based on the package name and version. Consider making ModuleId the only identifier of a package.
        // In the meantime, when the workspace flag in on,
        // the module Id is retrieved from the modules the workspace computed and assigned to the package.
        // Still, that moduleId is latter overridden at evaluation time. Clean this up.
        private ModuleDescriptor ConvertPackageToModuleDescriptor(Package package)
        {
            return new ModuleDescriptor(package.ModuleId, package.Descriptor.Name, package.Descriptor.DisplayName, package.Descriptor.Version, this.Kind, Name);
        }

        private Possible<ModuleDefinition> ConvertPackageToModuleDefinition(Package package)
        {
            var packageRoot = package.Path.GetParent(PathTable);

            List<AbsolutePath> projects;
            if (package.Descriptor.Projects == null)
            {
                projects = ProjectCollector.CollectAllProjects(
                    m_fileSystem,
                    package.Path.GetParent(PathTable));
            }
            else
            {
                projects = package.Descriptor.Projects.ToList();

                // Special case to unblock WDG:
                // packages field was specified but empty and main file is provided
                // Observe that for the config-as-package, the main file is not a real project but config.dsc. We don't add it in that case.
                if (package.Descriptor.NameResolutionSemantics() == NameResolutionSemantics.ExplicitProjectReferences &&
                    package.Descriptor.Name != Script.Constants.Names.ConfigAsPackageName)
                {
                    projects.Add(package.Path);
                }

                var outOfConeProject = projects.Find(project => !project.IsWithin(PathTable, packageRoot));
                if (outOfConeProject != default(AbsolutePath))
                {
                    return new ProjectOutsideModuleConeFailure(PathTable, package, outOfConeProject);
                }
            }

            // This is for v1 only, so we'll just have a hardcoded filename since it must be that name.
            var moduleConfigFile = packageRoot.Combine(PathTable, Script.Constants.Names.PackageConfigDsc);

            // Allowed dependencies and cyclic friends are, for now, strings representing module name references.
            var allowedDependencies = package.Descriptor.AllowedDependencies?.Select(
                moduleName => ModuleReferenceWithProvenance.FromNameAndPath(moduleName, package.Path.ToString(PathTable)));
            var cyclicalFriendModules = package.Descriptor.CyclicalFriendModules?.Select(
                moduleName => ModuleReferenceWithProvenance.FromNameAndPath(moduleName, package.Path.ToString(PathTable)));

            // projects field could contains 'glob(*.dsc)' and we have to exclude package configuration files from this list.
            projects = projects.Where(p => !IsWellKnownConfigFile(p)).ToList();

            return package.Descriptor.NameResolutionSemantics() == NameResolutionSemantics.ImplicitProjectReferences
                ? ModuleDefinition.CreateModuleDefinitionWithImplicitReferences(
                    ConvertPackageToModuleDescriptor(package), packageRoot, moduleConfigFile, projects, allowedDependencies, cyclicalFriendModules)

                    : ModuleDefinition.CreateModuleDefinitionWithExplicitReferences(
                    ConvertPackageToModuleDescriptor(package), package.Path, moduleConfigFile, projects, PathTable, Context.QualifierTable.EmptyQualifierSpaceId.Id);
        }

        /// <summary>
        /// Validates that explicitly specified packages are correct wrt names and file system location
        /// </summary>
        /// TODO: configPath is always invalid for the case of a regular source resolver (and it is not for the default source resolver). Revise this logic.
        protected async Task<bool> CheckUserExplicitlySpecifiedPackagesAsync(IReadOnlyList<AbsolutePath> modules, IReadOnlyList<AbsolutePath> packages, AbsolutePath configPath, AbsolutePath loggingLocation)
        {
            if (modules == null && packages == null)
            {
                // Nothing to check
                return true;
            }

            // Both cannot present at the same time. 'Packages' is the legacy version of 'modules'
            if (modules != null && packages != null)
            {
                Logger.CannotUsePackagesAndModulesSimultaneously(Context.LoggingContext, new Location() { File = configPath.ToString(PathTable) }, loggingLocation.ToString(PathTable));
                return false;
            }

            var packagePaths = modules ?? packages;

            AbsolutePath configDirPath = configPath.IsValid ? configPath.GetParent(PathTable) : AbsolutePath.Invalid;

            Func<AbsolutePath, bool> checkPath =
                path =>
                {
                    Contract.Requires(path.IsValid);

                    AbsolutePath packagePathToCheck = GetPackageConfigPath(path);

                    // If the directory of config.dsc is specified, then ensures that package.config.dsc is beneath that directory.
                    if (configDirPath.IsValid && (packagePathToCheck == configDirPath || !packagePathToCheck.IsWithin(PathTable, configDirPath)))
                    {
                        Logger.ReportSourceResolverPackageFileNotWithinConfiguration(
                            Context.LoggingContext,
                            new Location() { File = configPath.ToString(PathTable) },
                            Name,
                            packagePathToCheck.ToString(PathTable),
                            configDirPath.ToString(PathTable));
                        return false;
                    }
                    
                    // Ensure that package.config.dsc exists in the file system.
                    if (!Engine.FileExists(packagePathToCheck))
                    {
                        Logger.ReportSourceResolverPackageFilesDoNotExist(
                            Context.LoggingContext,
                            new Location() { File = configPath.ToString(PathTable) },
                            Name,
                            packagePathToCheck.ToString(PathTable));

                        return false;
                    }

                    return true;
                };

            return await CheckPathsInParallel(packagePaths, checkPath);
        }

        private static Task<bool> CheckPathsInParallel(IReadOnlyList<AbsolutePath> paths, Func<AbsolutePath, bool> checkFunc)
        {
            // TODO: I'm not sure about this pattern.
            return Task.Run(() => paths.AsParallel().Select(checkFunc).All(b => b));
        }

        /// <summary>
        /// Collects all packages under the root directory, as well as orphan projects, i.e., projects that do not belong to any of the collected packages.
        /// </summary>
        protected Task<bool> CollectPackagesAndProjects(
            IDScriptResolverSettings resolverSettings,
            bool shouldCollectPackage,
            bool shouldCollectOrphanProjects,
            List<AbsolutePath> packagesBuilder,
            List<AbsolutePath> projectsBuilder,
            AbsolutePath outputDirectory,
            bool skipConfigFile)
        {
            Contract.Requires(resolverSettings != null);
            Contract.Requires(resolverSettings.Root.IsValid);
            Contract.Requires(packagesBuilder != null);
            Contract.Requires(projectsBuilder != null);
            Contract.Requires(!string.IsNullOrWhiteSpace(Name));
            Contract.Requires(outputDirectory.IsValid);

            var rootPath = resolverSettings.Root;
            string rootPathStr = rootPath.ToString(PathTable);

            if (!Engine.DirectoryExists(rootPath))
            {
                Logger.ReportSourceResolverRootDirForPackagesDoesNotExist(Context.LoggingContext, Name, rootPath.ToString(PathTable), resolverSettings.GetLocationInfo(PathTable));
                return BoolTask.False;
            }

            Action<Tuple<AbsolutePath, bool>, Action<Tuple<AbsolutePath, bool>>> collect =
                (directoryPathAndPackageFoundStatus, adder) =>
                {
                    var currentDir = directoryPathAndPackageFoundStatus.Item1;

                    if (outputDirectory == currentDir)
                    {
                        // Do not search in output directory.
                        return;
                    }

                    bool packageFound = directoryPathAndPackageFoundStatus.Item2;

                    if (IsWellKnownModuleConfigurationFileExists(currentDir, out var moduleConfigFile))
                    {
                        // Only if package and its config/descriptor exist, then we collect the package.
                        if (shouldCollectPackage)
                        {
                            lock (packagesBuilder)
                            {
                                packagesBuilder.Add(moduleConfigFile);
                            }
                        }

                        // Found a package.
                        packageFound = true;
                    }

                    if (shouldCollectOrphanProjects && !packageFound)
                    {
                        // Collect orphan projects.
                        ExceptionUtilities.HandleRecoverableIOException(
                            (Action)(() =>
                                {
                                    foreach (var filePath in Engine.EnumerateFiles(currentDir).Where(filePath => ExtensionUtilities.IsNonConfigurationFile(filePath.GetName(PathTable).ToString(StringTable))))
                                    {
                                        if (ExtensionUtilities.IsNonConfigurationFile(filePath.GetName(PathTable).ToString(StringTable)))
                                        {
                                            var projectFilePath = filePath;

                                            lock (projectsBuilder)
                                            {
                                                projectsBuilder.Add(projectFilePath);
                                            }
                                        }
                                    }
                                }),
                            ex =>
                                {
                                    Logger.ReportUnableToEnumerateFilesOnCollectingPackagesAndProjects(Context.LoggingContext, Name, currentDir.ToString(PathTable), ex.ToStringDemystified(), resolverSettings.GetLocationInfo((PathTable)this.PathTable));
                                });
                    }

                    ExceptionUtilities.HandleRecoverableIOException(
                        () =>
                        {
                            foreach (var nextDir in Engine.EnumerateDirectories(currentDir, "*"))
                            {
                                var nextConfig = nextDir.Combine(PathTable, Script.Constants.Names.ConfigBc);
                                var nextLegacyConfig = nextDir.Combine(PathTable, Script.Constants.Names.ConfigDsc);

                                if (skipConfigFile || !Engine.FileExists(nextConfig) || !Engine.FileExists(nextLegacyConfig))
                                {
                                    // Recurse to sub-folders if there is no config.
                                    adder(Tuple.Create(nextDir, packageFound));
                                }
                            }
                        },
                        (Action<Exception>)(ex =>
                        {
                            Logger.ReportUnableToEnumerateDirectoriesOnCollectingPackagesAndProjects(
                                Context.LoggingContext, Name, currentDir.ToString(PathTable), ex.ToStringDemystified(), resolverSettings.GetLocationInfo((PathTable)this.PathTable));
                        }));
                };

            return Task.Run(
                () =>
                {
                    ParallelAlgorithms.WhileNotEmpty(
                        new ParallelOptions
                        {
                            CancellationToken = Context.CancellationToken
                        },
                        new[] { Tuple.Create(rootPath, false) }, 
                        collect);
                    return true;
                });
        }

        /// <summary>
        /// Initializes a list of packages
        /// </summary>
        protected async Task<bool> InitPackagesAsync(IReadOnlyList<AbsolutePath> packagesPaths, AbsolutePath configPath)
        {
            Contract.Requires(packagesPaths != null);

            // qualifierSpaceId is invalid if unspecified.
            var packagePathSet = new HashSet<AbsolutePath>(packagesPaths);
#pragma warning disable SA1009 // Closing parenthesis must be spaced correctly
            var initTasks = new Task<(bool success, Workspace moduleWorkspace)>[packagePathSet.Count];
#pragma warning restore SA1009 // Closing parenthesis must be spaced correctly

            int i = 0;
            foreach (var packagePath in packagePathSet)
            {
                var currentPackagePath = packagePath;

                // Per suggestion, create a task to avoid doing things sequential.
                initTasks[i] = Task.Run(async () => await InitPackageFromDescriptorAsync(currentPackagePath, configPath));
                ++i;
            }

            var results = await Task.WhenAll(initTasks);
            bool result = results.All(x => x.success);
            if (result && results.Length != 0)
            {
                // All modules were successfully process,
                // we can create a full workspace for all module configs.
                m_configurationFiles = results.SelectMany(t => t.moduleWorkspace.GetAllSourceFiles()).ToArray();
            }

            return result;
        }

#pragma warning disable SA1009 // Closing parenthesis must be spaced correctly
        /// <summary>
        /// Inits module by processing a given <paramref name="path"/>.
        /// </summary>
        /// <returns>
        /// Returns a tuple that indicates the result of the initalization and the workspace.
        /// Function can't return just a <see cref="ISourceFile"/> for a given module configuration, because module configuration can import other files and these files are part of the workspace as well.
        /// </returns>>
        private async Task<(bool success, Workspace moduleWorkspace)> InitPackageFromDescriptorAsync(AbsolutePath path, AbsolutePath configPath)
#pragma warning restore SA1009 // Closing parenthesis must be spaced correctly
        {
            Contract.Requires(path.IsValid);

            AbsolutePath packageConfigPath = GetPackageConfigPath(path);
            var parseResult = await m_configConversionHelper.ParseValidateAndConvertConfigFileAsync(packageConfigPath);

            if (!parseResult.Success)
            {
                // Error has been reported.
                return (success: false, moduleWorkspace: null);
            }

            Contract.Assert(parseResult.Result != null);

            // Instantiate parsed package-config module, and since this module is qualifier-agnostic,
            // it is instantiated with empty qualifier.
            var module = InstantiateModuleWithDefaultQualifier(parseResult.Result);

            // Decide here whether to use decoration for the init package phase.
            // Let's say no decorators allowed here.
            IDecorator<EvaluationResult> decoratorForInitPackage = null;

            // Create an evaluation context tree and root context.
            using (var contextTree = CreateContext(module, decoratorForInitPackage, EvaluatorConfigurationForConfig, FileType.ModuleConfiguration))
            {
                var context = contextTree.RootContext;

                // TODO: if module is empty for some reason (for instance, because ast translation went wrong),
                // then no error would be recorded here!
                if (!module.IsEmpty)
                {
                    var success = await module.EvaluateAllAsync(context, VisitedModuleTracker.Disabled);

                    if (!success)
                    {
                        // Error has been reported during the evaluation.
                        return (success: false, moduleWorkspace: null);
                    }

                    var bindings = module.GetAllBindings(context).ToList();
                    Contract.Assert(bindings.Count == 1, "Expected AstConverter to produce exactly one binding in the resulting ModuleLiteral when converting a module config file");

                    var binding = bindings.Single();
                    var packageDeclarationArrayLiteral = module
                        .GetOrEvalFieldBinding(
                            context,
                            parseResult.ConfigKeyword,
                            binding: binding.Value,
                            callingLocation: module.Location).Value as ArrayLiteral;

                    if (packageDeclarationArrayLiteral == null)
                    {
                        Logger.PackageDescriptorsIsNotArrayLiteral(Context.LoggingContext, Name, packageConfigPath.ToString(PathTable));
                        return (success: false, moduleWorkspace: null);
                    }

                    for (int i = 0; i < packageDeclarationArrayLiteral.Length; ++i)
                    {
                        var packageDeclarationObjectLiteral = packageDeclarationArrayLiteral[i].Value as ObjectLiteral;

                        if (packageDeclarationObjectLiteral == null)
                        {
                            Logger.PackageDescriptorIsNotObjectLiteral(Context.LoggingContext, Name, packageConfigPath.ToString(PathTable), i);
                            return (success: false, moduleWorkspace: null);
                        }

                        IPackageDescriptor packageConfiguration;

                        try
                        {
                            packageConfiguration = ConfigurationConverter.Convert<IPackageDescriptor>(
                                Context,
                                packageDeclarationObjectLiteral);
                        }
                        catch (ConversionException conversionException)
                        {
                            Logger.ReportConversionException(
                                Context.LoggingContext,
                                new Location() { File = packageConfigPath.ToString(PathTable) },
                                Name,
                                GetConversionExceptionMessage(packageConfigPath, conversionException));
                            return (success: false, moduleWorkspace: null);
                        }

                        ChangeNameResolutionSemanticIfNeeded(packageConfiguration, packageConfigPath.GetName(PathTable).ToString(StringTable));

                        if (!ValidatePackageConfiguration(packageConfiguration, packageConfigPath))
                        {
                            return (success: false, moduleWorkspace: null);
                        }

                        AbsolutePath packageMainFile;

                        // The following check makes sense only for DScript V1
                        if (packageConfiguration.NameResolutionSemantics() == NameResolutionSemantics.ExplicitProjectReferences)
                        {
                            packageMainFile = ComputePackageMainFile(packageConfiguration, packageConfigPath);

                            if (packageMainFile.GetParent(PathTable) != packageConfigPath.GetParent(PathTable))
                            {
                                // Main file must reside in the same directory as package.config.dsc.
                                Logger.PackageMainFileIsNotInTheSameDirectoryAsPackageConfiguration(
                                    Context.LoggingContext,
                                    Name,
                                    packageMainFile.ToString(PathTable),
                                    packageConfigPath.ToString(PathTable));

                                return (success: false, moduleWorkspace: null);
                            }
                        }
                        else
                        {
                            // V2 / implicit name resolution doesn't require a main file for anything other than
                            // defining the root of the package. To avoid conflicts with multiple packages/modules declared
                            // in a single file, use undefined file names of the form "package.config.dscX" where X is the index of the package.
                            packageMainFile = path.GetParent(PathTable).Combine(PathTable, Script.Constants.Names.PackageConfigDsc + i);
                        }

                        PackageId packageId = CreatePackageId(packageConfiguration);

                        CreatePackageAndUpdatePackageMaps(packageId, packageMainFile, packageConfiguration);
                    }
                }
            }

            // re-create workspace without typechecking
            var nonTypeCheckedWorkspace = await m_configConversionHelper.ParseAndValidateConfigFileAsync(packageConfigPath, typecheck: false);
            return (success: nonTypeCheckedWorkspace != null, moduleWorkspace: nonTypeCheckedWorkspace);
        }

        private void ChangeNameResolutionSemanticIfNeeded(IPackageDescriptor packageConfiguration, string packageConfigPath)
        {
            if (ExtensionUtilities.IsModuleConfigDsc(packageConfigPath))
            {
                // if this is the new file name, then the defaults are different and module resolution semantic is always V2
                packageConfiguration.NameResolutionSemantics = NameResolutionSemantics.ImplicitProjectReferences;
            }
        }

        private bool ValidatePackageConfiguration(IPackageDescriptor packageConfiguration, AbsolutePath packageConfigPath)
        {
            if (packageConfiguration.Name == null)
            {
                Logger.ReportMissingField(Context.LoggingContext, Name, packageConfigPath.ToString(PathTable), "name");
                return false;
            }

            if (string.Equals(packageConfiguration.Name, Script.Constants.Names.ConfigAsPackageName, StringComparison.OrdinalIgnoreCase))
            {
                Logger.ReportInvalidPackageNameDueToUsingConfigPackage(
                    Context.LoggingContext,
                    new Location() { File = packageConfigPath.ToString(PathTable) },
                    Name,
                    packageConfigPath.ToString(PathTable),
                    Script.Constants.Names.ConfigAsPackageName);
                return false;
            }

            // If the package resolution semantics is implicit references, then there should not be a main
            // field specified
            if (packageConfiguration.NameResolutionSemantics() ==
                NameResolutionSemantics.ImplicitProjectReferences && packageConfiguration.Main.IsValid)
            {
                Logger.ReportImplicitSemanticsDoesNotAdmitMainFile(
                    Context.LoggingContext,
                    packageConfigPath.ToString(PathTable),
                    packageConfiguration.Name);
                return false;
            }


            if (!ValidateModuleDependencies(packageConfiguration, packageConfigPath))
            {
                return false;
            }

            return true;
        }

        private bool ValidateModuleDependencies(IPackageDescriptor packageConfiguration, AbsolutePath packageConfigPath)
        {
            var allowedModuleDependencies = packageConfiguration.AllowedDependencies;

            // If the package resolution semantics is explicit references, then allowed module references is not allowed
            if (packageConfiguration.NameResolutionSemantics() ==
                NameResolutionSemantics.ExplicitProjectReferences && allowedModuleDependencies != null)
            {
                Logger.ReportExplicitSemanticsDoesNotAdmitAllowedModuleDependencies(
                    Context.LoggingContext,
                    packageConfigPath.ToString(PathTable),
                    packageConfiguration.Name);
                return false;
            }

            var cyclicalFriendModules = packageConfiguration.CyclicalFriendModules;

            // If the package resolution semantics is explicit references, then allowed cyclical friends is not allowed
            if (packageConfiguration.NameResolutionSemantics() ==
                NameResolutionSemantics.ExplicitProjectReferences && cyclicalFriendModules != null)
            {
                Logger.ReportExplicitSemanticsDoesNotAdmitCyclicalFriendModules(
                    Context.LoggingContext,
                    packageConfigPath.ToString(PathTable),
                    packageConfiguration.Name);
                return false;
            }

            // If the global policy to allow cycles is not on, then it the cyclic module list should not be defined
            if (!Configuration.FrontEnd.EnableCyclicalFriendModules() && cyclicalFriendModules != null)
            {
                Logger.ReportCyclicalFriendModulesNotEnabledByPolicy(
                    Context.LoggingContext,
                    packageConfigPath.ToString(PathTable),
                    packageConfiguration.Name);
                return false;
            }

            // The list of allowed modules, if specified, should not contain duplicates
            if (allowedModuleDependencies != null)
            {
                var duplicateModules = ComputeDuplicates(allowedModuleDependencies);

                if (duplicateModules.Count > 0)
                {
                    Logger.ReportDuplicateAllowedModuleDependencies(
                        Context.LoggingContext,
                        packageConfigPath.ToString(PathTable),
                        packageConfiguration.Name,
                        string.Join(", ", duplicateModules));

                    return false;
                }
            }

            // The list of cyclical friends, if specified, should not contain duplicates
            if (cyclicalFriendModules != null)
            {
                var duplicateModules = ComputeDuplicates(cyclicalFriendModules);

                if (duplicateModules.Count > 0)
                {
                    Logger.ReportDuplicateCyclicalFriendModules(
                        Context.LoggingContext,
                        packageConfigPath.ToString(PathTable),
                        packageConfiguration.Name,
                        string.Join(", ", duplicateModules));

                    return false;
                }
            }

            return true;
        }

        private static List<string> ComputeDuplicates(IEnumerable<string> moduleList)
        {
            return moduleList
                .GroupBy(module => module)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();
        }

        private PackageId CreatePackageId(IPackageDescriptor packageConfiguration)
        {
            // TODO: Add publisher, and parse version properly.
            return string.IsNullOrWhiteSpace(packageConfiguration.Version)
                ? PackageId.Create(StringId(packageConfiguration.Name))
                : PackageId.Create(
                    StringId(packageConfiguration.Name),
                    PackageVersion.Create(
                        StringId(packageConfiguration.Version),
                        StringId(packageConfiguration.Version)));
        }

        private StringId StringId(string value)
        {
            return BuildXL.Utilities.StringId.Create(StringTable, value);
        }

        /// <summary>
        /// Computes the main file of a package. This is either explicitly defined on the package, or is `package.dsc` in the package's root directory.
        /// </summary>
        /// <remarks>
        /// This method is only valid for V1 packages.
        /// </remarks>
        /// <returns>The absolute path to the package's Main file.</returns>
        private AbsolutePath ComputePackageMainFile(IPackageDescriptor packageConfiguration, AbsolutePath packageConfigPath)
        {
            Contract.Requires(packageConfiguration.NameResolutionSemantics() == NameResolutionSemantics.ExplicitProjectReferences);

            // Get package main file; package.dsc if not specified.
            return packageConfiguration.Main.IsValid ?
                packageConfiguration.Main :
                packageConfigPath.GetParent(PathTable).Combine(PathTable, m_packageDsc);
        }

        private AbsolutePath GetPackageConfigPath(AbsolutePath path)
        {
            Contract.Requires(path.IsValid);

            PathAtom fileName = path.GetName(PathTable);

            if (IsModuleConfigFile(fileName))
            {
                // File name is package.config.dsc, module.config.bm or module.config.dsc. Use as-is.
                return path;
            }

            if (IsLegacyPackageFile(fileName))
            {
                // File name is package.dsc.
                // Users may specify package.dsc instead, so change it to package.config.dsc.
                // This provide backward compatibility.
                return path.ChangeExtension(
                    PathTable,
                    m_dotConfigDotDscExtension);
            }

            // File name is neither package.config.dsc nor package.dsc.
            // We assume that users specify a directory containing a package.
            return path.Combine(PathTable, m_packageConfigDsc);
        }

        private bool IsWellKnownModuleConfigurationFileExists(AbsolutePath basePath, out AbsolutePath outputConfig)
        {
            foreach (var wellKnownConfigFileName in Script.Constants.Names.WellKnownModuleConfigFileNames)
            {
                outputConfig = basePath.Combine(PathTable, wellKnownConfigFileName);
                if (base.Engine.FileExists(outputConfig))
                {
                    return true;
                }
            }

            outputConfig = AbsolutePath.Invalid;
            return false;
        }

        private bool IsWellKnownConfigFile(AbsolutePath fileName)
        {
            return IsWellKnownConfigFile(fileName.GetName(PathTable));
        }

        private bool IsRootConfigurationFileExists(AbsolutePath searchPath, out AbsolutePath outputConfig)
        {
            // Check that the config package owns the search folder.
            // This prevents from file system probing in case of a miss.
            var legacyConfigFile = searchPath.Combine(PathTable, m_configDsc);
            if (Engine.FileExists(legacyConfigFile))
            {
                outputConfig = legacyConfigFile;
                return true;
            }

            var configFile = searchPath.Combine(PathTable, m_configBc);

            if (Engine.FileExists(configFile))
            {
                outputConfig = configFile;
                return true;
            }

            outputConfig = AbsolutePath.Invalid;
            return false;
        }
        
        private void CreatePackageAndUpdatePackageMaps(PackageId id, AbsolutePath mainFile, IPackageDescriptor descriptor)
        {
            // id can be invalid due to config as a package.
            Contract.Requires(mainFile.IsValid);
            Contract.Requires(descriptor != null);

            var package = Package.Create(id, mainFile, descriptor);

            // We populate the package moduleId, so the workspace will use it when projecting packages into module descriptors
            // TODO: The moduleId is later overridden at evaluation time! Consider removing the dependency on the env
            package.ModuleId = ModuleIdProvider.GetNextId();

            UpdatePackageMap(package);
        }

        /// <summary>
        /// Updates the package and package dictionary with a new package
        /// </summary>
        protected void UpdatePackageMap(Package package)
        {
            var dirPath = package.Path.GetParent(PathTable);
            m_packages[package.Id] = package;

            var packagesInDir = m_packageDirectories.GetOrAdd(dirPath, new List<Package>());
            packagesInDir.Add(package);
        }

        private bool ValidateFoundPackages()
        {
            bool validate = true;

            foreach (var packages in m_packageDirectories.Values)
            {
                if (packages.Count > 1)
                {
                    validate &= ValidateMultiplePackages(packages);
                }
            }

            return validate;
        }

        private bool ValidateMultiplePackages(IReadOnlyList<Package> packages)
        {
            // TODO: Move validation into the namespace layer (Work item: 934651).
            var projectOwners = new Dictionary<AbsolutePath, PackageId>();
            var packageOwningAllProjects = PackageId.Invalid;

            foreach (var package in packages)
            {
                if (package == m_configAsPackage)
                {
                    continue;
                }

                if (packageOwningAllProjects.IsValid)
                {
                    // There is an existing package that owns all projects.
                    // Thus, disjointness between projects cannot be satisfied.
                    // Log error.
                    Logger.FailAddingPackageDueToPackageOwningAllProjectsExists(
                        Context.LoggingContext,
                        Name,
                        GetModuleConfigurationPathFromParent(package.Path.GetParent(PathTable)),
                        package.Id.ToString(StringTable),
                        packageOwningAllProjects.ToString(StringTable));

                    return false;
                }

                if (package.DescriptorProjects == null && projectOwners.Count != 0)
                {
                    // The current packages wants to owns all projects, but
                    // some existing projects have been owned by other packages.
                    Logger.FailAddingPackageBecauseItWantsToOwnAllProjectsButSomeAreOwnedByOtherPackages(
                        Context.LoggingContext,
                        Name,
                        GetModuleConfigurationPathFromParent(package.Path.GetParent(PathTable)),
                        package.Id.ToString(StringTable));

                    return false;
                }

                Contract.Assert(!packageOwningAllProjects.IsValid);

                if (package.DescriptorProjects == null)
                {
                    // This package wants to owns all projects.
                    Contract.Assert(package.Id.IsValid);
                    Contract.Assert(projectOwners.Count == 0);

                    projectOwners.Add(package.Path, package.Id);
                    packageOwningAllProjects = package.Id;
                }
                else
                {
                    // Only validate ownership of the main file in V1 packages
                    if (package.Descriptor.NameResolutionSemantics != NameResolutionSemantics.ImplicitProjectReferences && projectOwners.TryGetValue(package.Path, out PackageId owningPackage))
                    {
                        // Main entry project in V1 package is owned by another package.
                        Logger.FailAddingPackageBecauseItsProjectIsOwnedByAnotherPackage(
                            Context.LoggingContext,
                            Name,
                            GetModuleConfigurationPathFromParent(package.Path.GetParent(PathTable)),
                            package.Id.ToString(StringTable),
                            package.Path.ToString(PathTable),
                            owningPackage.ToString(StringTable));

                        return false;
                    }

                    if (package.Descriptor.NameResolutionSemantics != NameResolutionSemantics.ImplicitProjectReferences)
                    {
                        projectOwners.Add(package.Path, package.Id);
                    }

                    foreach (var descriptorProject in package.DescriptorProjects)
                    {
                        if (projectOwners.TryGetValue(descriptorProject, out owningPackage)
                            && owningPackage != package.Id)
                        {
                            // Project has been owned by another project.
                            Logger.FailAddingPackageBecauseItsProjectIsOwnedByAnotherPackage(
                                Context.LoggingContext,
                                Name,
                                GetModuleConfigurationPathFromParent(package.Path.GetParent(PathTable)),
                                package.Id.ToString(StringTable),
                                descriptorProject.ToString(PathTable),
                                owningPackage.ToString(StringTable));

                            return false;
                        }

                        if (!projectOwners.ContainsKey(descriptorProject))
                        {
                            projectOwners.Add(descriptorProject, package.Id);
                        }
                    }
                }
            }

            return true;
        }

        private string GetModuleConfigurationPathFromParent(AbsolutePath moduleConfigurationParentFolder)
        {
            IsWellKnownModuleConfigurationFileExists(moduleConfigurationParentFolder, out var moduleConfig);

            Contract.Assert(moduleConfig.IsValid);
            return moduleConfig.ToString(PathTable);
        }

        /// <summary>
        /// Returns true if a given candidate is a package config file name (including legacy name).
        /// </summary>
        /// <remarks>
        /// The comparison is case insensitive.
        /// </remarks>
        private bool IsModuleConfigFile(PathAtom candidate)
        {
            return candidate.CaseInsensitiveEquals(StringTable, m_packageConfigDsc) ||
                   candidate.CaseInsensitiveEquals(StringTable, m_moduleConfigBm) ||
                   candidate.CaseInsensitiveEquals(StringTable, m_moduleConfigDsc);
        }

        /// <summary>
        /// Returns true if a given candidate is a package config file name (including legacy name) or a root config file name.
        /// </summary>
        /// <remarks>
        /// The comparison is case insensitive.
        /// </remarks>
        private bool IsWellKnownConfigFile(PathAtom candidate)
        {
            return IsModuleConfigFile(candidate) ||
                   candidate.CaseInsensitiveEquals(StringTable, m_configDsc) ||
                   candidate.CaseInsensitiveEquals(StringTable, m_configBc);
        }

        /// <summary>
        /// Returns true if a given candidate is a legacy package file name.
        /// </summary>
        /// <remarks>
        /// The comparison is case insensitive.
        /// </remarks>
        private bool IsLegacyPackageFile(PathAtom candidate)
        {
            return candidate.CaseInsensitiveEquals(StringTable, m_packageDsc);
        }
    }
}
