// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    /// Helper class responsible for validating a content directory.
    /// </summary>
    internal sealed class FileSystemContentStoreInternalChecker
    {
        private readonly IAbsFileSystem _fileSystem;
        private readonly IClock _clock;
        private readonly FileSystemContentStoreInternal _contentStoreInternal;
        private readonly AbsolutePath _selfCheckFilePath;

        private readonly ContentStoreSettings _settings;
        private readonly Tracer _tracer;

        /// <nodoc />
        public FileSystemContentStoreInternalChecker(
            IAbsFileSystem fileSystem,
            IClock clock,
            AbsolutePath rootPath,
            Tracer tracer,
            ContentStoreSettings settings,
            FileSystemContentStoreInternal contentStoreInternal)
        {
            _fileSystem = fileSystem;
            _clock = clock;
            _contentStoreInternal = contentStoreInternal;
            _tracer = tracer;
            _settings = settings;
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

                            var status = selfCheckState.CheckStatus(_clock.UtcNow, _settings.SelfCheckEpoch, _settings.SelfCheckFrequency);
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
            // Self checking procedure validates that in-memory content directory
            // is valid in respect to the state on disk.
            // Namely, it checks that the hashes for all the files and their size are correct.
            var stopwatch = Stopwatch.StartNew();

            var contentInfos = await _contentStoreInternal.EnumerateContentInfoAsync();
            var orderedContentHashInfos = contentInfos.OrderBy(ci => ci.ContentHash).ToList();
            _tracer.Debug(context, $"SelfCheck: Obtained {orderedContentHashInfos.Count} entries from content directory by {stopwatch.ElapsedMilliseconds}ms.");

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

            // Task for tracking progress.
            Task progressReportingTask = Task.Delay(_settings.SelfCheckProgressReportingInterval);

            int invalidEntries = 0;
            int processedEntries = 0;

            for (; index < orderedContentHashInfos.Count; index++)
            {
                if (context.Token.IsCancellationRequested)
                {
                    _tracer.Debug(context, "SelfCheck: Exiting self check because cancellation was requested.");
                    break;
                }

                var hashInfo = orderedContentHashInfos[index];
                processedEntries++;

                var (isValid, error) = await ValidateFileAsync(context, hashInfo.ContentHash, hashInfo.Size);
                if (!isValid)
                {
                    _tracer.Warning(context, $"SelfCheck: Found invalid entry in cache. Hash={hashInfo.ContentHash}. {error}. Evicting the file...");
                    await _contentStoreInternal.RemoveInvalidContentAsync(context, hashInfo.ContentHash);
                    invalidEntries++;
                }

                // Tracking the progress if needed.
                traceProgressIfNeeded(hashInfo.ContentHash);

                if (((index + 1) % _settings.SelfCheckFilesLimit) == 0)
                {
                    _tracer.Debug(context, "SelfCheck: Exiting self check because file count limit is reached.");
                    break;
                }
            }

            if (index == orderedContentHashInfos.Count)
            {
                // All the items are processed. Saving new stable checkpoint
                UpdateSelfCheckStateOnDisk(context, SelfCheckState.SelfCheckComplete(_settings.SelfCheckEpoch, _clock.UtcNow));
            }
            else
            {
                // The loop was interrupted. Saving an incremental state.
                var newStatus = selfCheckState.WithNewPosition(orderedContentHashInfos[index].ContentHash);
                UpdateSelfCheckStateOnDisk(context, newStatus);
            }

            return Result.Success(new SelfCheckResult(invalidHashes: invalidEntries, totalProcessedFiles: processedEntries));

            int findNextIndexToProcess(ContentHash lastProcessedHash)
            {
                var binarySearchResult = orderedContentHashInfos.BinarySearch(
                    new ContentInfo(lastProcessedHash, size: -1, lastAccessTimeUtc: DateTime.MinValue),
                    ContentInfoByHashComparer.Instance);

                int targetIndex = 0;
                if (binarySearchResult >= 0 && binarySearchResult < orderedContentHashInfos.Count - 1)
                {
                    targetIndex = binarySearchResult + 1;
                }
                else
                {
                    // The exact match is not found (which is fine, because the state on disk may changed between app invocations).
                    // BinarySearch returns a negative value that bitwise complement of the closest element in the sorted array.

                    binarySearchResult = ~binarySearchResult;
                    if (binarySearchResult < orderedContentHashInfos.Count)
                    {
                        targetIndex = binarySearchResult;
                    }
                }

                return targetIndex;
            }

            void traceProgressIfNeeded(ContentHash currentHash)
            {
                if (progressReportingTask.IsCompleted)
                {
                    progressReportingTask = Task.Delay(_settings.SelfCheckProgressReportingInterval);

                    _tracer.Debug(context, $"SelfCheck: progress ({processedEntries}/{orderedContentHashInfos.Count}): {new SelfCheckResult(invalidEntries, processedEntries)}.");

                    // Saving incremental state
                    var newStatus = selfCheckState.WithNewPosition(currentHash);
                    UpdateSelfCheckStateOnDisk(context, newStatus);
                }
            }
        }

        private async Task<(bool isValid, string error)> ValidateFileAsync(Context context, ContentHash expectedHash, long expectedFileSize)
        {
            try
            {
                var path = _contentStoreInternal.GetPrimaryPathFor(expectedHash);
                var sizeOnDisk = _fileSystem.GetFileSize(path);

                // The cache entry is invalid if the size in content directory doesn't mach an actual size
                if (expectedFileSize != sizeOnDisk)
                {
                    return (isValid: false, error: $"File size mismatch. Expected size is {expectedFileSize} and size on disk is {sizeOnDisk}");
                }

                // Or if the content doesn't match the hash.
                var actualHashAndSize = await _contentStoreInternal.TryHashFileAsync(context, path, expectedHash.HashType);
                if (actualHashAndSize != null && actualHashAndSize.Value.Hash != expectedHash)
                {
                    // Don't need to add an expected hash into the error string because the client code will always put it into the final error message.
                    return (isValid: false, error: $"Hash mismatch. Actual hash is {actualHashAndSize.Value.Hash}");
                }

                return (isValid: true, error: string.Empty);
            }
            catch (Exception e)
            {
                _tracer.Warning(context, $"SelfCheck: Content hash is invalid. Hash={expectedHash}, Error={e}");

                return (isValid: true, error: string.Empty);
            }
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
            private const string EmptyHash = "NO_HASH";

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

            public SelfCheckState WithNewPosition(ContentHash lastPosition) => new SelfCheckState(Epoch, LastReconcileTime, lastPosition);

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

        private class ContentInfoByHashComparer : IComparer<ContentInfo>
        {
            public static readonly ContentInfoByHashComparer Instance = new ContentInfoByHashComparer();

            /// <inheritdoc />
            public int Compare(ContentInfo x, ContentInfo y) => x.ContentHash.CompareTo(y.ContentHash);
        }
    }
}
