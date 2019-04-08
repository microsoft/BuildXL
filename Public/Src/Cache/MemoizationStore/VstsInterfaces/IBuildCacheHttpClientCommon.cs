// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;

namespace BuildXL.Cache.MemoizationStore.VstsInterfaces
{
    /// <summary>
    /// Represents a common interface for a BuildCacheHttpClient.
    /// </summary>
    public interface IBuildCacheHttpClientCommon : IDisposable
    {
        /// <summary>
        /// Incorporates a set of strong fingerprints and extends the lifetimes as specified in the request.
        /// </summary>
        Task IncorporateStrongFingerprints(
            string cacheNamespace,
            IncorporateStrongFingerprintsRequest incorporateRequest);

        /// <summary>
        /// Gets the current Cache Determinism GUID that the Build Cache Service is backed by.
        /// </summary>
        Task<Guid> GetBuildCacheServiceDeterminism(string cacheNamespace);
    }
}
