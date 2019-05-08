// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.FrontEnd.Nuget.Tracing;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.Mutable;
using BuildXL.FrontEnd.Sdk.Workspaces;
using BuildXL.FrontEnd.Workspaces;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Interop.MacOS;
using BuildXL.Native.IO;
using BuildXL.Processes;
using BuildXL.Processes.Containers;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using TypeScript.Net.DScript;
using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Nuget
{
    /// <summary>
    /// A workspace module resolver for NuGet.
    /// </summary>
    /// <remarks>
    /// DScript specs are generated for downloaded Nuget packages, unless the package
    /// already contains DScript specs. An embedded regular source resolver
    /// is set to pick up embedded specs.
    /// </remarks>
    public sealed class WorkspaceNugetModuleResolver : IWorkspaceModuleResolver
    {
        private readonly INugetStatistics m_statistics;
        internal const string NugetResolverName = "Nuget";

        private const int MaxPackagesToDisplay = 30;

        private const int MaxRetryCount = 2;
        private const int RetryDelayMs = 100;

        private NugetFrameworkMonikers m_nugetFrameworkMonikers;

        // These are set during Initialize
        private INugetResolverSettings m_resolverSettings;
        private QualifierId[] m_requestedQualifiers;
        private PackageRegistry m_packageRegistry;
        private IReadOnlyDictionary<string, string> m_repositories;
        private FrontEndContext m_context;
        private FrontEndHost m_host;

        private PathTable PathTable => m_context.PathTable;

        // Cached result from calling DownloadPackagesAndGenerateSpecsAsync.
        private CachedTask<Possible<NugetGenerationResult>> m_nugetGenerationResult = CachedTask<Possible<NugetGenerationResult>>.Create();

        // This resolver is used for the specs that may come embedded with the NuGet packages
        private readonly WorkspaceSourceModuleResolver m_embeddedSpecsResolver;

        private NugetResolverOutputLayout m_resolverOutputLayout;
        private IConfiguration m_configuration;

        private readonly Lazy<AbsolutePath> m_nugetToolFolder;

        /// <inheritdoc />
        public string Kind => KnownResolverKind.NugetResolverKind;

        /// <inheritdoc />
        public string Name { get; private set; }

        private readonly bool m_useMonoBasedNuGet;

        /// <nodoc/>
        public WorkspaceNugetModuleResolver(
            StringTable stringTable,
            IFrontEndStatistics statistics)
        {
            m_statistics = statistics.NugetStatistics;
            m_embeddedSpecsResolver = new WorkspaceSourceModuleResolver(stringTable, statistics, logger: null);

            m_useMonoBasedNuGet = OperatingSystemHelper.IsUnixOS;

            m_nugetToolFolder = new Lazy<AbsolutePath>(
                () => m_host.GetFolderForFrontEnd(NugetResolverName).Combine(PathTable, "nuget"),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <inheritdoc/>
        public async ValueTask<Possible<ModuleDefinition>> TryGetModuleDefinitionAsync(ModuleDescriptor moduleDescriptor)
        {
            var maybeResult = await DownloadPackagesAndGenerateSpecsIfNeededInternal();

            return await maybeResult.ThenAsync<ModuleDefinition>(async result =>
            {
                // If the descriptor is not in the collection of generated projects, we try looking at embedded specs
                if (!result.GeneratedProjectsByModuleDescriptor.TryGetValue(moduleDescriptor, out var pathToModuleConfig))
                {
                    var maybeEmbeddedResult = await m_embeddedSpecsResolver.TryGetModuleDefinitionAsync(moduleDescriptor);
                    if (maybeEmbeddedResult.Succeeded)
                    {
                        return maybeEmbeddedResult.Result;
                    }

                    return new ModuleNotOwnedByThisResolver(moduleDescriptor);
                }

                var packageRoot = pathToModuleConfig.GetParent(PathTable);

                // The path to the NuGet spec is in the same directory as the package configuration file with a known name
                var pathToSpec = packageRoot.Combine(PathTable, Names.PackageDsc);

                return ModuleDefinition.CreateModuleDefinitionWithImplicitReferences(
                    moduleDescriptor,
                    packageRoot,
                    pathToModuleConfig,
                    new[] { pathToSpec },
                    allowedModuleDependencies: null,
                    cyclicalFriendModules: null); // A NuGet package does not have any module dependency restrictions nor whitelists cycles
            });
        }

        /// <inheritdoc/>
        public async ValueTask<Possible<IReadOnlyCollection<ModuleDescriptor>>> TryGetModuleDescriptorsAsync(ModuleReferenceWithProvenance moduleReference)
        {
            var maybeResult = await DownloadPackagesAndGenerateSpecsIfNeededInternal();

            return await maybeResult.ThenAsync(async result =>
            {
                // If there is no descriptors in the generated specs, we check the embedded ones
                if (!result.GeneratedProjectsByModuleName.TryGetValue(moduleReference.Name, out var descriptors))
                {
                    var embeddedDescriptors = await m_embeddedSpecsResolver.TryGetModuleDescriptorsAsync(moduleReference);
                    return embeddedDescriptors;
                }

                return new Possible<IReadOnlyCollection<ModuleDescriptor>>(descriptors);
            });
        }

        /// <inheritdoc/>
        public async ValueTask<Possible<ModuleDescriptor>> TryGetOwningModuleDescriptorAsync(AbsolutePath specPath)
        {
            var maybeResult = await DownloadPackagesAndGenerateSpecsIfNeededInternal();

            return await maybeResult.ThenAsync<ModuleDescriptor>(async result =>
            {
                // At generation time, this resolver lays on disk packages in a flat and uniform way: a project.bp next to a module.config.bm (or their legacy name versions).
                // So determining ownership is straightforward

                foreach (var configFileName in Names.WellKnownModuleConfigFileNames)
                {
                    var configPath = specPath.GetParent(PathTable).Combine(PathTable, configFileName);
                    if (result.GeneratedProjectsByPath.TryGetValue(configPath, out var moduleDescriptor))
                    {
                        return moduleDescriptor;
                    }
                }

                // If the owner is not part of the generated specs, we try with the embedded resolver
                var maybeEmbeddedDescriptor = await m_embeddedSpecsResolver.TryGetOwningModuleDescriptorAsync(specPath);

                if (maybeEmbeddedDescriptor.Succeeded)
                {
                    return maybeEmbeddedDescriptor.Result;
                }

                return new SpecNotOwnedByResolverFailure(specPath.ToString(PathTable));
            });
        }

        /// <inheritdoc/>
        public async ValueTask<Possible<HashSet<ModuleDescriptor>>> GetAllKnownModuleDescriptorsAsync()
        {
            var maybeResult = await DownloadPackagesAndGenerateSpecsIfNeededInternal();

            return
                await maybeResult.ThenAsync(
                    async result =>
                    {
                        // We augment the generated descriptors with the embedded ones
                        // Generated descriptors win over embedded ones when they are the same
                        var generatedDescriptors = new HashSet<ModuleDescriptor>(
                            result.GeneratedProjectsByModuleDescriptor.Keys,
                            ModuleDescriptorWorkspaceComparer.Comparer);

                        var embeddedDescriptors = await m_embeddedSpecsResolver.GetAllKnownModuleDescriptorsAsync();

                        if (!embeddedDescriptors.Succeeded)
                        {
                            return embeddedDescriptors;
                        }

                        generatedDescriptors.UnionWith(embeddedDescriptors.Result);

                        return generatedDescriptors;
                    });
        }

        /// <summary>
        /// Returns all packages known by this resolver. This includes potentially embedded packages
        /// </summary>
        public async Task<Possible<IEnumerable<Package>>> GetAllKnownPackagesAsync()
        {
            var maybeResult = await DownloadPackagesAndGenerateSpecsIfNeededInternal();

            return await maybeResult.ThenAsync(
                async result =>
                      {
                          var maybeDefinitions = await this.GetAllModuleDefinitionsAsync();
                          return maybeDefinitions.Then(
                              definitions => definitions.Select(GetPackageForGeneratedProject));
                      });
        }

        private Package GetPackageForGeneratedProject(ModuleDefinition moduleDefinition)
        {
            var moduleDescriptor = moduleDefinition.Descriptor;
            var id = GetPackageIdFromModuleDescriptor(moduleDescriptor);

            var packageDescriptor = new PackageDescriptor
            {
                Name = moduleDescriptor.Name,
                Main = moduleDefinition.MainFile,
                NameResolutionSemantics = NameResolutionSemantics.ImplicitProjectReferences,
                Publisher = null,
                Version = moduleDescriptor.Version,
                Projects = new List<AbsolutePath>(moduleDefinition.Specs),
            };

            // We know that the generated Nuget package config does not have any qualifier space defined.
            var package = Package.Create(id, moduleDefinition.ModuleConfigFile, packageDescriptor);
            package.ModuleId = moduleDescriptor.Id;

            return package;
        }

        private PackageId GetPackageIdFromModuleDescriptor(ModuleDescriptor moduleDescriptor)
        {
            return string.IsNullOrWhiteSpace(moduleDescriptor.Version)
                ? PackageId.Create(StringId.Create(m_context.StringTable, moduleDescriptor.Name))
                : PackageId.Create(
                    StringId.Create(m_context.StringTable, moduleDescriptor.Name),
                    PackageVersion.Create(
                        StringId.Create(m_context.StringTable, moduleDescriptor.Version),
                        StringId.Create(m_context.StringTable, moduleDescriptor.Version)));
        }

        /// <summary>
        /// Uses the TypeScript parser to parse the spec
        /// TODO: The nuget resolver is generating specs on disk that are later parsed. Consider avoiding the roundtrip and generating ISourceFile directly
        /// </summary>
        public async Task<Possible<ISourceFile>> TryParseAsync(AbsolutePath pathToParse, AbsolutePath moduleOrConfigPathPromptingParse, ParsingOptions parsingOptions = null)
        {
            Contract.Requires(pathToParse.IsValid);

            var specPathString = pathToParse.ToString(PathTable);

            if (!File.Exists(specPathString))
            {
                return new CannotReadSpecFailure(specPathString, CannotReadSpecFailure.CannotReadSpecReason.SpecDoesNotExist);
            }

            if (Directory.Exists(specPathString))
            {
                return new CannotReadSpecFailure(specPathString, CannotReadSpecFailure.CannotReadSpecReason.PathIsADirectory);
            }

            return await m_embeddedSpecsResolver.TryParseAsync(pathToParse, moduleOrConfigPathPromptingParse, parsingOptions);
        }

        /// <inheritdoc/>
        public string DescribeExtent()
        {
            var maybeModules = GetAllKnownModuleDescriptorsAsync().GetAwaiter().GetResult();

            if (!maybeModules.Succeeded)
            {
                return string.Format(CultureInfo.InvariantCulture, "Module extent could not be computed. {0}",
                    maybeModules.Failure.Describe());
            }

            return string.Join(
                ", ",
                maybeModules.Result.Select(module => module.Name));
        }

        /// <inheritdoc/>
        public bool TryInitialize(FrontEndHost host, FrontEndContext context, IConfiguration configuration, IResolverSettings resolverSettings, QualifierId[] requestedQualifiers)
        {
            Contract.Requires(context != null);
            Contract.Requires(host != null);
            Contract.Requires(configuration != null);
            Contract.Requires(resolverSettings != null);
            Contract.Requires(requestedQualifiers?.Length > 0);

            var nugetResolverSettings = resolverSettings as INugetResolverSettings;
            Contract.Assert(nugetResolverSettings != null);

            m_context = context;
            m_resolverSettings = nugetResolverSettings;
            m_requestedQualifiers = requestedQualifiers;

            m_repositories = ComputeRepositories(nugetResolverSettings.Repositories);
            var possibleRegistry = PackageRegistry.Create(context, m_resolverSettings.Packages);
            if (!possibleRegistry.Succeeded)
            {
                Logger.Log.NugetFailedDownloadPackagesAndGenerateSpecs(m_context.LoggingContext, possibleRegistry.Failure.DescribeIncludingInnerFailures());
                return false;
            }
            m_packageRegistry = possibleRegistry.Result;
            m_host = host;
            m_configuration = configuration;
            Name = resolverSettings.Name;

            var resolverFolder = m_host.GetFolderForFrontEnd(NugetResolverName);
            m_resolverOutputLayout = new NugetResolverOutputLayout(PathTable, resolverFolder);

            m_nugetFrameworkMonikers = new NugetFrameworkMonikers(context.StringTable);

            return true;
        }

        /// <summary>
        /// Configures the resolver to work with a fixed set of packages for testing purposes
        /// </summary>
        public void SetDownloadedPackagesForTesting(IDictionary<string, NugetAnalyzedPackage> downloadedPackages)
        {
            var possiblePackages = GenerateSpecsForDownloadedPackages(downloadedPackages);
            var result = m_embeddedSpecsResolver.TryInitialize(m_host, m_context, m_configuration, CreateSettingsForEmbeddedResolver(downloadedPackages.Values), m_requestedQualifiers);
            Contract.Assert(result);

            Analysis.IgnoreResult(
                m_nugetGenerationResult
                    .GetOrCreate(
                        (@this: this, possiblePackages),
                        tpl => Task.FromResult(tpl.@this.GetNugetGenerationResultFromDownloadedPackages(tpl.possiblePackages))
                    )
                    .GetAwaiter()
                    .GetResult()
                );
        }

        /// <nodoc />
        public Task ReinitializeResolver()
        {
            return Task.FromResult<object>(null);
        }

        /// <inheritdoc />
        public ISourceFile[] GetAllModuleConfigurationFiles()
        {
            return CollectionUtilities.EmptyArray<ISourceFile>();
        }

        private static IReadOnlyDictionary<string, string> ComputeRepositories(IReadOnlyDictionary<string, string> repositories)
        {
            var repositoriesSpecifiedExplicitly = repositories?.Any() == true;
            return repositoriesSpecifiedExplicitly
                ? repositories
                : new Dictionary<string, string>
                {
                    ["nuget"] = "https://api.nuget.org/v3/index.json",
                };
        }

        private Task<Possible<NugetGenerationResult>> DownloadPackagesAndGenerateSpecsIfNeededInternal()
        {
            return m_nugetGenerationResult.GetOrCreate(this, @this => @this.DownloadPackagesAndGenerateSpecsAsync());
        }

        private AbsolutePath GetNugetToolFolder()
        {
            return m_nugetToolFolder.Value;
        }

        private AbsolutePath GetNugetConfigPath()
        {
            return GetNugetToolFolder().Combine(PathTable, "NuGet.config");
        }

        private async Task<Possible<NugetGenerationResult>> DownloadPackagesAndGenerateSpecsAsync()
        {
            // Log if the full package restore is requested.
            if (m_configuration.FrontEnd.ForcePopulatePackageCache())
            {
                Logger.Log.ForcePopulateTheCacheOptionWasSpecified(m_context.LoggingContext);
            }

            if (m_configuration.FrontEnd.UsePackagesFromFileSystem())
            {
                Logger.Log.UsePackagesFromDisOptionWasSpecified(m_context.LoggingContext);
            }

            // This is used when computing dependencies to have a quick access to the list of configured packages
            var packageValidity = m_packageRegistry.Validate();
            if (!packageValidity.Succeeded)
            {
                Logger.Log.NugetFailedDownloadPackagesAndGenerateSpecs(m_context.LoggingContext, packageValidity.Failure.DescribeIncludingInnerFailures());
                return packageValidity.Failure;
            }

            using (var nugetEndToEndStopWatch = m_statistics.EndToEnd.Start())
            {
                var possiblePaths = await TryDownloadNugetAsync(m_resolverSettings.Configuration, GetNugetToolFolder());
                if (!possiblePaths.Succeeded)
                {
                    Logger.Log.NugetFailedDownloadPackagesAndGenerateSpecs(m_context.LoggingContext, possiblePaths.Failure.DescribeIncludingInnerFailures());
                    return possiblePaths.Failure;
                }

                var possibleNugetConfig = CreateNuGetConfig(m_repositories);

                var possibleNuGetConfig = TryWriteXmlConfigFile(
                    package: null,
                    targetFile: GetNugetConfigPath(),
                    xmlDoc: possibleNugetConfig.Result);

                if (!possibleNuGetConfig.Succeeded)
                {
                    Logger.Log.NugetFailedDownloadPackagesAndGenerateSpecs(m_context.LoggingContext, possibleNuGetConfig.Failure.DescribeIncludingInnerFailures());
                    return possibleNuGetConfig.Failure;
                }

                // Will contain all packages successfully downloaded and analyzed
                var restoredPackagesById = new Dictionary<string, NugetAnalyzedPackage>();

                // We keep a bit array that represents what packages have been downloaded so far. Index matches nugetSettings.Packages.
                var stopWatch = Stopwatch.StartNew();
                var nugetProgress = m_resolverSettings.Packages.Select(p => new NugetProgress(p, stopWatch)).ToArray();

                using (
                    new StoppableTimer(
                        () => LogDownloadProgress(nugetProgress, m_resolverSettings.Packages),
                        0,
                        m_host.Engine.GetTimerUpdatePeriod))
                {
                    var concurrencyLevel = GetRestoreNugetConcurrency();

                    try
                    {
                        var aggregateResult = new Possible<NugetAnalyzedPackage>[nugetProgress.Length];

                        Parallel.For(fromInclusive: 0,
                            toExclusive: aggregateResult.Length,
                            new ParallelOptions()
                            {
                                MaxDegreeOfParallelism = concurrencyLevel,
                                CancellationToken = m_context.CancellationToken,
                            },
                            (index) =>
                            {
                                aggregateResult[index] = TryRestorePackageAsync(nugetProgress[index], possiblePaths.Result).GetAwaiter().GetResult();
                                return;
                            });

                        // Log first, and only after that we can process the results.
                        if (m_statistics.Failures.Count == 0)
                        {
                            Logger.Log.NugetPackagesAreRestored(m_context.LoggingContext, m_statistics.AllSuccessfulPackages(), (long)nugetEndToEndStopWatch.Elapsed.TotalMilliseconds);
                        }

                        Logger.Log.NugetStatistics(m_context.LoggingContext, m_statistics.PackagesFromDisk.Count, m_statistics.PackagesFromCache.Count, m_statistics.PackagesFromNuget.Count,
                            m_statistics.Failures.Count, (long)nugetEndToEndStopWatch.Elapsed.TotalMilliseconds, concurrencyLevel);

                        for (var i = 0; i < aggregateResult.Length; ++i)
                        {
                            var nugetPackage = nugetProgress[i];
                            var packageResult = aggregateResult[i];

                            if (!packageResult.Succeeded)
                            {
                                Logger.Log.NugetFailedDownloadPackage(
                                    m_context.LoggingContext,
                                    nugetPackage.Package.GetPackageIdentity(),
                                    packageResult.Failure.DescribeIncludingInnerFailures());
                                return packageResult.Failure;
                            }
                            else
                            {
                                restoredPackagesById[packageResult.Result.ActualId] = packageResult.Result;
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Cancellation was triggered due to an error or an user's input
                        return new WorkspaceModuleResolverGenericInitializationFailure(Kind);
                    }
                }

                var possiblePackages = GenerateSpecsForDownloadedPackages(restoredPackagesById);

                // At this point we know which are all the packages that contain embedded specs, so we can initialize the embedded resolver properly
                if (!m_embeddedSpecsResolver.TryInitialize(m_host, m_context, m_configuration, CreateSettingsForEmbeddedResolver(restoredPackagesById.Values), m_requestedQualifiers))
                {
                    var failure = new WorkspaceModuleResolverGenericInitializationFailure(Kind);
                    Logger.Log.NugetFailedDownloadPackagesAndGenerateSpecs(m_context.LoggingContext, failure.DescribeIncludingInnerFailures());
                    return failure;
                }

                return GetNugetGenerationResultFromDownloadedPackages(possiblePackages);
            }
        }

        private int GetRestoreNugetConcurrency()
        {
            // Nuget concurrency depends on the disk kind: for sdd disk the concurrency is twice as high as the number of cores
            // and for hdd - is twice as low.

            var configuredConcurrency = m_configuration.FrontEnd.MaxRestoreNugetConcurrency;
            string message;
            int nugetConcurrency;

            if (configuredConcurrency != null)
            {
                nugetConcurrency = configuredConcurrency.Value;
                message = I($"Using the user-specified concurrency {configuredConcurrency}.");
            }
            else
            {
                if (m_configuration.DoesSourceDiskDriveHaveSeekPenalty(PathTable))
                {
                    nugetConcurrency = Environment.ProcessorCount / 2;
                    message = I($"Lowering restore package concurrency to {nugetConcurrency} because a source drive is on HDD.");
                }
                else
                {
                    nugetConcurrency = Math.Min(16, Environment.ProcessorCount * 2);
                    message = I($"Increasing restore package concurrency to {nugetConcurrency} because a source drive is on SSD.");
                }
            }

            Logger.Log.NugetConcurrencyLevel(m_context.LoggingContext, message);

            return nugetConcurrency;
        }

        private async Task<Possible<NugetAnalyzedPackage>> TryRestorePackageAsync(
            NugetProgress progress,
            IReadOnlyList<AbsolutePath> credentialProviderPaths)
        {
            progress.StartRunning();

            // Enforce the method to run in a thread pool
            await Awaitables.ToThreadPool();

            var package = progress.Package;
            try
            {
                var layout = NugetPackageOutputLayout.Create(
                    PathTable,
                    package,
                    nugetTool: credentialProviderPaths[0],
                    nugetConfig: GetNugetConfigPath(),
                    resolverLayout: m_resolverOutputLayout);

                var possiblePkg = await TryRestorePackageWithCache(package, progress, layout, credentialProviderPaths);

                if (!possiblePkg.Succeeded)
                {
                    m_statistics.Failures.Increment();
                    progress.FailedDownload();
                    return possiblePkg.Failure;
                }

                IncrementStatistics(possiblePkg.Result.PackageDownloadResult.Source);

                var analyzedPackage = AnalyzeNugetPackage(
                    possiblePkg.Result,
                    m_resolverSettings.DoNotEnforceDependencyVersions);
                if (!analyzedPackage.Succeeded)
                {
                    m_statistics.Failures.Increment();
                    progress.FailedDownload();
                    return analyzedPackage;
                }

                progress.CompleteDownload();
                return analyzedPackage;
            }
            catch (Exception e)
            {
                var str = e.ToStringDemystified();
                Logger.Log.NugetUnhandledError(m_context.LoggingContext, package.Id, package.Version, str);
                m_statistics.Failures.Increment();
                progress.FailedDownload();
                return new NugetFailure(NugetFailure.FailureType.UnhandledError, e);
            }
        }

        private void IncrementStatistics(PackageSource source)
        {
            switch (source)
            {
                case PackageSource.Disk:
                    m_statistics.PackagesFromDisk.Increment();
                    break;
                case PackageSource.Cache:
                    m_statistics.PackagesFromCache.Increment();
                    break;
                case PackageSource.RemoteStore:
                    m_statistics.PackagesFromNuget.Increment();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(source), source, null);
            }
        }

        private Possible<NugetGenerationResult> GetNugetGenerationResultFromDownloadedPackages(
            Dictionary<string, Possible<AbsolutePath>> possiblePackages)
        {
            var generatedProjectsByPackageDescriptor = new Dictionary<ModuleDescriptor, AbsolutePath>(m_resolverSettings.Packages.Count);
            var generatedProjectsByPath = new Dictionary<AbsolutePath, ModuleDescriptor>(m_resolverSettings.Packages.Count);
            var generatedProjectsByPackageName = new MultiValueDictionary<string, ModuleDescriptor>(m_resolverSettings.Packages.Count);

            foreach (var packageName in possiblePackages.Keys)
            {
                var possiblePackage = possiblePackages[packageName];
                if (!possiblePackage.Succeeded)
                {
                    Logger.Log.NugetFailedGenerationResultFromDownloadedPackage(
                        m_context.LoggingContext,
                        packageName,
                        possiblePackage.Failure.DescribeIncludingInnerFailures());
                    return possiblePackage.Failure;
                }

                var moduleDescriptor = ModuleDescriptor.CreateWithUniqueId(packageName, this);

                generatedProjectsByPackageDescriptor[moduleDescriptor] = possiblePackage.Result;
                generatedProjectsByPath[possiblePackage.Result] = moduleDescriptor;
                generatedProjectsByPackageName.Add(moduleDescriptor.Name, moduleDescriptor);
            }

            return new NugetGenerationResult(generatedProjectsByPackageDescriptor, generatedProjectsByPath, generatedProjectsByPackageName);
        }

        /// <summary>
        /// Creates a source resolver settings that points to the package root folder so embedded specs can be picked up
        /// </summary>
        private static IResolverSettings CreateSettingsForEmbeddedResolver(IEnumerable<NugetAnalyzedPackage> values)
        {
            // We point the embedded resolver to all the downloaded packages that contain a DScript package config file
            var embeddedSpecs = values.Where(nugetPackage => nugetPackage.PackageOnDisk.ModuleConfigFile.IsValid)
                .Select(nugetPackage => nugetPackage.PackageOnDisk.ModuleConfigFile).ToArray();

            var settings = new SourceResolverSettings { Modules = embeddedSpecs };

            return settings;
        }

        /// <summary>
        /// Generate DScript specs for downloaded packages.
        /// </summary>
        /// <remarks>
        /// Topo sorts the collection of downloaded packages to compute the right qualifier space based on dependencies and creates DScript specs.
        /// TODO: this is done in a single-threaded fashion. Consider making this multi-threaded and cacheable
        /// </remarks>
        internal Dictionary<string, Possible<AbsolutePath>> GenerateSpecsForDownloadedPackages(
            IDictionary<string, NugetAnalyzedPackage> analyzedPackages)
        {
            using (var sw = m_statistics.SpecGeneration.Start())
            {
                Logger.Log.NugetRegeneratingNugetSpecs(m_context.LoggingContext);

                var result = new Dictionary<string, Possible<AbsolutePath>>(analyzedPackages.Count);
                var generatedSpecsCount = 0;
                foreach (var analyzedPackage in analyzedPackages.Values)
                {
                    // If the NuGet package has a DScript package config, then don't generate a spec
                    // The embedded source resolver will pick this up later
                    if (!analyzedPackage.PackageOnDisk.ModuleConfigFile.IsValid)
                    {
                        if (!CanReuseSpecFromDisk(analyzedPackage))
                        {
                            generatedSpecsCount++;
                            result.Add(analyzedPackage.ActualId, GenerateSpecFile(analyzedPackage));
                        }
                        else
                        {
                            result.Add(analyzedPackage.ActualId, GetPackageConfigDscFile(analyzedPackage));
                        }
                    }
                }

                Logger.Log.NugetRegenerateNugetSpecs(m_context.LoggingContext, generatedSpecsCount, (long)sw.Elapsed.TotalMilliseconds);

                return result;
            }
        }

        private void LogDownloadProgress(NugetProgress[] downloadProgress, IReadOnlyList<INugetPackage> packagesOnConfig)
        {
            var packagesToDownload = new List<Tuple<TimeSpan, string>>();

            var alreadyDownloadedPackages = 0;
            var skipped = 0;

            for (var i = 0; i < downloadProgress.Length; i++)
            {
                switch (downloadProgress[i].State)
                {
                    case NugetProgressState.Running:
                    case NugetProgressState.DownloadingFromNuget:
                        if (packagesToDownload.Count < MaxPackagesToDisplay)
                        {
                            var elapsed = downloadProgress[i].Elapsed();
                            var elapsedString = FormattingEventListener.TimeSpanToString(TimeDisplay.Seconds, elapsed);
                            var nugetMarker = downloadProgress[i].State == NugetProgressState.DownloadingFromNuget ? " (from nuget)" : " (from cache)";
                            packagesToDownload.Add(Tuple.Create(elapsed, I($"\t{elapsedString} - {packagesOnConfig[i].Id}{nugetMarker}")));
                        }
                        else
                        {
                            skipped++;
                        }

                        break;
                    case NugetProgressState.Succeeded:
                    case NugetProgressState.Failed:
                        alreadyDownloadedPackages++;
                        break;
                }
            }

            if (skipped > 0)
            {
                packagesToDownload.Add(Tuple.Create(TimeSpan.Zero, "\t + " + skipped + " more"));
            }

            var packagesDownloadingDetailString = string.Join(Environment.NewLine, packagesToDownload.OrderByDescending(t => t.Item1).Select(t => t.Item2));

            Logger.Log.NugetPackageDownloadedCount(m_context.LoggingContext, alreadyDownloadedPackages, packagesOnConfig.Count);
            Logger.Log.NugetPackageDownloadedCountWithDetails(m_context.LoggingContext, alreadyDownloadedPackages,
                packagesOnConfig.Count, Environment.NewLine + packagesDownloadingDetailString);
        }

        private bool ValidatePackageConfiguration(INugetPackage packageConfiguration)
        {
            if (!PathAtom.TryCreate(m_context.StringTable, packageConfiguration.Version, out _))
            {
                Logger.Log.ConfigNugetPackageVersionIsInvalid(m_context.LoggingContext, packageConfiguration.Version, packageConfiguration.Id);
                return false;
            }

            return true;
        }

        private Possible<XDocument, NugetFailure> CreateNuGetConfig(IReadOnlyDictionary<string, string> repositories)
        {
            XElement credentials = null;
            if (m_useMonoBasedNuGet)
            {
                var localNuGetConfigPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".config",
                    "NuGet",
                    "NuGet.Config");

                try
                {
                    // Sadly nuget goes all over the disk to chain configs, but when it comes to the credentials it decides not to properly merge them.
                    // So for now we have to hack and read the credentials from the users profile and stick them in the local config....
                    if (FileUtilities.Exists(localNuGetConfigPath))
                    {
                        ExceptionUtilities.HandleRecoverableIOException(
                            () =>
                            {
                                var doc = XDocument.Load(localNuGetConfigPath);
                                credentials = doc.Element("configuration")?.Element("packageSourceCredentials");
                            },
                            e => throw new BuildXLException($"Failed to load nuget config {localNuGetConfigPath}", e));
                    }
                }
                catch (BuildXLException e)
                {
                    Logger.Log.NugetFailedToWriteConfigurationFile(
                        m_context.LoggingContext,
                        localNuGetConfigPath,
                        e.LogEventMessage);
                    return new NugetFailure(NugetFailure.FailureType.WriteConfigFile, e.InnerException);
                }
            }

            return new XDocument(
                new XElement(
                    "configuration",
                    new XElement(
                        "packageRestore",
                        new XElement("clear"),
                        new XElement("add", new XAttribute("key", "enabled"), new XAttribute("value", "True"))),
                    new XElement(
                        "disabledPackageSources",
                        new XElement("clear")),
                    credentials,
                    new XElement(
                        "packageSources",
                        new XElement("clear"),
                        repositories.Select(
                            kv => new XElement("add", new XAttribute("key", kv.Key), new XAttribute("value", kv.Value))))));
        }

        private Possible<AbsolutePath> TryWriteSourceFile(INugetPackage package, AbsolutePath targetFile, ISourceFile sourceFile)
        {
            Contract.Requires(package != null);
            Contract.Requires(targetFile.IsValid);
            Contract.Requires(sourceFile != null);

            var targetFilePath = targetFile.ToString(m_context.PathTable);

            try
            {
                FileUtilities.CreateDirectory(Path.GetDirectoryName(targetFilePath));

                ExceptionUtilities.HandleRecoverableIOException(
                    () =>
                    {
                        // TODO: Use FileSTream for writing in the future but we don't have that right now.
                        File.WriteAllText(targetFilePath, sourceFile.GetFormattedText());
                    },
                    e =>
                    {
                        throw new BuildXLException("Cannot write package's spec file to disk", e);
                    });
            }
            catch (BuildXLException e)
            {
                Logger.Log.NugetFailedToWriteSpecFileForPackage(
                    m_context.LoggingContext,
                    package.Id,
                    package.Version,
                    targetFilePath,
                    e.LogEventMessage);
                return new NugetFailure(package, NugetFailure.FailureType.WriteSpecFile, e.InnerException);
            }

            m_host.Engine.RecordFrontEndFile(targetFile, NugetResolverName);

            return targetFile;
        }

        private Possible<AbsolutePath> TryWriteXmlConfigFile(INugetPackage package, AbsolutePath targetFile, XDocument xmlDoc)
        {
            var targetFilePath = targetFile.ToString(PathTable);

            try
            {
                FileUtilities.CreateDirectory(Path.GetDirectoryName(targetFilePath));
                ExceptionUtilities.HandleRecoverableIOException(
                    () =>
                    xmlDoc.Save(targetFilePath, SaveOptions.DisableFormatting),
                    e =>
                    {
                        throw new BuildXLException("Cannot save document", e);
                    });
            }
            catch (BuildXLException e)
            {
                if (package == null)
                {
                    Logger.Log.NugetFailedToWriteConfigurationFile(m_context.LoggingContext, targetFilePath, e.LogEventMessage);
                }
                else
                {
                    Logger.Log.NugetFailedToWriteConfigurationFileForPackage(
                        m_context.LoggingContext,
                        package.Id,
                        package.Version,
                        targetFilePath,
                        e.LogEventMessage);
                }

                return new NugetFailure(package, NugetFailure.FailureType.WriteConfigFile, e.InnerException);
            }

            m_host.Engine.RecordFrontEndFile(targetFile, NugetResolverName);

            return targetFile;
        }

        private AbsolutePath GetPackageSpecDir(NugetAnalyzedPackage analyzedPackage)
        {
            return m_resolverOutputLayout.GeneratedSpecsFolder
                .Combine(PathTable, analyzedPackage.PackageOnDisk.Package.GetPackageIdentity())
                .Combine(PathTable, analyzedPackage.PackageOnDisk.Package.Version);
        }

        private AbsolutePath GetPackageDscFile(NugetAnalyzedPackage analyzedPackage)
        {
            return GetPackageSpecDir(analyzedPackage).Combine(PathTable, Names.PackageDsc);
        }

        private AbsolutePath GetPackageConfigDscFile(NugetAnalyzedPackage analyzedPackage)
        {
            return GetPackageSpecDir(analyzedPackage).Combine(PathTable, Names.ModuleConfigBm);
        }

        private bool CanReuseSpecFromDisk(NugetAnalyzedPackage analyzedPackage)
        {
            var packageDsc = GetPackageDscFile(analyzedPackage).ToString(PathTable);

            if (analyzedPackage.Source == PackageSource.Disk &&
                analyzedPackage.PackageOnDisk.PackageDownloadResult.SpecsFormatIsUpToDate &&
                File.Exists(packageDsc))
            {
                return true;
            }

            return false;
        }

        [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly")]
        internal Possible<AbsolutePath> GenerateSpecFile(NugetAnalyzedPackage analyzedPackage)
        {
            var packageSpecDirStr = GetPackageSpecDir(analyzedPackage).ToString(PathTable);

            // Delete only the contents of the specs folder if it already exists
            DeleteDirectoryContentIfExists(packageSpecDirStr);

            // No-op if the directory exists
            FileUtilities.CreateDirectory(packageSpecDirStr);

            var nugetSpecGenerator = new NugetSpecGenerator(PathTable, analyzedPackage);

            var possibleProjectFile = TryWriteSourceFile(
                analyzedPackage.PackageOnDisk.Package,
                GetPackageDscFile(analyzedPackage),
                nugetSpecGenerator.CreateScriptSourceFile(analyzedPackage));

            if (!possibleProjectFile.Succeeded)
            {
                return possibleProjectFile.Failure;
            }

            return TryWriteSourceFile(
                analyzedPackage.PackageOnDisk.Package,
                GetPackageConfigDscFile(analyzedPackage),
                nugetSpecGenerator.CreatePackageConfig());
        }

        internal Possible<NugetAnalyzedPackage> AnalyzeNugetPackage(
            PackageOnDisk packageOnDisk,
            bool doNotEnforceDependencyVersions)
        {
            Contract.Requires(packageOnDisk != null);

            var package = packageOnDisk.Package;
            var nuspecFile = packageOnDisk.NuSpecFile;

            if (!nuspecFile.IsValid)
            {
                // Bug #1149939: One needs to investigate why the nuspec file is missing.
                Logger.Log.NugetFailedNuSpecFileNotFound(m_context.LoggingContext, package.Id, package.Version, packageOnDisk.PackageFolder.ToString(PathTable));
                return new NugetFailure(package, NugetFailure.FailureType.ReadNuSpecFile);
            }

            var nuspecPath = nuspecFile.ToString(m_context.PathTable);

            XDocument xdoc = null;
            Exception exception = null;
            try
            {
                xdoc = ExceptionUtilities.HandleRecoverableIOException(
                    () =>
                    XDocument.Load(nuspecPath),
                    e =>
                    {
                        throw new BuildXLException("Cannot load document", e);
                    });
            }
            catch (XmlException e)
            {
                exception = e;
            }
            catch (BuildXLException e)
            {
                exception = e.InnerException;
            }

            if (exception != null)
            {
                Logger.Log.NugetFailedToReadNuSpecFile(
                    m_context.LoggingContext,
                    package.Id,
                    package.Version,
                    nuspecPath,
                    exception.ToStringDemystified());
                return new NugetFailure(package, NugetFailure.FailureType.ReadNuSpecFile, exception);
            }

            var result = NugetAnalyzedPackage.TryAnalyzeNugetPackage(m_context, m_nugetFrameworkMonikers, xdoc,
                packageOnDisk, m_packageRegistry.AllPackagesById, doNotEnforceDependencyVersions);

            if (result == null)
            {
                // error already logged
                return new NugetFailure(package, NugetFailure.FailureType.AnalyzeNuSpec);
            }

            return result;
        }

        private async Task<Possible<PackageOnDisk>> TryRestorePackageWithCache(
            INugetPackage package,
            NugetProgress progress,
            NugetPackageOutputLayout layout,
            IEnumerable<AbsolutePath> credentialProviderPaths)
        {
            var fingerprintParams = new List<string>
            {
                "id=" + package.Id,
                "version=" + package.Version,
                "repos=" + UppercaseSortAndJoinStrings(m_repositories.Values),
                "cred=" + UppercaseSortAndJoinStrings(credentialProviderPaths.Select(p => p.ToString(PathTable))),
            };

            var weakFingerprint = "nuget://" + string.Join("&", fingerprintParams);
            var maybePackage = await m_host.DownloadPackage(
                weakFingerprint,
                PackageIdentity.Nuget(package.Id, package.Version, package.Alias),
                layout.PackageFolder,
                () =>
                {
                    progress.StartDownloadFromNuget();
                    return TryDownloadPackage(package, layout, credentialProviderPaths);
                });

            return maybePackage.Then(downloadResult =>
            {
                m_host.Engine.TrackDirectory(layout.PackageFolder.ToString(PathTable));
                return new PackageOnDisk(PathTable, package, downloadResult);
            });
        }

        private static string UppercaseSortAndJoinStrings(IEnumerable<string> values)
        {
            return string.Join(",", values.Select(s => s.ToUpperInvariant()).OrderBy(s => s));
        }

        private async Task<Possible<IReadOnlyList<RelativePath>>> TryDownloadPackage(
            INugetPackage package, NugetPackageOutputLayout layout, IEnumerable<AbsolutePath> credentialProviderPaths)
        {
            var xmlConfigResult = TryWriteXmlConfigFile(package, layout.PackagesConfigFile, GetPackagesXml(package));
            if (!xmlConfigResult.Succeeded)
            {
                return xmlConfigResult.Failure;
            }

            var cleanUpResult = TryCleanupPackagesFolder(package, layout);
            if (!cleanUpResult.Succeeded)
            {
                return cleanUpResult.Failure;
            }

            var nugetExeResult = await TryLaunchNugetExeAsync(package, layout, credentialProviderPaths);
            if (!nugetExeResult.Succeeded)
            {
                return nugetExeResult.Failure;
            }

            var contentResult = TryEnumerateDirectory(package, layout.PackageDirectory);
            if (!contentResult.Succeeded)
            {
                return contentResult.Failure;
            }

            return contentResult.Result;
        }

        private Possible<Unit> TryCleanupPackagesFolder(INugetPackage package, NugetPackageOutputLayout layout)
        {
            // Keep a current folder for logging purposes.
            // This method does a few things and we should log exactly what the operation was doing.
            var currentDirectory = layout.TempDirectory.ToString(PathTable);

            // Delete the previous downloaded folder for safety
            try
            {
                // Nuget fails if the temp folder is not there
                FileUtilities.CreateDirectory(currentDirectory);

                // for safety reasons: delete previous package directory and nuget cache directory for the package
                currentDirectory = layout.PackageDirectory;
                DeleteDirectoryContentIfExists(layout.PackageDirectory);

                currentDirectory = layout.PackageTmpDirectory;
                DeleteDirectoryContentIfExists(layout.PackageTmpDirectory);

                // everything went ok
                return Unit.Void;
            }
            catch (BuildXLException e)
            {
                Logger.Log.NugetFailedToCleanTargetFolder(
                    m_context.LoggingContext,
                    package.Id,
                    package.Version,
                    currentDirectory,
                    e.LogEventMessage);
                return new NugetFailure(package, NugetFailure.FailureType.CleanTargetFolder, e.InnerException ?? e);
            }
        }

        private static void DeleteDirectoryContentIfExists(string directory)
        {
            if (Directory.Exists(directory))
            {
                FileUtilities.DeleteDirectoryContents(directory);
            }
        }

        private async Task<Possible<Unit>> TryLaunchNugetExeAsync(INugetPackage package, NugetPackageOutputLayout layout, IEnumerable<AbsolutePath> credentialProviderPaths)
        {
            var fileAccessManifest = GenerateFileAccessManifest(layout, credentialProviderPaths);

            var buildParameters = BuildParameters
                .GetFactory()
                .PopulateFromEnvironment();

            var tool = layout.NugetTool.ToString(PathTable);

            var argumentsBuilder = new StringBuilder();
            if (m_useMonoBasedNuGet)
            {
                argumentsBuilder.AppendFormat("\"{0}\"", tool);
                argumentsBuilder.Append(" ");

                if (!buildParameters.ToDictionary().TryGetValue("MONO_HOME", out var monoHome))
                {
                    return new NugetFailure(package, NugetFailure.FailureType.MissingMonoHome);
                }

                tool = Path.Combine(monoHome, "mono");
            }

            // TODO:escape quotes properly
            argumentsBuilder
                .AppendFormat("restore \"{0}\"", layout.PackagesConfigFile.ToString(PathTable))
                .AppendFormat(" -OutputDirectory \"{0}\"", layout.PackageRootFolder.ToString(PathTable))
                .Append(" -Verbosity detailed")
                .AppendFormat(" -ConfigFile  \"{0}\"", layout.NugetConfig.ToString(PathTable))
                .Append(" -PackageSaveMode nuspec")
                .Append(" -NonInteractive")
                .Append(" -NoCache")
                // Currently we have to hack nuget to MsBuild version 4 which should come form the current CLR.
                .Append(" -MsBuildVersion 4");

            var arguments = argumentsBuilder.ToString();

            Logger.Log.LaunchingNugetExe(m_context.LoggingContext, package.Id, package.Version, tool + " " + arguments);
            try
            {
                // For NugetFrontEnd always create a new ConHost process.
                // The NugetFrontEnd is normally executed only once, so the overhead is low.
                // Also the NugetFrontEnd is a really long running process, so creating the ConHost is relatively
                // very cheap. It provides guarantee if the process pollutes the ConHost env,
                // it will not affect the server ConHost.
                var info =
                    new SandboxedProcessInfo(
                        m_context.PathTable,
                        new NugetFileStorage(layout.PackageTmpDirectory),
                        tool,
                        fileAccessManifest,
                        disableConHostSharing: true,
                        ContainerConfiguration.DisabledIsolation,
                        loggingContext: m_context.LoggingContext,
                        sandboxedKextConnection: m_useMonoBasedNuGet ? new FakeKextConnection() : null)
                    {
                        Arguments = arguments,
                        WorkingDirectory = layout.TempDirectory.ToString(PathTable),
                        PipSemiStableHash = 0,
                        PipDescription = "NuGet FrontEnd",
                        EnvironmentVariables = GetNugetEnvironmentVariables(),
                        Timeout = TimeSpan.FromMinutes(20), // Limit the time nuget has to download each nuget package
                    };

                return await RetryOnFailure(
                    runNuget: async () =>
                    {
                        var process = await SandboxedProcessFactory.StartAsync(info, forceSandboxing: !m_useMonoBasedNuGet);
                        var result = await process.GetResultAsync();
                        return (result, result.ExitCode == 0);
                    },
                    onError: async result =>
                    {
                        // Log the result before trying again
                        var (stdOut, stdErr) = await GetStandardOutAndError(result);

                        Logger.Log.NugetFailedWithNonZeroExitCodeDetailed(
                            m_context.LoggingContext,
                            package.Id,
                            package.Version,
                            result.ExitCode,
                            stdOut,
                            stdErr);

                        return (stdOut, stdErr);
                    },
                    onFinalFailure: (exitCode, stdOut, stdErr) =>
                    {
                        // Give up and fail
                        return NugetFailure.CreateNugetInvocationFailure(package, exitCode, stdOut, stdErr);
                    });
            }
            catch (BuildXLException e)
            {
                Logger.Log.NugetLaunchFailed(m_context.LoggingContext, package.Id, package.Version, e.LogEventMessage);
                return new NugetFailure(package, NugetFailure.FailureType.NugetFailedWithIoException);
            }
            catch (Exception e)
            {
                return new NugetFailure(package, NugetFailure.FailureType.NugetFailedWithIoException, e);
            }

            async Task<(string stdOut, string stdErr)> GetStandardOutAndError(SandboxedProcessResult result)
            {
                try
                {
                    await result.StandardOutput.SaveAsync();
                    var stdOut = await result.StandardOutput.ReadValueAsync();

                    await result.StandardError.SaveAsync();
                    var stdErr = await result.StandardError.ReadValueAsync();

                    return (stdOut, stdErr);
                }
                catch (BuildXLException e)
                {
                    return (e.LogEventMessage, string.Empty);
                }
            }

            BuildParameters.IBuildParameters GetNugetEnvironmentVariables()
            {
                // the environment variable names below should use the casing appropriate for the target OS
                // (on Windows it won't matter, but on Unix-like systems, including Cygwin environment on Windows,
                // it matters, and has to be all upper-cased). See also doc comment for IBuildParameters.Select
                return buildParameters
                .Select(
                    new[]
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
                            "SYSTEMTYPE",
                            "NUGET_CREDENTIALPROVIDERS_PATH",
                            "__CLOUDBUILD_AUTH_HELPER_CONFIG__",
                            "__Q_DPAPI_Secrets_Dir",

                            // Auth material needed for low-privilege build.
                            "QAUTHMATERIALROOT"
                    })
                .Override(
                    new Dictionary<string, string>()
                    {
                            {"TMP", layout.TempDirectoryAsString},
                            {"TEMP", layout.TempDirectoryAsString},
                            {"NUGET_PACKAGES", layout.TempDirectoryAsString},
                            {"NUGET_ROOT", layout.TempDirectoryAsString},
                    });
            }
        }

        private class FakeKextConnection : IKextConnection
        {
            public int NumberOfKextConnections => 1;

            public ulong MinReportQueueEnqueueTime { get; set; }

            public TimeSpan CurrentDrought
            {
                get
                {
                    var nowNs = Sandbox.GetMachAbsoluteTime();
                    var minReportTimeNs = MinReportQueueEnqueueTime;
                    return TimeSpan.FromTicks(nowNs > minReportTimeNs ? (long)((nowNs - minReportTimeNs) / 100) : 0);
                }
            }

            public void Dispose() { }

            public bool IsInTestMode => true;

            public bool MeasureCpuTimes => true;

            public bool NotifyUsage(uint cpuUsage, uint availableRamMB) { return true; }

            public bool NotifyKextPipStarted(FileAccessManifest fam, SandboxedProcessMacKext process) { return true; }

            public void NotifyKextPipProcessTerminated(long pipId, int processId) { }

            public bool NotifyKextProcessFinished(long pipId, SandboxedProcessMacKext process) { return true; }

            public void ReleaseResources() { }
        }

        private static async Task<Possible<Unit>> RetryOnFailure(
            Func<Task<(SandboxedProcessResult result, bool isPassed)>> runNuget,
            Func<SandboxedProcessResult, Task<(string, string)>> onError,
            Func<int, string, string, Possible<Unit>> onFinalFailure,
            int retryCount = MaxRetryCount,
            int retryDelayMs = RetryDelayMs)
        {
            Contract.Assert(retryCount >= 1, "Maximum retry count must be greater than or equal to one. Found " + retryCount);

            var pass = false;
            var iteration = 1;

            while (!pass)
            {
                var tuple = await runNuget();
                var result = tuple.result;
                pass = tuple.isPassed;

                if (!pass)
                {
                    var (stdOut, stdErr) = await onError(result);

                    if (iteration >= retryCount)
                    {
                        return onFinalFailure(result.ExitCode, stdOut, stdErr);
                    }

                    // Try again!
                    iteration++;

                    await Task.Delay(retryDelayMs);
                }
            }

            return Unit.Void;
        }

        private FileAccessManifest GenerateFileAccessManifest(NugetPackageOutputLayout layout, IEnumerable<AbsolutePath> credentialProviderPaths)
        {
            var fileAccessManifest = new FileAccessManifest(PathTable)
            {
                // TODO: If this is set to true, then NuGet will fail if TMG Forefront client is running.
                //                 Filtering out in SandboxedProcessReport won't work because Detours already blocks the access to FwcWsp.dll.
                //                 Almost all machines in Office run TMG Forefront client.
                //                 So far for WDG, FailUnexpectedFileAccesses is false due to whitelists.
                //                 As a consequence, the file access manifest below gets nullified.
                FailUnexpectedFileAccesses = false,
                ReportFileAccesses = true,
                MonitorNtCreateFile = true,
                MonitorZwCreateOpenQueryFile = true,
            };

            fileAccessManifest.AddScope(layout.TempDirectory, FileAccessPolicy.MaskAll, FileAccessPolicy.AllowAllButSymlinkCreation);
            fileAccessManifest.AddScope(layout.PackageRootFolder, FileAccessPolicy.MaskAll, FileAccessPolicy.AllowAllButSymlinkCreation);
            if (!OperatingSystemHelper.IsUnixOS)
            {
                fileAccessManifest.AddScope(
                    AbsolutePath.Create(PathTable, SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.Windows)),
                    FileAccessPolicy.MaskAll,
                    FileAccessPolicy.AllowAllButSymlinkCreation);
            }

            fileAccessManifest.AddPath(layout.NugetTool, values: FileAccessPolicy.AllowRead, mask: FileAccessPolicy.MaskNothing);
            fileAccessManifest.AddPath(layout.NugetToolExeConfig, values: FileAccessPolicy.AllowReadIfNonexistent, mask: FileAccessPolicy.MaskNothing);
            fileAccessManifest.AddPath(layout.NugetConfig, values: FileAccessPolicy.AllowRead, mask: FileAccessPolicy.MaskNothing);
            fileAccessManifest.AddPath(layout.PackagesConfigFile, values: FileAccessPolicy.AllowRead, mask: FileAccessPolicy.MaskNothing);

            // Nuget is picky
            fileAccessManifest.AddScope(layout.ResolverFolder, FileAccessPolicy.MaskAll, FileAccessPolicy.AllowAllButSymlinkCreation);

            // Nuget fails if it can't access files in the users (https://github.com/NuGet/Home/issues/2676) profile.
            // We'll have to explicitly set all config in our nuget.config file and override anything the user can set and allow the read here.
            var roamingAppDataNuget = AbsolutePath.Create(PathTable, SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.ApplicationData)).Combine(PathTable, "NuGet");
            fileAccessManifest.AddScope(roamingAppDataNuget, FileAccessPolicy.MaskAll, FileAccessPolicy.AllowAllButSymlinkCreation);

            var localAppDataNuget = AbsolutePath.Create(PathTable, SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)).Combine(PathTable, "NuGet");
            fileAccessManifest.AddScope(localAppDataNuget, FileAccessPolicy.MaskAll, FileAccessPolicy.AllowAllButSymlinkCreation);

            // Nuget also probes in ProgramData on the machine.
            var commonAppDataNuget = AbsolutePath.Create(PathTable, SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)).Combine(PathTable, "NuGet");
            fileAccessManifest.AddScope(commonAppDataNuget, FileAccessPolicy.MaskAll, FileAccessPolicy.AllowAllButSymlinkCreation);

            foreach (var providerPath in credentialProviderPaths)
            {
                fileAccessManifest.AddPath(providerPath, values: FileAccessPolicy.AllowRead, mask: FileAccessPolicy.MaskNothing);
                fileAccessManifest.AddPath(
                    providerPath.ChangeExtension(PathTable, PathAtom.Create(PathTable.StringTable, ".exe.config")),
                    values: FileAccessPolicy.AllowRead,
                    mask: FileAccessPolicy.MaskNothing);
            }

            return fileAccessManifest;
        }

        private static XDocument GetPackagesXml(INugetPackage package)
        {

            var version = package.Version;
            var plusIndex = version.IndexOf('+');
            if (plusIndex > 0)
            {
                version = version.Substring(0, plusIndex);
            }
            return new XDocument(
                new XElement(
                    "packages",
                    new XElement(
                        "package",
                        new XAttribute("id", package.Id),
                        new XAttribute("version", version))));
        }

        private Possible<List<RelativePath>, NugetFailure> TryEnumerateDirectory(INugetPackage package, string packagePath)
        {
            var enumerateDirectoryResult = EnumerateDirectoryRecursively(packagePath, out var contents);

            if (!enumerateDirectoryResult.Succeeded)
            {
                var message = enumerateDirectoryResult.GetNativeErrorMessage();
                Logger.Log.NugetFailedToListPackageContents(m_context.LoggingContext, package.Id, package.Version, packagePath, message);
                return new NugetFailure(package, NugetFailure.FailureType.ListPackageContents, enumerateDirectoryResult.CreateExceptionForError());
            }

            return contents;
        }

        [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic", Justification = "False positive: the analyzer can't detect that the instance member was used in the local function.")]
        private EnumerateDirectoryResult EnumerateDirectoryRecursively(string packagePath, out List<RelativePath> resultingContent)
        {
            resultingContent = new List<RelativePath>();
            return EnumerateDirectoryRecursively(RelativePath.Empty, resultingContent);

            EnumerateDirectoryResult EnumerateDirectoryRecursively(RelativePath relativePath, List<RelativePath> contents)
            {
                var result = FileUtilities.EnumerateDirectoryEntries(
                    Path.Combine(packagePath, relativePath.ToString(m_context.StringTable)),
                    (name, attr) =>
                    {
                        var nestedRelativePath = relativePath.Combine(PathAtom.Create(m_context.StringTable, name));
                        if ((attr & FileAttributes.Directory) != 0)
                        {
                            EnumerateDirectoryRecursively(nestedRelativePath, contents);
                        }
                        else
                        {
                            contents.Add(nestedRelativePath);
                        }
                    });

                return result;
            }
        }

        private async Task<Possible<AbsolutePath[]>> TryDownloadNugetAsync(INugetConfiguration configuration, AbsolutePath targetFolder)
        {
            configuration = configuration ?? new NugetConfiguration();

            var downloads = new Task<Possible<ContentHash>>[1 + configuration.CredentialProviders.Count];
            var paths = new AbsolutePath[downloads.Length];

            var nugetTargetLocation = targetFolder.Combine(m_context.PathTable, "nuget.exe");

            var nugetLocation = configuration.ToolUrl;
            if (string.IsNullOrEmpty(nugetLocation))
            {
                var version = configuration.Version ?? "latest";
                nugetLocation = string.Format(CultureInfo.InvariantCulture, "https://dist.nuget.org/win-x86-commandline/{0}/nuget.exe", version);
            }

            TryGetExpectedContentHash(configuration, out var expectedHash);

            downloads[0] = m_host.DownloadFile(nugetLocation, nugetTargetLocation, expectedHash, NugetResolverName);
            paths[0] = nugetTargetLocation;

            for (var i = 0; i < configuration.CredentialProviders.Count; i++)
            {
                var credentialProvider = configuration.CredentialProviders[i];
                var credentialProviderName = NugetResolverName + ".credentialProvider." + i.ToString(CultureInfo.InvariantCulture);

                TryGetExpectedContentHash(credentialProvider, out var expectedProviderHash);

                var toolUrl = credentialProvider.ToolUrl;
                if (string.IsNullOrEmpty(toolUrl))
                {
                    // TODO: Have better provenance for configuration values.
                    Logger.Log.CredentialProviderRequiresToolUrl(m_context.LoggingContext, credentialProviderName);
                    return new NugetFailure(NugetFailure.FailureType.FetchCredentialProvider);
                }

                var fileNameStart = toolUrl.LastIndexOfAny(new[] { '/', '\\' });
                var fileName = fileNameStart >= 0 ? toolUrl.Substring(fileNameStart + 1) : toolUrl;
                var targetLocation = targetFolder.Combine(m_context.PathTable, fileName);
                downloads[i + 1] = m_host.DownloadFile(toolUrl, targetLocation, expectedProviderHash, credentialProviderName);
                paths[i + 1] = targetLocation;
            }

            var results = await Task.WhenAll(downloads);

            foreach (var result in results)
            {
                if (!result.Succeeded)
                {
                    return result.Failure;
                }
            }

            return paths;
        }

        /// <nodoc />
        private bool TryGetExpectedContentHash(IArtifactLocation artifactLocation, out ContentHash? expectedHash)
        {
            expectedHash = null;
            if (!string.IsNullOrEmpty(artifactLocation.Hash))
            {
                if (!ContentHashingUtilities.TryParse(artifactLocation.Hash, out var contentHash))
                {
                    // TODO: better provenance for configuration settings.
                    Logger.Log.NugetDownloadInvalidHash(
                        m_context.LoggingContext,
                        "nuget.exe",
                        artifactLocation.Hash,
                        ContentHashingUtilities.HashInfo.HashType.ToString(),
                        ContentHashingUtilities.HashInfo.ByteLength);
                    return false;
                }

                expectedHash = contentHash;
            }

            return true;
        }

        /// <nodoc />
        private sealed class NugetFileStorage : ISandboxedProcessFileStorage
        {
            private readonly string m_directory;

            /// <nodoc />
            public NugetFileStorage(string directory)
            {
                m_directory = directory;
            }

            /// <inheritdoc />
            public string GetFileName(SandboxedProcessFile file)
            {
                return Path.Combine(m_directory, file.DefaultFileName());
            }
        }

        /// <summary>
        /// Helper class that holds all folders required for a nuget resolver
        /// </summary>
        internal sealed class NugetResolverOutputLayout
        {
            public NugetResolverOutputLayout(PathTable pathTable, AbsolutePath resolverFolder)
            {
                ResolverDirectory = resolverFolder.ToString(pathTable);
                TempDirectory = resolverFolder.Combine(pathTable, "tmp");
                TempDirectoryAsString = TempDirectory.ToString(pathTable);
                PackageRootFolder = resolverFolder.Combine(pathTable, "pkgs");
                ConfigRootFolder = resolverFolder.Combine(pathTable, "cfgs");
                ResolverFolder = resolverFolder;
                GeneratedSpecsFolder = resolverFolder.Combine(pathTable, "specs");
            }

            public AbsolutePath GeneratedSpecsFolder { get; }

            public AbsolutePath ResolverFolder { get; }

            public AbsolutePath ConfigRootFolder { get; }

            public AbsolutePath PackageRootFolder { get; }

            public string TempDirectoryAsString { get; }

            public AbsolutePath TempDirectory { get; }

            public string ResolverDirectory { get; }
        }

        /// <summary>
        /// Helper class that holds all folders required for nuget package.
        /// </summary>
        internal sealed class NugetPackageOutputLayout
        {
            private readonly NugetResolverOutputLayout m_resolverLayout;

            public NugetPackageOutputLayout(PathTable pathTable, INugetPackage package, AbsolutePath nugetTool, AbsolutePath nugetConfig, NugetResolverOutputLayout resolverLayout)
            {
                m_resolverLayout = resolverLayout;
                Package = package;
                NugetTool = nugetTool;
                NugetConfig = nugetConfig;

                var idAndVersion = package.Id + "." + package.Version;

                NugetToolExeConfig = nugetTool.ChangeExtension(pathTable, PathAtom.Create(pathTable.StringTable, ".exe.config"));
                // All the folders should include version to avoid potential race conditions during nuget execution.
                PackageFolder = PackageRootFolder.Combine(pathTable, idAndVersion);
                PackageDirectory = PackageFolder.ToString(pathTable);
                PackageTmpDirectory = TempDirectory.Combine(pathTable, idAndVersion).ToString(pathTable);
                PackagesConfigFile = ConfigRootFolder.Combine(pathTable, idAndVersion).Combine(pathTable, "packages.config");
            }

            public static NugetPackageOutputLayout Create(PathTable pathTable, INugetPackage package,
                AbsolutePath nugetTool, AbsolutePath nugetConfig, NugetResolverOutputLayout resolverLayout)
            {
                return new NugetPackageOutputLayout(pathTable, package, nugetTool, nugetConfig, resolverLayout);
            }

            public INugetPackage Package { get; }

            public AbsolutePath ResolverFolder => m_resolverLayout.ResolverFolder;

            public string ResolverDirectory => m_resolverLayout.ResolverDirectory;

            public AbsolutePath NugetTool { get; }

            public AbsolutePath NugetToolExeConfig { get; }

            public AbsolutePath NugetConfig { get; }

            public AbsolutePath TempDirectory => m_resolverLayout.TempDirectory;

            public string TempDirectoryAsString => m_resolverLayout.TempDirectoryAsString;

            public AbsolutePath PackageRootFolder => m_resolverLayout.PackageRootFolder;

            public AbsolutePath ConfigRootFolder => m_resolverLayout.ConfigRootFolder;

            public AbsolutePath PackagesConfigFile { get; }

            public AbsolutePath PackageFolder { get; }

            public string PackageDirectory { get; }

            public string PackageTmpDirectory { get; }
        }
    }

    /// <summary>
    /// Internal helper structure that groups the result of downloading and generating packages.
    /// </summary>
    internal struct NugetGenerationResult
    {
        public NugetGenerationResult(Dictionary<ModuleDescriptor, AbsolutePath> generatedProjectsByModuleDescriptor, Dictionary<AbsolutePath, ModuleDescriptor> generatedProjectsByPath, MultiValueDictionary<string, ModuleDescriptor> generatedProjectsByModuleName)
        {
            GeneratedProjectsByModuleDescriptor = generatedProjectsByModuleDescriptor;
            GeneratedProjectsByPath = generatedProjectsByPath;
            GeneratedProjectsByModuleName = generatedProjectsByModuleName;
        }

        /// <summary>
        /// All generated path to specs, indexed by package descriptor.
        /// </summary>
        public Dictionary<ModuleDescriptor, AbsolutePath> GeneratedProjectsByModuleDescriptor { get; }

        /// <summary>
        /// All package descriptors, indexed by path.
        /// </summary>
        public Dictionary<AbsolutePath, ModuleDescriptor> GeneratedProjectsByPath { get; }

        /// <summary>
        /// All package descriptors, indexed by name.
        /// </summary>
        public MultiValueDictionary<string, ModuleDescriptor> GeneratedProjectsByModuleName { get; set; }
    }
}
