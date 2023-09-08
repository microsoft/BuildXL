// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Native.IO;
using BuildXL.Utilities.ParallelAlgorithms;
using BuildXL.Utilities.Core;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// This interface must be implemented by any database that can be checkpointed by <see cref="CheckpointManifest"/>
    /// </summary>
    public interface ICheckpointable : IStartupShutdownSlim
    {
        public BoolResult SaveCheckpoint(OperationContext context, AbsolutePath checkpointDirectory);

        public BoolResult RestoreCheckpoint(OperationContext context, AbsolutePath checkpointDirectory);

        public bool IsImmutable(AbsolutePath filePath);

        public void SetGlobalEntry(string key, string? value);

        public bool TryGetGlobalEntry(string key, [NotNullWhen(true)] out string? value);
    }

    /// <summary>
    /// Helper class responsible for creating and restoring checkpoints of a local database.
    /// </summary>
    public sealed class CheckpointManager : StartupShutdownComponentBase
    {
        protected override Tracer Tracer => WorkaroundTracer;

        public Tracer WorkaroundTracer { get; set; } = new Tracer(nameof(CheckpointManager));

        private const string IncrementalCheckpointIdSuffix = "|Incremental";
        private const string CheckpointStateKey = "CheckpointManager.CheckpointState";
        private const string CheckpointManifestKey = "CheckpointManager.Manifest";

        public ICheckpointable Database { get; }

        public ICheckpointRegistry CheckpointRegistry { get; }

        public CentralStorage Storage { get; }

        private readonly IAbsFileSystem _fileSystem;

        private readonly AbsolutePath _checkpointStagingDirectory;

        internal CounterCollection<ContentLocationStoreCounters> Counters { get; }

        private readonly CheckpointManagerConfiguration _configuration;

        private readonly IClock _clock;

        /// <inheritdoc />
        public CheckpointManager(
            ICheckpointable database,
            ICheckpointRegistry checkpointRegistry,
            CentralStorage storage,
            CheckpointManagerConfiguration configuration,
            CounterCollection<ContentLocationStoreCounters> counters,
            IClock clock)
        {
            Database = database;
            CheckpointRegistry = checkpointRegistry;
            Storage = storage;
            _configuration = configuration;
            _fileSystem = new PassThroughFileSystem();
            _checkpointStagingDirectory = configuration.WorkingDirectory / "staging";
            Counters = counters;
            _clock = clock;

            LinkLifetime(Database);
            LinkLifetime(CheckpointRegistry);
            LinkLifetime(Storage);

            if (_configuration.RestoreCheckpoints)
            {
                RunInBackground(nameof(RestoreCheckpointLoopAsync), RestoreCheckpointLoopAsync, fireAndForget: true);
            }
        }

        private async Task<BoolResult> RestoreCheckpointLoopAsync(OperationContext context)
        {
            while (!context.Token.IsCancellationRequested)
            {
                await PeriodicRestoreCheckpointAsync(context).IgnoreFailure();

                await Task.Delay(_configuration.RestoreCheckpointInterval, context.Token);
            }

            return BoolResult.Success;
        }

        public Task<BoolResult> PeriodicRestoreCheckpointAsync(OperationContext context)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var checkpointState = await CheckpointRegistry.GetCheckpointStateAsync(context).ThrowIfFailureAsync();

                    return await RestoreCheckpointAsync(context, checkpointState);
                });
        }

        private record DatabaseStats
        {
            public double ContentColumnFamilySizeMb { get; set; } = -1;

            public double ContentDataSizeMb { get; set; } = -1;

            public double MetadataColumnFamilySizeMb { get; set; } = -1;

            public double MetadataDataSizeMb { get; set; } = -1;

            public double SizeOnDiskMb { get; set; } = -1;
        }

        /// <summary>
        /// Creates a checkpoint for a given sequence point.
        /// </summary>
        public Task<BoolResult> CreateCheckpointAsync(OperationContext context, EventSequencePoint sequencePoint, TimeSpan? maxEventProcessingDelay)
        {
            context = context.CreateNested(Tracer.Name);

            string checkpointId = "Unknown";
            string eventProcessingDelayMessage =
                maxEventProcessingDelay == null ? string.Empty : $" MaxEventProcessingDelay=[{maxEventProcessingDelay}]";
            var dbStats = new DatabaseStats();
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    // Creating a working temporary directory
                    using (new DisposableDirectory(_fileSystem, _checkpointStagingDirectory))
                    {
                        // Write out the time this checkpoint was generated to the database. This will be used by
                        // the workers in order to determine whether they should restore or not after restart. The
                        // checkpoint id is generated inside the upload methods, so we only generate the guid here.
                        // Since this is only used for reporting purposes, there's no harm in it.
                        var checkpointGuid = Guid.NewGuid();
                        DatabaseWriteCheckpointCreationTime(context, checkpointGuid.ToString(), DateTime.UtcNow);

                        // NOTE(jubayard): this needs to be done previous to checkpointing, because we always
                        // fetch the latest version's size in this way. This implies there may be some difference
                        // between the reported value and the actual size on disk: updates will get in in-between.
                        // The better alternative is to actually open the checkpoint and ask, but it seems like too
                        // much.
                        TryFillDatabaseStats(dbStats);

                        // Saving checkpoint for the database into the temporary folder
                        Database.SaveCheckpoint(context, _checkpointStagingDirectory).ThrowIfFailure();

                        try
                        {
                            dbStats.SizeOnDiskMb = _fileSystem
                                .EnumerateFiles(_checkpointStagingDirectory, EnumerateOptions.Recurse)
                                .Sum(fileInfo => fileInfo.Length) * 1e-6;

                            Tracer.TrackMetric(context, "CheckpointSize", (long)dbStats.SizeOnDiskMb);
                        }
                        catch (IOException e)
                        {
                            Tracer.Error(context, $"Error counting size of checkpoint's staging directory `{_checkpointStagingDirectory}`: {e}");
                        }

                        checkpointId = await CreateCheckpointIncrementalAsync(context, sequencePoint, checkpointGuid);

                        return BoolResult.Success;
                    }
                },
                extraStartMessage: $"SequencePoint=[{sequencePoint}]{eventProcessingDelayMessage}",
                extraEndMessage: result => $"SequencePoint=[{sequencePoint}]{eventProcessingDelayMessage} Id=[{checkpointId}] {dbStats}");
        }

        private void TryFillDatabaseStats(DatabaseStats stats)
        {
            if (Database is RocksDbContentLocationDatabase rocksDb)
            {
                stats.ContentColumnFamilySizeMb = rocksDb.GetLongProperty(
                    RocksDbContentLocationDatabase.LongProperty.LiveFilesSizeBytes,
                    RocksDbContentLocationDatabase.Entity.ContentTracking).Select(x => x * 1e-6).GetValueOrDefault(-1);

                stats.ContentDataSizeMb = rocksDb.GetLongProperty(
                    RocksDbContentLocationDatabase.LongProperty.LiveDataSizeBytes,
                    RocksDbContentLocationDatabase.Entity.ContentTracking).Select(x => x * 1e-6).GetValueOrDefault(-1);

                stats.MetadataColumnFamilySizeMb = rocksDb.GetLongProperty(
                    RocksDbContentLocationDatabase.LongProperty.LiveFilesSizeBytes,
                    RocksDbContentLocationDatabase.Entity.Metadata).Select(x => x * 1e-6).GetValueOrDefault(-1);

                stats.MetadataDataSizeMb = rocksDb.GetLongProperty(
                    RocksDbContentLocationDatabase.LongProperty.LiveDataSizeBytes,
                    RocksDbContentLocationDatabase.Entity.Metadata).Select(x => x * 1e-6).GetValueOrDefault(-1);
            }
        }

        private async Task<string> CreateCheckpointIncrementalAsync(OperationContext context, EventSequencePoint sequencePoint, Guid checkpointGuid)
        {
            var incrementalCheckpointsPrefix = $"incrementalCheckpoints/{sequencePoint.SequenceNumber}.{checkpointGuid}/";

            var files = _fileSystem.EnumerateFiles(_checkpointStagingDirectory, EnumerateOptions.Recurse).Select(s => s.FullPath);

            var currentManifest = DatabaseLoadManifest(context);
            var newManifest = await UploadFilesAsync(
                context,
                _checkpointStagingDirectory,
                files,
                incrementalCheckpointsPrefix,
                currentManifest);

            var manifestFilePath = _checkpointStagingDirectory / "checkpointInfo.txt";
            DumpCheckpointManifest(manifestFilePath, newManifest);

            var checkpointId = await Storage.UploadFileAsync(
                context,
                manifestFilePath,
                incrementalCheckpointsPrefix + manifestFilePath.FileName,
                garbageCollect: true).ThrowIfFailureAsync();

            AddEntryToManifest(newManifest, manifestFilePath.FileName, checkpointId);

            // Add incremental suffix so consumer knows that the checkpoint is an incremental checkpoint
            checkpointId += IncrementalCheckpointIdSuffix;

            var checkpointState = new CheckpointState(
                StartSequencePoint: sequencePoint,
                CheckpointId: checkpointId,
                Manifest: newManifest,
                CheckpointTime: _clock.UtcNow,
                Producer: _configuration.PrimaryMachineLocation);

            await CheckpointRegistry.RegisterCheckpointAsync(context, checkpointState).ThrowIfFailure();

            if (currentManifest != null) {
                RemoveOldCheckpointsAsync(context, currentManifest, newManifest).FireAndForget(context);
            }

            return checkpointId;
        }

        private async Task<CheckpointManifest> UploadFilesAsync(
            OperationContext context,
            AbsolutePath basePath,
            IEnumerable<AbsolutePath> files,
            string incrementalCheckpointsPrefix,
            CheckpointManifest? currentManifest)
        {
            // Since checkpoints are extremely large and upload incurs a hashing operation, not having incrementality
            // at this level means that we may need to hash >100GB of data every few minutes, which is a lot.
            //
            // Hence:
            //  - We store the manifest for the previous checkpoint inside of the DB
            //  - When creating a new checkpoint, we load the currentManifest, and we only upload the files that were either not in
            //  there, are new, or are mutable.
            //  - Then, we store the new manifest into the DB.

            long uploadSize = 0;
            long retainedSize = 0;

            int uploadCount = 0;
            int retainedCount = 0;

            var newManifest = new CheckpointManifest();
            await ParallelAlgorithms.WhenDoneAsync(
                _configuration.IncrementalCheckpointDegreeOfParallelism,
                context.Token,
                action: async (_, file) =>
                        {
                            var relativePath = file.Path.Substring(basePath.Path.Length + 1);

                            bool attemptUpload = true;

                            var entry = currentManifest?.TryGetValue(relativePath);
                            if (entry is not null && Database.IsImmutable(file))
                            {
                                // File was present in last checkpoint. Just add it to the new incremental checkpoint info
                                var touchResult = await Storage.TouchBlobAsync(
                                    context,
                                    file,
                                    entry.StorageId,
                                    isUploader: true,
                                    isImmutable: true);
                                if (touchResult.Succeeded)
                                {
                                    AddEntryToManifest(newManifest, relativePath, entry.StorageId);
                                    attemptUpload = false;

                                    Interlocked.Increment(ref retainedCount);
                                    Interlocked.Add(ref retainedSize, _fileSystem.GetFileSize(file));
                                    Counters[ContentLocationStoreCounters.IncrementalCheckpointFilesUploadSkipped].Increment();
                                }
                            }

                            if (attemptUpload)
                            {
                                var storageId = await Storage.UploadFileAsync(
                                    context,
                                    file,
                                    incrementalCheckpointsPrefix + relativePath).ThrowIfFailureAsync();
                                AddEntryToManifest(newManifest, relativePath, storageId);

                                Interlocked.Increment(ref uploadCount);
                                Interlocked.Add(ref uploadSize, _fileSystem.GetFileSize(file));
                                Counters[ContentLocationStoreCounters.IncrementalCheckpointFilesUploaded].Increment();
                            }
                        },
                items: files.ToArray());

            Tracer.TrackMetric(context, "CheckpointUploadSize", uploadSize);
            Tracer.TrackMetric(context, "CheckpointRetainedSize", retainedSize);
            Tracer.TrackMetric(context, "CheckpointTotalSize", uploadSize + retainedSize);

            Tracer.TrackMetric(context, "CheckpointUploadCount", uploadCount);
            Tracer.TrackMetric(context, "CheckpointRetainedCount", retainedCount);
            Tracer.TrackMetric(context, "CheckpointTotalCount", uploadCount + retainedCount);

            DatabaseWriteManifest(context, newManifest);

            return newManifest;
        }

        public async Task RemoveOldCheckpointsAsync(OperationContext context, CheckpointManifest oldManifest, CheckpointManifest newManifest)
        {
            foreach (var content in oldManifest.ContentByPath)
            {
                if (newManifest.TryGetValue(content.RelativePath) == null)
                {
                    await Storage.PruneInternalCacheAsync(context, content.StorageId).IgnoreFailure();
                }
            }
        }

        internal static readonly Regex RocksDbCorruptionRegex = new Regex(
            @".*Slot(?:1|2)(?:\/|\\)(?<name>.*\.sst).*",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Restores the checkpoint for a given checkpoint id.
        /// </summary>
        public Task<BoolResult> RestoreCheckpointAsync(OperationContext context, CheckpointState checkpointState)
        {
            context = context.CreateNested(Tracer.Name);

            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                async nestedContext =>
                {
                    var checkpointFile = _checkpointStagingDirectory / (checkpointState.CheckpointTime.ToReadableString() + ".chkpt.txt");
                    var extractedCheckpointDirectory = _checkpointStagingDirectory / "chkpt";

                    FileUtilities.DeleteDirectoryContents(_checkpointStagingDirectory.ToString());
                    FileUtilities.DeleteDirectoryContents(extractedCheckpointDirectory.ToString());

                    // Creating a working temporary folder
                    using (new DisposableDirectory(_fileSystem, _checkpointStagingDirectory))
                    {
                        // Making sure that the extracted checkpoint directory exists before copying the files there.
                        _fileSystem.CreateDirectory(extractedCheckpointDirectory);

                        var oldManifest = DatabaseLoadManifest(context);
                        var checkpointManifest = new CheckpointManifest();

                        if (!string.IsNullOrEmpty(checkpointState.CheckpointId) || checkpointState.Manifest is not null)
                        {
                            if (checkpointState.Manifest is not null)
                            {
                                checkpointManifest = checkpointState.Manifest;
                            }
                            else
                            {
                                checkpointManifest = await DownloadCheckpointManifestAsync(nestedContext, checkpointState, checkpointFile);
                            }

                            await RestoreCheckpointIncrementalAsync(
                                nestedContext,
                                checkpointManifest,
                                extractedCheckpointDirectory).ThrowIfFailure();
                        }

                        // Restoring the checkpoint
                        var restoreResult = Database.RestoreCheckpoint(nestedContext, extractedCheckpointDirectory);
                        if (restoreResult.Succeeded)
                        {
                            if (oldManifest != null)
                            {
                                RemoveOldCheckpointsAsync(nestedContext, oldManifest, checkpointManifest).FireAndForget(nestedContext);
                            }
                            return BoolResult.Success;
                        }

                        // The Diagnostics property might be null if the error was constructed from the error message only.
                        if (restoreResult.Diagnostics is null)
                        {
                            return restoreResult;
                        }

                        var attemptPrune = restoreResult.Diagnostics.Contains("block checksum mismatch")
                                           || restoreResult.Diagnostics.Contains("Bad table magic number");
                        if (!attemptPrune)
                        {
                            return restoreResult;
                        }

                        // There is corruption. We now need to find out what sst file it is and remove it. We'll then
                        // return failure to restore, which will trigger a re-download in the next heartbeat.
                        var match = RocksDbCorruptionRegex.Match(restoreResult.Diagnostics);
                        var group = match.Groups["name"];
                        if (!match.Success || !group.Success)
                        {
                            return new BoolResult(restoreResult, "RocksDb corruption found, but couldn't extract sst file name from error message");
                        }

                        var sstName = group.Value;
                        var entry = checkpointManifest.TryGetValue(sstName);
                        if (entry is null)
                        {
                            return new BoolResult(
                                restoreResult,
                                $"RocksDb corruption found for file `{sstName}`, but could not obtain storage id from checkpoint manifest");
                        }

                        var pruneResult = await Storage.PruneInternalCacheAsync(context, entry.StorageId);
                        return pruneResult & restoreResult;
                    }
                },
                extraStartMessage: $"CheckpointId=[{checkpointState.CheckpointId}]",
                extraEndMessage: _ => $"CheckpointId=[{checkpointState.CheckpointId}]",
                timeout: _configuration.RestoreCheckpointTimeout);
        }

        private async Task<CheckpointManifest> DownloadCheckpointManifestAsync(
            OperationContext context,
            CheckpointState checkpointState,
            AbsolutePath manifestPath)
        {
            // Remove the suffix to get the real checkpoint id used with central storage
            // NOTE: We allow null checkpoint id to restore 'empty' checkpoint
            var checkpointId = string.IsNullOrEmpty(checkpointState.CheckpointId)
                ? null
                : checkpointState.CheckpointId.Substring(
                    0,
                    checkpointState.CheckpointId.Length - IncrementalCheckpointIdSuffix.Length);

            // Getting the checkpoint from the central store
            await Storage.TryGetFileAsync(
                context,
                checkpointId,
                manifestPath,
                isImmutable: true).ThrowIfFailure();

            try
            {
                return LoadCheckpointManifest(manifestPath);
            }
            catch (JsonException)
            {
                // The file download is direct to a FileStream and it's not guaranteed to match because
                // we don't actually have the hash of the file. It sometimes happens that the download
                // gets corrupted and we wind up with a non-parseable json. In such cases, we do an
                // explicit minor retry. We don't want an actual retry policy here because this is
                // already a very infrequent occurrence.
                // If this does fail, however, it can trigger a state of permanent breakage if it's the
                // leader that fails. The reason being that we'll keep on refreshing the master status
                // without being able to restore a checkpoint and make progress.
                await Storage.PruneInternalCacheAsync(context, checkpointId).IgnoreFailure();
                await Storage.TryGetFileAsync(
                    context,
                    checkpointId,
                    manifestPath,
                    isImmutable: true).ThrowIfFailure();
                return LoadCheckpointManifest(manifestPath);
            }
        }

        private Task<BoolResult> RestoreCheckpointIncrementalAsync(
            OperationContext context,
            CheckpointManifest manifest,
            AbsolutePath checkpointTargetDirectory)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    await ParallelAlgorithms.WhenDoneAsync(
                        _configuration.IncrementalCheckpointDegreeOfParallelism,
                        context.Token,
                        action: async (addItem, entry) =>
                                {
                                    await RestoreFileAsync(
                                        context,
                                        checkpointTargetDirectory,
                                        relativePath: entry.RelativePath,
                                        storageId: entry.StorageId).ThrowIfFailure();
                                },
                        items: manifest.ContentByPath.ToArray());

                    return BoolResult.Success;
                },
                extraEndMessage: _ => manifest.ToString());
        }

        private Task<BoolResult> RestoreFileAsync(
            OperationContext context,
            AbsolutePath checkpointTargetDirectory,
            string relativePath,
            string storageId)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var targetFilePathResult = CreatePath(checkpointTargetDirectory, relativePath);
                    if (!targetFilePathResult.Succeeded)
                    {
                        return targetFilePathResult;
                    }

                    await Storage.TryGetFileAsync(
                        context,
                        storageId,
                        targetFilePath: targetFilePathResult.Value,
                        isImmutable: Database.IsImmutable(targetFilePathResult.Value)).ThrowIfFailure();
                    Counters[ContentLocationStoreCounters.IncrementalCheckpointFilesDownloaded].Increment();

                    return BoolResult.Success;
                },
                extraEndMessage: _ => $"RelativePath=[{relativePath}] StorageId=[{storageId}]",
                traceOperationStarted: false);
        }

        private static Result<AbsolutePath> CreatePath(AbsolutePath basePath, string relativePath)
        {
            try
            {
                // In some cases, the incremental checkpoint state can be corrupted,
                // causing this operation to fail with ArgumentException.
                return basePath / relativePath;
            }
            catch (ArgumentException e) when (e.Message.Contains("Illegal characters in path"))
            {
                return Result.FromErrorMessage<AbsolutePath>($"Illegal characters in path '{relativePath}'.");
            }
        }

        private void DatabaseWriteCheckpointCreationTime(OperationContext context, string checkpointId, DateTime checkpointTime)
        {
            try
            {
                Database.SetGlobalEntry(CheckpointStateKey, $"{checkpointId},{checkpointTime}");
            }
            catch (Exception e)
            {
                Tracer.Warning(context, $"Failed to write checkpoint info for `{checkpointId}` at `{checkpointTime}` to database: {e}");
            }
        }

        internal (string checkpointId, DateTime checkpointTime)? DatabaseGetLatestCheckpointInfo(OperationContext context)
        {
            try
            {
                if (Database.TryGetGlobalEntry(CheckpointStateKey, out var checkpointText))
                {
                    var segments = checkpointText.Split(',');
                    var id = segments[0];
                    var date = DateTime.Parse(segments[1]);
                    return (id, date);
                }
                else
                {
                    return null;
                }
            }
            catch (Exception e)
            {
                Tracer.Debug(context, $"Failed to read latest checkpoint state from disk: {e}");
                return null;
            }
        }

        private void DumpCheckpointManifest(AbsolutePath path, CheckpointManifest manifest)
        {
            File.WriteAllText(path.Path, JsonUtilities.JsonSerialize(manifest));
        }

        /// <nodoc />
        public static CheckpointManifest LoadCheckpointManifest(AbsolutePath checkpointFile)
        {
            return JsonUtilities.JsonDeserialize<CheckpointManifest>(File.ReadAllText(checkpointFile.ToString()));
        }

        private void DatabaseWriteManifest(OperationContext context, CheckpointManifest manifest)
        {
            try
            {
                Database.SetGlobalEntry(CheckpointManifestKey, JsonUtilities.JsonSerialize(manifest, indent: true));
            }
            catch (Exception e)
            {
                Tracer.Warning(context, $"Failed to write checkpoint manifest into database: {e}");
            }
        }

        internal CheckpointManifest? DatabaseLoadManifest(OperationContext context)
        {
            try
            {
                return Database.TryGetGlobalEntry(CheckpointManifestKey, out var value)
                    ? JsonUtilities.JsonDeserialize<CheckpointManifest>(value)
                    : new CheckpointManifest();
            }
            catch (Exception e)
            {
                Tracer.Debug(context, $"Failed to read latest checkpoint manifest from database: {e}");
                return null;
            }
        }

        private void AddEntryToManifest(CheckpointManifest manifest, string relativePath, string storageId)
        {
            var storage = (Storage as CachingCentralStorage)!;

            // TODO: avoid doing this lookup by using information from the previous CheckpointManifest
            ContentHash hash = default;
            if (storage?.TryGetContentInfo(storageId, out hash, out var size) != true)
            {
                size = -1;
            }

            lock (manifest)
            {
                manifest.Add(new CheckpointManifestContentEntry(Hash: hash, Size: size, StorageId: storageId, RelativePath: relativePath));
            }
        }
    }
}
