using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Engine.Cache.KeyValueStores;
using BuildXL.Utilities.Serialization;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using RocksDbSharp;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

namespace BuildXL.Cache.ContentStore.Sessions
{
    public class RocksDbFileSystemContentSession : ContentSessionBase
    {
        protected override Tracer Tracer { get; } = new Tracer(nameof(RocksDbFileSystemContentSession));

        /// <summary>
        ///     LockSet used to ensure thread safety on write operations.
        /// </summary>
        private readonly LockSet<ContentHash> _lockSet;

        private readonly IAbsFileSystem _fileSystem;

        private readonly IClock _clock;

        /// <summary>
        ///     Gets root directory path.
        /// </summary>
        private readonly AbsolutePath _rootPath;

        /// <summary>
        /// Path: _rootPath / Constants.SharedDirectoryName - where content will be stored
        /// </summary>
        private readonly AbsolutePath _storePath;

        /// <summary>
        /// Path: _storePath / temp. Used to help write content to _storePath
        /// </summary>
        private readonly DisposableDirectory _tempDisposableDirectory;

        /// <summary>
        /// Manages and provides access to RocksDB. Throws if null.
        /// </summary>
        private readonly KeyValueStoreAccessor _accessor;

        private const string BlobNameExtension = "blob";

        /// <summary>
        ///     Length of subdirectory names used for storing files. For example with length 3,
        ///     content with hash "abcdefg" will be stored in $root\abc\abcdefg.blob.
        /// </summary>
        internal const int HashDirectoryNameLength = 3;

        /// <summary>
        ///     This is the value stored in RocksDB
        /// </summary>
        /// <param name="size">File size</param>
        /// <param name="LastAccessTime">Last Access Time of the file (PutFile & Openstream modify this)</param>
        /// <param name="LastCheckTime">Last time the value was synced with the disk</param>
        /// <param name="LastCheckTime">When the structure was created</param>
        public record struct ContentMetadata(long Size, DateTime LastAccessTime, DateTime LastCheckTime, DateTime CreationTime = default);

        /// <nodoc />
        public RocksDbFileSystemContentSession(
            string name,
            LockSet<ContentHash> lockSet,
            IAbsFileSystem fileSystem,
            IClock clock,
            AbsolutePath rootPath,
            AbsolutePath storePath,
            DisposableDirectory tempDisposableDirectory,
            KeyValueStoreAccessor accessor)
            : base(name)
        {
            Contract.Requires(lockSet != null);
            Contract.Requires(fileSystem != null);
            Contract.Requires(clock != null);
            Contract.Requires(rootPath != null);
            Contract.Requires(tempDisposableDirectory != null);
            Contract.Requires(accessor != null);

            _lockSet = lockSet;
            _fileSystem = fileSystem;
            _clock = clock;
            _rootPath = rootPath;
            _storePath = storePath;
            _tempDisposableDirectory = tempDisposableDirectory;
            _accessor = accessor;
        }

