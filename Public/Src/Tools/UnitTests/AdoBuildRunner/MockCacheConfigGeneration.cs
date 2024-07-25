// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.AdoBuildRunner;

namespace Test.Tool.AdoBuildRunner
{
    /// <summary>
    /// Mock implementation of ICacheConfigGenerationConfiguration for testing purpose.
    /// </summary>
    public class MockCacheConfigGeneration : ICacheConfigGenerationConfiguration
    {
        /// <nodoc/>
        public Uri StorageAccountEndpoint { get; set; }

        /// <nodoc/>
        public Guid ManagedIdentityId { get; set; }

        /// <nodoc/>
        public int? RetentionPolicyInDays { get; set; }

        /// <nodoc/>
        public string Universe { get; set; }

        /// <nodoc/>
        public int? CacheSizeInMB { get; set; }

        /// <nodoc/>
        public string CacheId { get; set; }

        /// <nodoc/>
        public CacheType? CacheType { get; set; }

        /// <nodoc/>
        public bool? LogGeneratedConfiguration { get; set; }
    }
}