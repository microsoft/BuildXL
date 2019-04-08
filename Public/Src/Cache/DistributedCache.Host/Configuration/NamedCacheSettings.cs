// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Newtonsoft.Json;

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

        [JsonConstructor]
        private NamedCacheSettings()
        {
        }

        [DataMember]
        public string CacheRootPath { get; private set; }

        [DataMember]
        public string CacheSizeQuotaString { get; private set; }

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
