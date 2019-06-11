// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Exceptions;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Stores
{
    using ContentMap = ConcurrentDictionary<ContentHash, ContentFileInfo>;

    /// <summary>
    ///     In-memory implementation of a content directory.
    /// </summary>
    public class MemoryContentDirectory : StartupShutdownBase, IContentDirectory
    {
        /// <summary>
        /// Defines a file name where content directory information is stored.
        /// </summary>
        internal const string BinaryFileName = "Directory.bin";

        /// <summary>
        /// Defines a file name for backup file.
        /// </summary>
        internal const string BinaryBackupFileName = "Directory.backup.bin";

        private const byte BinaryFormatMagicFlag = 0xcd;

        private const byte BinaryFormatVersion = 4;
        private const int UnusedAccessCount = 0;

        // Size of header for v2 version of file
        private const int BinaryHeaderSizeV2 = 6;

        // Extra header size on top of v2 size required for v3 header.
        private const int BinaryHeaderExtraSizeV3 = 16;

        // Exact size of an entry for the current version.
        private const int BinaryEntrySize = ContentHash.SerializedLength + 8 + 8 + 4 + 4;

        // Number of entries read/written in parallel at once.
        private const int BinaryMaxEntriesPerChunk = 250000;

        private readonly CounterCollection<MemoryContentDirectoryCounters> _counters = new CounterCollection<MemoryContentDirectoryCounters>();
        private readonly IAbsFileSystem _fileSystem;
        private readonly AbsolutePath _filePath;
        private readonly AbsolutePath _backupFilePath;
        private readonly IContentDirectoryHost _host;

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(MemoryContentDirectory));
        private string Name => Tracer.Name;

        private bool ContentDirectoryInitialized => _initializeContentDirectory?.IsCompleted == true;
        private ContentMap _contentDirectory;

        /// <summary>
        ///     MemoryContentDirectory file is expensive to deserialize.
        ///     To make deserialization faster, the file divided into two parts, header and body.
        ///     The header is read first, and body is read asynchronously and only waited on when it is needed.
        ///     The header contains the version, size of cache, number of elements, etc...
        /// </summary>
        private MemoryContentDirectoryHeader _header;

        /// <summary>
        ///     In-memory mapping of all content hashes stored to their metadata
        /// </summary>
        private ContentMap ContentDirectory => (_contentDirectory = _contentDirectory ?? _initializeContentDirectory?.GetAwaiter().GetResult());

        /// <summary>
        /// Async task to deserialize ContentDirectory
        /// </summary>
        private Task<ContentMap> _initializeContentDirectory;

        /// <summary>
        ///     Initializes a new instance of the <see cref="MemoryContentDirectory" /> class.
        /// </summary>
        public MemoryContentDirectory(IAbsFileSystem fileSystem, AbsolutePath directoryPath, IContentDirectoryHost host = null)
        {
            Contract.Requires(fileSystem != null);
            Contract.Requires(directoryPath != null);

            _fileSystem = fileSystem;
            _filePath = directoryPath / BinaryFileName;
            _backupFilePath = directoryPath / BinaryBackupFileName;
            _host = host;

            if (!_fileSystem.DirectoryExists(directoryPath))
            {
                throw new ArgumentException("must be path to a directory", nameof(directoryPath));
            }

            FilePath = _filePath;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            Tracer.Info(context, $"{Name} startup");

            if (_fileSystem.FileExists(_filePath))
            {
                _header = await DeserializeHeaderAsync(context, _filePath);
                _initializeContentDirectory = InitializeContentDirectoryAsync(context, _header, _filePath, isLoadingBackup: false);
                Tracer.Info(context, $"{Name} starting with {_header.EntryCount} entries.");
            }
            else if (_fileSystem.FileExists(_backupFilePath))
            {
                var backupHeader = await DeserializeHeaderAsync(context, _backupFilePath);
                _header = new MemoryContentDirectoryHeader();
                _initializeContentDirectory = InitializeContentDirectoryAsync(context, backupHeader, _backupFilePath, isLoadingBackup: true);
                Tracer.Info(context, $"{Name} starting with {backupHeader.EntryCount} entries from backup file.");
            }
            else
            {
                _header = new MemoryContentDirectoryHeader();
                _initializeContentDirectory = InitializeContentDirectoryAsync(context, _header, path: null, isLoadingBackup: false);

                Tracer.Info(context, $"{Name} starting with {ContentDirectory.Count} entries from no file.");
            }

            // Tracing initialization asynchronously.
            _initializeContentDirectory.ContinueWith(
                t =>
                {
                    Tracer.Info(context, $"{Name} started with {ContentDirectory.Count} entries.");
                }).FireAndForget(context);

            return BoolResult.Success;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            await SerializeAsync();

            if (StartupCompleted)
            {
                Tracer.Info(context, $"{Name} ending with {ContentDirectory?.Count} entries");
            }

            return BoolResult.Success;
        }

        /// <inheritdoc />
        public AbsolutePath FilePath { get; }

        /// <inheritdoc />
        public CounterSet GetCounters() => _counters.ToCounterSet();

        /// <inheritdoc />
        public Task<long> GetSizeAsync()
        {
            GetSizeAndReplicaCount(out var contentSize, out _);
            return Task.FromResult(contentSize);
        }

        /// <inheritdoc />
        public Task<long> GetTotalReplicaCountAsync()
        {
            GetSizeAndReplicaCount(out _, out var replicaCount);
            return Task.FromResult(replicaCount);
        }

        private void GetSizeAndReplicaCount(out long contentSize, out long replicaCount)
        {
            if (_header.Version == BinaryFormatVersion && !ContentDirectoryInitialized)
            {
                // Successfully loaded the header and we haven't yet initialized in-memory content directory
                replicaCount = _header.ReplicaCount;
                contentSize = _header.ContentSize;
                return;
            }

            contentSize = 0;
            replicaCount = 0;

            // NOTE: This blocks until content directory is initialized (this may entail reconstruction)
            foreach (var value in ContentDirectory.Values)
            {
                contentSize += value.FileSize * value.ReplicaCount;
                replicaCount += value.ReplicaCount;
            }
        }

        [SuppressMessage("AsyncUsage", "AsyncFixer02:Long running or blocking operations under an async method")]
        private async Task SerializeAsync()
        {
            if (ContentDirectory == null || ContentDirectory.Count == 0)
            {
                return;
            }

            var openTask = _fileSystem.OpenSafeAsync(FilePath, FileAccess.Write, FileMode.Create, FileShare.Delete);
            var sync = new object();
            var writeHeader = true;
            var entries = ContentDirectory.ToArray();
            var entriesRemaining = entries.Length;
            var startIndex = 0;
            var tasks = new List<Task>();
            GetSizeAndReplicaCount(out var contentSize, out var replicaCount);

            using (var stream = await openTask)
            {
                Action<int, int> writeChunk = (index, count) =>
                {
                    var endIndexExclusive = index + count;
                    var entryCount = endIndexExclusive - index;
                    var bufferLength = entryCount * BinaryEntrySize;
                    var partitionBuffer = new byte[bufferLength];
                    var partitionContext = new BufferSerializeContext(partitionBuffer);

                    for (var i = index; i < endIndexExclusive; i++)
                    {
                        ContentFileInfo entry = entries[i].Value;
                        partitionContext.SerializeFull(entries[i].Key);
                        partitionContext.Serialize(entry.FileSize);
                        partitionContext.Serialize(entry.LastAccessedFileTimeUtc);
                        partitionContext.Serialize(UnusedAccessCount);
                        partitionContext.Serialize(entry.ReplicaCount);
                    }

                    lock (sync)
                    {
                        if (writeHeader)
                        {
                            writeHeader = false;
                            var headerBuffer = new byte[22];
                            var headerContext = new BufferSerializeContext(headerBuffer);
                            headerContext.Serialize(BinaryFormatMagicFlag);
                            headerContext.Serialize(BinaryFormatVersion);
                            headerContext.Serialize(entries.Length);
                            headerContext.Serialize(contentSize);
                            headerContext.Serialize(replicaCount);
                            stream.Write(headerBuffer, 0, headerBuffer.Length);
                        }

                        stream.Write(partitionBuffer, 0, partitionContext.Offset);
                    }
                };

                while (startIndex < entries.Length)
                {
                    var i = startIndex;
                    var chunkCount = Math.Min(entriesRemaining, BinaryMaxEntriesPerChunk);
                    tasks.Add(Task.Run(() => writeChunk(i, chunkCount)));
                    startIndex += chunkCount;
                    entriesRemaining -= chunkCount;
                }

                await TaskSafetyHelpers.WhenAll(tasks);
                Contract.Assert(startIndex == entries.Length);
            }
        }

        [SuppressMessage("AsyncUsage", "AsyncFixer02:Long running or blocking operations under an async method")]
        private async Task<MemoryContentDirectoryHeader> DeserializeHeaderAsync(Context context, AbsolutePath path)
        {
            try
            {
                var directoryHeader = new MemoryContentDirectoryHeader();
                using (var stream = await _fileSystem.OpenSafeAsync(path, FileAccess.Read, FileMode.Open, FileShare.Read))
                {
                    var header = new byte[BinaryHeaderSizeV2];
                    stream.Read(header, 0, header.Length);
                    var headerContext = new BufferSerializeContext(header);
                    directoryHeader.MagicFlag = headerContext.DeserializeByte();
                    if (directoryHeader.MagicFlag != BinaryFormatMagicFlag)
                    {
                        throw new CacheException("{0} binary format missing magic flag", Name);
                    }

                    directoryHeader.Version = headerContext.DeserializeByte();
                    directoryHeader.EntryCount = headerContext.DeserializeInt32();
                    if (directoryHeader.Version == BinaryFormatVersion)
                    {
                        header = new byte[BinaryHeaderExtraSizeV3];
                        stream.Read(header, 0, header.Length);
                        headerContext = new BufferSerializeContext(header);
                        directoryHeader.ContentSize = headerContext.DeserializeInt64();
                        directoryHeader.ReplicaCount = headerContext.DeserializeInt64();
                        directoryHeader.HeaderSize = BinaryHeaderSizeV2 + BinaryHeaderExtraSizeV3;
                    }
                    else
                    {
                        throw new CacheException("{0} expected {1} but read binary format version {2}", Name, BinaryFormatVersion, directoryHeader.Version);
                    }

                    return directoryHeader;
                }
            }
            catch (Exception exception)
            {
                context.Warning($"{Name} failed to deserialize header of {FilePath} - starting with empty directory: {exception}");
                return new MemoryContentDirectoryHeader();
            }
        }

        private async Task<ContentMap> InitializeContentDirectoryAsync(Context context, MemoryContentDirectoryHeader header, AbsolutePath path, bool isLoadingBackup)
        {
            var operationContext = new OperationContext(context);

            var result = await operationContext.PerformOperationAsync<Result<ContentMap>>(
                Tracer,
                async () =>
                {
                    // Making this method asynchronous to initialize _initializeContentDirectory field as fast as possible
                    // even if the initialization is synchronous.
                    // This will enforce the invariant that ContentDirectory property is not null.
                    await Task.Yield();

                    bool canLoadContentDirectory = path != null && header.Version == BinaryFormatVersion;
                    var loadedContentDirectory = canLoadContentDirectory
                        ? await DeserializeBodyAsync(context, header, path, isLoadingBackup)
                        : new ContentMap();

                    if (!isLoadingBackup && loadedContentDirectory.Count != 0)
                    {
                        // Successfully loaded primary content directory
                        return loadedContentDirectory;
                    }

                    // Primary content directory is empty, missing, or failed to load
                    // Reconstruct to ensure content is populated.
                    ContentMap contentDirectory = new ContentMap();
                    var backupContentDirectory = loadedContentDirectory;
                    if (_host != null)
                    {
                        await AddBulkAsync(
                            contentDirectory: contentDirectory,
                            backupContentDirectory: backupContentDirectory,
                            hashInfoPairs: _host.Reconstruct(context));
                    }
                    else
                    {
                        // Host may be null in tests. Warn?
                    }

                    return contentDirectory;
                },
                _counters[MemoryContentDirectoryCounters.InitializeContentDirectory]).ThrowIfFailure();

                return result.Value;
        }

        [SuppressMessage("AsyncUsage", "AsyncFixer02:Long running or blocking operations under an async method")]
        private async Task<ContentMap> DeserializeBodyAsync(Context context, MemoryContentDirectoryHeader header, AbsolutePath path, bool isLoadingBackup)
        {
            var contentDirectory = new ContentMap();
            
            try
            {
                var sw = Stopwatch.StartNew();
                using (var stream = await _fileSystem.OpenSafeAsync(path, FileAccess.Read, FileMode.Open, FileShare.Read))
                {
                    byte[] headerBuffer = new byte[header.HeaderSize];
                    stream.Read(headerBuffer, 0, header.HeaderSize);

                    var streamSync = new object();
                    var entriesSync = new object();
                    var entries = new List<KeyValuePair<ContentHash, ContentFileInfo>>(header.EntryCount);
                    var entriesRemaining = header.EntryCount;
                    var tasks = new List<Task>();
                    var nowFileTimeUtc = DateTime.UtcNow.ToFileTimeUtc();

                    long totalSize = 0;
                    long totalUniqueSize = 0;
                    long oldestContentAccessTimeUtc = nowFileTimeUtc;
                    long totalReplicaCount = 0;
                    var statsLock = new object();

                    Action<int> readChunk = count =>
                    {
                        var bufferLength = count * BinaryEntrySize;
                        var buffer = new byte[bufferLength];
                        int bytesRead;

                        lock (streamSync)
                        {
                            bytesRead = stream.Read(buffer, 0, bufferLength);
                        }

                        if (bytesRead != buffer.Length)
                        {
                            return;
                        }

                        var serializeContext = new BufferSerializeContext(buffer);
                        var partitionEntries = new List<KeyValuePair<ContentHash, ContentFileInfo>>(count);

                        for (var i = 0; i < count; i++)
                        {
                            var contentHash = serializeContext.DeserializeFullContentHash();
                            var fileSize = serializeContext.DeserializeInt64();
                            var lastAccessedFileTimeUtc = serializeContext.DeserializeInt64();

                            // Guard against corruption of serialized timestamps which affect LRU. If we get something out of range,
                            // force it to now.
                            if (lastAccessedFileTimeUtc < 0 || lastAccessedFileTimeUtc > nowFileTimeUtc)
                            {
                                lastAccessedFileTimeUtc = nowFileTimeUtc;
                            }

                            // ReSharper disable once UnusedVariable
                            var accessCount = serializeContext.DeserializeInt32();
                            var replicaCount = serializeContext.DeserializeInt32();

                            var contentFileInfo = new ContentFileInfo(fileSize, lastAccessedFileTimeUtc, replicaCount);
                            Interlocked.Add(ref totalSize, fileSize * replicaCount);
                            Interlocked.Add(ref totalUniqueSize, fileSize);
                            Interlocked.Add(ref totalReplicaCount, replicaCount);

                            if (oldestContentAccessTimeUtc > lastAccessedFileTimeUtc)
                            {
                                lock (statsLock)
                                {
                                    if (oldestContentAccessTimeUtc > lastAccessedFileTimeUtc)
                                    {
                                        oldestContentAccessTimeUtc = lastAccessedFileTimeUtc;
                                    }
                                }
                            }

                            partitionEntries.Add(new KeyValuePair<ContentHash, ContentFileInfo>(contentHash, contentFileInfo));
                        }

                        lock (entriesSync)
                        {
                            entries.AddRange(partitionEntries);
                        }
                    };

                    while (entriesRemaining > 0)
                    {
                        var chunkCount = Math.Min(entriesRemaining, BinaryMaxEntriesPerChunk);
                        tasks.Add(Task.Run(() => readChunk(chunkCount)));
                        entriesRemaining -= chunkCount;
                    }

                    await TaskSafetyHelpers.WhenAll(tasks);

                    context.Debug($"{Name}: Loaded content directory with {entries.Count} entries by {sw.ElapsedMilliseconds}ms: TotalContentSize={totalSize}, TotalUniqueSize={totalUniqueSize}, TotalReplicaCount={totalReplicaCount}, OldestContentTime={DateTime.FromFileTimeUtc(oldestContentAccessTimeUtc)}.");

                    if (entries.Count == header.EntryCount)
                    {
                        contentDirectory = new ContentMap(entries);
                    }
                    else
                    {
                        throw new CacheException($"Failed to read expected number of entries. Entries.Count={entries.Count}, Header.EntryCount={header.EntryCount}.");
                    }
                }

                if (!isLoadingBackup)
                {
                    // At this point, we've either successfully read the file or tried and failed. Either way, the existing file should now be
                    // deleted.  On a clean shutdown, it will be regenerated. On a dirty shutdown, we want it to already be gone otherwise
                    // we'll read in a file that is out-of-date.
                    _fileSystem.MoveFile(path, _backupFilePath, true);
                }
            }
            catch (Exception exception)
            {
                context.Warning($"{Name} failed to deserialize {FilePath} - starting with empty directory: {exception}");
                contentDirectory.Clear();
            }

            return contentDirectory;
        }

        /// <inheritdoc />
        public Task<long> GetCountAsync()
        {
            return Task.FromResult<long>(ContentDirectoryInitialized ? ContentDirectory.Count : _header.EntryCount);
        }

        /// <inheritdoc />
        public Task<IEnumerable<ContentHash>> EnumerateContentHashesAsync()
        {
            return Task.FromResult<IEnumerable<ContentHash>>(ContentDirectory.Keys);
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<ContentInfo>> EnumerateContentInfoAsync()
        {
            var snapshot = ContentDirectory.ToArray();
            return Task.FromResult<IReadOnlyList<ContentInfo>>(snapshot.Select(x => new ContentInfo(x.Key, x.Value.FileSize, DateTime.FromFileTimeUtc(x.Value.LastAccessedFileTimeUtc))).ToList());
        }

        /// <inheritdoc />
        public Task<ContentFileInfo> RemoveAsync(ContentHash contentHash)
        {
            ContentDirectory.TryRemove(contentHash, out var info);
            return Task.FromResult(info);
        }

        /// <inheritdoc />
        public void UpdateContentWithLastAccessTime(ContentHash contentHash, DateTime lastAccess)
        {
            if (ContentDirectory.TryGetValue(contentHash, out var existingInfo))
            {
                existingInfo.UpdateLastAccessed(lastAccess);
            }
        }

        /// <inheritdoc />
        public async Task UpdateAsync(ContentHash contentHash, bool touch, IClock clock, UpdateFileInfo updateFileInfo)
        {
            ContentDirectory.TryGetValue(contentHash, out var existingInfo);

            ContentFileInfo cloneInfo = null;
            if (existingInfo != null)
            {
                cloneInfo = new ContentFileInfo(existingInfo.FileSize, existingInfo.LastAccessedFileTimeUtc, existingInfo.ReplicaCount);
                if (touch)
                {
                    existingInfo.UpdateLastAccessed(clock);
                }
            }

            var updateTask = updateFileInfo(cloneInfo);
            if (updateTask != null)
            {
                var updateInfo = await updateTask;
                if (updateInfo != null)
                {
                    ContentDirectory.TryGetValue(contentHash, out existingInfo);
                    if (existingInfo == null)
                    {
                        ContentDirectory.TryAdd(contentHash, updateInfo);
                    }
                    else if (existingInfo.ReplicaCount != updateInfo.ReplicaCount)
                    {
                        ContentDirectory[contentHash].ReplicaCount = updateInfo.ReplicaCount;
                    }
                }
            }
        }

        /// <inheritdoc />
        public bool TryGetFileInfo(ContentHash contentHash, out ContentFileInfo fileInfo)
        {
            return ContentDirectory.TryGetValue(contentHash, out fileInfo);
        }

        private static Task AddBulkAsync(ContentMap contentDirectory, ContentMap backupContentDirectory, ContentDirectorySnapshot<ContentFileInfo> hashInfoPairs)
        {
            return hashInfoPairs.ParallelAddToConcurrentDictionaryAsync(
                contentDirectory, hashInfoPair => hashInfoPair.Hash, hashInfoPair =>
                {
                    var info = hashInfoPair.Payload;
                    if (backupContentDirectory.TryGetValue(hashInfoPair.Hash, out var backupInfo))
                    {
                        // Recover the last access time from the backup. This has the affect that
                        // content mentioned in the backup will be older than newly discovered content
                        return new ContentFileInfo(
                            info.FileSize,
                            backupInfo.LastAccessedFileTimeUtc,
                            info.ReplicaCount);
                    }

                    return info;
                });
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<ContentHash>> GetLruOrderedCacheContentAsync()
        {
            // http://stackoverflow.com/questions/11692389/getting-argument-exception-in-concurrent-dictionary-when-sorting-and-displaying
            // LINQ assumes an immutable source.
            var snapshotContentDirectory = ContentDirectory.ToArray();
            return Task.FromResult<IReadOnlyList<ContentHash>>(snapshotContentDirectory
                .OrderBy(fileInfoByHash => fileInfoByHash.Value.LastAccessedFileTimeUtc)
                .Select(fileInfoByHash => fileInfoByHash.Key)
                .ToList());
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<ContentHashWithLastAccessTimeAndReplicaCount>> GetLruOrderedCacheContentWithTimeAsync()
        {
            // http://stackoverflow.com/questions/11692389/getting-argument-exception-in-concurrent-dictionary-when-sorting-and-displaying
            // LINQ assumes an immutable source.
            var snapshotContentDirectory = ContentDirectory.ToArray();
            return Task.FromResult<IReadOnlyList<ContentHashWithLastAccessTimeAndReplicaCount>>(snapshotContentDirectory
                .OrderBy(fileInfoByHash => fileInfoByHash.Value.LastAccessedFileTimeUtc)
                .Select(fileInfoByHash => new ContentHashWithLastAccessTimeAndReplicaCount(fileInfoByHash.Key, DateTime.FromFileTimeUtc(fileInfoByHash.Value.LastAccessedFileTimeUtc)))
                .ToList());
        }

        /// <inheritdoc />
        public Task SyncAsync()
        {
            return Task.FromResult(0);
        }

        /// <summary>
        ///     Transform entries of an existing serialized file.
        /// </summary>
        public static async Task TransformFile(
            Context context,
            IAbsFileSystem fileSystem,
            AbsolutePath directoryPath,
            Func<KeyValuePair<ContentHash, ContentFileInfo>, KeyValuePair<ContentHash, ContentFileInfo>> transformer)
        {
            Contract.Requires(fileSystem != null);
            Contract.Requires(directoryPath != null);
            Contract.Requires(transformer != null);

            var entries = new Dictionary<ContentHash, ContentFileInfo>();

            using (var contentDirectory = new MemoryContentDirectory(fileSystem, directoryPath))
            {
                try
                {
                    var r = await contentDirectory.StartupAsync(context);
                    if (!r)
                    {
                        throw new CacheException($"Failed to startup {nameof(MemoryContentDirectory)} for transformation. {r}.");
                    }

                    foreach (var entry in contentDirectory.ContentDirectory)
                    {
                        var newEntry = transformer(entry);
                        entries.Add(newEntry.Key, newEntry.Value);
                    }

                    contentDirectory.ContentDirectory.Clear();

                    foreach (var kvp in entries)
                    {
                        contentDirectory.ContentDirectory.TryAdd(kvp.Key, kvp.Value);
                    }
                }
                finally
                {
                    var r = await contentDirectory.ShutdownAsync(context);
                    if (!r)
                    {
                        throw new CacheException($"Failed to shutdown {nameof(MemoryContentDirectory)} for transformation. {r}.");
                    }
                }
            }
        }

        /// <summary>
        ///     MemoryContentDirectory file is expensive to deserialize.
        ///     To make deserialization faster, the file divided into two parts, header and body.
        ///     The header is read first, and body is read asynchronously and only waited on when it is needed.
        /// </summary>
        internal struct MemoryContentDirectoryHeader
        {
            /// <summary>
            /// Number of bytes in the file the header consists of.
            /// File format v2 doesn't contain contentsize/replicacount (it is computed), so it is different than v3+.
            /// </summary>
            public int HeaderSize;

            /// <summary>
            /// Used to check for errors.
            /// </summary>
            public byte MagicFlag;

            /// <summary>
            /// Version of the file format
            /// </summary>
            public byte Version;

            /// <summary>
            /// Number of items in the memory content directory
            /// </summary>
            public int EntryCount;

            /// <summary>
            /// Total size of the content in the directory
            /// </summary>
            public long ContentSize;

            /// <summary>
            /// Total replica count in the directory.
            /// </summary>
            public long ReplicaCount;
        }
    }
}
