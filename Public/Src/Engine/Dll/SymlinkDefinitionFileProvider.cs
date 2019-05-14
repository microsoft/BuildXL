// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Engine.Distribution;
using BuildXL.Native.IO;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Artifacts;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Engine
{
    /// <summary>
    /// Provider for symlink file.
    /// </summary>
    internal static class SymlinkDefinitionFileProvider
    {
        /// <summary>
        /// Loads configured symlink definitions (if not already loaded)
        /// Stores to cache for use by workers in distributed build
        /// Eagerly creates symlinks if lazy symlink creation is disabled
        /// </summary>
        public static async Task<Possible<SymlinkDefinitions>> TryPrepareSymlinkDefinitionsAsync(
            LoggingContext loggingContext,
            GraphReuseResult reuseResult,
            IConfiguration configuration,
            MasterService masterService,
            CacheInitializationTask cacheInitializerTask,
            PipExecutionContext context,
            ITempDirectoryCleaner tempDirectoryCleaner = null)
        {
            var pathTable = context.PathTable;
            bool isDistributedMaster = configuration.Distribution.BuildRole == DistributedBuildRoles.Master;
            Possible<SymlinkDefinitions> maybeSymlinkDefinitions = new Possible<SymlinkDefinitions>((SymlinkDefinitions)null);
            if (reuseResult?.IsFullReuse == true)
            {
                maybeSymlinkDefinitions = reuseResult.EngineSchedule.Scheduler.SymlinkDefinitions;
            }
            else if (configuration.Layout.SymlinkDefinitionFile.IsValid)
            {
                var symlinkFilePath = configuration.Layout.SymlinkDefinitionFile.ToString(pathTable);
                Logger.Log.SymlinkFileTraceMessage(loggingContext, I($"Loading symlink file from location '{symlinkFilePath}'."));
                maybeSymlinkDefinitions = await SymlinkDefinitions.TryLoadAsync(
                    loggingContext,
                    pathTable,
                    symlinkFilePath,
                    symlinksDebugPath: configuration.Logging.LogsDirectory.Combine(pathTable, "DebugSymlinksDefinitions.log").ToString(pathTable),
                    tempDirectoryCleaner: tempDirectoryCleaner);
            }

            if (!maybeSymlinkDefinitions.Succeeded || maybeSymlinkDefinitions.Result == null)
            {
                return maybeSymlinkDefinitions;
            }

            // Need to store symlinks to cache for workers
            if (configuration.Distribution.BuildRole == DistributedBuildRoles.Master)
            {
                var possibleCacheInitializer = await cacheInitializerTask;
                if (!possibleCacheInitializer.Succeeded)
                {
                    return possibleCacheInitializer.Failure;
                }

                Logger.Log.SymlinkFileTraceMessage(loggingContext, I($"Storing symlink file for use by workers."));

                var symlinkFile = configuration.Layout.SymlinkDefinitionFile.Expand(pathTable);

                var possibleStore = await TryStoreToCacheAsync(
                    loggingContext,
                    cache: possibleCacheInitializer.Result.CreateCacheForContext().ArtifactContentCache,
                    symlinkFile: symlinkFile);

                if (!possibleStore.Succeeded)
                {
                    return possibleStore.Failure;
                }

                masterService.SymlinkFileContentHash = possibleStore.Result;
                Logger.Log.SymlinkFileTraceMessage(loggingContext, I($"Stored symlink file for use by workers."));
            }

            if (!configuration.Schedule.UnsafeLazySymlinkCreation || configuration.Engine.PopulateSymlinkDirectories.Count != 0)
            {
                // Symlink definition file is defined, and BuildXL intends to create it eagerly.
                // At this point master and worker should have had its symlink definition file, if specified.
                if (!FileContentManager.CreateSymlinkEagerly(loggingContext, configuration, pathTable, maybeSymlinkDefinitions.Result, context.CancellationToken))
                {
                    return new Failure<string>("Failed eagerly creating symlinks");
                }
            }

            return maybeSymlinkDefinitions;
        }

        /// <summary>
        /// Tries to store symlink file to cache.
        /// </summary>
        public static async Task<Possible<ContentHash>> TryStoreToCacheAsync(
            LoggingContext loggingContext,
            IArtifactContentCache cache,
            ExpandedAbsolutePath symlinkFile)
        {
            var possibleStore = await cache.TryStoreAsync(FileRealizationMode.HardLinkOrCopy, symlinkFile);
            if (!possibleStore.Succeeded)
            {
                Tracing.Logger.Log.FailedStoreSymlinkFileToCache(loggingContext, symlinkFile.ExpandedPath, possibleStore.Failure.DescribeIncludingInnerFailures());
                return possibleStore.Failure;
            }

            Logger.Log.SymlinkFileTraceMessage(loggingContext, I($"Stored symlink file '{symlinkFile}' with hash '{possibleStore.Result}'."));
            return possibleStore.Result;
        }

        private const string SymlinkFileName = "SymlinkDefinitions";

        /// <summary>
        /// Tries to fetch symlink file from the cache.
        /// </summary>
        public static async Task<bool> TryFetchWorkerSymlinkFileAsync(
            LoggingContext loggingContext,
            PathTable pathTable,
            EngineCache cache,
            ILayoutConfiguration layoutConfiguration,
            WorkerService workerService,
            AsyncOut<AbsolutePath> symlinkPathAsyncOut)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(pathTable != null);
            Contract.Requires(cache != null);
            Contract.Requires(workerService != null);
            Contract.Requires(symlinkPathAsyncOut != null);

            symlinkPathAsyncOut.Value = AbsolutePath.Invalid;
            var symlinkFileContentHash = workerService.BuildStartData.SymlinkFileContentHash.ToContentHash();
            if (symlinkFileContentHash == WellKnownContentHashes.AbsentFile)
            {
                // Absent file meaning there is no symlink file to use
                return true;
            }

            Logger.Log.SymlinkFileTraceMessage(loggingContext, I($"Attempting to retrieve symlink file with hash '{symlinkFileContentHash}'."));

            var maybeLoaded = await cache.ArtifactContentCache.TryLoadAvailableContentAsync(new[] { symlinkFileContentHash });
            if (!maybeLoaded.Succeeded)
            {
                Tracing.Logger.Log.FailedLoadSymlinkFileFromCache(loggingContext, maybeLoaded.Failure.DescribeIncludingInnerFailures());
                return false;
            }

            var destinationPath = layoutConfiguration.EngineCacheDirectory.Combine(pathTable, SymlinkFileName).Expand(pathTable);
            var materializedFile =
                await
                    cache.ArtifactContentCache.TryMaterializeAsync(
                        FileRealizationMode.HardLinkOrCopy,
                        destinationPath,
                        symlinkFileContentHash);

            if (!materializedFile.Succeeded)
            {
                Tracing.Logger.Log.FailedMaterializeSymlinkFileFromCache(
                    loggingContext,
                    destinationPath.ExpandedPath,
                    materializedFile.Failure.DescribeIncludingInnerFailures());
                return false;
            }

            Logger.Log.SymlinkFileTraceMessage(loggingContext, I($"Symlink file with hash '{symlinkFileContentHash}' materialized to location '{destinationPath}'."));
            symlinkPathAsyncOut.Value = destinationPath.Path;
            return true;
        }
    }
}
