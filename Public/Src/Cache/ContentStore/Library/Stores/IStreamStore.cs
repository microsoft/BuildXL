// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
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
