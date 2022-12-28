// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Engine.Cache.KeyValueStores;
using BuildXL.Utilities;
using RocksDbSharp;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

namespace BuildXL.Cache.ContentStore.Stores
{

    /// <summary>
    ///     An <see cref="IContentStore"/> implemented over <see cref="FileSystemContentStoreInternal"/>
    /// </summary>
    public class RocksDbFileSystemContentStore : StartupShutdownBase, IContentStore
    {
        protected override Tracer Tracer => _tracer;

        private readonly ContentStoreTracer _tracer = new ContentStoreTracer(nameof(RocksDbFileSystemContentStore));

        /// <summary>
        ///     LockSet used to ensure thread safety on write operations.
        /// </summary>
        private readonly LockSet<ContentHash> _lockSet = new LockSet<ContentHash>();

        private readonly IAbsFileSystem _fileSystem;

        private readonly IClock _clock;

        /// <summary>
        ///     Gets root directory path.
        /// </summary>
        private readonly AbsolutePath _rootPath;

        /// <summary>
        /// Path: _rootPath / Constants.SharedDirectoryName. Where content will be stored
        /// </summary>
        private readonly AbsolutePath _storePath;

        /// <summary>
        /// Path: _storePath / temp. Used to help write content to _storePath
        /// </summary>
        private DisposableDirectory? _tempDisposableDirectory;

        /// <summary>
        /// Manages and provides access to RocksDB. Throws if null.
        /// </summary>
        private KeyValueStoreAccessor? _accessor;

        internal static readonly ReadOnlyMemory<byte> CacheSizeKey = Encoding.ASCII.GetBytes("cacheSize");

        internal static readonly string CacheSizeColumnFamily = "cacheSizeColumnFamily";

        internal static readonly string HashAndMetadataColumnFamily = "hashAndMetadataColumnFamily";

        /// <nodoc />
        public RocksDbFileSystemContentStore(
            IAbsFileSystem fileSystem,
            IClock clock,
            AbsolutePath rootPath)
        {
            Contract.Requires(fileSystem != null);
            Contract.Requires(clock != null);
            Contract.Requires(rootPath != null);

            _fileSystem = fileSystem;
            _clock = clock;
            _rootPath = rootPath;
            _storePath = rootPath / Constants.SharedDirectoryName;
        }

        /// <inheritdoc />
        protected override Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            InitializeRocksDbStore(_storePath.Path);
            Directory.CreateDirectory(_storePath.Path);
            AbsolutePath tempDirectory = _storePath / "temp";

            if (_fileSystem.DirectoryExists(tempDirectory))
            {
                _fileSystem.DeleteDirectory(tempDirectory, DeleteOptions.Recurse);
            }

            _tempDisposableDirectory = new DisposableDirectory(_fileSystem, tempDirectory);

            IEnumerable<string> hashPrefixes = GetAllThreeLetterHexPrefixes();
            foreach (var hashPrefix in hashPrefixes)
            {
                string directoryPath = $"{_storePath.Path}\\{HashType.Vso0}\\{hashPrefix}";;
                Directory.CreateDirectory(directoryPath); ;
            }
            return base.StartupCoreAsync(context);
        }

