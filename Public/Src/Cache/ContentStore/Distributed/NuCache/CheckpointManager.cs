// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Native.IO;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.ParallelAlgorithms;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Helper class responsible for creating and restoring checkpoints of a local database.
    /// </summary>
    public sealed class CheckpointManager
    {
        private readonly Tracer _tracer = new Tracer(nameof(CheckpointManager));

        private const string IncrementalCheckpointIdSuffix = "|Incremental";
        private const string CheckpointInfoKey = "CheckpointManager.CheckpointState";
        private const string CheckpointManifestKey = "CheckpointManager.Manifest";

        private const string IncrementalCheckpointInfoEntrySeparator = "\n";

        private readonly ContentLocationDatabase _database;
        private readonly ICheckpointRegistry _checkpointRegistry;
        private readonly CentralStorage _storage;
        private readonly IAbsFileSystem _fileSystem;

        private readonly AbsolutePath _checkpointStagingDirectory;

        private CounterCollection<ContentLocationStoreCounters> Counters { get; }

        private readonly CheckpointConfiguration _configuration;

        /// <inheritdoc />
        public CheckpointManager(
            ContentLocationDatabase database,
            ICheckpointRegistry checkpointRegistry,
            CentralStorage storage,
            CheckpointConfiguration configuration,
            CounterCollection<ContentLocationStoreCounters> counters)
        {
            _database = database;
            _checkpointRegistry = checkpointRegistry;
            _storage = storage;
            _configuration = configuration;
            _fileSystem = new PassThroughFileSystem();
            _checkpointStagingDirectory = configuration.WorkingDirectory / "staging";

            Counters = counters;
        }

        /// <summary>
        /// Creates a checkpoint for a given sequence point.
        /// </summary>
        public Task<BoolResult> CreateCheckpointAsync(OperationContext context, EventSequencePoint sequencePoint)
        {
            context = context.CreateNested(nameof(CheckpointManager));

            string checkpointId = "Unknown";
            double contentColumnFamilySizeMb = -1;
            double contentDataSizeMb = -1;
            double metadataColumnFamilySizeMb = -1;
            double metadataDataSizeMb = -1;
            double sizeOnDiskMb = -1;
            return context.PerformOperationAsync(
                _tracer,
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
                        if (_database is RocksDbContentLocationDatabase rocksDb)
                        {
                            contentColumnFamilySizeMb = rocksDb.GetLongProperty(
                                RocksDbContentLocationDatabase.LongProperty.LiveFilesSizeBytes,
                                RocksDbContentLocationDatabase.Entity.ContentTracking).Select(x => x * 1e-6).GetValueOrDefault(-1);

                            contentDataSizeMb = rocksDb.GetLongProperty(
                                RocksDbContentLocationDatabase.LongProperty.LiveDataSizeBytes,
                                RocksDbContentLocationDatabase.Entity.ContentTracking).Select(x => x * 1e-6).GetValueOrDefault(-1);

                            metadataColumnFamilySizeMb = rocksDb.GetLongProperty(
                                RocksDbContentLocationDatabase.LongProperty.LiveFilesSizeBytes,
                                RocksDbContentLocationDatabase.Entity.Metadata).Select(x => x * 1e-6).GetValueOrDefault(-1);

                            metadataDataSizeMb = rocksDb.GetLongProperty(
                                RocksDbContentLocationDatabase.LongProperty.LiveDataSizeBytes,
                                RocksDbContentLocationDatabase.Entity.Metadata).Select(x => x * 1e-6).GetValueOrDefault(-1);
                        }

                        // Saving checkpoint for the database into the temporary folder
                        _database.SaveCheckpoint(context, _checkpointStagingDirectory).ThrowIfFailure();

                        try
                        {
                            sizeOnDiskMb = _fileSystem
                                .EnumerateFiles(_checkpointStagingDirectory, EnumerateOptions.Recurse)
                                .Sum(fileInfo => fileInfo.Length) * 1e-6;
                        }
                        catch (IOException e)
                        {
                            _tracer.Error(context, $"Error counting size of checkpoint's staging directory `{_checkpointStagingDirectory}`: {e}");
                        }

                        checkpointId = await CreateCheckpointIncrementalAsync(context, sequencePoint, checkpointGuid);

                        return BoolResult.Success;
                    }
                },
                extraStartMessage: $"SequencePoint=[{sequencePoint}]",
                extraEndMessage: result => $"SequencePoint=[{sequencePoint}] Id=[{checkpointId}] SizeMb=[{sizeOnDiskMb}] ContentColumnFamilySizeMb=[{contentColumnFamilySizeMb}] ContentDataSizeMb=[{contentDataSizeMb}] MetadataColumnFamilySizeMb=[{metadataColumnFamilySizeMb}] MetadataDataSizeMb=[{metadataDataSizeMb}]");
        }

        private async Task<string> CreateCheckpointIncrementalAsync(OperationContext context, EventSequencePoint sequencePoint, Guid checkpointGuid)
        {
            var incrementalCheckpointsPrefix = $"incrementalCheckpoints/{sequencePoint.SequenceNumber}.{checkpointGuid}.";

            var files = _fileSystem.EnumerateFiles(_checkpointStagingDirectory, EnumerateOptions.Recurse).Select(s => s.FullPath);
            var manifest = await UploadFilesAsync(
                context,
                _checkpointStagingDirectory,
                files,
                "incrementalCheckpoints/");

            var manifestFilePath = _checkpointStagingDirectory / "checkpointInfo.txt";
            DumpCheckpointManifest(manifestFilePath, manifest);

            var checkpointId = await _storage.UploadFileAsync(
                context,
                manifestFilePath,
                incrementalCheckpointsPrefix + manifestFilePath.FileName,
                garbageCollect: true).ThrowIfFailureAsync();

            // Add incremental suffix so consumer knows that the checkpoint is an incremental checkpoint
            checkpointId += IncrementalCheckpointIdSuffix;

            await _checkpointRegistry.RegisterCheckpointAsync(context, checkpointId, sequencePoint).ThrowIfFailure();

            return checkpointId;
        }

        private async Task<IReadOnlyDictionary<string, string>> UploadFilesAsync(OperationContext context, AbsolutePath basePath, IEnumerable<AbsolutePath> files, string incrementalCheckpointsPrefix)
        {
            // Since checkpoints are extremely large and upload incurs a hashing operation, not having incrementality
            // at this level means that we may need to hash >100GB of data every few minutes, which is a lot.
            //
            // Hence:
            //  - We store the manifest for the previous checkpoint inside of the DB
            //  - When creating a new checkpoint, we load that, and we only upload the files that were either not in
            //  there, are new, or are mutable.
            //  - Then, we store the manifest into the DB.
            var currentManifest = DatabaseLoadManifest(context);

            var newManifest = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            await ParallelAlgorithms.WhenDoneAsync(
                _configuration.IncrementalCheckpointDegreeOfParallelism,
                context.Token,
                action: async (addItem, file) =>
                {
                    var relativePath = file.Path.Substring(basePath.Path.Length + 1);

                    bool attemptUpload = true;
                    if (currentManifest.TryGetValue(relativePath, out var storageId) && _database.IsImmutable(file))
                    {
                        // File was present in last checkpoint. Just add it to the new incremental checkpoint info
                        var touchResult = await _storage.TouchBlobAsync(
                            context,
                            file,
                            storageId,
                            isUploader: true,
                            isImmutable: true);
                        if (touchResult.Succeeded)
                        {
                            newManifest[relativePath] = storageId;
                            attemptUpload = false;

                            Counters[ContentLocationStoreCounters.IncrementalCheckpointFilesUploadSkipped].Increment();
                        }
                    }

                    if (attemptUpload)
                    {
                        storageId = await _storage.UploadFileAsync(
                            context,
                            file,
                            incrementalCheckpointsPrefix + file.FileName).ThrowIfFailureAsync();
                        newManifest[relativePath] = storageId;

                        Counters[ContentLocationStoreCounters.IncrementalCheckpointFilesUploaded].Increment();
                    }
                },
                items: files.ToArray());

            DatabaseWriteManifest(context, newManifest);

            return newManifest;
        }

        /// <summary>
        /// Restores the checkpoint for a given checkpoint id.
        /// </summary>
        public Task<BoolResult> RestoreCheckpointAsync(OperationContext context, CheckpointState checkpointState)
        {
            context = context.CreateNested(nameof(CheckpointManager));
            var checkpointId = checkpointState.CheckpointId;
            return context.PerformOperationWithTimeoutAsync(
                _tracer,
                async nestedContext =>
                {
                    // Remove the suffix to get the real checkpoint id used with central storage
                    checkpointId = checkpointId.Substring(0, checkpointId.Length - IncrementalCheckpointIdSuffix.Length);

                    var checkpointFile = _checkpointStagingDirectory / $"chkpt.txt";
                    var extractedCheckpointDirectory = _checkpointStagingDirectory / "chkpt";

                    FileUtilities.DeleteDirectoryContents(_checkpointStagingDirectory.ToString());
                    FileUtilities.DeleteDirectoryContents(extractedCheckpointDirectory.ToString());

                    // Creating a working temporary folder
                    using (new DisposableDirectory(_fileSystem, _checkpointStagingDirectory))
                    {
                        // Getting the checkpoint from the central store
                        await _storage.TryGetFileAsync(
                            nestedContext,
                            checkpointId,
                            checkpointFile,
                            isImmutable: true).ThrowIfFailure();

                        await RestoreCheckpointIncrementalAsync(
                            nestedContext,
                            checkpointFile,
                            extractedCheckpointDirectory).ThrowIfFailure();

                        // Restoring the checkpoint
                        _database.RestoreCheckpoint(nestedContext, extractedCheckpointDirectory).ThrowIfFailure();

                        return BoolResult.Success;
                    }
                },
                extraStartMessage: $"CheckpointId=[{checkpointId}]",
                extraEndMessage: _ => $"CheckpointId=[{checkpointId}]",
                timeout: _configuration.RestoreCheckpointTimeout);
        }

        private Task<BoolResult> RestoreCheckpointIncrementalAsync(OperationContext context, AbsolutePath checkpointFile, AbsolutePath checkpointTargetDirectory)
        {
            return context.PerformOperationAsync(
                _tracer,
                async () =>
                {
                    var manifest = LoadCheckpointManifest(checkpointFile);

                    await ParallelAlgorithms.WhenDoneAsync(
                        _configuration.IncrementalCheckpointDegreeOfParallelism,
                        context.Token,
                        action: async (addItem, kvp) =>
                        {
                            await RestoreFileAsync(
                                context,
                                checkpointTargetDirectory,
                                relativePath: kvp.Key,
                                storageId: kvp.Value).ThrowIfFailure();
                        },
                        items: manifest.ToArray());

                    return BoolResult.Success;
                });
        }

        private Task<BoolResult> RestoreFileAsync(OperationContext context, AbsolutePath checkpointTargetDirectory, string relativePath, string storageId)
        {
            return context.PerformOperationAsync(
                _tracer,
                async () =>
                {
                    var targetFilePathResult = CreatePath(checkpointTargetDirectory, relativePath);
                    if (!targetFilePathResult.Succeeded)
                    {
                        return targetFilePathResult;
                    }

                    await _storage.TryGetFileAsync(
                        context,
                        storageId,
                        targetFilePath: targetFilePathResult.Value,
                        isImmutable: _database.IsImmutable(targetFilePathResult.Value)).ThrowIfFailure();
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
                _database.SetGlobalEntry(CheckpointInfoKey, $"{checkpointId},{checkpointTime}");
            }
            catch (Exception e)
            {
                _tracer.Warning(context, $"Failed to write checkpoint info for `{checkpointId}` at `{checkpointTime}` to database: {e}");
            }
        }

        internal (string checkpointId, DateTime checkpointTime)? DatabaseGetLatestCheckpointInfo(OperationContext context)
        {
            try
            {
                if (_database.TryGetGlobalEntry(CheckpointInfoKey, out var checkpointText))
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
                _tracer.Debug(context, $"Failed to read latest checkpoint state from disk: {e}");
                return null;
            }
        }

        private void DumpCheckpointManifest(AbsolutePath path, IReadOnlyDictionary<string, string> checkpointInfo)
        {
            File.WriteAllText(path.Path, SerializeCheckpointManifest(checkpointInfo));
        }

        /// <nodoc />
        public static IReadOnlyDictionary<string, string> LoadCheckpointManifest(AbsolutePath checkpointFile)
        {
            return DeserializeCheckpointManifest(File.ReadAllText(checkpointFile.ToString()));
        }

        private void DatabaseWriteManifest(OperationContext context, IReadOnlyDictionary<string, string> manifest)
        {
            try
            {
                _database.SetGlobalEntry(CheckpointManifestKey, SerializeCheckpointManifest(manifest));
            }
            catch (Exception e)
            {
                _tracer.Warning(context, $"Failed to write checkpoint manifest into database: {e}");
            }
        }

        internal IReadOnlyDictionary<string, string> DatabaseLoadManifest(OperationContext context)
        {
            try
            {
                if (_database.TryGetGlobalEntry(CheckpointManifestKey, out var value))
                {
                    return DeserializeCheckpointManifest(value);
                }
                else
                {
                    return new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }

            }
            catch (Exception e)
            {
                _tracer.Debug(context, $"Failed to read latest checkpoint manifest from database: {e}");
                return null;
            }
        }

        private static string SerializeCheckpointManifest(IReadOnlyDictionary<string, string> manifest)
        {
            // Format is newline (IncrementalCheckpointInfoEntrySeparator) separated entries with {Key}={Value}
            return string.Join(IncrementalCheckpointInfoEntrySeparator, manifest.Select(s => $"{s.Key}={s.Value}"));
        }

        private static IReadOnlyDictionary<string, string> DeserializeCheckpointManifest(string serialized)
        {
            // Format is newline (IncrementalCheckpointInfoEntrySeparator) separated entries with {Key}={Value}
            return serialized
                .Split(new[] { IncrementalCheckpointInfoEntrySeparator }, StringSplitOptions.RemoveEmptyEntries)
                .Select(entry => entry.Split('='))
                .ToDictionary(
                    entry => entry[0],
                    entry => entry[1],
                    StringComparer.OrdinalIgnoreCase);
        }
    }
}
