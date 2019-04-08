// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace BuildXL.Cache.MemoizationStore.VstsInterfaces
{
    /// <summary>
    /// An interface for any admin operations on the build cache service.
    /// </summary>
    public interface IAdminBuildCacheHttpClient
    {
        /// <summary>
        /// Resets the determinism guid set by the build cache service for a given namespace.
        /// </summary>
        [SuppressMessage("ReSharper", "UnusedMember.Global")]
        [SuppressMessage("ReSharper", "UnusedParameter.Global")]
        Task<Guid> ResetBuildCacheServiceDeterminism(string cacheNamespace, Guid existingCacheDeterminism);
    }
}
