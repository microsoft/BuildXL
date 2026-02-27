// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.ComponentModel;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.Interfaces;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;

namespace BuildXL.Cache.MemoizationStoreAdapter
{
    /// <summary>
    /// Configuration object for <see cref="BlobWithLocalCacheFactory"/>
    /// </summary>
    public class BlobWithLocalCacheConfig : IEngineDependentSettingsConfiguration
    {
        /// <summary>
        /// The local cache configuration
        /// </summary>
        public MemoizationStoreCacheFactory.Config LocalCache { get; set; }

        /// <summary>
        /// The blob-based remote cache configuration
        /// </summary>
        public BlobCacheConfig RemoteCache { get; set; }

        /// <summary>
        /// When true (the default), cache initialization fails if the remote cache cannot be constructed.
        /// When false, the factory falls back to using the local cache only when the remote cache fails to initialize.
        /// </summary>
        [DefaultValue(true)]
        public bool FailIfRemoteFails { get; set; } = true;

        /// <inheritdoc/>
        public bool TryPopulateFrom(Guid activityId, IConfiguration configuration, BuildXLContext buildXLContext, out Failure failure)
        {
            // The local cache config does not depend on engine configurations. Asserting that in case things change and this goes unnoticed.
#pragma warning disable CS0184 // 'is' expression's given expression is never of the provided type
            Contract.Assert(!(LocalCache is IEngineDependentSettingsConfiguration));
#pragma warning restore CS0184 // 'is' expression's given expression is never of the provided type

            return RemoteCache.TryPopulateFrom(activityId, configuration, buildXLContext, out failure);
        }
    }
}
