// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Native.IO;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;
using FileInfo = BuildXL.Cache.ContentStore.Interfaces.FileSystem.FileInfo;

#nullable enable

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <nodoc />
    public sealed class SelfCheckSettings
    {
        /// <summary>
        /// If true, then <see cref="FileSystemContentStoreInternal"/> will start a self-check to validate that the content in cache is valid at startup.
        /// </summary>
        /// <remarks>
        /// If the property is false, then the self check is still possible but <see cref="FileSystemContentStoreInternal.SelfCheckContentDirectoryAsync(Interfaces.Tracing.Context, System.Threading.CancellationToken)"/>
        /// method should be called manually.
        /// </remarks>
        public bool StartSelfCheckInStartup { get; set; } = false;

        /// <summary>
        /// An interval between self checks performed by a content store to make sure that all the data on disk matches it's hashes.
        /// </summary>
        public TimeSpan Frequency { get; set; } = TimeSpan.FromDays(1);

        /// <summary>
        /// An epoch used for reseting self check of a content directory.
        /// </summary>
        public string Epoch { get; set; } = "E0";

        /// <summary>
        /// An interval for tracing self check progress.
        /// </summary>
        public TimeSpan ProgressReportingInterval { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// A number of invalid hashes that the checker will process in one attempt.
        /// </summary>
        /// <remarks>
        /// Used for testing purposes.
        /// </remarks>
        public long InvalidFilesLimit { get; set; } = long.MaxValue;

        /// <summary>
        /// The delay between file IO operations.
        /// </summary>
        /// <remarks>
        /// For HDD drives, reading the entire content directory even from one thread can fully saturate the HDD drive causing significant slowdown for all the other disk IO operations.
        /// </remarks>
        public TimeSpan? HashAnalysisDelay { get; set; }

        /// <summary>
        /// The default delay used by HDD drives during self check to avoid HDD saturation.
        /// </summary>
        public TimeSpan DefaultHddHashAnalysisDelay { get; set; } = TimeSpan.FromMilliseconds(100);

        /// <inheritdoc />
        public override string ToString()
        {
            return $"Epoch=[{Epoch}], ProgressReportingInterval=[{ProgressReportingInterval}], HashAnalysisDelay=[{HashAnalysisDelay}]";
        }
    }

    /// <summary>
    /// Helper class responsible for validating a content directory.
    /// </summary>
    internal sealed class FileSystemContentStoreInternalChecker
    {
        private readonly IAbsFileSystem _fileSystem;
        private readonly IClock _clock;
        private readonly FileSystemContentStoreInternal _contentStoreInternal;
        private readonly AbsolutePath _selfCheckFilePath;

        private readonly SelfCheckSettings _settings;
        private readonly Tracer _tracer;

        /// <nodoc />
        public FileSystemContentStoreInternalChecker(
            IAbsFileSystem fileSystem,
            IClock clock,
            AbsolutePath rootPath,
            Tracer tracer,
            SelfCheckSettings? settings,
            FileSystemContentStoreInternal contentStoreInternal)
        {
            _fileSystem = fileSystem;
            _clock = clock;
            _contentStoreInternal = contentStoreInternal;
            _tracer = tracer;
            _settings = CreateOrAdjustSelfCheckSettings(settings, rootPath);
            _selfCheckFilePath = rootPath / "selfCheckMarker.txt";
        }

        /// <summary>
        /// Checks that the content on disk is correct and every file in content directory matches it's size and hash.
        /// </summary>
        public Task<Result<SelfCheckResult>> SelfCheckContentDirectoryAsync(OperationContext context)
        {
            return context.PerformOperationAsync(
                        _tracer,
                        async () =>
                        {
                            var selfCheckState = GetSelfCheckState(context);

                            var status = selfCheckState.CheckStatus(_clock.UtcNow, _settings.Epoch, _settings.Frequency);
                            if (status == SelfCheckStatus.UpToDate)
                            {
                                _tracer.Debug(
                                    context,
                                    $"SelfCheck: Skipping self check because the self check status is up-to-date. Last checked at '{selfCheckState.LastReconcileTime.ToReadableString()}'.");
                                return Result.Success(SelfCheckResult.UpToDate());
                            }

                            var result = await SelfCheckContentDirectoryCoreAsync(context, selfCheckState, status);

                            if (!result)
                            {
                                RemoveSelfCheckStateFromDisk(context);
                            }

                            return result;
                        },
                        extraStartMessage: _settings.ToString(),
                        extraEndMessage: r => r ? r.Value.ToString() : string.Empty);
        }

        private SelfCheckState GetSelfCheckState(Context context)
        {
            if (!_fileSystem.FileExists(_selfCheckFilePath))
            {
                return SelfCheckState.OutOfDate;
            }

            var content = _fileSystem.ReadAllText(_selfCheckFilePath);

            _tracer.Debug(context, $"SelfCheck: got self check status from disk '{content}'.");
            return SelfCheckState.TryParse(content) ?? SelfCheckState.OutOfDate;
        }

        private void UpdateSelfCheckStateOnDisk(Context context, SelfCheckState selfCheckState)
        {
            try
            {
                _tracer.Debug(context, $"SelfCheck: Updating self check status on disk with new state: {selfCheckState.ToParseableString()}");
                _fileSystem.WriteAllText(_selfCheckFilePath, selfCheckState.ToParseableString());
            }
            catch (IOException e)
            {
                _tracer.Warning(context, $"SelfCheck: Failed updating self check status on disk: {e}");
            }
        }

        private void RemoveSelfCheckStateFromDisk(Context context)
        {
            try
            {
                _tracer.Debug(context, $"SelfCheck: Removing self check status file from disk");
                _fileSystem.DeleteFile(_selfCheckFilePath);
            }
            catch (IOException e)
            {
                _tracer.Warning(context, $"SelfCheck: Failed updating self check status on disk: {e}");
            }
        }

        private async Task<Result<SelfCheckResult>> SelfCheckContentDirectoryCoreAsync(
            OperationContext context,
            SelfCheckState selfCheckState,
            SelfCheckStatus status)
        {
            _tracer.Always(context, "Starting self check.");
            // Self checking procedure validates that in-memory content directory
            // is valid in respect to the state on disk.
            // Namely, it checks that the hashes for all the files and their size are correct.
            var stopwatch = Stopwatch.StartNew();

            // Enumerating files from disk instead of looking them up from content directory.
            // This is done due to simplicity (we don't have to worry about replicas) and because an additional IO cost is negligible compared to the cost of rehashing.
            var contentHashes = _contentStoreInternal.ReadSnapshotFromDisk(context).ListOrderedByHash();
            _tracer.Debug(context, $"SelfCheck: Enumerated {contentHashes.Count} entries from disk by {stopwatch.ElapsedMilliseconds}ms.");

            stopwatch.Restart();

            // Trying to restore the index of a hash that we processed before.
            int index = 0;
            if (status == SelfCheckStatus.InProgress)
            {
                index = findNextIndexToProcess(selfCheckState.LastPosition.Value);
                _tracer.Debug(context, $"SelfCheck: skipping {index} elements based on previous state '{selfCheckState.ToParseableString()}'.");
            }
            else
            {
                string statusAsString = status == SelfCheckStatus.Force ? "the epoch has changed" : "a self check is out of date";
                _tracer.Debug(
                    context,
                    $"SelfCheck: starting self check for the entire content directory because {statusAsString}. Previous state '{selfCheckState.ToParseableString()}'.");
            }

            // Time span for tracking progress.
            TimeSpan progressTracker = TimeSpan.FromSeconds(0);
            long processedBytes = 0;

            int invalidEntries = 0;
            int processedEntries = 0;
            
            for (; index < contentHashes.Count; index++)
            {
                if (context.Token.IsCancellationRequested)
                {
                    _tracer.Debug(context, "SelfCheck: Exiting self check because cancellation was requested.");
                    break;
                }

                var hashInfo = contentHashes[index];
                processedEntries++;

                var (isValid, error) = await ValidateFileAsync(context, hashInfo.Hash, hashInfo.Payload);
                if (!isValid)
                {
                    _tracer.Warning(context, $"SelfCheck: Found invalid entry in cache. Hash={hashInfo.Hash.ToShortString()}. {error}. Evicting the file...");
                    await _contentStoreInternal.RemoveInvalidContentAsync(context, hashInfo.Hash);
                    invalidEntries++;
                }

                // Tracking the progress if needed.
                traceProgressIfNeeded(hashInfo.Hash, index);

                // If the current entry is not the last one, and we reached the number of invalid files,
                // then exiting the loop.
                if (invalidEntries ==  _settings.InvalidFilesLimit && index != contentHashes.Count - 1)
                {
                    _tracer.Debug(context, $"SelfCheck: Exiting self check because invalid file limit of {_settings.InvalidFilesLimit} is reached.");
                    break;
                }

                if (_settings.HashAnalysisDelay != null)
                {
                    await Task.Delay(_settings.HashAnalysisDelay.Value, context.Token);
                }
            }

            if (index == contentHashes.Count)
            {
                // All the items are processed. Saving new stable checkpoint
                UpdateSelfCheckStateOnDisk(context, SelfCheckState.SelfCheckComplete(_settings.Epoch, _clock.UtcNow));
            }
            else
            {
                // The loop was interrupted. Saving an incremental state.
                var newStatus = selfCheckState.WithEpochAndPosition(_settings.Epoch, contentHashes[index].Hash);
                UpdateSelfCheckStateOnDisk(context, newStatus);
            }

            return Result.Success(new SelfCheckResult(invalidHashes: invalidEntries, totalProcessedFiles: processedEntries));

            int findNextIndexToProcess(ContentHash lastProcessedHash)
            {
                var binarySearchResult = contentHashes.BinarySearch(new PayloadFromDisk<FileInfo>(lastProcessedHash, default), new ByHashPayloadFromDiskComparer<FileInfo>());

                int targetIndex = 0;
                if (binarySearchResult >= 0 && binarySearchResult < contentHashes.Count - 1)
                {
                    targetIndex = binarySearchResult + 1;
                }
                else
                {
                    // The exact match is not found (which is fine, because the state on disk may changed between app invocations).
                    // BinarySearch returns a negative value that bitwise complement of the closest element in the sorted array.

                    binarySearchResult = ~binarySearchResult;
                    if (binarySearchResult < contentHashes.Count)
                    {
                        targetIndex = binarySearchResult;
                    }
                }

                return targetIndex;
            }

            void traceProgressIfNeeded(ContentHash currentHash, int currentHashIndex)
            {
                processedBytes += contentHashes[currentHashIndex].Payload.Length;

                var swTime = stopwatch.Elapsed;
                if (swTime - progressTracker > _settings.ProgressReportingInterval)
                {
                    // It is possible to have multiple replicas with the same hash.
                    // We need to save the state only when *all* the replicas are processed.
                    // So we check if the next item has a different hash.
                    // No check is performed on the last element because the state will be saved immediately after
                    if (currentHashIndex + 1 < contentHashes.Count && currentHash != contentHashes[currentHashIndex + 1].Hash)
                    {
                        var speed = ((double)processedBytes / (1024*1024)) / _settings.ProgressReportingInterval.TotalSeconds;
                        _tracer.Always(context, $"SelfCheck: processed {index}/{contentHashes.Count}: {new SelfCheckResult(invalidEntries, processedEntries)}. Hashing speed {speed:0.##}Mb/s.");
                        processedBytes = 0;

                        // Saving incremental state
                        var newStatus = selfCheckState.WithEpochAndPosition(_settings.Epoch, currentHash);
                        UpdateSelfCheckStateOnDisk(context, newStatus);

                        progressTracker = swTime;
                    }
                }
            }
        }

        private async Task<(bool isValid, string error)> ValidateFileAsync(Context context, ContentHash expectedHash, FileInfo fileInfo)
        {
            try
            {
                var path = fileInfo.FullPath;

                // The cache entry is invalid if the size in content directory doesn't mach an actual size
                if (_contentStoreInternal.TryGetFileInfo(expectedHash, out var contentFileInfo) && contentFileInfo.FileSize != fileInfo.Length)
                {
                    return (isValid: false, error: $"File size mismatch. Expected size is {contentFileInfo.FileSize} and size on disk is {fileInfo.Length}.");
                }

                // Or if the content doesn't match the hash.
                var actualHashAndSize = await _contentStoreInternal.TryHashFileAsync(context, path, expectedHash.HashType);
                if (actualHashAndSize != null && actualHashAndSize.Value.Hash != expectedHash)
                {
                    // Don't need to add an expected hash into the error string because the client code will always put it into the final error message.
                    return (isValid: false, error: $"Hash mismatch. Actual hash is {actualHashAndSize.Value.Hash.ToShortString()}");
                }

                return (isValid: true, error: string.Empty);
            }
            catch (Exception e)
            {
                _tracer.Warning(context, $"SelfCheck: Content hash is invalid. Hash={expectedHash.ToShortString()}, Error={e}");

                return (isValid: true, error: string.Empty);
            }
        }

        private static SelfCheckSettings CreateOrAdjustSelfCheckSettings(SelfCheckSettings? settings, AbsolutePath rootPath)
        {
            var currentDrive = rootPath.DriveLetter;
            settings ??= new SelfCheckSettings();

            // Setting the hash analysis delay if the current drive is hdd drive and the analysis delay is not set yet.
            bool? seekPenalty = (char.IsLetter(currentDrive) && currentDrive > 64 && currentDrive < 123)
                ? FileUtilities.DoesLogicalDriveHaveSeekPenalty(currentDrive)
                : false;

            if (seekPenalty == true && settings.HashAnalysisDelay == null)
            {
                settings.HashAnalysisDelay = settings.DefaultHddHashAnalysisDelay;
            }

            return settings;
        }

        internal enum SelfCheckStatus
        {
            /// <summary>
            /// The epoch has changed and we should immediately start a self check process with max throughput.
            /// </summary>
            Force,

            /// <summary>
            /// Self check is out of date and we should start a "soft" self check process that should consume 
            /// </summary>
            OutOfDate,

            /// <summary>
            /// A self check process was interrupted and we should continue from a previously saved position.
            /// </summary>
            InProgress,

            /// <summary>
            /// The state is up-to-date.
            /// </summary>
            UpToDate,
        }

        /// <summary>
        /// Self check state serialized/deserialized from disk.
        /// </summary>
        internal readonly struct SelfCheckState : IEquatable<SelfCheckState>
        {
            private const string EmptyHash = "FINISHED_NOT_A_HASH";

            /// <summary>
            /// Epoch used during the last self check.
            /// </summary>
            public string Epoch { get; }

            /// <summary>
            /// Date and time of a last fully finished self check.
            /// </summary>
            public DateTime LastReconcileTime { get; }

            /// <summary>
            /// Last position of a previously non-finished self-check.
            /// </summary>
            public ContentHash? LastPosition { get; }

            /// <summary>
            /// Returns true if the current instance is created by a default struct's constructor.
            /// </summary>
            public bool IsEmpty => Epoch == null;

            /// <nodoc />
            public SelfCheckState(string epoch, DateTime lastReconcileTime, ContentHash? lastPosition)
                => (Epoch, LastReconcileTime, LastPosition) = (epoch, lastReconcileTime, lastPosition);

            /// <nodoc />
            public SelfCheckState(string epoch, DateTime lastReconcileTime)
                => (Epoch, LastReconcileTime, LastPosition) = (epoch, lastReconcileTime, null);

            /// <nodoc />
            public static SelfCheckState OutOfDate { get; } = new SelfCheckState();

            public SelfCheckState WithEpochAndPosition(string epoch, ContentHash lastPosition) => new SelfCheckState(epoch, LastReconcileTime, lastPosition);

            public static SelfCheckState SelfCheckComplete(string epoch, DateTime now) => new SelfCheckState(epoch, now);

            /// <summary>
            /// Gets the current status of a self check.
            /// </summary>
            public SelfCheckStatus CheckStatus(DateTime now, string currentEpoch, TimeSpan selfCheckFrequency)
            {
                if (IsEmpty)
                {
                    return SelfCheckStatus.OutOfDate;
                }

                if (Epoch != currentEpoch)
                {
                    return SelfCheckStatus.Force;
                }

                if (LastPosition != null)
                {
                    return SelfCheckStatus.InProgress;
                }

                return LastReconcileTime.IsRecent(now, selfCheckFrequency) ? SelfCheckStatus.UpToDate : SelfCheckStatus.OutOfDate;
            }

            /// <nodoc />
            public static SelfCheckState? TryParse(string content)
            {
                content = content.Trim('\r', '\n');
                // The format is:
                // Epoch|LastFullReconcileTime|LastContentHash
                var parts = content.Split('|');
                if (parts.Length != 3)
                {
                    return null;
                }

                var reconcileTime = DateTimeUtilities.FromReadableTimestamp(parts[1]);
                if (reconcileTime == null)
                {
                    return null;
                }

                ContentHash? hash = null;
                if (ContentHash.TryParse(parts[2], out var parsedHash))
                {
                    hash = parsedHash;
                }

                return new SelfCheckState(parts[0], reconcileTime.Value, hash);
            }

            /// <summary>
            /// Gets parseable representation of a current instance.
            /// </summary>
            public string ToParseableString()
            {
                var lastPosition = LastPosition?.ToString() ?? EmptyHash;
                return $"{Epoch}|{LastReconcileTime.ToReadableString()}|{lastPosition}";
            }

            /// <inheritdoc />
            public bool Equals(SelfCheckState other) => string.Equals(Epoch, other.Epoch) && LastReconcileTime == other.LastReconcileTime && LastPosition.Equals(other.LastPosition);

            /// <inheritdoc />
            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj))
                {
                    return false;
                }

                return obj is SelfCheckState other && Equals(other);
            }

            /// <inheritdoc />
            public override int GetHashCode()
            {
                return (Epoch, LastReconcileTime, LastPosition.GetHashCode()).GetHashCode();
            }

            /// <nodoc />
            public static bool operator ==(SelfCheckState left, SelfCheckState right) => left.Equals(right);

            /// <nodoc />
            public static bool operator !=(SelfCheckState left, SelfCheckState right) => !left.Equals(right);
        }

        private class DelegateBasedComparer<T> : IComparer<T> where T : IComparable<T>
        {
            private readonly Func<T, T, int> _comparer;

            public DelegateBasedComparer(Func<T, T, int> comparer) => _comparer = comparer;

            /// <inheritdoc />
            public int Compare(T x, T y) => _comparer(x, y);
        }

        private static DelegateBasedComparer<T> CreateComparer<T>(Func<T, T, int> comparer) where T : IComparable<T> => new DelegateBasedComparer<T>(comparer);
    }

    /// <summary>
    /// A result of <see cref="FileSystemContentStoreInternal.SelfCheckContentDirectoryAsync"/>.
    /// </summary>
    public struct SelfCheckResult
    {
        /// <summary>
        /// Total number of invalid files.
        /// </summary>
        public int InvalidFiles { get; }

        /// <summary>
        /// Total number of files scanned on disk.
        /// </summary>
        public int TotalProcessedFiles { get; }

        /// <nodoc />
        public SelfCheckResult(int invalidHashes, int totalProcessedFiles) =>
            (InvalidFiles, TotalProcessedFiles) = (invalidHashes, totalProcessedFiles);

        /// <nodoc />
        public static SelfCheckResult UpToDate() => new SelfCheckResult(invalidHashes: 0, totalProcessedFiles: -1);

        /// <inheritdoc />
        public override string ToString() => $"{nameof(InvalidFiles)}={InvalidFiles}, {nameof(TotalProcessedFiles)}={TotalProcessedFiles}";
    }
}
