// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Native.IO;
using BuildXL.Storage.Tracing;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Tracing;
using static BuildXL.Utilities.Core.FormattableStringEx;

#nullable enable

namespace BuildXL.Storage
{
    /// <summary>
    /// Functions for hashing streams and buffers with the selected hash function (currently defaults to VSO hash).
    /// </summary>
    public static class ContentHashingUtilities
    {
#if NETCOREAPP
        /// <summary>
        /// Default threshold for asynchronous file hashing with memory mapped files.
        /// </summary>
        /// <remarks>
        /// Consider using different thresholds for sdd and hdd drives.
        /// </remarks>
        public const int DefaultAsyncMemoryMappedFileHashingThreshold = 10 * 1024 * 1024;
        private static bool s_useMemoryMappedFileHashing;
        private static int s_asyncMemoryMappedFileHashingThreshold = DefaultAsyncMemoryMappedFileHashingThreshold;
#endif // NETCOREAPP
        private static bool s_isInitialized;
        private static HashInfo? s_hashingAlgorithm;
        private static IContentHasher? s_contentHasher;
        private static ContentHash s_zeroHash;

        /// <summary>
        /// HashTypes used in SBOMs
        /// </summary>
        public static readonly HashType[] BuildManifestHashTypes = new[] { HashType.SHA256, HashType.SHA1 };

        private static ConcurrentDictionary<HashType, IContentHasher> s_contentHasherByHashType = new ConcurrentDictionary<HashType, IContentHasher>();

        /// <summary>
        /// Returns the <see cref="IContentHasher"/> for a given <see cref="HashType"/>
        /// </summary>
        public static IContentHasher GetContentHasher(HashType hashType)
        {
            if (hashType == HashInfo.HashType || hashType == HashType.Unknown)
            {
                return s_hasher;
            }

            return s_contentHasherByHashType.GetOrAdd(hashType, static ht => HashInfoLookup.Find(ht).CreateContentHasher());
        }

        /// <summary>
        /// Selected content hash information.
        /// </summary>
        public static HashInfo HashInfo
        {
            get
            {
                Contract.Assert(s_isInitialized, "HashInfo was not initialized");
                return s_hashingAlgorithm!;
            }
            set
            {
                s_hashingAlgorithm = value;
                DisposeAndResetHasher();
                s_zeroHash = CreateSpecialValue(0);
            }
        }

        /// <summary>
        /// Hash of no content (array of zero bytes)
        /// </summary>
        public static ContentHash EmptyHash
        {
            get
            {
                Contract.Assert(s_isInitialized, "HashInfo was not initialized");
                return HashInfo.EmptyHash;
            }
        }

        /// <summary>
        /// Hash value of all zeros to use instead of default(Hash), ends with algorithm ID.
        /// </summary>
        public static ContentHash ZeroHash
        {
            get
            {
                Contract.Assert(s_isInitialized, "HashInfo was not initialized");
                return s_zeroHash;
            }
            set => s_zeroHash = value;
        }

        /// <summary>
        /// Hasher used internally.
        /// TODO: This is IDisposable but is used so prevalently, leaving the IDisposable dangling is better than
        /// recreating instances all over the place.
        /// </summary>
        private static IContentHasher s_hasher
        {
            get
            {
                Contract.Assert(s_isInitialized, "HashInfo was not initialized");
                return s_contentHasher!;
            }
            set => s_contentHasher = value;
        }

        /// <summary>
        /// Disposes the hasher to save on memory when the server mode process runs
        /// </summary>
        public static void DisposeAndResetHasher()
        {
            s_hasher?.Dispose();
            s_hasher = HashInfo.CreateContentHasher();
            
            foreach (var hasher in s_contentHasherByHashType.Values)
            {
                hasher.Dispose();
            }
            s_contentHasherByHashType = new ConcurrentDictionary<HashType, IContentHasher>();
        }

        /// <summary>
        /// Clients may switch between hashing algorithms. Must be set at the beginning of the build.
        /// </summary>
        public static void SetDefaultHashType(bool force = false)
        {
            if (!s_isInitialized || force)
            {
                SetDefaultHashType(HashType.Vso0);
            }
        }

#if NETCOREAPP
        /// <summary>
        /// Enable the file hashing based on memory-mapped files instead of hashing the file by reading the pieces of them into memory.
        /// </summary>
        /// <remarks>
        /// The memory-mapped-based hashing is only available for .net core because the required API was not ported to full framework.
        /// </remarks>
        public static void EnableMemoryMappedBasedFileHashing(int asyncFileHashingThreshold = DefaultAsyncMemoryMappedFileHashingThreshold)
        {
            s_useMemoryMappedFileHashing = true;
            s_asyncMemoryMappedFileHashingThreshold = asyncFileHashingThreshold;
        }

        /// <summary>
        /// Sets the old (legacy) file hashing.
        /// </summary>
        public static void DisableMemoryMappedBasedFileHashing()
        {
            s_useMemoryMappedFileHashing = false;
        }
#endif // NETCOREAPP

