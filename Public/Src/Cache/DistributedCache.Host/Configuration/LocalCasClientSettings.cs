// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace BuildXL.Cache.Host.Configuration
{
    [DataContract]
    public class LocalCasClientSettings
    {
        public const uint DefaultConnectionsPerSession = 4;
        public const uint DefaultRetryIntervalSecondsOnFailServiceCalls = 5;
        public const uint DefaultRetryCountOnFailServiceCalls = 12;

        [JsonConstructor]
        public LocalCasClientSettings()
        {
        }

        public LocalCasClientSettings(
            bool useCasService,
            string defaultCacheName = LocalCasServiceSettings.DefaultCacheName,
            uint connectionsPerSession = DefaultConnectionsPerSession,
            uint retryIntervalSecondsOnFailServiceCalls = DefaultRetryIntervalSecondsOnFailServiceCalls,
            uint retryCountOnFailServiceCalls = DefaultRetryCountOnFailServiceCalls)
        {
            UseCasService = useCasService;
            DefaultCacheName = defaultCacheName;
            ConnectionsPerSession = connectionsPerSession;
            RetryIntervalSecondsOnFailServiceCalls = retryIntervalSecondsOnFailServiceCalls;
            RetryCountOnFailServiceCalls = retryCountOnFailServiceCalls;
        }

        /// <summary>
        /// Feature flag to use the CAS service.
        /// </summary>
        [DataMember]
        public bool UseCasService { get; set; }

        [DataMember]
        public string DefaultCacheName { get; set; } = LocalCasServiceSettings.DefaultCacheName;
        
        /// <summary>
        /// Number of pipes used for each client for concurrent requests.
        /// </summary>
        [DataMember]
        public uint ConnectionsPerSession { get; set; } = DefaultConnectionsPerSession;

        /// <summary>
        /// Time in seconds between each client request retry attempt.
        /// </summary>
        [DataMember]
        public uint RetryIntervalSecondsOnFailServiceCalls { get; set; } = DefaultRetryIntervalSecondsOnFailServiceCalls;

        /// <summary>
        /// Number of times client will retry a request before giving up.
        /// </summary>
        [DataMember]
        public uint RetryCountOnFailServiceCalls { get; set; } = DefaultRetryCountOnFailServiceCalls;
    }
}
