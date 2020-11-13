// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

#nullable disable

namespace BuildXL.Cache.Host.Configuration
{
    /// <summary>
    /// Settings specific to a single cache.
    /// Things may include settings like
    /// WHether or not the cache supports Sensitivite sessions
    /// Whether or not the cache allows proactive replication consumption
    /// Garbage collection and quota requirements.
    /// </summary>
    [DataContract]
    public class NamedCacheSettings
    {
        public NamedCacheSettings(
            string cacheRootPath, string cacheSizeQuotaString, bool supportsSensitiveSessions, bool supportsProactiveReplication, IEnumerable<string> requiredCapabilites)
        {
            CacheRootPath = cacheRootPath;
            CacheSizeQuotaString = cacheSizeQuotaString;
            SupportsSensitiveSessions = supportsSensitiveSessions;
            SupportsProactiveReplication = supportsProactiveReplication;
            RequiredCapabilities = requiredCapabilites?.ToList();
        }

        public NamedCacheSettings()
        {
        }

        [DataMember]
        public string CacheRootPath { get; set; }

        [DataMember]
        public string CacheSizeQuotaString { get; set; }

        [DataMember]
        public bool SupportsSensitiveSessions { get; private set; }

        [DataMember]
        public bool SupportsProactiveReplication { get; set; }

        [DataMember]
        public List<string> RequiredCapabilities { get; set; }

        public override string ToString()
        {
            return
                $"CacheRootPath={CacheRootPath}; CacheSizeQuotaString={CacheSizeQuotaString}; SupportsSensitiveSessions={SupportsSensitiveSessions}; SupportsProactiveReplication={SupportsProactiveReplication}";
        }
    }
}