        /// <summary>
        /// Clients may switch between hashing algorithms. Must be set at the beginning of the build.
        /// </summary>
        public static void SetDefaultHashType(HashType hashType)
        {
            s_isInitialized = true;
            HashInfo = HashInfoLookup.Find(hashType);

            if (hashType.IsValidDedup())
            {
                if (Chunker.IsComChunkerSupported)
                {
                    if (Chunker.ComChunkerLoadError.Value != null)
                    {
                        Logger.Log.ComChunkerFailulre(Events.StaticContext, Chunker.ComChunkerLoadError.Value.ToString());
                    }
                    else
                    {
                        Logger.Log.ChunkerType(Events.StaticContext, nameof(ComChunker));
                    }
                }
                else
                {
                    Logger.Log.ChunkerType(Events.StaticContext, nameof(ManagedChunker));
                }
            }
        }

        /// <summary>
        /// Sets the ContentHasher idle pool size
        /// </summary>
        public static void SetContentHasherIdlePoolSize(int idlePoolSize)
        {
            Contract.Requires(!s_isInitialized, "ContentHasher idle pool size must be specified before setting the default hash type");
            HashInfoLookup.ContentHasherIdlePoolSize = idlePoolSize;
        }

        /// <summary>
        /// Create a ContentHash with all zeros except the given value in the first byte and the algorithm ID in the last byte.
        /// </summary>
        public static ContentHash CreateSpecialValue(byte first)
        {
            Contract.Requires(first >= 0);

            var hashBytes = new byte[HashInfo.ByteLength];
            hashBytes[0] = first;

            if (HashInfo is TaggedHashInfo taggedHashInfo)
            {
                hashBytes[HashInfo.ByteLength - 1] = taggedHashInfo.AlgorithmId;
            }

            return new ContentHash(HashInfo.HashType, hashBytes);
        }

