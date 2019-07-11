// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    ///     Interface to store that is able to store and retrieve content based on its content hash
    /// </summary>
    public interface IContentStoreInternal : IStartupShutdown
    {
        /// <summary>
        ///     Gets root directory path.
        /// </summary>
        AbsolutePath RootPath { get; }

        /// <summary>
        ///     Gets or sets the announcer to receive updates when content added and removed.
        /// </summary>
        IContentChangeAnnouncer Announcer { get; set; }

        /// <summary>
        ///     Pin existing content.
        /// </summary>
        /// <param name="context">
        ///     Tracing context.
        /// </param>
        /// <param name="contentHash">
        ///     Hash of the content to be pinned.
        /// </param>
        /// <param name="pinContext">
        ///     Context that will hold the pin record.
        /// </param>
        Task<PinResult> PinAsync(Context context, ContentHash contentHash, PinContext pinContext);

        /// <summary>
        ///     Pin existing content.
        /// </summary>
        /// <param name="context">
        ///     Tracing context.
        /// </param>
        /// <param name="contentHashes">
        ///     Collection of content hashes to be pinned.
        /// </param>
        /// <param name="pinContext">
        ///     Context that will hold the pin record.
        /// </param>
        Task<IEnumerable<Indexed<PinResult>>> PinAsync(Context context, IReadOnlyList<ContentHash> contentHashes, PinContext pinContext);

        /// <summary>
        /// Adds the content to the store without rehashing it.
        /// </summary>
        Task<PutResult> PutTrustedFileAsync(Context context, AbsolutePath path, FileRealizationMode realizationMode, ContentHashWithSize contentHash, PinRequest? pinContext = null);

        /// <summary>
        ///     Adds the content to the store.
        /// </summary>
        /// <param name="context">
        ///     Tracing context.
        /// </param>
        /// <param name="path">The path of the content file to add.</param>
        /// <param name="realizationMode">Which realization modes can the cache use to ingress the content.</param>
        /// <param name="hashType">Select hash type</param>
        /// <param name="pinRequest">
        ///     Optional set of parameters for requesting that content be pinned or asserting that content should already be
        ///     pinned.
        ///     Disposing a context after content has been pinned to it will remove the pin.
        /// </param>
        /// <returns>Hash of the content.</returns>
        Task<PutResult> PutFileAsync(
            Context context,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            HashType hashType,
            PinRequest? pinRequest = null);

        /// <summary>
        ///     Adds the content to the store, while providing an option to wrap the stream used while hashing with another stream.
        /// </summary>
        Task<PutResult> PutFileAsync(Context context, AbsolutePath path, HashType hashType, FileRealizationMode realizationMode, Func<Stream, Stream> wrapStream, PinRequest? pinRequest);

        /// <summary>
        /// Adds the content to the store without rehashing it. If hashing ends up being necessary, provides an option to wrap the stream used while hashing with another stream.
        /// </summary>
        Task<PutResult> PutFileAsync(Context context, AbsolutePath path, ContentHash contentHash, FileRealizationMode realizationMode, Func<Stream, Stream> wrapStream, PinRequest? pinRequest);

        /// <summary>
        ///     Adds the content to the store.
        /// </summary>
        /// <param name="context">
        ///     Tracing context.
        /// </param>
        /// <param name="path">The path of the content file to add.</param>
        /// <param name="realizationMode">Which realization modes can the cache use to ingress the content.</param>
        /// <param name="contentHash">
        ///     Optional hash of the content. If provided and the content with that hash is present in the cache,
        ///     a best effort will be made to avoid hashing the given content. Note that in this case the cache does not need to
        ///     verify a match
        ///     between the given content and hash.
        /// </param>
        /// <param name="pinRequest">
        ///     Optional set of parameters for requesting that content be pinned or asserting that content should already be
        ///     pinned.
        ///     Disposing a context after content has been pinned to it will remove the pin.
        /// </param>
        /// <returns>Hash of the content.</returns>
        Task<PutResult> PutFileAsync(
            Context context,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            ContentHash contentHash,
            PinRequest? pinRequest = null);

        /// <summary>
        ///     Adds the content to the store.
        /// </summary>
        /// <param name="context">
        ///     Tracing context.
        /// </param>
        /// <param name="stream">The path of the content file to add.</param>
        /// <param name="hashType">Select hash type</param>
        /// <param name="pinRequest">
        ///     Optional set of parameters for requesting that content be pinned or asserting that content should already be
        ///     pinned.
        ///     Disposing a context after content has been pinned to it will remove the pin.
        /// </param>
        /// <returns>Hash of the content.</returns>
        Task<PutResult> PutStreamAsync(Context context, Stream stream, HashType hashType, PinRequest? pinRequest = null);

        /// <summary>
        ///     Adds the content to the store.
        /// </summary>
        /// <param name="context">
        ///     Tracing context.
        /// </param>
        /// <param name="stream">The path of the content file to add.</param>
        /// <param name="contentHash">
        ///     If the content with that hash is present in the cache, a best effort will be made to avoid
        ///     hashing the given content. Note that in this case the cache does not need to verify a match
        ///     between the given content and hash.
        /// </param>
        /// <param name="pinRequest">
        ///     Optional set of parameters for requesting that content be pinned or asserting that content should already be
        ///     pinned.
        ///     Disposing a context after content has been pinned to it will remove the pin.
        /// </param>
        /// <returns>Hash of the content.</returns>
        Task<PutResult> PutStreamAsync(Context context, Stream stream, ContentHash contentHash, PinRequest? pinRequest = null);

        /// <summary>
        ///     Materializes the referenced content to a local path. Prefers hard link over copy.
        /// </summary>
        /// <param name="context">
        ///     Tracing context.
        /// </param>
        /// <param name="contentHash">Hash of the desired content</param>
        /// <param name="destinationPath">Destination path on the local system.</param>
        /// <param name="accessMode">
        ///     Indicates the expected usage of a file that is being deployed from a content store via
        ///     BuildCache.Interfaces.CacheWrapper{TContentBag}.GetContentAsync.
        ///     In some cases, a <see cref="FileAccessMode.ReadOnly" /> access level allows elision of local copies.
        ///     Implementations may set file ACLs
        ///     to enforce the requested access level.
        /// </param>
        /// <param name="replacementMode">
        ///     Specifies how to handle the existence of destination files in methods that expect to
        ///     create and write new files.
        /// </param>
        /// <param name="realizationMode">Specifies how to realize the content on disk.</param>
        /// <param name="pinRequest">
        ///     Optional set of parameters for requesting that content be pinned or asserting that content should already be
        ///     pinned.
        ///     Disposing a context after content has been pinned to it will remove the pin.
        /// </param>
        /// <returns>Value describing the outcome.</returns>
        /// <remarks>
        ///     May use symbolic links or hard links to achieve an equivalent effect.
        ///     If token's content hash doesn't match actual hash of the content, throws ContentHashMismatchException.
        ///     May throw other file system exceptions.
        /// </remarks>
        Task<PlaceFileResult> PlaceFileAsync(
            Context context,
            ContentHash contentHash,
            AbsolutePath destinationPath,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            PinRequest? pinRequest = null);

        /// <summary>
        ///     Bulk version of <see cref="PlaceFileAsync(Context,ContentHash,AbsolutePath,FileAccessMode,FileReplacementMode,FileRealizationMode,PinRequest?)"/>
        /// </summary>
        Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> PlaceFileAsync(
            Context context,
            IReadOnlyList<ContentHashWithPath> placeFileArgs,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            PinRequest? pinRequest = null);

        /// <summary>
        ///     Checks if the store contains content with the given hash.
        /// </summary>
        /// <param name="context">
        ///     Tracing context.
        /// </param>
        /// <param name="contentHash">Hash of the content to check for.</param>
        /// <param name="pinRequest">
        ///     Optional set of parameters for requesting that content be pinned or asserting that content should already be
        ///     pinned.
        ///     Disposing a context after content has been pinned to it will remove the pin.
        /// </param>
        /// <returns>True if the content is in the store; false if it is not.</returns>
        /// <remarks>
        ///     Although it makes no guarantees on retention of the file, if this returns true, then the LRU for the file was
        ///     updated as well.
        /// </remarks>
        Task<bool> ContainsAsync(Context context, ContentHash contentHash, PinRequest? pinRequest = null);

        /// <summary>
        ///     Get size of content and check if pinned.
        /// </summary>
        /// <param name="context">
        ///     Tracing context.
        /// </param>
        /// <param name="contentHash">Hash of the content to check for.</param>
        /// <param name="pinRequest">
        ///     Optional set of parameters for requesting that content be pinned or asserting that content should already be
        ///     pinned.
        ///     Disposing a context after content has been pinned to it will remove the pin.
        /// </param>
        /// <returns>Content size and if content is pinned or not.</returns>
        Task<GetContentSizeResult> GetContentSizeAndCheckPinnedAsync(Context context, ContentHash contentHash, PinRequest? pinRequest = null);

        /// <summary>
        ///     Opens a stream of the content with the given hash.
        /// </summary>
        /// <param name="context">Tracing context</param>
        /// <param name="contentHash">Hash of the content to open a stream of.</param>
        /// <param name="pinRequest">
        ///     Optional set of parameters for requesting that content be pinned or asserting that content should already be
        ///     pinned.
        ///     Disposing a context after content has been pinned to it will remove the pin.
        /// </param>
        /// <returns>A stream of the content.</returns>
        /// <remarks>The content will not be deleted from the store while the stream is open.</remarks>
        Task<OpenStreamResult> OpenStreamAsync(Context context, ContentHash contentHash, PinRequest? pinRequest = null);

        /// <summary>
        ///     Provides a disposable pin context which may be optionally provided to other functions in order to pin the relevant
        ///     content in the cache.
        ///     Disposing it will release all of the pins.
        /// </summary>
        /// <returns>A pin context.</returns>
        PinContext CreatePinContext();

        /// <summary>
        ///     Gets a current stats snapshot.
        /// </summary>
        Task<GetStatsResult> GetStatsAsync(Context context);

        /// <summary>
        ///     Validate the store integrity.
        /// </summary>
        Task<bool> Validate(Context context);

        /// <summary>
        ///     Returns list of content hashes in the order by which they should be LRU-ed.
        /// </summary>
        Task<IReadOnlyList<ContentHash>> GetLruOrderedContentListAsync();

        /// <summary>
        ///     Returns list of content hashes in the order by which they should be LRU-ed with their last access time.
        /// </summary>
        Task<IReadOnlyList<ContentHashWithLastAccessTimeAndReplicaCount>> GetLruOrderedContentListWithTimeAsync();

        /// <summary>
        ///     Purge specified content.
        /// </summary>
        Task<EvictResult> EvictAsync(Context context, ContentHashWithLastAccessTimeAndReplicaCount contentHashInfo, bool onlyUnlinked, Action<long> evicted);

        /// <summary>
        ///     Enumerate all content currently in the cache. Returns list of hashes and their respective size.
        /// </summary>
        Task<IReadOnlyList<ContentInfo>> EnumerateContentInfoAsync();

        /// <summary>
        ///     Enumerate all content currently in the cache.
        /// </summary>
        Task<IEnumerable<ContentHash>> EnumerateContentHashesAsync();

        /// <summary>
        ///     Complete all pending/background operations.
        /// </summary>
        Task SyncAsync(Context context, bool purge = true);

        /// <summary>
        ///     Read pin history.
        /// </summary>
        PinSizeHistory.ReadHistoryResult ReadPinSizeHistory(int windowSize);

        /// <summary>
        ///     Remove given content from the store.
        /// </summary>
        Task<DeleteResult> DeleteAsync(Context context, ContentHash contentHash);
    }
}
