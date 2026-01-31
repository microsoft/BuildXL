// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AdoBuildRunner.Configuration;
using AdoBuildRunner.Configuration.Mutable;
using BuildXL.AdoBuildRunner;
using BuildXL.AdoBuildRunner.Vsts;

namespace AdoBuildRunner.Utilties
{
    /// <nodoc />
    public static class BuildOverridesHelper
    {
        private static readonly JsonSerializerOptions s_jsonSerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        internal const string ConfigurationEnvironmentVariable = "ONEES_TOOL_CONFIG_LOCATION"; // This is defined in the 1ESPT workflow


        /// <summary>
        /// Load build overrides from the well-known file that is placed by the 1ES build tools installer,
        /// match them against the current build, and return the matching override.
        /// Returns null if the override file can't be found or no overrides match the current build
        /// </summary>
        public static async Task<IBuildOverrides?> LoadBuildOverrides(string? invocationKey, IAdoEnvironment adoEnvironment, Logger logger)
        {
            var configLocation = Environment.GetEnvironmentVariable(ConfigurationEnvironmentVariable);
            if (string.IsNullOrEmpty(configLocation))
            {
                logger.Info($"No build overrides configuration location defined in environment variable '{ConfigurationEnvironmentVariable}'");
                return null;
            }

            // Deserialize the AdoBuildRunnerConfiguration/buildOverrides.json file, if exists, in the config location
            var buildOverridesPath = Path.Combine(configLocation, "AdoBuildRunnerConfiguration", "buildOverrides.json");
            OverridesConfiguration? OverridesConfiguration = null;
            if (File.Exists(buildOverridesPath))
            {
                OverridesConfiguration = await DeserializeOverrides(buildOverridesPath, logger);
            }
            else
            {
                logger.Debug($"No build overrides configuration file found at '{buildOverridesPath}'");
                return null;
            }

            if (OverridesConfiguration != null)
            {
                return TryGetMatchingOverride(invocationKey, OverridesConfiguration, adoEnvironment, logger);
            }
            else
            {
                // Error should have been logged
                return null;
            }
        }

        internal static async ValueTask<OverridesConfiguration?> DeserializeOverrides(string path, ILogger logger)
        {
            try
            {
                using var stream = File.OpenRead(path);
                return await JsonSerializer.DeserializeAsync<OverridesConfiguration?>(stream, s_jsonSerializerOptions);
            }
            catch (Exception e)
            {
                logger.Warning($"Exception thrown while deserializing AdoBuildRunner overrides. Continuing build without overrides. Details: {e}");
                return null;
            }
        }

        /// <summary>
        /// Tries to resolve the version from the overrides given an <see cref="OverridesConfiguration"/>
        /// Return false if no override matches the current build 
        /// </summary>
        internal static IBuildOverrides? TryGetMatchingOverride(string? invocationKey, OverridesConfiguration OverridesConfiguration, IAdoEnvironment adoEnvironment, ILogger logger)
        {
            if (OverridesConfiguration is null || OverridesConfiguration.Rules.Count == 0)
            {
                return null;
            }

            var repository = adoEnvironment.RepositoryName;
            var pipelineId = adoEnvironment.DefinitionId;

            foreach (var rule in OverridesConfiguration.Rules)
            {
                if (string.Equals(rule.Repository, repository, StringComparison.OrdinalIgnoreCase)
                    && (rule.PipelineIds == null || rule.PipelineIds.Contains(pipelineId))
                    && (rule.InvocationKeys == null || rule.InvocationKeys.Contains(invocationKey)))
                {
                    // Rule matches
                    logger.Info($"Applying AdoBuildRunner build overrides for this build. Comment: {rule.Comment ?? "<no comment>"}");
                    return rule.Overrides;
                }
            }

            // No matches
            return null;
        }
    }
}
