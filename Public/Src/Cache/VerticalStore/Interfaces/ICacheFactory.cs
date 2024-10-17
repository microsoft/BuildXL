// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;
using System.Diagnostics.CodeAnalysis;

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
        /// <param name="configuration">Configuration object, which may influence how the cache is configured</param>
        /// <param name="buildXLContext">The BuildXL context associated to the configuration object</param>
        /// <returns>Cache object or a Failure</returns>
        Task<Possible<ICache, Failure>> InitializeCacheAsync([NotNull]ICacheConfigData cacheData, Guid activityId = default(Guid), IConfiguration configuration = null, BuildXLContext buildXLContext = null);

        /// <summary>
        /// Validates a configuration object.
        /// </summary>
        /// <remarks>
        /// This is currently only called from the VerticalAggregator, and does not account for validations that involve populating data from the build engine.
        /// TODO: refactor to make it work in the rest of the scenarios.
        /// </remarks>
        IEnumerable<Failure> ValidateConfiguration([NotNull]ICacheConfigData cacheData);
    }
}
