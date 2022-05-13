// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;

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
            OperationContext context,
            string name,
            string cacheName,
            ImplicitPin implicitPin);

        /// <summary>
        /// Ensure content does not get deleted.
        /// </summary>
        Task<PinResult> PinAsync(OperationContext context, ContentHash contentHash, UrgencyHint urgencyHint = UrgencyHint.Nominal);

        /// <summary>
        /// Bulk pins content in the CAS.
        /// </summary>
        Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(OperationContext context, IReadOnlyList<ContentHash> contentHashes, UrgencyHint urgencyHint = UrgencyHint.Nominal);

        /// <summary>
        /// Get content stream
        /// </summary>
        Task<OpenStreamResult> OpenStreamAsync(OperationContext context, ContentHash contentHash, UrgencyHint urgencyHint = UrgencyHint.Nominal);

        /// <summary>
        /// Materialize content to the filesystem.
        /// </summary>
        Task<PlaceFileResult> PlaceFileAsync
        (
            OperationContext context,
            ContentHash contentHash,
            AbsolutePath path,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            UrgencyHint urgencyHint = UrgencyHint.Nominal
        );

        /// <summary>
        /// Add content from a stream.
        /// </summary>
        Task<PutResult> PutStreamAsync(OperationContext context, HashType hashType, Stream stream, bool createDirectory, UrgencyHint urgencyHint = UrgencyHint.Nominal);

        /// <summary>
        /// Add content from a stream.
        /// </summary>
        Task<PutResult> PutStreamAsync(OperationContext context, ContentHash contentHash, Stream stream, bool createDirectory, UrgencyHint urgencyHint = UrgencyHint.Nominal);

        /// <summary>
        /// Add content from a file.
        /// </summary>
        Task<PutResult> PutFileAsync(OperationContext context, HashType hashType, AbsolutePath path, FileRealizationMode realizationMode, UrgencyHint urgencyHint = UrgencyHint.Nominal);

        /// <summary>
        /// Add content from a file.
        /// </summary>
        Task<PutResult> PutFileAsync(
            OperationContext context,
            ContentHash contentHash,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            UrgencyHint urgencyHint = UrgencyHint.Nominal);

        /// <summary>
        /// Remove given content from all sessions.
        /// </summary>
        Task<DeleteResult> DeleteContentAsync(OperationContext context, ContentHash hash, bool deleteLocalOnly);
    }
}
