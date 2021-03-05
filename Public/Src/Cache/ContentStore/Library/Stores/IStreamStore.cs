// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <nodoc/>
    public interface IStreamStore
    {
        /// <summary>
        /// Writes content for a given hash to a stream.
        /// </summary>
        Task<OpenStreamResult> StreamContentAsync(Context context, ContentHash contentHash);
    }

    /// <nodoc/>
    public interface IDistributedStreamStore
    {
        /// <summary>
        /// Gets the content for the given hash. Optionally copying from a peer if available.
        /// </summary>
        Task<OpenStreamResult> OpenStreamAsync(OperationContext context, ContentHash contentHash);
    }
}
