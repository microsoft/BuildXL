// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Engine.Cache.Artifacts
{
    /// <summary>
    /// An artifact content cache stores file artifact content, addressable by hash. Executing pips requires an artifact content cache
    /// (even if the cache happens to exist only for the duration of that build); for example, a 'CopyFile' pip is expected to look up the input hash
    /// and call <see cref="TryMaterializeAsync"/> rather than actually doing file-copy work itself.
    ///
    /// An artifact content cache is responsible for materializing content (cache -> disk), storing content (disk -> cache), and establishing the availability
    /// of content (cache query). Content that has not been explicitly stored or established as available on a particular instance may not be successfully materializable.
    /// However, content that has been explicitly stored or established as available *should* be successfully materialiable - i.e., content is not subject to eviction
    /// or accidental disappearance once seen.
    ///
    /// This property allows a useful invariant in executing pips: As a post-condition of a 'Done' pip, its outputs have been 'seen' by the cache and so can be assumed
    /// present in the build's content cache by subsequent pips (recall the supposed implementation of 'CopyFile').
    ///
    /// Note that a content cache is NOT responsible for especially clever interactions with input / output paths. In particular:
    /// - A cache should not try to memoize hashes of paths on disk (e.g. with a <see cref="BuildXL.Storage.FileContentTable"/>)
    /// - A cache should not try to elide materialization operations if the destination is up to date.
    /// - A cache should not track changes to paths on disk
    /// Those responsibilities are instead allocated to a caller, such as <see cref="LocalDiskContentStore"/> (which does provide those three behaviors).
    /// </summary>
    public interface IArtifactContentCache
    {
        /// <summary>
        /// Establishes availability of a list of hashes, in batch.
        /// - If a hash has been previously stored or established available in this build, this query will indicate it as still available.
        /// - Other hashes not yet obtained *may* be available. This may be the case for implementations which store content durably, build over build,
        ///   and for implementations which have some remote backing store ('L2').
        /// This is the only operation that is permitted to retrieve content from remote locations, and so is the most expensive.
        ///
        /// Note that this operation succeeds with a <see cref="ContentAvailabilityBatchResult"/> even if some or all of the content was unavailable.
        /// Each result item should be checked for <see cref="ContentAvailabilityResult.IsAvailable"/>. For any other failure - such as a local or network I/O
        /// issue - an implementation may choose to return a <see cref="BuildXL.Utilities.Failure"/> instead.
        ///
        /// The returned batch result will contain an entry for all requested hashes, in the same order as <paramref name="hashes"/>
        /// (for each <c>i</c>, <c>hashes[i] == batchResult.Results[i].Hash</c>).
        /// </summary>
        Task<Possible<ContentAvailabilityBatchResult, Failure>> TryLoadAvailableContentAsync(IReadOnlyList<ContentHash> hashes);

        /// <summary>
        /// Attempts to open a read-only stream for content. The content should have previously been stored or loaded.
        /// The caller is responsible for closing the returned stream, if any.
        /// </summary>
        Task<Possible<Stream, Failure>> TryOpenContentStreamAsync(ContentHash contentHash);

        /// <summary>
        /// Attempts to place content at the specified path. The content should have previously been stored or loaded.
        /// If not, this operation may fail. This operation may also fail due to I/O-related issues, such as lack of permissions to the destination.
        /// Note that the containing directory for <paramref name="path"/> is assumed to be created already.
        /// An implementation MUST delete and replace existing files rather than overwriting them.
        /// </summary>
        Task<Possible<Unit, Failure>> TryMaterializeAsync(
            FileRealizationMode fileRealizationModes,
            ExpandedAbsolutePath path,
            ContentHash contentHash);

        /// <summary>
        /// Attempts to store content as found at the specified path. This may be a no-op if the cache already has the specified content, in
        /// which case an implementation may choose to not query the destination path at all.
        /// This operation may fail due to I/O-related issues, such as lack of permissions to the destination.
        /// </summary>
        Task<Possible<Unit, Failure>> TryStoreAsync(
            FileRealizationMode fileRealizationModes,
            ExpandedAbsolutePath path,
            ContentHash contentHash);

        /// <summary>
        /// Attempts to store content as found at the specified path. The specified path is always opened to compute its hash, which is returned on successful store.
        /// This operation may fail due to I/O-related issues, such as lack of permissions to the destination.
        /// </summary>
        Task<Possible<ContentHash, Failure>> TryStoreAsync(
            FileRealizationMode fileRealizationModes,
            ExpandedAbsolutePath path);

        /// <summary>
        /// Attempts to store content from the given stream. This may be a no-op if the cache already has the specified content, in
        /// which case an implementation may choose to not read the provided stream.
        /// This operation may fail due to I/O failures in reading the stream.
        /// </summary>
        Task<Possible<Unit, Failure>> TryStoreAsync(
            Stream content,
            ContentHash contentHash);
    }
}
