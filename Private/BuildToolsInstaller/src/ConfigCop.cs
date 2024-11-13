// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildToolsInstaller.Config;
using BuildToolsInstaller.Logging;
using BuildToolsInstaller.Utiltiies;

namespace BuildToolsInstaller
{
    internal class ConfigCop
    {
        /// <summary>
        /// Ring names in the configuration are not enforced by serialization (the name can be any string).
        /// The intention is to support heterogeneity in the ring definitions, because different tools might have
        /// different ring models. 
        /// But tools might want to validate that they are indeed deployed in particular rings
        /// This mapping provides a way to perform that validation.
        /// </summary>
        public static readonly Dictionary<BuildTool, List<string>> RingsByTool = new Dictionary<BuildTool, List<string>>()
        {
            { BuildTool.BuildXL, [ "Dogfood", "GeneralPublic", "Golden" ] }
        };

        public static async Task<int> ValidateConfiguration(string path)
        {
            ILogger logger = AdoService.Instance.IsEnabled ? new AdoConsoleLogger() : new ConsoleLogger();
            logger.Info($"Deserializing configuration.");
            var configuration = await JsonUtilities.DeserializeAsync<DeploymentConfiguration>(path, logger, default);

            if (configuration is null)
            {
                logger.Error($"Error deserializing configuration to {nameof(DeploymentConfiguration)}. Details should have been logged.");
                logger.Warning($"Note that property names are case insensitive, and should be in camelCase.");
                return 1;
            }

            logger.Info("Configuration was correctly deserialized.");
            logger.Info("Validating contents.");
            // 1. Validate rings
            if (!ValidateRings(configuration, logger))
            {
                return 1;
            }

            logger.Info("Configuration is valid");
            return 0;
        }

        private static bool ValidateRings(DeploymentConfiguration configuration, ILogger logger)
        {
            // Reverse mapping - get all the tools defined per ring
            var toolsByRing = new Dictionary<string, HashSet<BuildTool>>();
            foreach (var (tool, rings) in RingsByTool)
            {
                foreach (var ring in rings)
                {
                    toolsByRing.TryAdd(ring, new HashSet<BuildTool>());
                    toolsByRing[ring].Add(tool);
                }
            }

            bool success = true;
            var names = new HashSet<string>();
            for (var i = 0; i < configuration.Rings.Count; i++)
            {
                var ring = configuration.Rings[i];
                if (string.IsNullOrEmpty(ring.Name))
                {
                    logger.Error($"Rings can't have empty names (property: rings. index: {i})");
                    success = false;
                }
                
                if (!names.Add(ring.Name))
                {
                    logger.Error($"Duplicate name {ring.Name} found at index {i}");
                    success = false;
                }

                // Verify that all the tools that are known for the ring by this installer are there
                // Don't fail if this ring is not known by any tool in this installer, it just means that it will be ignored
                if (toolsByRing.TryGetValue(ring.Name, out var knownToolsForRing))
                {
                    foreach (var expectedTool in knownToolsForRing)
                    {
                        if (!ring.Tools.ContainsKey(expectedTool))
                        {
                            logger.Error($"Tool {expectedTool} is expected to be defined for ring {ring.Name}, but it wasn't found");
                            success = false;
                        }
                    }
                }

                foreach (var toolDef in ring.Tools)
                {
                    if (string.IsNullOrEmpty(toolDef.Value.Version))
                    {
                        logger.Error($"Version for tool {toolDef.Key} in ring {ring.Name} is empty");
                        success = false;
                    }

                    if (RingsByTool.TryGetValue(toolDef.Key, out var ringsForTool) && !ringsForTool.Contains(ring.Name))
                    {
                        logger.Error($"Tool {toolDef.Key} is not expected to be supported in ring {ring.Name}");
                        success = false;
                    }
                }
            }

            return success;
        }
    }
}
