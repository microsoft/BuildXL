// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Engine.Cache.Fingerprints.TwoPhase;
using BuildXL.Engine;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.TestUtilities;

namespace Test.BuildXL.EngineTestUtilities
{
    /// <summary>
    /// Helper to create Test Configuration
    /// </summary>
    public static class ConfigHelpers
    {
        /// <summary>
        /// Configures the engine to use an in-memory cache for testing
        /// </summary>
        public static BuildXLEngine ConfigureInMemoryCache(this BuildXLEngine engine, TestCache testCache = null)
        {
            if (engine.TestHooks == null)
            {
                engine.TestHooks = new EngineTestHooksData();
            }

            if (testCache != null)
            {
                engine.TestHooks.CacheFactory = () =>
                    {
                        return new EngineCache(
                            testCache.GetArtifacts(),
                            testCache.Fingerprints);
                    };
            }
            else
            {
                engine.TestHooks.CacheFactory = () =>
                    {
                        return new EngineCache(
                            new InMemoryArtifactContentCache(),
                            new InMemoryTwoPhaseFingerprintStore());
                    };
            }

            return engine;
        }

        /// <summary>
        /// Creates a default configuration
        /// </summary>
        public static CommandLineConfiguration CreateDefault(PathTable pathTable, string configFilePath, TempFileStorage tempStorage)
        {
            var paths = new Paths(pathTable);

            var configFile = paths.CreateAbsolutePath(configFilePath);

            var result = ConfigurationHelpers.GetDefaultForTesting(pathTable, configFile);
            result.Layout.ObjectDirectory = paths.CreateAbsolutePath(tempStorage.GetUniqueDirectory());
            result.Layout.CacheDirectory = paths.CreateAbsolutePath(tempStorage.GetUniqueDirectory());

            return result;
        }

        /// <summary>
        /// Creates a default configuration
        /// </summary>
        public static CommandLineConfiguration CreateDefaultForXml(PathTable pathTable, AbsolutePath rootPath)
        {
            var paths = new Paths(pathTable);

            var configFile = paths.CreateAbsolutePath(rootPath, "config.dc");
            return ConfigurationHelpers.GetDefaultForTesting(pathTable, configFile);
        }

        /// <summary>
        /// Creates a default configuration
        /// </summary>
        public static CommandLineConfiguration CreateDefaultForScript(PathTable pathTable, AbsolutePath rootPath)
        {
            var paths = new Paths(pathTable);

            var configFile = paths.CreateAbsolutePath(rootPath, Names.ConfigDsc);
            return ConfigurationHelpers.GetDefaultForTesting(pathTable, configFile);
        }
    }
}