        /// <summary>
        /// Gets whether the hash is a special hash (i.e., all zeros except first and last byte). These hashes
        /// are assumed to not represent real content (unless a counter-example can be found).
        /// See BuildXL.Scheduler.WellKnownContentHashes
        /// </summary>
        public static bool IsSpecialValue(in this ContentHash hash)
        {
            for (int i = 1; i < hash.ByteLength - 1; i++)
            {
                if (hash[i] != 0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Gets whether the hash is a special hash (i.e., all zeros except first and last byte),
        /// additionally checking the value of the first byte. These hashes are assumed to not
        /// represent real content (unless a counter-example can be found).
        /// See BuildXL.Scheduler.WellKnownContentHashes
        /// </summary>
        public static bool IsSpecialValue(in this ContentHash hash, byte first)
        {
            return hash[0] == first && hash.IsSpecialValue();
        }

        /// <summary>
        /// Create a ContentHash from the given bytes, which must match exactly with the select type.
        /// </summary>
        public static ContentHash CreateFrom(byte[] value, int offset = 0)
        {
            return new ContentHash(HashInfo.HashType, value, offset);
        }

        /// <summary>
        /// Create a ContentHash from the given bytes, which must match exactly with the select type.
        /// </summary>
        public static ContentHash FromSpan(ReadOnlySpan<byte> value)
        {
            return new ContentHash(HashInfo.HashType, value);
        }

        /// <summary>
        /// Create a ContentHash from the given bytes for a given HashType.
        /// </summary>
        public static ContentHash CreateFrom(byte[] value, HashType hashType)
        {
            return new ContentHash(hashType, value);
        }

        /// <summary>
        /// Create a ContentHash from bytes read from the given reader.
        /// </summary>
        public static ContentHash CreateFrom(BinaryReader reader)
        {
            return new ContentHash(HashInfo.HashType, reader);
        }

        /// <summary>
        /// Create a ContentHash from a <see cref="MurmurHash3"/>.
        /// </summary>
        public static unsafe ContentHash CreateFrom(in MurmurHash3 hash)
        {
            var bufferSize = EmptyHash.ByteLength;
            var buffer = new byte[bufferSize];
            fixed(byte* b = buffer)
            {
                *((ulong*)b) = hash.High;
                *((ulong*)b + 1) = hash.Low;
            }

            return new ContentHash(HashInfo.HashType, buffer);
        }

        /// <summary>
        /// Create a random ContentHash value.
        /// </summary>
        public static ContentHash CreateRandom()
        {
            return ContentHash.Random(HashInfo.HashType);
        }

        /// <summary>
        /// Try to parse a string as currently selected hash bytes.
        /// </summary>
        public static bool TryParse(string serialized, out ContentHash contentHash)
        {
            return ContentHash.TryParse(HashInfo.HashType, serialized, out contentHash);
        }

        /// <summary>
        /// Returns a <see cref="ContentHash" /> of the given stream.
        /// </summary>
        /// <remarks>
        /// The stream is read from its current location until the end. The stream is not closed or disposed upon completion.
        /// </remarks>
        public static ContentHash HashContentStream(StreamWithLength content)
        {
            // Not passing a hash type to the next method will force using s_hasher.
            return HashContentStreamAsync(content).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Returns a <see cref="ContentHash" /> of the given stream.
        /// </summary>
        /// <remarks>
        /// The stream is read from its current location until the end. The stream is not closed or disposed upon completion.
        /// </remarks>
        public static async Task<ContentHash> HashContentStreamAsync(StreamWithLength content, HashType hashType = HashType.Unknown)
        {
            var result = await ExceptionUtilities.HandleRecoverableIOException(
                () => HashContentStreamCoreAsync(content, hashType),
                ex => throw new BuildXLException(I($"Cannot read from stream '{content}'"), ex));
            Contract.Assert(result.HashType != HashType.Unknown);
            return result;
        }

        private static Task<ContentHash> HashContentStreamCoreAsync(StreamWithLength content, HashType hashType)
        {
            hashType = GetKnownHashType(hashType);

#if NETCOREAPP
            if (s_useMemoryMappedFileHashing && content.Stream is FileStream fileStream)
            {
                // Hashing synchronously for small files.
                if (fileStream.Length >= s_asyncMemoryMappedFileHashingThreshold)
                {
                    return Task.Run(() => fileStream.HashFile(hashType));
                }

                return Task.FromResult(fileStream.HashFile(hashType));
            }
#endif // NETCOREAPP

            return GetContentHasher(hashType).GetContentHashAsync(content);
        }

        /// <summary>
        /// Returns a <see cref="ContentHash" /> of the file at the given absolute path.
        /// </summary>
        public static async Task<ContentHash> HashFileAsync(string absoluteFilePath, HashType hashType = HashType.Unknown)
        {
            Contract.Requires(Path.IsPathRooted(absoluteFilePath), "File path must be absolute");
            hashType = GetKnownHashType(hashType);

#if NETCOREAPP
            if (s_useMemoryMappedFileHashing)
            {
                return ContentHashingHelper.HashFile(absoluteFilePath, hashType);
            }
#endif // NETCOREAPP

            // TODO: Specify a small buffer size here (see HashFileAsync(SafeFileHandle))
            using (var fileStream = FileUtilities.CreateAsyncFileStream(absoluteFilePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                return await HashContentStreamAsync(fileStream, hashType).ConfigureAwait(false);
            }
        }
        
        /// <summary>
        /// Returns a <see cref="ContentHash" /> of the file at the given absolute path.
        /// </summary>
        public static ContentHash HashFile(string absoluteFilePath, HashType hashType = HashType.Unknown)
        {
            Contract.Requires(Path.IsPathRooted(absoluteFilePath), "File path must be absolute");
            hashType = GetKnownHashType(hashType);

#if NETCOREAPP
            if (s_useMemoryMappedFileHashing)
            {
                return ContentHashingHelper.HashFile(absoluteFilePath, hashType);
            }
#endif // NETCOREAPP

            return HashFileAsync(absoluteFilePath, hashType).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Returns a <see cref="ContentHash" /> of the given byte array.
        /// </summary>
        public static ContentHash HashBytes(byte[] content)
        {
            var result = s_hasher.GetContentHash(content);
            Contract.Assert(result.HashType != HashType.Unknown);
            return result;
        }

        /// <summary>
        /// Returns a <see cref="ContentHash" /> of the given byte array.
        /// </summary>
        public static ContentHash HashBytes(byte[] content, int offset, int count)
        {
            var result = s_hasher.GetContentHash(content, offset, count);
            Contract.Assert(result.HashType != HashType.Unknown);
            return result;
        }

        /// <summary>
        /// Returns a <see cref="ContentHash" /> of the given string.
        /// </summary>
        public static ContentHash HashString(string content)
        {
            return HashBytes(Encoding.UTF8.GetBytes(content));
        }

        /// <summary>
        /// Combines two content hashes by XOR-ing their bytes. This creates a combined hash in an order-independent manner
        /// (combining a sequence of hashes yields a particular hash, regardless of permutation).
        /// Note that combining a content hash with itself gives a null (all-zero) hash; consequently, care may be needed
        /// to ensure that a sequence being hashed in this manner does not contain duplicates.
        /// </summary>
        public static ContentHash CombineOrderIndependent(ContentHash left, ContentHash right)
        {
            Contract.Requires(left.HashType == right.HashType);
            Contract.Requires(left.HashType != HashType.Unknown);

            var leftBytes = left.ToHashByteArray();
            var rightBytes = right.ToHashByteArray();

            for (var i = 0; i < leftBytes.Length; i++)
            {
                leftBytes[i] ^= rightBytes[i];
            }

            return new ContentHash(left.HashType, leftBytes);
        }

        private static HashType GetKnownHashType(HashType hashType)
        {
            return hashType != HashType.Unknown ? hashType : s_hasher.Info.HashType;
        }
    }
}
