// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using BuildToolsInstaller.Config;

namespace BuildToolsInstaller.Utilities
{
    internal class ConfigurationUtilities
    {
        public static string? ResolveVersion(DeploymentConfiguration deploymentConfiguration, string ring, BuildTool tool, IAdoService adoService, ILogger logger)
        {
            // 1. Check overrides
            if (TryGetFromOverride(deploymentConfiguration, tool, adoService, out var resolvedVersion, logger))
            {
                return resolvedVersion;
            }

            // 2. Resolve from ring
            var selectedRing = deploymentConfiguration.Rings.FirstOrDefault(r => r.Name == ring);
            if (selectedRing == null)
            {
                logger.Error($"Could not find configuration for ring {ring}. Available rings are: [{string.Join(", ", deploymentConfiguration.Rings.Select(r => r.Name))}]");
                return null;
            }

            if(!selectedRing.Tools.TryGetValue(tool, out var resolved))
            {
                logger.Error($"Could not find configuration for tool {tool} in ring {ring}.");
                return null;
            }

            return resolved.Version;
        }

        private static bool TryGetFromOverride(DeploymentConfiguration deploymentConfiguration, BuildTool tool, IAdoService adoService, [NotNullWhen(true)] out string? resolvedVersion, ILogger logger)
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
