// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace BuildXL.Cache.ContentStore.Service
{
    /// <summary>
    /// RPC Client abstraction for service client.
    /// </summary>
    public interface IRpcClient : IShutdown<BoolResult>
    {
        /// <summary>
        /// Create and apply new session to this connection group.
        /// </summary>
        Task<BoolResult> CreateSessionAsync(
            Context context,
            string name,
            string cacheName,
            ImplicitPin implicitPin);

        /// <summary>
        /// Ensure content does not get deleted.
        /// </summary>
        Task<PinResult> PinAsync(Context context, ContentHash contentHash);

        /// <summary>
        /// Bulk pins content in the CAS.
        /// </summary>
        Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(Context context, IReadOnlyList<ContentHash> contentHashes);

        /// <summary>
        /// Get content stream
        /// </summary>
        Task<OpenStreamResult> OpenStreamAsync(Context context, ContentHash contentHash);

        /// <summary>
        /// Materialize content to the filesystem.
        /// </summary>
        Task<PlaceFileResult> PlaceFileAsync
        (
            Context context,
            ContentHash contentHash,
            AbsolutePath path,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode
        );

        /// <summary>
        /// Add content from a stream.
        /// </summary>
        Task<PutResult> PutStreamAsync(Context context, HashType hashType, Stream stream);

        /// <summary>
        /// Add content from a stream.
        /// </summary>
        Task<PutResult> PutStreamAsync(Context context, ContentHash contentHash, Stream stream);

        /// <summary>
        /// Add content from a file.
        /// </summary>
        Task<PutResult> PutFileAsync(Context context, HashType hashType, AbsolutePath path, FileRealizationMode realizationMode);

        /// <summary>
        /// Add content from a file.
        /// </summary>
        Task<PutResult> PutFileAsync(
            Context context,
            ContentHash contentHash,
            AbsolutePath path,
            FileRealizationMode realizationMode);

        /// <summary>
        /// Remove given content from all sessions.
        /// </summary>
        Task<DeleteResult> DeleteContentAsync(Context context, ContentHash hash);
    }
}