        /// <inheritdoc />
        protected override async Task<PutResult> PutFileCoreAsync(
            OperationContext operationContext,
            ContentHash hash,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            using (LockSet<ContentHash>.LockHandle contentHashHandle = await _lockSet.AcquireAsync(hash))
            {
                if (TryGetContentMetadata(hash, out var contentMetadata))
                {
                    // Update RocksDB with new LastAccessTime
                    PutContentMetadata(hash, contentMetadata with { LastAccessTime = _clock.UtcNow }, updateCacheSize: false);
                    return new PutResult(hash, contentMetadata.Size, contentAlreadyExistsInCache: true);
                }
                else
                {
                    // HardLink, if hardlink succeeds: insert content<hash,metadata> and update cache size in RocksDB

                    AbsolutePath destinationPath = GetPath(hash);

                    long fileSize;
                    try { fileSize = _fileSystem.GetFileSize(path); }
                    // PutResult(hash, errorMessage: e.Message)
                    catch (FileNotFoundException e) { return new PutResult(e, hash); }
                    // evict content call here

                    AbsolutePath tempPath = _tempDisposableDirectory.CreateRandomFileName();
                    using (var disposableFile = new DisposableFile(operationContext, _fileSystem, tempPath))
                    {
                        bool useTempPath = false;
                        if (!ShouldAttemptHardLink(destinationPath, FileAccessMode.ReadOnly, realizationMode)) {
                            _fileSystem.CopyFile(path, tempPath, replaceExisting: false);
                            useTempPath = true;
                        }

                        CreateHardLinkResult result = _fileSystem.CreateHardLink(useTempPath ? tempPath : path, destinationPath, replaceExisting: false);
                        return HandleCreateHardLinkCodes(result, hash, fileSize, useTempPath ? tempPath: path, destinationPath, tempPath, triedCopyingWithTempPath: useTempPath);
                    }
                }
            }
        }

        /// <summary>
        ///     Handles CreateHardLinkResult Codes. <see cref="CreateHardLinkResult"/>
        /// </summary>
        /// <param name="result">CreateHardLinkResult</param>
        /// <param name="hash">ContentHash</param>
        /// <param name="fileSize">File size of 'path'</param>
        /// <param name="destinationPath">Destination path (calculated from hash)</param>
        /// <param name="parentDestinationPath">Parent destintion path</param>
        private PutResult HandleCreateHardLinkCodes(
            CreateHardLinkResult result,
            ContentHash hash,
            long fileSize,
            AbsolutePath path,
            AbsolutePath destinationPath,
            AbsolutePath tempPath,
            bool triedCopyingWithTempPath)
        {
            if (result == CreateHardLinkResult.FailedDestinationDirectoryDoesNotExist)
            {
                _fileSystem.CreateDirectory(destinationPath!.Parent!);;
                result = _fileSystem.CreateHardLink(path, destinationPath, replaceExisting: false);
            }
            if (result == CreateHardLinkResult.Unknown && !triedCopyingWithTempPath)
            {
                _fileSystem.CopyFile(path, tempPath, replaceExisting: false);
                result = _fileSystem.CreateHardLink(tempPath, destinationPath, replaceExisting: false);
            }

            if (result == CreateHardLinkResult.Success)
            {
                _fileSystem.DenyFileWrites(destinationPath);
                PutContentMetadata(hash, new ContentMetadata(fileSize, _clock.UtcNow, _clock.UtcNow), updateCacheSize: true);
                return new PutResult(hash, fileSize, contentAlreadyExistsInCache: false);
            }
            else
            {
                return new PutResult(hash, errorMessage: $"Failed to hardlink {path} to {destinationPath} with errorcode {result}.");
            }
        }

