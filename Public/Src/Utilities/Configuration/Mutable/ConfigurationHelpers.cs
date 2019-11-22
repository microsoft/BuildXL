// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <summary>
    /// Helper methods for configuration values.
    /// </summary>
    public static class ConfigurationHelpers
    {
        /// <summary>
        /// Helper for tests to create a default configuration object
        /// </summary>
        public static CommandLineConfiguration GetDefaultForTesting(PathTable pathTable, AbsolutePath configFile)
        {
            var rootPath = configFile.GetParent(pathTable);
            var outPath = rootPath.Combine(pathTable, PathAtom.Create(pathTable.StringTable, "Out"));
            var logsPath = outPath.Combine(pathTable, PathAtom.Create(pathTable.StringTable, "Logs"));
            var engineCacheDir = rootPath.Combine(pathTable, PathAtom.Create(pathTable.StringTable, "cache"), PathAtom.Create(pathTable.StringTable, "engineCache"));

            return new CommandLineConfiguration
            {
                Startup =
                {
                    ConfigFile = configFile,
                    CurrentHost = new Host(),
                },
                Layout =
                {
                    PrimaryConfigFile = configFile,
                    SourceDirectory = rootPath,
                    OutputDirectory = outPath,
                    ObjectDirectory = rootPath.Combine(pathTable, PathAtom.Create(pathTable.StringTable, "obj")),
                    CacheDirectory = rootPath.Combine(pathTable, PathAtom.Create(pathTable.StringTable, "cache")),
                    BuildEngineDirectory = rootPath.Combine(pathTable, PathAtom.Create(pathTable.StringTable, "bxl.exe")),
                    EngineCacheDirectory = engineCacheDir,
                },
                Logging =
                {
                    LogsDirectory = logsPath,
                    LogExecution = false,
                    StoreFingerprints = false
                },
                Engine =
                {
                    MaxRelativeOutputDirectoryLength = 260,
                    TrackBuildsInUserFolder = false,
                },
                Schedule =
                {
                    MaxIO = 1,
                    MaxProcesses = 1,
                },
                Cache =
                {
                    CacheSpecs = SpecCachingOption.Disabled,
                    CacheLogFilePath = logsPath.Combine(pathTable, PathAtom.Create(pathTable.StringTable, "cache.log")),
                },
                FrontEnd =
                {
                    MaxFrontEndConcurrency = 1,
                },
                Sandbox =
                {
                    FileSystemMode = FileSystemMode.RealAndMinimalPipGraph,
                },
            };
        }
    }
}
