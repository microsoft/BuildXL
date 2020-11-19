// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.Serialization;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
#nullable disable
namespace BuildXL.Cache.Host.Configuration
{
    [DataContract]
    public class DistributedCacheServiceConfiguration
    {
        public DistributedCacheServiceConfiguration(
            LocalCasSettings localCasSettings,
            DistributedContentSettings distributedCacheSettings,
            LoggingSettings loggingSettings = null)
        {
            LocalCasSettings = localCasSettings;
            DistributedContentSettings = distributedCacheSettings;
            LoggingSettings = loggingSettings;
        }

        /// <summary>
        /// Constructor for deserialization
        /// </summary>
        public DistributedCacheServiceConfiguration() { }

        [DataMember]
        public string DataRootPath { get; set; }

        /// <summary>
        /// Indicates that when components call LifetimeManager.RequestTeardown that the service should
        /// shutdown independent of whether host responds and triggers cancellation.
        /// </summary>
        [DataMember]
        public bool RespectRequestTeardown { get; set; }

        [DataMember]
        public DistributedContentSettings DistributedContentSettings { get; set; }

        /// <summary>
        /// Cache settings for the local cache.
        /// </summary>
        [DataMember]
        public LocalCasSettings LocalCasSettings { get; set; }

        /// <summary>
        /// Cache settings for the local cache.
        /// 
        /// Forwarded this property to <see cref="LocalCasSettings"/> during deserialization.
        /// Eventually we should unify these into a single property. Since this property appears in the config it would
        /// be harder to deprecated. Rather <see cref="LocalCasSettings"/> can be removed with a simple rename
        /// once construction of copier is moved from CloudBuild codebase.
        /// </summary>
        [DataMember]
        public LocalCasSettings CasSettings { set => LocalCasSettings = value; }

        /// <summary>
        /// Use a per stamp isolation for cache.
        /// </summary>
        [DataMember]
        public bool UseStampBasedIsolation { get; set; } = true;

        /// <summary>
        /// Minimum logging severity.
        /// </summary>
        [DataMember]
        public Severity MinimumLogSeverity { get; set; } = Severity.Debug;

        /// <summary>
        /// Configure the logging behavior for the service
        /// </summary>
        public LoggingSettings LoggingSettings { get; set; } = null;
    }
}
