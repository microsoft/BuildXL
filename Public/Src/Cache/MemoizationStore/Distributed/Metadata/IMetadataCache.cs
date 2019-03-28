// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.MemoizationStore.Distributed.Metadata.Tracing;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

namespace BuildXL.Cache.MemoizationStore.Distributed.Metadata
{
    /// <summary>
    /// A metadata cache
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         An interface for a simple proxy cache which can be used to cache metadata queries for any given ICache (primarily VSTS BuildCache).
    ///
    ///         The initial implementation <see cref="RedisMetadataCache"/> is backed by Redis cache because we're already use Redis for
    ///         content locations. The BuildCache service is RESTful, so it is also possible to implement this using a standard HTTP caching proxy.
    ///     </para>
    ///     <para>
    ///         Current interface is based on IConcurrentDictionary interface and assumes no guarantees about the metadata. Cache is populated
    ///         using results of the Get* metadata calls on the ICache interface and AddOrGet* calls invalidate the cache entries for given strong
    ///         and weak fingerprints.
    ///         Note: this is also the reason why method names here are GetOrAdd instead of AddOrGet like in other interfaces.
    ///     </para>
    ///     <para>
    ///         An alternate approach could be an Aggregator model, e.g. calls to GetContentHashList populate the selectors alongwith ContentHashList
    ///         and calls to AddOrGetContentHashList only remove the specific ContentHashList and let the corresponding selector age out.
    ///         This model is better in terms of reducing GetSelector calls once the cache is populated since we don't remove all Selectors
    ///         for given WeakFingerprint. But this requires lifecycle management for individual Selector entries which has a negative perf
    ///         impact in the Redis implementation and hence was avoided.
    ///     </para>
    /// </remarks>
    public interface IMetadataCache : IStartupShutdown
    {
        /// <summary>
        /// Returns cached copy of selectors associated with the weak fingerprint or adds if not found
        /// </summary>
        /// <param name="context">
        /// Tracing context.
        /// </param>
        /// <param name="weakFingerprint">
        /// Weak fingerprint key for multiple selector values.
        /// </param>
        /// <param name="getFunc">
        /// Function used to get the selectors to populate the cache on a miss for given fingerprint
        /// </param>
        /// <returns>
        ///     Result providing the call's completion status.
        /// </returns>
        Task<Result<Selector[]>> GetOrAddSelectorsAsync(Context context, Fingerprint weakFingerprint, Func<Fingerprint, Task<Result<Selector[]>>> getFunc);

        /// <summary>
        /// Returns cached copy of content hashlist associated with the strong fingerprint or adds if not found
        /// </summary>
        /// <param name="context">
        /// Tracing context.
        /// </param>
        /// <param name="strongFingerprint">
        /// Full key for ContentHashList value.
        /// </param>
        /// <param name="getFuncAsync">
        /// Function used to get the content hash list to populate the cache on a miss for given fingerprint
        /// </param>
        /// <returns>
        ///     Result providing the call's completion status.
        /// </returns>
        Task<GetContentHashListResult> GetOrAddContentHashListAsync(Context context, StrongFingerprint strongFingerprint, Func<StrongFingerprint, Task<GetContentHashListResult>> getFuncAsync);

        /// <summary>
        /// Deletes cached entries for both strong and weak fingerprint
        /// </summary>
        /// <param name="context">
        /// Tracing context.
        /// </param>
        /// <param name="strongFingerprint">
        /// Full key for ContentHashList and list of Selectors to be deleted
        /// </param>
        Task<BoolResult> DeleteFingerprintAsync(Context context, StrongFingerprint strongFingerprint);
    }
}
