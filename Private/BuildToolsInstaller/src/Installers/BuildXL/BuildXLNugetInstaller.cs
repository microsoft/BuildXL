// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using System.Threading;
using BuildToolsInstaller.Config;
using BuildToolsInstaller.Utiltiies;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace BuildToolsInstaller
{
    /// <summary>
    /// Installs BuildXL from the well-known feed that is assumed to be mirrored in
    /// the organization that is running the installer. 
    /// </summary>
    public class BuildXLNugetInstaller : IToolInstaller
    {
        // Default ring for BuildXL installation
        public string DefaultRing => "GeneralPublic";

        private readonly IAdoService m_adoService;
        private readonly INugetDownloader m_downloader;
        private readonly ILogger m_logger;
        private BuildXLNugetInstallerConfig? m_config;
        private const int DefaultWorkerTimeoutMin = 20;
        private const int PropertiesPollDelaySeconds = 5;

        private static string PackageName => OperatingSystem.IsWindows() ? "BuildXL.win-x64" : "BuildXL.linux-x64";

        // Use only after calling TryInitializeConfig()
        private BuildXLNugetInstallerConfig Config => m_config!;

        public BuildXLNugetInstaller(INugetDownloader downloader, IAdoService adoService, ILogger logger)
        {
            m_adoService = adoService;
            m_downloader = downloader;
            m_logger = logger;
        }

        /// <inheritdoc />
        public async Task<bool> InstallAsync(string selectedVersion, BuildToolsInstallerArgs args)
        {
            if (!await TryInitializeConfigAsync(args))
            {
                return false;
            }

            try
            {
                // Version override
                var version = await TryResolveVersionAsync(selectedVersion);
                if (version == null)
                {
                    // Error should have been logged
                    return false;
                }

                var downloadLocation = GetCachedToolRootDirectory(args.ToolsDirectory);
                var engineLocation = GetDownloadLocation(args.ToolsDirectory, version);
                if (Path.Exists(engineLocation))
                {
                    // TODO: Can we use the Nuget cache to handle this instead of doing this naive existence check?

                    m_logger.Info($"BuildXL version {version} already installed at {engineLocation}.");

                    if (args.ForceInstallation)
                    {
                        m_logger.Info($"Installation is forced. Deleting {engineLocation} and re-installing.");
                        
                        // Delete the contents of the installation directory and continue
                        if (!TryDeleteInstallationDirectory(engineLocation))
                        {
                            return false;
                        }
                    }
                    else
                    {
                        m_logger.Info($"Skipping download");
                        SetLocationVariable(engineLocation);
                        return true;
                    }
                }

                if (!NuGetVersion.TryParse(version, out var nugetVersion))
                {
                    m_logger.Error($"The provided version for BuildXL package is malformed: {Config.Version}.");
                    return false;
                }

                var feed = Config.FeedOverride ?? InferSourceRepository(m_adoService);

                var repository = CreateSourceRepository(feed);
                if (await m_downloader.TryDownloadNugetToDiskAsync(repository, PackageName, nugetVersion, downloadLocation, m_logger))
                {
                    SetLocationVariable(engineLocation);
                    return true;
                }
            }
            catch (Exception ex)
            {
                m_logger.Error($"Failed trying to download nuget package '{PackageName}' : '{ex}'");
            }

            return false;
        }

        private bool TryDeleteInstallationDirectory(string engineLocation)
        {
            try
            {
                Directory.Delete(engineLocation, true);
            }
            catch (Exception e)
            {
                m_logger.Error(e, "Couldn't delete pre-existing installation directory {engineLocation}");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Construct the implicit source repository for installers, a well-known feed that should be installed in the organization
        /// </summary>
        private static string InferSourceRepository(IAdoService adoService)
        {
            if (!adoService.IsEnabled)
            {
                throw new InvalidOperationException("Automatic source repository inference is only supported when running on an ADO Build");
            }

            if (!adoService.TryGetOrganizationName(out var adoOrganizationName))
            {
                throw new InvalidOperationException("Could not retrieve organization name");
            }

            // This feed is installed in every organization as part of 1ESPT onboarding,
            // so we can assume its existence in this context, but we also assume throughout
            // that this feed will upstream the relevant feeds needed to acquire BuildXL
            // (as this set-up should be a part of the onboarding to 'BuildXL on 1ESPT').
            return $"https://pkgs.dev.azure.com/{adoOrganizationName}/_packaging/Guardian1ESPTUpstreamOrgFeed/nuget/v3/index.json";
        }

        /// <nodoc />
        private static SourceRepository CreateSourceRepository(string feedUrl)
        {
            var packageSource = new PackageSource(feedUrl, "SourceFeed");

            // Because the feed is known (either the well-known mirror or the user-provided override),
            // we can simply use a PAT that we assume will grant the appropriate privileges instead of going through a credential provider.
            packageSource.Credentials = new PackageSourceCredential(feedUrl, "IrrelevantUsername", GetPatFromEnvironment(), true, string.Empty);
            return Repository.Factory.GetCoreV3(packageSource);
        }

        private void SetLocationVariable(string engineLocation)
        {
            if (m_adoService.IsEnabled)
            {
                m_adoService.SetVariable("ONEES_BUILDXL_LOCATION", engineLocation, isReadOnly: true);
            }
        }

        private async Task<bool> TryInitializeConfigAsync(BuildToolsInstallerArgs args)
        {
            if (args.ConfigFilePath == null)
            {
                m_config = new BuildXLNugetInstallerConfig();
                return true;
            }

            m_config = await JsonUtilities.DeserializeAsync<BuildXLNugetInstallerConfig>(args.ConfigFilePath, m_logger, serializerOptions: new () { PropertyNameCaseInsensitive = true });
            if (m_config == null)
            {
                m_logger.Error("Could not parse the BuildXL installer configuration. Installation will fail.");
                return false;
            }

            if (m_config.DistributedRole != null && !m_adoService.IsEnabled)
            {
                m_logger.Error("Distributed mode can only be enabld in ADO Builds. Installation will fail.");
                return false;
            }

            return true;
        }

        internal static string GetDownloadLocation(string toolDirectory, string version) => Path.Combine(GetCachedToolRootDirectory(toolDirectory), $"{PackageName}.{version}");
        private static string GetCachedToolRootDirectory(string toolDirectory) => Path.Combine(toolDirectory, "BuildXL", "x64");

        private static string GetPatFromEnvironment()
        {
            return Environment.GetEnvironmentVariable("BUILDTOOLSDOWNLOADER_NUGET_PAT") ?? Environment.GetEnvironmentVariable("SYSTEM_ACCESSTOKEN") ?? "";
        }

        private async Task<string?> TryResolveVersionAsync(string selectedVersion)
        {
            var resolvedVersionProperty = "BuildXLResolvedVersion_" + (Config.InvocationKey ?? "default");
            string? resolvedVersion = null;
            if (Config.Version != null)
            {
                // A version specified in the configuration is preferred to anything else
                resolvedVersion = Config.Version;
            }
            else if (Config.DistributedRole == DistributedRole.Worker)
            {
                var timeoutMinutes = Config.WorkerTimeoutMin ?? DefaultWorkerTimeoutMin;
                m_logger.Info($"The installer is running in worker mode. Poll the build properties to get the resolved version from the main agent. This operation will time out after {timeoutMinutes} minutes");

                using var cts = new CancellationTokenSource();
                cts.CancelAfter(TimeSpan.FromMinutes(timeoutMinutes)); // Cancel automatically after the timeout

                try
                {
                    resolvedVersion = await ResolveVersionFromOrchestratorAsync(resolvedVersionProperty, cts.Token);
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested)
                {
                    m_logger.Error($"Timed out waiting for resolved version from orchestrator. Timeout: {timeoutMinutes} min");
                    resolvedVersion = null;
                }
            }
            else
            {
                // Orchestrator (or default) mode 
                resolvedVersion = selectedVersion;
            }

            if (Config.DistributedRole == DistributedRole.Orchestrator)
            {
                // We resolved a version - we should push it to the properties for the workers to consume.
                // If the version is null it means we encountered some error above, so push the empty string
                // (the workers must be signalled that there was an error somehow).
                await m_adoService.SetBuildPropertyAsync(resolvedVersionProperty, resolvedVersion ?? string.Empty);
            }

            return resolvedVersion;
        }

        private async Task<string?> ResolveVersionFromOrchestratorAsync(string propertyKey, CancellationToken token)
        {
            await Task.Yield();
            while (true)
            {
                token.ThrowIfCancellationRequested();

                var maybeProperty = await m_adoService.GetBuildPropertyAsync(propertyKey);
                if (maybeProperty != null)
                {
                    // Orchestrator pushes an empty string on error
                    if (maybeProperty == string.Empty)
                    {
                        m_logger.Error("The orchestrator stage installer encountered an error resolving the version. This installer is running in worker mode and it can't continue.");
                        return null;
                    }

                    // Should have the resolved property
                    return maybeProperty;
                }

                // Wait before polling again
                await Task.Delay(TimeSpan.FromSeconds(PropertiesPollDelaySeconds), token);
            }

        }
    }
}
