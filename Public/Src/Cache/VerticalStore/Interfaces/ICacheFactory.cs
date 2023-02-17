// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;
using JetBrains.Annotations;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// Creates Cache instances from Json data strings or ICacheConfigData data
    /// </summary>
    public interface ICacheFactory
    {
        /// <summary>
        /// Creates a cache instance from a ICacheConfigData data structure
        /// </summary>
        /// <param name="cacheData">ICacheConfigData input data</param>
        /// <param name="activityId">Guid that identifies the parent of this call for tracing.</param>
        /// <param name="cacheConfiguration">Cache configuration object, which may influence how the cache is configured</param>
        /// <returns>Cache object or a Failure</returns>
        Task<Possible<ICache, Failure>> InitializeCacheAsync([NotNull]ICacheConfigData cacheData, Guid activityId = default(Guid), ICacheConfiguration cacheConfiguration = null);

        /// <summary>
        /// Validates a configuration object.
        /// </summary>
        IEnumerable<Failure> ValidateConfiguration([NotNull]ICacheConfigData cacheData);
    }
}
