// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildToolsInstaller.Config;
using BuildToolsInstaller.Utilities;
using BuildToolsInstaller.Installers;
using NuGet.Versioning;
using System;
using Microsoft.TeamFoundation.Build.WebApi;

namespace BuildToolsInstaller
{
    /// <summary>
    /// Installs BuildXL from the well-known feed that is assumed to be mirrored in
    /// the organization that is running the installer. 
    /// </summary>
    public class BuildXLNugetInstaller : CentralNugetFeedInstallerBase
    {
        private BuildXLNugetInstallerConfig? m_config;
        private const int DefaultWorkerTimeoutMin = 20;
        private const int PropertiesPollDelaySeconds = 5;
        protected override BuildTool Tool => BuildTool.BuildXL;
        public override string DefaultToolLocationVariable => "ONEES_BUILDXL_LOCATION";

        // Use only after calling TryInitializeConfig()
        private BuildXLNugetInstallerConfig Config => m_config!;

        public BuildXLNugetInstaller(INugetDownloader downloader, string configDirectory, IAdoService adoService, ILogger logger) : base(downloader, configDirectory, adoService, logger)
        {
        }

        protected override async Task<bool> TryInitializeConfigAsync(string? extraConfiguration)
        {
            if (extraConfiguration == null)
            {
                m_config = new BuildXLNugetInstallerConfig();
                return true;
            }

            if (File.Exists(extraConfiguration))
            {
                // The extra configuration is a file path to a JSON that can be deserialized into a BuildXLNugetInstallerConfig
                m_config = await JsonUtilities.DeserializeAsync<BuildXLNugetInstallerConfig>(extraConfiguration, Logger);
            }
            else 
            {
                // The extra configuration is serialized as a string that we can deserialize with this method
                m_config = BuildXLNugetInstallerConfig.DeserializeFromString(extraConfiguration, Logger);
            }

            if (m_config == null)
            {
                Logger.Error("Could not parse the BuildXL installer configuration. Installation will fail.");
                return false;
            }

            if (m_config.DistributedRole != null && !AdoService.IsEnabled)
            {
                Logger.Error("Distributed mode can only be enabld in ADO Builds. Installation will fail.");
                return false;
            }

            return true;
        }

        protected override async Task<(NuGetVersion Version, bool IgnoreCache)?> TryResolveVersionAsync(string packageName, string? versionDescriptor)
        {
            var resolvedVersionProperty = "BuildXLResolvedVersion_" + (Config.InvocationKey ?? string.Empty);
            bool ignoreCache = false;
            string? resolvedVersion = null;
            NuGetVersion? resolvedNugetVersion = null;
            if (Config.Version != null)
            {
                // A version specified in the configuration is preferred to anything else
                resolvedVersion = Config.Version;
            }
            else if (Config.DistributedRole == DistributedRole.Worker)
            {
                var timeoutMinutes = Config.WorkerTimeoutMin ?? DefaultWorkerTimeoutMin;
                Logger.Info($"The installer is running in worker mode. Poll the build properties to get the resolved version from the main agent. This operation will time out after {timeoutMinutes} minutes");

                using var cts = new CancellationTokenSource();
                cts.CancelAfter(TimeSpan.FromMinutes(timeoutMinutes)); // Cancel automatically after the timeout

                try
                {
                    resolvedVersion = await ResolveVersionFromOrchestratorAsync(resolvedVersionProperty, cts.Token);
                    if (resolvedVersion == null)
                    {
                        // Errors have been logged
                        return null;
                    }

                    return (NuGetVersion.Parse(resolvedVersion), ignoreCache);
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested)
                {
                    Logger.Error($"Timed out waiting for resolved version from orchestrator. Timeout: {timeoutMinutes} min");
                    resolvedVersion = null;
                }
            }
            else
            {
                // Orchestrator (or default) mode. Check if we were provided a ring and resolve from it
                var overrides = await GetOverridesConfigurationAsync(Logger);
                if (overrides == null)
                {
                    Logger.Error("Failed to get the overrides configuration. Installation has failed.");
                    return null;
                }

                if (ConfigurationUtilities.TryGetOverride(overrides, BuildTool.BuildXL, AdoService, out var overridenVersion, Logger))
                {
                    // The version is selected from an explicit override
                    resolvedVersion = overridenVersion;
                }
                if (versionDescriptor == null || IsValidRing(versionDescriptor))
                {
                    resolvedVersion = ConfigurationUtilities.ResolveVersion(DeploymentConfig, versionDescriptor, out ignoreCache, AdoService, Logger);
                    if (resolvedVersion == null)
                    {
                        Logger.Error($"Failed to resolve version to install for ring {versionDescriptor}. Installation has failed.");
                        return null;
                    }
                }
                else
                {
                    // If it's not a ring, then it should be a valid Nuget version, which we'll parse below
                    resolvedVersion = versionDescriptor;
                }
            }

            if (!NuGetVersion.TryParse(resolvedVersion, out resolvedNugetVersion))
            {
                Logger.Error($"The provided version for the {packageName} package is malformed: {versionDescriptor}.");
                return null;
            }

            if (Config.DistributedRole == DistributedRole.Orchestrator)
            {
                // We resolved a version - we should push it to the properties for the workers to consume.
                // If the version is null it means we encountered some error above, so push the empty string
                // (the workers must be signalled that there was an error somehow).
                await AdoService.SetBuildPropertyAsync(resolvedVersionProperty, resolvedVersion ?? string.Empty);
            }

            return (resolvedNugetVersion, ignoreCache);
        }

        private Task<OverrideConfiguration?> GetOverridesConfigurationAsync(ILogger logger)
        {
            var overridesConfigPath = Path.Combine(ConfigurationUtilities.GetConfigurationPathForTool(ConfigDirectory, BuildTool.BuildXL), "overrides.json");
            if (File.Exists(overridesConfigPath))
            {
                return JsonUtilities.DeserializeAsync<OverrideConfiguration>(overridesConfigPath, logger);
            }
            else
            {
                return Task.FromResult<OverrideConfiguration?>(new() { Overrides = [] });
            }
        }

        private bool IsValidRing(string selectedVersion)
        {
            return
                selectedVersion == "Dogfood"
                || selectedVersion == "GeneralPublic"
                || selectedVersion == "Golden";
        }

        private async Task<string?> ResolveVersionFromOrchestratorAsync(string propertyKey, CancellationToken token)
        {
            await Task.Yield();
            while (true)
            {
                token.ThrowIfCancellationRequested();

                var maybeProperty = await AdoService.GetBuildPropertyAsync(propertyKey);
                if (maybeProperty != null)
                {
                    // Orchestrator pushes an empty string on error
                    if (maybeProperty == string.Empty)
                    {
                        Logger.Error("The orchestrator stage installer encountered an error resolving the version. This installer is running in worker mode and it can't continue.");
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
