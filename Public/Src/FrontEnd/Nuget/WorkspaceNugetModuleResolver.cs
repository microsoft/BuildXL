// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
using BuildXL.Interop;
using BuildXL.Interop.Unix;
using BuildXL.Native.IO;
using BuildXL.Processes;
using BuildXL.Processes.Containers;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Instrumentation.Common;
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

        private const string SpecGenerationVersionFileSuffix = ".version";

        private const string NugetCredentialProviderEnv = "NUGET_CREDENTIALPROVIDERS_PATH";

        private NugetFrameworkMonikers m_nugetFrameworkMonikers;

        // These are set during Initialize
        private INugetResolverSettings m_resolverSettings;
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
                    cyclicalFriendModules: null, // A NuGet package does not have any module dependency restrictions nor allowlists cycles
                    mounts: null);
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
        ///
        /// The multi-value dictionary maps the original Nuget package name to (possibly multiple) generated DScript packages.
        /// </summary>
        public async Task<Possible<MultiValueDictionary<string, Package>>> GetAllKnownPackagesAsync()
        {
            var maybeResult = await DownloadPackagesAndGenerateSpecsIfNeededInternal();

            return await maybeResult.ThenAsync(
                async result =>
                {
                    var maybeDefinitions = await this.GetAllModuleDefinitionsAsync();
                    return maybeDefinitions.Then(
                        definitions => definitions.Aggregate(
                            seed: new MultiValueDictionary<string, Package>(),
                            func: (acc, def) => AddPackage(acc, result.GetOriginalNugetPackageName(def), GetPackageForGeneratedProject(def))));
                });
        }

        private MultiValueDictionary<string, Package> AddPackage(MultiValueDictionary<string, Package> dict, string nugetPackageName, Package package)
        {
            dict.Add(nugetPackageName, package);
            return dict;
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
            return Package.Create(id, moduleDefinition.ModuleConfigFile, packageDescriptor, moduleId: moduleDescriptor.Id);
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
        public bool TryInitialize(FrontEndHost host, FrontEndContext context, IConfiguration configuration, IResolverSettings resolverSettings)
        {
            Contract.Requires(context != null);
            Contract.Requires(host != null);
            Contract.Requires(configuration != null);
            Contract.Requires(resolverSettings != null);

            var nugetResolverSettings = resolverSettings as INugetResolverSettings;
            Contract.Assert(nugetResolverSettings != null);

            m_context = context;
            m_resolverSettings = nugetResolverSettings;

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
            var result = m_embeddedSpecsResolver.TryInitialize(m_host, m_context, m_configuration, CreateSettingsForEmbeddedResolver(downloadedPackages.Values));
            Contract.Assert(result);

            Analysis.IgnoreResult(
                m_nugetGenerationResult
                    .GetOrCreate(
                        (@this: this, possiblePackages),
                        tpl => Task.FromResult(tpl.@this.GetNugetGenerationResultFromDownloadedPackages(tpl.possiblePackages, null))
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
            return m_nugetGenerationResult.GetOrCreate(this, @this => Task.FromResult(@this.DownloadPackagesAndGenerateSpecs()));
        }

        private Possible<AbsolutePath> TryResolveCredentialProvider()
        {
            if (!m_host.Engine.TryGetBuildParameter(NugetCredentialProviderEnv, nameof(NugetFrontEnd), out string credentialProvidersPaths))
            {
                return new NugetFailure(NugetFailure.FailureType.FetchCredentialProvider, $"Environment variable {NugetCredentialProviderEnv} is not set");
            }
            
            // Here we do something slightly simpler than what NuGet does and just look for the first credential
            // provider we can find
            AbsolutePath credentialProviderPath = AbsolutePath.Invalid;
            foreach (string path in credentialProvidersPaths.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!AbsolutePath.TryCreate(m_context.PathTable, path, out var absolutePath))
                {
                    break;
                }    

                // Use the engine to enumerate, since the result of the enumeration should be sensitive
                // to the graph building process
                credentialProviderPath = m_host.Engine.EnumerateFiles(absolutePath, "CredentialProvider*.exe").FirstOrDefault();
                if (credentialProviderPath.IsValid)
                {
                    break;
                }
            }

            if (!credentialProviderPath.IsValid)
            {
                return new NugetFailure(NugetFailure.FailureType.FetchCredentialProvider, $"Unable to authenticate using a credential provider: Credential provider was not found under '{credentialProvidersPaths}'.");
            }

            // We want to rebuild the build graph if the credential provider changed. Running the whole auth process under detours sounds like too much,
            // let's just read the credential provider main .exe so presence/hash gets recorded instead.
            m_host.Engine.TryGetFrontEndFile(credentialProviderPath, nameof(NugetFrontEnd), out _);

            return m_host.Engine.Translate(credentialProviderPath);
        }

        private Possible<NugetGenerationResult> DownloadPackagesAndGenerateSpecs()
        {
            var maybeCredentialProviderPath = TryResolveCredentialProvider();

            var nugetInspector = new NugetPackageInspector(
                m_resolverSettings.Repositories.Select(kvp => (kvp.Key, new Uri(kvp.Value))), 
                PathTable.StringTable,
                () => maybeCredentialProviderPath.Then(path => path.ToString(PathTable)),
                m_context.CancellationToken,
                m_context.LoggingContext);

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

                        if (!m_host.Engine.TryGetBuildParameter(NugetCredentialProviderEnv, nameof(NugetFrontEnd), out string allCredentialProviderPaths))
                        {
                            allCredentialProviderPaths = string.Empty;
                        }

                        var loopState = Parallel.For(fromInclusive: 0,
                            toExclusive: aggregateResult.Length,
                            new ParallelOptions()
                            {
                                MaxDegreeOfParallelism = concurrencyLevel,
                                CancellationToken = m_context.CancellationToken,
                            },
                            (index, state) =>
                            {
                                aggregateResult[index] = TryInspectPackageAsync(
                                    nugetProgress[index], 
                                    maybeCredentialProviderPath.Succeeded ? maybeCredentialProviderPath.Result : AbsolutePath.Invalid, 
                                    allCredentialProviderPaths, 
                                    nugetInspector).GetAwaiter().GetResult();

                                // Let's not schedule more work in the parallel for if one of the inspections failed
                                if (!aggregateResult[index].Succeeded)
                                {
                                    state.Break();
                                }

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
                                // If the package result is null, that means there was at least one failure and the parallel for ended prematurely, but the failure
                                // happened after the current index
                                if (packageResult.Result == null)
                                {
                                    // There should be at least one failure and the loop state should not be completed
                                    Contract.Assert(!loopState.IsCompleted);
                                    // Just skip this iteration. The failure is ahead of the current index and we'll eventually reach it
                                    continue;
                                }

                                packageResult.Result.NugetName = nugetPackage.Package.Id;
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
                if (!m_embeddedSpecsResolver.TryInitialize(m_host, m_context, m_configuration, CreateSettingsForEmbeddedResolver(restoredPackagesById.Values)))
                {
                    var failure = new WorkspaceModuleResolverGenericInitializationFailure(Kind);
                    Logger.Log.NugetFailedDownloadPackagesAndGenerateSpecs(m_context.LoggingContext, failure.DescribeIncludingInnerFailures());
                    return failure;
                }

                return GetNugetGenerationResultFromDownloadedPackages(possiblePackages, restoredPackagesById);
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
                    nugetConcurrency = Environment.ProcessorCount;
                    message = I($"Lowering restore package concurrency to {nugetConcurrency} because a source drive is on HDD.");
                }
                else
                {
                    nugetConcurrency = Math.Min(128, Environment.ProcessorCount * 4);
                    message = I($"Increasing restore package concurrency to {nugetConcurrency} because a source drive is on SSD.");
                }
            }

            Logger.Log.NugetConcurrencyLevel(m_context.LoggingContext, message);

            return nugetConcurrency;
        }

        private async Task<Possible<NugetAnalyzedPackage>> TryInspectPackageAsync(
            NugetProgress progress,
            AbsolutePath selectedCredentialProviderPath,
            string allCredentialProviderPaths,
            NugetPackageInspector nugetInspector)
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
                    resolverLayout: m_resolverOutputLayout);

                var possiblePkg = await TryInpectPackageWithCache(package, progress, layout, allCredentialProviderPaths, nugetInspector);

                if (!possiblePkg.Succeeded)
                {
                    m_statistics.Failures.Increment();
                    progress.FailedDownload();
                    return possiblePkg.Failure;
                }

                IncrementStatistics(possiblePkg.Result.PackageDownloadResult.Source);

                var analyzedPackage = AnalyzeNugetPackage(
                    possiblePkg.Result,
                    selectedCredentialProviderPath,
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
                case PackageSource.Stub:
                    m_statistics.PackageGenStubs.Increment();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(source), source, null);
            }
        }

        private Possible<NugetGenerationResult> GetNugetGenerationResultFromDownloadedPackages(
            Dictionary<string, Possible<AbsolutePath>> possiblePackages,
            Dictionary<string, NugetAnalyzedPackage> nugetPackagesByModuleName)
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

                var moduleDescriptor = ModuleDescriptor.CreateWithUniqueId(m_context.StringTable, packageName, this);

                generatedProjectsByPackageDescriptor[moduleDescriptor] = possiblePackage.Result;
                generatedProjectsByPath[possiblePackage.Result] = moduleDescriptor;
                generatedProjectsByPackageName.Add(moduleDescriptor.Name, moduleDescriptor);
            }

            return new NugetGenerationResult(generatedProjectsByPackageDescriptor, generatedProjectsByPath, generatedProjectsByPackageName, nugetPackagesByModuleName);
        }

        /// <summary>
        /// Creates a source resolver settings that points to the package root folder so embedded specs can be picked up
        /// </summary>
        private static IResolverSettings CreateSettingsForEmbeddedResolver(IEnumerable<NugetAnalyzedPackage> values)
        {
            // We point the embedded resolver to all the downloaded packages that contain a DScript package config file
            var embeddedSpecs = values.Where(nugetPackage => nugetPackage.PackageOnDisk.ModuleConfigFile.IsValid)
                .Select(nugetPackage => new DiscriminatingUnion<AbsolutePath, IInlineModuleDefinition>(
                    nugetPackage.PackageOnDisk.ModuleConfigFile)).ToArray();

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
                            Logger.Log.NugetCannotReuseSpecOnDisk(m_context.LoggingContext, analyzedPackage.ActualId);
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

            // This file contains some state from the last time the spec file was generated. It includes
            // the fingerprint of the package (name, version, etc) and the version of the spec generator.
            // It is stored next to the primary generated spec file
            var (fileFormat, packageRestoreFingerprint, generateSpecFingerprint) = ReadGeneratedSpecStateFile(packageDsc + SpecGenerationVersionFileSuffix);

            var expectedGenerateSpecFingerprint  = CreateSpecGenFingerPrint(analyzedPackage.PackageOnDisk.Package);

            // We can reuse the already generated spec file if all of the following are true:
            //  * The spec generator is of the same format as when the spec was generated
            //  * The package fingerprint is the same. This means the binaries are the same
            //  * Both the generated spec and package config file exist on disk
            // NOTE: This is not resilient to the specs being modified by other entities than the build engine.
            if (fileFormat == NugetSpecGenerator.SpecGenerationFormatVersion &&
                !m_host.Configuration.FrontEnd.ForceGenerateNuGetSpecs() &&
                packageRestoreFingerprint != null && string.Equals(packageRestoreFingerprint, analyzedPackage.PackageOnDisk.PackageDownloadResult.FingerprintHash, StringComparison.Ordinal) &&
                generateSpecFingerprint != null && string.Equals(generateSpecFingerprint, expectedGenerateSpecFingerprint, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(packageDsc) &&
                File.Exists(GetPackageConfigDscFile(analyzedPackage).ToString(PathTable)))
            {
                return true;
            }

            return false;
        }

        private static (int fileFormat, string packageRestoreFingerprint, string generateSpecFingerprint) ReadGeneratedSpecStateFile(string path)
        {
            if(File.Exists(path))
            {
                int fileFormat;
                string[] lines = File.ReadAllLines(path);
                if(lines.Length == 3)
                {
                    if (int.TryParse(lines[0], out fileFormat))
                    {
                        string packageRestoreFingerprint = lines[1];
                        string generateSpecFingerprint = lines[2];
                        return (fileFormat, packageRestoreFingerprint, generateSpecFingerprint);
                    }
                }
            }

            // Error
            return (-1, null, null);
        }

        private void WriteGeneratedSpecStateFile(string path, (int fileFormat, string packageRestoreFingerprint, string generateSpecFingerprint) data)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));

                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.WriteAllLines(path, new string[] { data.fileFormat.ToString(), data.packageRestoreFingerprint, data.generateSpecFingerprint });
            }
            catch (IOException ex)
            {
                Logger.Log.NugetFailedToWriteGeneratedSpecStateFile(m_context.LoggingContext, ex.Message);
            }
        }

        [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly")]
        internal Possible<AbsolutePath> GenerateSpecFile(NugetAnalyzedPackage analyzedPackage)
        {
            var packageSpecDirStr = GetPackageSpecDir(analyzedPackage).ToString(PathTable);

            // Delete only the contents of the specs folder if it already exists
            DeleteDirectoryContentIfExists(packageSpecDirStr);

            // No-op if the directory exists
            FileUtilities.CreateDirectory(packageSpecDirStr);
            
            var nugetSpecGenerator = new NugetSpecGenerator(PathTable, analyzedPackage, m_resolverSettings.Repositories, 
                m_configuration.Layout.SourceDirectory, m_resolverSettings.Configuration.DownloadTimeoutMin, m_resolverSettings.EsrpSignConfiguration);

            var possibleProjectFile = TryWriteSourceFile(
                analyzedPackage.PackageOnDisk.Package,
                GetPackageDscFile(analyzedPackage),
                nugetSpecGenerator.CreateScriptSourceFile(analyzedPackage));

            if (!possibleProjectFile.Succeeded)
            {
                return possibleProjectFile.Failure;
            }

            var writeResult = TryWriteSourceFile(
                analyzedPackage.PackageOnDisk.Package,
                GetPackageConfigDscFile(analyzedPackage),
                nugetSpecGenerator.CreatePackageConfig());

            if (writeResult.Succeeded && possibleProjectFile.Succeeded)
            {
                var generateSpecFingerprint = CreateSpecGenFingerPrint(analyzedPackage.PackageOnDisk.Package);
                WriteGeneratedSpecStateFile(
                    possibleProjectFile.Result.ToString(PathTable) + SpecGenerationVersionFileSuffix,
                    (
                        NugetSpecGenerator.SpecGenerationFormatVersion,
                        analyzedPackage.PackageOnDisk.PackageDownloadResult.FingerprintHash,
                        generateSpecFingerprint
                    )
                );
            }

            return writeResult;
        }

        internal Possible<NugetAnalyzedPackage> AnalyzeNugetPackage(
            PackageOnDisk packageOnDisk,
            AbsolutePath credentialProviderPath,
            bool doNotEnforceDependencyVersions)
        {
            Contract.Requires(packageOnDisk != null);

            var package = packageOnDisk.Package;

            var maybeNuspecXdoc = TryLoadNuSpec(packageOnDisk);
            if (!maybeNuspecXdoc.Succeeded)
            {
                return maybeNuspecXdoc.Failure;
            }

            var result = NugetAnalyzedPackage.TryAnalyzeNugetPackage(m_context, m_nugetFrameworkMonikers, maybeNuspecXdoc.Result,
                packageOnDisk, m_packageRegistry.AllPackagesById, doNotEnforceDependencyVersions, credentialProviderPath);

            if (result == null)
            {
                // error already logged
                return new NugetFailure(package, NugetFailure.FailureType.AnalyzeNuSpec);
            }

            return result;
        }

        private Possible<XDocument> TryLoadNuSpec(PackageOnDisk packageOnDisk)
        {
            // no nuspec file needed for stub packags
            if (packageOnDisk.PackageDownloadResult.Source == PackageSource.Stub)
            {
                return (XDocument)null;
            }

            var package = packageOnDisk.Package;
            var nuspecFile = packageOnDisk.NuSpecFile;

            if (!nuspecFile.IsValid)
            {
                // Bug #1149939: One needs to investigate why the nuspec file is missing.
                Logger.Log.NugetFailedNuSpecFileNotFound(m_context.LoggingContext, package.Id, package.Version, packageOnDisk.PackageFolder.ToString(PathTable));
                return new NugetFailure(package, NugetFailure.FailureType.ReadNuSpecFile);
            }

            var nuspecPath = nuspecFile.ToString(m_context.PathTable);

            try
            {
                return ExceptionUtilities.HandleRecoverableIOException(
                    () =>
                    XDocument.Load(nuspecPath),
                    e =>
                    {
                        throw new BuildXLException("Cannot load document", e);
                    });
            }
            catch (XmlException e)
            {
                return logFailure(e);
            }
            catch (BuildXLException e)
            {
                return logFailure(e.InnerException);
            }

            NugetFailure logFailure(Exception exception)
            {
                Logger.Log.NugetFailedToReadNuSpecFile(
                    m_context.LoggingContext,
                    package.Id,
                    package.Version,
                    nuspecPath,
                    exception.ToStringDemystified());
                return new NugetFailure(package, NugetFailure.FailureType.ReadNuSpecFile, exception);
            }
        }

        private string CreateRestoreFingerPrint(INugetPackage package, string credentialProviderPaths)
        {
            var fingerprintParams = new List<string>
                                    {
                                        "id=" + package.Id,
                                        "version=" + package.Version,
                                        "repos=" + UppercaseSortAndJoinStrings(m_repositories.Values),
                                    };
            if (credentialProviderPaths != null)
            {
                fingerprintParams.Add("cred=" + credentialProviderPaths);
            }

            return  "nuget://" + string.Join("&", fingerprintParams);
        }

        private string CreateSpecGenFingerPrint(INugetPackage package)
        {
            var restoreFingerPrint = CreateRestoreFingerPrint(package, null);

            var fingerprintParams = new List<string>
                                    {
                                        "pkgDepSkips=" + UppercaseSortAndJoinStrings(package.DependentPackageIdsToSkip),
                                        "pkgDepsIgnore=" + UppercaseSortAndJoinStrings(package.DependentPackageIdsToIgnore),
                                        "forceFullOnly=" + (package.ForceFullFrameworkQualifiersOnly ? "1" : "0")
                                    };
            return  restoreFingerPrint + "&" + string.Join("&", fingerprintParams);
        }

        private async Task<Possible<PackageOnDisk>> TryInpectPackageWithCache(
            INugetPackage package,
            NugetProgress progress,
            NugetPackageOutputLayout layout,
            string credentialProviderPaths,
            NugetPackageInspector nugetInspector)
        {
            var packageRestoreFingerprint = CreateRestoreFingerPrint(package, credentialProviderPaths);
            var identity = PackageIdentity.Nuget(package.Id, package.Version, package.Alias);

            var currentOs = Host.Current.CurrentOS.GetDScriptValue();
            var maybePackage = package.OsSkip?.Contains(currentOs) == true
                ? PackageDownloadResult.EmptyStub(packageRestoreFingerprint, identity, layout.PackageFolder)
                : await m_host.DownloadPackage(
                    packageRestoreFingerprint,
                    identity,
                    layout.PackageFolder,
                    layout.PathToNuspec,
                    async () =>
                    {
                        progress.StartDownloadFromNuget();
                        
                        // We want to delay initialization until the first inspection that is actually needed
                        // Initializing the inspector involves resolving the index service for each specified repository
                        if (!await nugetInspector.IsInitializedAsync())
                        {
                            var initResult = await nugetInspector.TryInitAsync();
                            if (!initResult.Succeeded)
                            {
                                return initResult.Failure;
                            }
                        }

                        return await TryInspectPackage(package, layout, nugetInspector);
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

        private async Task<Possible<IReadOnlyList<RelativePath>>> TryInspectPackage(
            INugetPackage package, 
            NugetPackageOutputLayout layout, 
            NugetPackageInspector nugetInspector)
        {
            var cleanUpResult = TryCleanupPackagesFolder(package, layout);
            if (!cleanUpResult.Succeeded)
            {
                return cleanUpResult.Failure;
            }

            // Inspect the package (get nuspec and layout)
            var maybeInspectedPackage = await nugetInspector.TryInspectAsync(package);
            if (!maybeInspectedPackage.Succeeded)
            {
                return maybeInspectedPackage.Failure;
            }

            var inspectedPackage = maybeInspectedPackage.Result;

            // Serialize the nuspec to disk. In this way we can also use the bxl cache to avoid
            // downloading this content again. The hash file will contain the layout, which is serialized
            // later
            try 
            {
#if NET_FRAMEWORK
                return ExceptionUtilities.HandleRecoverableIOException(
                   () =>
#else
                return await ExceptionUtilities.HandleRecoverableIOException(
                   async () =>
#endif
                   {
                       FileUtilities.CreateDirectoryWithRetry(layout.PackageFolder.ToString(PathTable));
                       
                       // XML files need to be serialized with the right enconding, so let's use XDocument
                       // for that
                       var xdocument = XDocument.Parse(inspectedPackage.Nuspec);

                       using (var nuspec = new FileStream(layout.PathToNuspec.ToString(PathTable), FileMode.Create))
                       {
#if NET_FRAMEWORK
                           xdocument.Save(nuspec);
#else
                           await xdocument.SaveAsync(nuspec, SaveOptions.None, m_context.CancellationToken);
#endif
                       }

                       return new Possible<IReadOnlyList<RelativePath>>(inspectedPackage.Content);
                   },
                   e =>
                   {
                       throw new BuildXLException("Cannot write package's nuspec file to disk", e);
                   });
            }
            catch (BuildXLException e)
            {
                Logger.Log.NugetFailedToWriteSpecFileForPackage(
                    m_context.LoggingContext,
                    package.Id,
                    package.Version,
                    layout.PathToNuspec.ToString(PathTable),
                    e.LogEventMessage);
                return new NugetFailure(package, NugetFailure.FailureType.WriteSpecFile, e.InnerException);
            }
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

            public NugetPackageOutputLayout(PathTable pathTable, INugetPackage package, NugetResolverOutputLayout resolverLayout)
            {
                m_resolverLayout = resolverLayout;

                var idAndVersion = package.Id + "." + package.Version;

                // All the folders should include version to avoid potential race conditions during nuget execution.
                PackageFolder = PackageRootFolder.Combine(pathTable, idAndVersion);
                PackageDirectory = PackageFolder.ToString(pathTable);
                PackageTmpDirectory = TempDirectory.Combine(pathTable, idAndVersion).ToString(pathTable);
                PathToNuspec = PackageFolder.Combine(pathTable, $"{package.Id}.nuspec");
            }

            public static NugetPackageOutputLayout Create(PathTable pathTable, INugetPackage package,
                NugetResolverOutputLayout resolverLayout)
            {
                return new NugetPackageOutputLayout(pathTable, package, resolverLayout);
            }

            public AbsolutePath TempDirectory => m_resolverLayout.TempDirectory;

            public AbsolutePath PackageRootFolder => m_resolverLayout.PackageRootFolder;

            public AbsolutePath PathToNuspec { get; }

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
        public NugetGenerationResult(
            Dictionary<ModuleDescriptor, AbsolutePath> generatedProjectsByModuleDescriptor,
            Dictionary<AbsolutePath, ModuleDescriptor> generatedProjectsByPath,
            MultiValueDictionary<string, ModuleDescriptor> generatedProjectsByModuleName,
            Dictionary<string, NugetAnalyzedPackage> nugetPackagesByModuleName)
        {
            GeneratedProjectsByModuleDescriptor = generatedProjectsByModuleDescriptor;
            GeneratedProjectsByPath = generatedProjectsByPath;
            GeneratedProjectsByModuleName = generatedProjectsByModuleName;
            NugetPackagesByModuleName = nugetPackagesByModuleName;
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

        public Dictionary<string, NugetAnalyzedPackage> NugetPackagesByModuleName { get; }

        internal string GetOriginalNugetPackageName(ModuleDefinition def)
        {
            return NugetPackagesByModuleName != null && NugetPackagesByModuleName.TryGetValue(def.Descriptor.Name, out var value)
                ? value.NugetName
                : def.Descriptor.Name;
        }
    }
}
