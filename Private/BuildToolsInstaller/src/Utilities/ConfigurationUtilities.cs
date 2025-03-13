// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using BuildToolsInstaller.Config;

namespace BuildToolsInstaller.Utilities
{
    internal class ConfigurationUtilities
    {
        public static string GetConfigurationPathForTool(string configurationRootDirectory, BuildTool tool)
        {
            return Path.Combine(configurationRootDirectory, "tools", tool.ToString().ToLower());
        }

        /// <summary>
        /// Resolves a version descriptor to a literal version given in a <see cref="DeploymentConfiguration"/>
        /// </summary>
        public static string? ResolveVersion(DeploymentConfiguration deploymentConfiguration, string? versionDescriptor, out bool ignoreCache, IAdoService adoService, ILogger logger)
        {
            // Default the descriptor to the one in the configuration
            versionDescriptor ??= deploymentConfiguration.Default;
            ignoreCache = false;

            //  Resolve from ring
            if (!deploymentConfiguration.Rings.ContainsKey(versionDescriptor))
            {
                logger.Info($"Could not find configuration for ring {versionDescriptor}. Available rings are: [{string.Join(", ", deploymentConfiguration.Rings.Keys)}]");
                return null;
            }

            ignoreCache = deploymentConfiguration.Rings[versionDescriptor].IgnoreCache;
            return deploymentConfiguration.Rings[versionDescriptor].Version;
        }

        /// <summary>
        /// Tries to resolve the version from the overrides given an <see cref="OverrideConfiguration"/>
        /// Return false if no override matches the current build 
        /// </summary>
        public static bool TryGetOverride(OverrideConfiguration deploymentConfiguration, BuildTool tool, IAdoService adoService, [NotNullWhen(true)] out string? resolvedVersion, ILogger logger)
        {
            resolvedVersion = null;
            if (!adoService.IsEnabled || deploymentConfiguration.Overrides is null || deploymentConfiguration.Overrides.Count == 0)
            {
                // Overrides can only be applied when running an ADO build
                return false;
            }

            var repository = adoService.RepositoryName;
            var pipelineId = adoService.PipelineId;

            foreach (var exception in deploymentConfiguration.Overrides)
            {
                if (string.Equals(exception.Repository, repository, StringComparison.OrdinalIgnoreCase)
                    && (exception.PipelineIds == null || exception.PipelineIds.Contains(pipelineId))
                    && exception.Tools.TryGetValue(tool, out var toolDeployment))
                {
                        resolvedVersion = toolDeployment.Version;
                        var details = string.IsNullOrEmpty(exception.Comment) ? string.Empty : $" Details: {exception.Comment}";
                        logger.Info($"Selecting version {resolvedVersion} for tool {tool} from a global configuration override.{details}");
                        return true;
                }
            }

            // No matches
            return false;
        }
    }
}