        /// <inheritdoc />
        protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            if (_accessor != null) { _accessor.Dispose(); }
            if (_tempDisposableDirectory != null) { _tempDisposableDirectory.Dispose(); }
            return base.ShutdownCoreAsync(context);
        }

        /// <inheritdoc />
        public CreateSessionResult<IReadOnlyContentSession> CreateReadOnlySession(Context context, string name, ImplicitPin implicitPin)
        {
            return CreateReadOnlySessionCall.Run(_tracer, OperationContext(context), name, () =>
            {
#pragma warning disable CS8604 // Possible null reference argument.
                var session = new RocksDbFileSystemContentSession(name, _lockSet, _fileSystem, _clock, _rootPath, _storePath, _tempDisposableDirectory, _accessor);
#pragma warning restore CS8604 // Possible null reference argument.
                return new CreateSessionResult<IReadOnlyContentSession>(session);
            });
        }

        /// <inheritdoc />
        public CreateSessionResult<IContentSession> CreateSession(Context context, string name, ImplicitPin implicitPin)
        {
            return CreateSessionCall.Run(_tracer, OperationContext(context), name, () =>
            {
#pragma warning disable CS8604 // Possible null reference argument.
                var session = new RocksDbFileSystemContentSession(name, _lockSet, _fileSystem, _clock, _rootPath, _storePath, _tempDisposableDirectory, _accessor);
#pragma warning restore CS8604 // Possible null reference argument.
                return new CreateSessionResult<IContentSession>(session);
            });
        }

        /// <inheritdoc />
        public Task<DeleteResult> DeleteAsync(Context context, ContentHash contentHash, DeleteContentOptions? deleteOptions)
        {
            return Task.FromResult(new DeleteResult(DeleteResult.ResultCode.ContentNotDeleted, contentHash, -1));
        }

        /// <inheritdoc />
        public Task<GetStatsResult> GetStatsAsync(Context context)
        {
            return Task.FromResult(new GetStatsResult(errorMessage: $"{nameof(RocksDbFileSystemContentStore)} does not support {nameof(GetStatsAsync)}"));
        }

        /// <inheritdoc />
        public void PostInitializationCompleted(Context context, BoolResult result)
        {
            // Unused on purpose
        }

        /// <summary>
        ///     Opens RocksDB OR creates RocksDB with cacheSizeColumnFamily, hashAndMetaDataColumnFamily and inits cacheSize to 0
        ///     Sets _accessor, throws if _accessor is nul>
        /// </summary>
        private void InitializeRocksDbStore(string storePath)
        {
            Possible<KeyValueStoreAccessor> possibleAccessor = KeyValueStoreAccessor.Open(
                new RocksDbStoreConfiguration(storePath)
                {
                    AdditionalColumns = new[] { CacheSizeColumnFamily, HashAndMetadataColumnFamily },
                    MergeOperators = GetMergeOperators(),
                });

            KeyValueStoreAccessor? accessor = possibleAccessor.Succeeded ? possibleAccessor.Result : null;
            if (accessor is null)
            {
                throw new Exception("Could not open a KeyValueStoreAccessor");
            }
            _accessor = accessor;

            _ = _accessor.Use(store =>
            {
                if (!store.TryGetValue(CacheSizeKey.Span, out var contentValue, CacheSizeColumnFamily))
                {
                    Span<byte> size = stackalloc byte[sizeof(long)];
                    SerializeInt64(0, size);
                    store.Put(CacheSizeKey.Span, size, CacheSizeColumnFamily);
                }
            });
        }

        /// <summary>
        ///     Creates the merge operator for the CacheSizeColumnFamily
        /// </summary>
        /// <param name="value">Byte representation of Int64</param>
        /// <returns>Int64</returns>
        private Dictionary<string, MergeOperator> GetMergeOperators()
        {
            return new Dictionary<string, MergeOperator>() {
                {
                    CacheSizeColumnFamily,
                    MergeOperators.CreateAssociative(
                        "MergeContent",
                        merge: MergeCacheSizes,
                        transformSingle: MergeSingleCacheSize)
                },
            };
        }

        /// <summary>
        ///     Merge algorithm for single cache size
        /// </summary>
        /// <param name="key">Cache Size Key</param>
        /// <param name="value">A Number</param>
        /// <param name="result">value + default</param>
        private bool MergeSingleCacheSize(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, MergeResult result)
        {
            Span<byte> serializedZero = stackalloc byte[sizeof(long)];
            SerializeInt64(0, serializedZero);
            return MergeCacheSizes(key, value, serializedZero, result);
        }

        /// <summary>
        ///     Merge algorithm for cache sizes
        /// </summary>
        /// <param name="key">Cache Size Key</param>
        /// <param name="value1">Number 1</param>
        /// <param name="value2">Number 2</param>
        /// <param name="result">value1 + value2</param>
        private bool MergeCacheSizes(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value1, ReadOnlySpan<byte> value2, MergeResult result)
        {
            var minSize = sizeof(long);
            if (result.ValueBuffer.Value.Length < minSize)
            {
                result.ValueBuffer.Resize(minSize);
            }

            var lhs = DeserializeInt64(value1);
            var rhs = DeserializeInt64(value2);
            var sum = lhs + rhs;
            SerializeInt64(sum, result.ValueBuffer.Value);

            return true;
        }

        /// <summary>
        ///     Finds and returns all hex permutations of length 3
        /// <returns>IEnumerable<string> of hex permuations></returns>
        /// </summary>
        public static IEnumerable<string> GetAllThreeLetterHexPrefixes()
        {
            string alphabet = "0123456789ABCDEF";
            IEnumerable<string> hashPrefixes = alphabet.Select(x => x.ToString());
            int size = 3;
            for (int i = 0; i < size - 1; i++)
            {
                hashPrefixes = hashPrefixes.SelectMany(x => alphabet, (x, y) => x + y);
            }

            return hashPrefixes;
        }

        /// <summary>
        ///     Converts a ReadOnlySpan<byte> to Int64
        /// </summary>
        /// <param name="value">Byte representation of Int64</param>
        /// <returns>Int64</returns>
        public static long DeserializeInt64(ReadOnlySpan<byte> value)
        {     
            long output = 0;

            for (var i = 0; i < Math.Min(value.Length, sizeof(long)); i++)
            {
                unchecked
                {
                    output |= ((long)value[i]) << (8 * i);
                }
            }
            return output;
        }

        /// <summary>
        ///     Converts a Int64 to ReadOnlySpan<byte> representation
        /// </summary>
        /// <param name="value">Byte representation of Int64</param>
        public static void SerializeInt64(long value, Span<byte> output)
        {
            Contract.Requires(output.Length >= sizeof(long), "Could not SerializeInt64. Output length must be >= sizeof(long)");

            for (var i = 0; i < sizeof(long); i++)
            {
                output[i] = (byte)(value & 0xFF);
                value = value >> 8;
            }
        }
    }
}
