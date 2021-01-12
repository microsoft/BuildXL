// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

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
}