        /// <inheritdoc /> 
        protected override async Task<OpenStreamResult> OpenStreamCoreAsync(
            OperationContext operationContext,
            ContentHash contentHash,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            using (LockSet<ContentHash>.LockHandle contentHashHandle = await _lockSet.AcquireAsync(contentHash))
            {
                if (!TryGetContentMetadata(contentHash, out var contentMetadata))
                {
                    return new OpenStreamResult(OpenStreamResult.ResultCode.ContentNotFound, "Content Hash not in RocksDB");
                }
                StreamWithLength? streamResult = _fileSystem.TryOpen(GetPath(contentHash), FileAccess.Read, FileMode.Open, FileShare.Read, FileOptions.None, FileSystemDefaults.DefaultFileStreamBufferSize);
                if (streamResult is null)
                {
                    RemoveContentMetaData(contentHash);
                    return new OpenStreamResult(OpenStreamResult.ResultCode.ContentNotFound, $"The file: {GetPath(contentHash)} of Content Hash: {contentHash} does not exist");
                }
                PutContentMetadata(contentHash, contentMetadata with { LastAccessTime = _clock.UtcNow }, updateCacheSize: false);
                return new OpenStreamResult(streamResult);
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Used for Tests
        /// </remarks>
        protected override Task<PutResult> PutStreamCoreAsync(
            OperationContext operationContext,
            HashType hashType,
            Stream stream,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            var bytesFromStream = StreamToByteArray(stream);
            var hash = HashInfoLookup.GetContentHasher(hashType).GetContentHash(bytesFromStream);
            return PutStreamCoreAsync(operationContext, hash, stream, urgencyHint, retryCounter);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Used for Tests
        /// </remarks>
        protected override async Task<PutResult> PutStreamCoreAsync(
            OperationContext operationContext,
            ContentHash contentHash,
            Stream stream,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            AbsolutePath sourcePath = _tempDisposableDirectory.CreateRandomFileName();

            using (var disposableFile = new DisposableFile(operationContext, _fileSystem, sourcePath))
            {
                using (Stream? outStream = _fileSystem.TryOpenForWrite(sourcePath, stream.Length, FileMode.OpenOrCreate, FileShare.ReadWrite))
                {
                    if (outStream is null) { return new PutResult(contentHash, $"Path: {GetPath(contentHash)} does not exist"); } // Parent directory is created on startup, if should never be true
                    await stream.CopyToAsync(outStream!);
                    return await PutFileCoreAsync(operationContext, contentHash, sourcePath, FileRealizationMode.HardLink, urgencyHint, retryCounter);
                }
            }
        }

        /// <inheritdoc />
        protected override Task<PinResult> PinCoreAsync(
            OperationContext operationContext,
            ContentHash contentHash,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        protected override Task<IEnumerable<Task<Indexed<PinResult>>>> PinCoreAsync(
            OperationContext operationContext,
            IReadOnlyList<ContentHash> contentHashes,
            UrgencyHint urgencyHint,
            Counter retryCounter,
            Counter fileCounter)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        protected override Task<PlaceFileResult> PlaceFileCoreAsync(
            OperationContext operationContext,
            ContentHash contentHash,
            AbsolutePath path,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        protected override Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> PlaceFileCoreAsync(
            OperationContext operationContext,
            IReadOnlyList<ContentHashWithPath> hashesWithPaths,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        ///     The hash is the key. The metadata is the value.
        ///     Determines whether the key exists in the store. If it does, assigns the value to metadata
        /// </summary>
        /// <param name="hash">ContentHash</param>
        /// <param name="metadata">Metadata</param>
        public bool TryGetContentMetadata(ContentHash hash, out ContentMetadata metadata)
        {
            return _accessor.Use(store =>
            {
                var tryDeserializeValue = store.TryDeserializeValue(
                    MemoryMarshal.AsBytes(stackalloc[] { hash }),
                    RocksDbFileSystemContentStore.HashAndMetadataColumnFamily,
                    reader => reader.Read<ContentMetadata>(),
                    out var metadata);
                return BuildXL.Utilities.Optional<ContentMetadata>.Create(tryDeserializeValue, metadata);
            }).ToResult().ThrowIfFailure().TryGetValue(out metadata);
        }

        /// <summary>
        ///     Returns the Cache Size
        /// </summary>
        public long GetCacheSize()
        {
            return _accessor.Use<long>(store =>
            {
                if (!store.TryDeserializeValue(
                    RocksDbFileSystemContentStore.CacheSizeKey.Span,
                    RocksDbFileSystemContentStore.CacheSizeColumnFamily,
                    reader => reader.Read<long>(),
                    out var cacheSizeTemporary))
                {
                    return 0;
                }
                return cacheSizeTemporary;
            }).ToResult().ThrowIfFailure();         
        }

        /// <summary>
        ///     Puts hash & contentMetadata in RocksDB, updates cacheSize
        /// </summary>
        /// <param name="hash">ContentHash</param>
        /// <param name="metadata">Metadata</param>
        /// <param name="updateCacheSize">Fglag for whether or not to update cache size</param>
        internal void PutContentMetadata(
            ContentHash hash,
            ContentMetadata metadata,
            bool updateCacheSize)
        {
            _accessor.Use(store =>
            {
                PutContentMetadataInternal(store, hash, metadata, updateCacheSize);
                return Unit.Void;
            }).ToResult().ThrowIfFailure();
        }

        /// <summary>
        ///     Puts hash & contentMetadata in RocksDB, updates cacheSize
        /// </summary>
        /// <param name="store">ContentHash</param>
        /// <param name="hash">ContentHash</param>
        /// <param name="metadata">Metadata</param>
        /// <param name="updateCacheSize">Fglag for whether or not to update cache size</param>
        private void PutContentMetadataInternal(
            RocksDbStore store,
            ContentHash hash,
            ContentMetadata metadata,
            bool updateCacheSize)
        {
            store.ApplyBatch((hash, metadata, updateCacheSize), static (writeBatch, data, getHandle) =>
            {
                writeBatch.Put(
                    MemoryMarshal.AsBytes(stackalloc[] { data.hash }),
                    MemoryMarshal.AsBytes(stackalloc[] { data.metadata }),
                    getHandle(RocksDbFileSystemContentStore.HashAndMetadataColumnFamily)!);

                // Update cache size
                if (data.updateCacheSize)
                {
                    writeBatch.Merge(
                        RocksDbFileSystemContentStore.CacheSizeKey.Span,
                        MemoryMarshal.AsBytes(stackalloc[] { data.metadata.Size }),
                        getHandle(RocksDbFileSystemContentStore.CacheSizeColumnFamily)!);
                }
            });
        }

        public void RemoveContentMetaData(ContentHash hash)
        {
            if (!TryGetContentMetadata(hash, out var metadata)) { return; }

            _ = _accessor.Use(store =>
            {
                store.ApplyBatch((hash, metadata), static (writeBatch, data, getHandle) =>
                {
                    writeBatch.Delete(
                         MemoryMarshal.AsBytes(stackalloc[] { data.hash }),
                         getHandle(RocksDbFileSystemContentStore.HashAndMetadataColumnFamily)!);

                    writeBatch.Merge(
                         RocksDbFileSystemContentStore.CacheSizeKey.Span,
                         MemoryMarshal.AsBytes(stackalloc[] { data.metadata.Size * -1 }),
                         getHandle(RocksDbFileSystemContentStore.CacheSizeColumnFamily)!);
                });
            });
        }

        /// <nodoc />
        public bool ShouldAttemptHardLink(
            AbsolutePath contentPath,
            FileAccessMode accessMode,
            FileRealizationMode realizationMode)
        {
            return contentPath.IsLocal && accessMode == FileAccessMode.ReadOnly &&
                   (realizationMode == FileRealizationMode.Any ||
                    realizationMode == FileRealizationMode.HardLink);
        }

        /// <summary>
        ///     Gets the path that points to the location of this content hash.
        /// </summary>
        /// <param name="contentHash">Content hash to get path for</param>
        /// <returns>Path for the hash</returns>
        /// <remarks>Does not guarantee anything is at the returned path</remarks>
        public AbsolutePath GetPath(ContentHash contentHash)
        {
            return _storePath / contentHash.HashType.Serialize() / new RelativePath(contentHash.ToHex().Substring(0, HashDirectoryNameLength)) / string.Format(CultureInfo.InvariantCulture, "{0}.{1}", contentHash.ToHex(), BlobNameExtension);
        }

        /// <summary>
        ///     Converts a stream to a byte array
        /// <returns>byte array</returns>
        /// </summary>
        public byte[] StreamToByteArray(Stream input)
        {
            using (var ms = new MemoryStream())
            {
                input.CopyTo(ms);
                return ms.ToArray();
            }
        }
    }
}
