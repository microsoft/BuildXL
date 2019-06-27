// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Native.IO;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Helper class responsible for creating and restoring checkpoints of a local database.
    /// </summary>
    internal sealed class CheckpointManager
    {
        private readonly Tracer _tracer = new Tracer(nameof(CheckpointManager));

        private const string CheckpointSizeMetricName = "CheckpointSizeBytes";
        private const string IncrementalCheckpointIdSuffix = "|Incremental";

        private const string IncrementalCheckpointInfoEntrySeparator = "\n";

        private readonly ContentLocationDatabase _database;
        private readonly ICheckpointRegistry _checkpointRegistry;
        private readonly CentralStorage _storage;
        private readonly IAbsFileSystem _fileSystem;
        private readonly AbsolutePath _checkpointStagingDirectory;
        private readonly AbsolutePath _incrementalCheckpointDirectory;
        private readonly AbsolutePath _incrementalCheckpointInfoFile;

        private CounterCollection<ContentLocationStoreCounters> Counters { get; }

        private readonly CheckpointConfiguration _configuration;

        /// <summary>
        /// Maps file name to storage id for the currently downloaded checkpoint
        /// </summary>
        private IReadOnlyDictionary<string, string> _incrementalCheckpointInfo = CollectionUtilities.EmptyDictionary<string, string>();

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
            _incrementalCheckpointDirectory = configuration.WorkingDirectory / "incremental";
            _fileSystem.CreateDirectory(_incrementalCheckpointDirectory);

            _incrementalCheckpointInfoFile = _incrementalCheckpointDirectory / "checkpointInfo.txt";
            Counters = counters;
        }


        /// <summary>
        /// Creates a checkpoint for a given sequence point.
        /// </summary>
        public Task<BoolResult> CreateCheckpointAsync(OperationContext context, EventSequencePoint sequencePoint)
        {
            context = context.CreateNested();
            return context.PerformOperationAsync(
                _tracer,
                async () =>
                {
                    bool successfullyUpdatedIncrementalState = false;
                    try
                    {
                        // Creating a working temporary directory
                        using (new DisposableDirectory(_fileSystem, _checkpointStagingDirectory))
                        {
                            // Saving checkpoint for the database into the temporary folder
                            _database.SaveCheckpoint(context, _checkpointStagingDirectory).ThrowIfFailure();

                            if (_configuration.UseIncrementalCheckpointing)
                            {
                                successfullyUpdatedIncrementalState = await CreateCheckpointIncrementalAsync(context, sequencePoint, successfullyUpdatedIncrementalState);
                            }
                            else
                            {
                                await CreateFullCheckpointAsync(context, sequencePoint);
                            }

                            return BoolResult.Success;
                        }
                    }
                    finally
                    {
                        ClearIncrementalCheckpointStateIfNeeded(successfullyUpdatedIncrementalState);
                    }
                });
        }

        private async Task CreateFullCheckpointAsync(OperationContext context, EventSequencePoint sequencePoint)
        {
            // Zipping the checkpoint
            var targetZipFile = _checkpointStagingDirectory + ".zip";
            File.Delete(targetZipFile);
            ZipFile.CreateFromDirectory(_checkpointStagingDirectory.ToString(), targetZipFile);

            // Track checkpoint size
            var fileInfo = new System.IO.FileInfo(targetZipFile);
            _tracer.TrackMetric(context, CheckpointSizeMetricName, fileInfo.Length);

            var checkpointBlobName = $"checkpoints/{sequencePoint.SequenceNumber}.{Guid.NewGuid()}.zip";
            var checkpointId = await _storage.UploadFileAsync(context, new AbsolutePath(targetZipFile), checkpointBlobName, garbageCollect: true).ThrowIfFailureAsync();

            // Uploading the checkpoint
            await _checkpointRegistry.RegisterCheckpointAsync(context, checkpointId, sequencePoint).ThrowIfFailure();
        }

        private async Task<bool> CreateCheckpointIncrementalAsync(OperationContext context, EventSequencePoint sequencePoint, bool successfullyUpdatedIncrementalState)
        {
            InitializeIncrementalCheckpointIfNeeded(restoring: false);

            BoolResult result = BoolResult.Success;
            var incrementalCheckpointsPrefix = $"incrementalCheckpoints/{sequencePoint.SequenceNumber}.{Guid.NewGuid()}.";
            var newCheckpointInfo = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Get files in checkpoint and apply changes to the incremental checkpoint directory (locally and in blob storage)
            var files = _fileSystem.EnumerateFiles(_checkpointStagingDirectory, EnumerateOptions.Recurse).Select(s => s.FullPath).ToList();
            foreach (var file in files)
            {
                var relativePath = file.Path.Substring(_checkpointStagingDirectory.Path.Length + 1);
                var incrementalCheckpointFile = _incrementalCheckpointDirectory / relativePath;
                if (_incrementalCheckpointInfo.TryGetValue(relativePath, out var storageId) && _database.IsImmutable(file) && _fileSystem.FileExists(incrementalCheckpointFile))
                {
                    // File was present in last checkpoint. Just add it to the new incremental checkpoint info
                    await _storage.TouchBlobAsync(context, file, storageId, isUploader: true).ThrowIfFailure();
                    newCheckpointInfo[relativePath] = storageId;
                    Counters[ContentLocationStoreCounters.IncrementalCheckpointFilesUploadSkipped].Increment();
                }
                else
                {
                    // File is new or mutable. Need to add to storage and update local incremental checkpoint
                    await HardlinkWithFallBackAsync(context, file, incrementalCheckpointFile);

                    storageId = await _storage.UploadFileAsync(context, file, incrementalCheckpointsPrefix + file.FileName).ThrowIfFailureAsync();
                    newCheckpointInfo[relativePath] = storageId;
                    Counters[ContentLocationStoreCounters.IncrementalCheckpointFilesUploaded].Increment();
                }
            }

            // Finalize by writing the checkpoint info into the incremental checkpoint directory and updating checkpoint registry and storage
            WriteCheckpointInfo(_incrementalCheckpointInfoFile, newCheckpointInfo);

            var checkpointId = await _storage.UploadFileAsync(context, _incrementalCheckpointInfoFile, incrementalCheckpointsPrefix + _incrementalCheckpointInfoFile.FileName, garbageCollect: true).ThrowIfFailureAsync();

            // Add incremental suffix so consumer knows that the checkpoint is an incremental checkpoint
            checkpointId += IncrementalCheckpointIdSuffix;

            await _checkpointRegistry.RegisterCheckpointAsync(context, checkpointId, sequencePoint).ThrowIfFailure();
            UpdateIncrementalCheckpointInfo(newCheckpointInfo);
            successfullyUpdatedIncrementalState = true;
            return successfullyUpdatedIncrementalState;
        }

        private void WriteCheckpointInfo(AbsolutePath path, Dictionary<string, string> newCheckpointInfo)
        {
            // Format is newline (IncrementalCheckpointInfoEntrySeparator) separated entries with {Key}={Value}
            File.WriteAllText(path.Path, string.Join(IncrementalCheckpointInfoEntrySeparator, newCheckpointInfo.Select(s => $"{s.Key}={s.Value}")));
        }

        private static Dictionary<string, string> ParseCheckpointInfo(AbsolutePath checkpointFile)
        {
            // Format is newline (IncrementalCheckpointInfoEntrySeparator) separated entries with {Key}={Value}
            return File.ReadAllText(checkpointFile.Path).Split(new[] { IncrementalCheckpointInfoEntrySeparator }, StringSplitOptions.RemoveEmptyEntries)
                                                .Select(entry => entry.Split('=')).ToDictionary(entry => entry[0], entry => entry[1], StringComparer.OrdinalIgnoreCase);
        }

        private void InitializeIncrementalCheckpointIfNeeded(bool restoring)
        {
            // If incremental checkpoint info is not initialized. Clean up incremental checkpoint directory
            // before proceeding

            if (_configuration.UseIncrementalCheckpointing)
            {
                if (_incrementalCheckpointInfo.Count == 0)
                {
                    _fileSystem.CreateDirectory(_incrementalCheckpointDirectory);

                    if (restoring)
                    {
                        // Only RestoreCheckpoint should read the incremental checkpoint file
                        // Thereby, when CreateCheckpoint is not preceded by a RestoreCheckpoint
                        // (i.e. creating checkpoint for new epoch), it will not reuse files

                        if (_fileSystem.FileExists(_incrementalCheckpointInfoFile))
                        {
                            // An incremental checkpoint exists. Make sure that it is loaded
                            _incrementalCheckpointInfo = ParseCheckpointInfo(_incrementalCheckpointInfoFile);
                        }
                    }
                }

                // Synchronize incremental checkpoint directory with incremental checkpoint file
                var files = _fileSystem.EnumerateFiles(_incrementalCheckpointDirectory, EnumerateOptions.Recurse).Select(s => s.FullPath).ToList();
                foreach (var file in files)
                {
                    if (file != _incrementalCheckpointInfoFile)
                    {
                        var relativePath = file.Path.Substring(_incrementalCheckpointDirectory.Path.Length + 1);

                        if (!_incrementalCheckpointInfo.ContainsKey(relativePath))
                        {
                            _fileSystem.DeleteFile(file);
                        }
                    }
                }
            }
        }

        private void ClearIncrementalCheckpointStateIfNeeded(bool successfullyUpdatedIncrementalState)
        {
            if (!successfullyUpdatedIncrementalState && _configuration.UseIncrementalCheckpointing)
            {
                _incrementalCheckpointInfo = CollectionUtilities.EmptyDictionary<string, string>();
                _fileSystem.DeleteFile(_incrementalCheckpointInfoFile);
            }
        }

        private void UpdateIncrementalCheckpointInfo(Dictionary<string, string> newCheckpointInfo)
        {
            // Remove extraneous files from local incremental checkpoint
            foreach (var snapshotFileRelativePath in _incrementalCheckpointInfo.Keys)
            {
                if (!newCheckpointInfo.ContainsKey(snapshotFileRelativePath))
                {
                    // Delete any files no longer present in the current snapshot
                    _fileSystem.DeleteFile(_incrementalCheckpointDirectory / snapshotFileRelativePath);
                }
            }

            // Update the in-memory view
            _incrementalCheckpointInfo = newCheckpointInfo;
        }

        private async Task HardlinkWithFallBackAsync(OperationContext context, AbsolutePath source, AbsolutePath target)
        {
            _fileSystem.CreateDirectory(target.Parent);

            var createHardLinkResult = _fileSystem.CreateHardLink(source, target, replaceExisting: true);
            if (createHardLinkResult != CreateHardLinkResult.Success)
            {
                context.TraceDebug($"{_tracer.Name}: Hardlinking {source} to {target} failed: {createHardLinkResult}. Copying...");
                await _fileSystem.CopyFileAsync(source, target, replaceExisting: true);
            }
        }

        /// <summary>
        /// Restores the checkpoint for a given checkpoint id.
        /// </summary>
        public Task<BoolResult> RestoreCheckpointAsync(OperationContext context, string checkpointId)
        {
            context = context.CreateNested();
            return context.PerformOperationAsync(
                _tracer,
                async () =>
                {
                    bool successfullyUpdatedIncrementalState = false;
                    try
                    {
                        bool isIncrementalCheckpoint = false;
                        var checkpointFileExtension = ".zip";
                        if (checkpointId.EndsWith(IncrementalCheckpointIdSuffix, StringComparison.OrdinalIgnoreCase))
                        {
                            isIncrementalCheckpoint = true;
                            checkpointFileExtension = ".txt";
                            // Remove the suffix to get the real checkpoint id used with central storage
                            checkpointId = checkpointId.Substring(0, checkpointId.Length - IncrementalCheckpointIdSuffix.Length);
                        }

                        var checkpointFile = _checkpointStagingDirectory / $"chkpt{checkpointFileExtension}";
                        var extractedCheckpointDirectory = _checkpointStagingDirectory / "chkpt";

                        FileUtilities.DeleteDirectoryContents(_checkpointStagingDirectory.ToString());
                        FileUtilities.DeleteDirectoryContents(extractedCheckpointDirectory.ToString());

                        // Creating a working temporary folder
                        using (new DisposableDirectory(_fileSystem, _checkpointStagingDirectory))
                        {
                            // Getting the checkpoint from the central store
                            await _storage.TryGetFileAsync(context, checkpointId, checkpointFile).ThrowIfFailure();

                            if (isIncrementalCheckpoint)
                            {
                                successfullyUpdatedIncrementalState = await RestoreCheckpointIncrementalAsync(context, checkpointFile, extractedCheckpointDirectory);
                            }
                            else
                            {
                                RestoreFullCheckpointAsync(checkpointFile, extractedCheckpointDirectory);
                            }

                            // Restoring the checkpoint
                            return _database.RestoreCheckpoint(context, extractedCheckpointDirectory);
                        }
                    }
                    finally
                    {
                        ClearIncrementalCheckpointStateIfNeeded(successfullyUpdatedIncrementalState);
                    }
                });
        }

        private static void RestoreFullCheckpointAsync(AbsolutePath checkpointFile, AbsolutePath extractedCheckpointDirectory)
        {
            // Extracting the checkpoint archive
            ZipFile.ExtractToDirectory(checkpointFile.ToString(), extractedCheckpointDirectory.ToString());
        }

        private async Task<bool> RestoreCheckpointIncrementalAsync(OperationContext context, AbsolutePath checkpointFile, AbsolutePath checkpointTargetDirectory)
        {
            InitializeIncrementalCheckpointIfNeeded(restoring: true);

            // Parse the checkpoint info for the checkpoint being restored
            var newCheckpointInfo = ParseCheckpointInfo(checkpointFile);


            foreach (var (key, value) in newCheckpointInfo)
            {
                await RestoreFileAsync(context, checkpointTargetDirectory, key, value).ThrowIfFailure();
            }
            
            // Finalize by adding the incremental checkpoint info file to the local incremental checkpoint directory
            await HardlinkWithFallBackAsync(context, checkpointFile, _incrementalCheckpointInfoFile);
            UpdateIncrementalCheckpointInfo(newCheckpointInfo);
            return true;
        }

        private Task<BoolResult> RestoreFileAsync(OperationContext context, AbsolutePath checkpointTargetDirectory, string relativePath, string storageId)
        {
            return context.PerformOperationAsync(
                _tracer,
                async () =>
                {
                    var incrementalCheckpointFile = _incrementalCheckpointDirectory / relativePath;
                    if ((_incrementalCheckpointInfo.TryGetValue(relativePath, out var fileStorageId)
                            && storageId == fileStorageId)
                        && _database.IsImmutable(incrementalCheckpointFile)
                        && _fileSystem.FileExists(incrementalCheckpointFile))
                    {
                        // File is already present in the incremental checkpoint directory, no need to download it
                        await _storage.TouchBlobAsync(context, incrementalCheckpointFile, storageId, isUploader: false).ThrowIfFailure();
                        Counters[ContentLocationStoreCounters.IncrementalCheckpointFilesDownloadSkipped].Increment();
                    }
                    else
                    {
                        // File is missing, different, or mutable so download it and update it in the incremental checkpoint
                        _fileSystem.DeleteFile(incrementalCheckpointFile);
                        await _storage.TryGetFileAsync(context, storageId, incrementalCheckpointFile).ThrowIfFailure();
                        Counters[ContentLocationStoreCounters.IncrementalCheckpointFilesDownloaded].Increment();
                    }

                    // Move the file from the incremental checkpoint into the extraction directory for loading by the database
                    await HardlinkWithFallBackAsync(context, incrementalCheckpointFile, checkpointTargetDirectory / relativePath);
                    return BoolResult.Success;
                }, extraStartMessage: relativePath
            );
        }
    }
}
