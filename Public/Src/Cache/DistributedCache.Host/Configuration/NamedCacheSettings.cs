// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Runtime.Serialization;

#nullable enable

namespace BuildXL.Cache.Host.Configuration
{
    /// <summary>
    /// Settings specific to a cache instance on a single drive
    /// </summary>
    [DataContract]
    public class NamedCacheSettings
    {
        [DataMember]
        public required string CacheRootPath { get; set; }

        [DataMember]
        public string? CacheSizeQuotaString { get; set; }

        public override string ToString()
        {
            return
                $"CacheRootPath={CacheRootPath}; CacheSizeQuotaString={CacheSizeQuotaString};";
        }
    }
}
