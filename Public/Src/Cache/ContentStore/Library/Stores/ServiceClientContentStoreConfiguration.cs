// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Sessions;
using Microsoft.Practices.TransientFaultHandling;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    /// Configuration object for <see cref="ServiceClientContentStore"/>.
    /// </summary>
    public sealed class ServiceClientContentStoreConfiguration
    {
        private readonly Lazy<RetryPolicy> _retryPolicy;

        /// <summary>
        ///     Default interval, in seconds, between client retries.
        /// </summary>
        public const int DefaultRetryIntervalSeconds = 5;

        /// <summary>
        ///     Default number of client retries to attempt before giving up.
        /// </summary>
        public const uint DefaultRetryCount = 12;

        /// <nodoc />
        public string CacheName { get; }

        /// <nodoc />
        public ServiceClientRpcConfiguration RpcConfiguration { get; }

        /// <nodoc />
        public uint RetryIntervalSeconds { get; set; } = DefaultRetryIntervalSeconds;

        /// <nodoc />
        public uint RetryCount { get; set; } = DefaultRetryCount;

        /// <nodoc />
        public string Scenario { get; }

        /// <nodoc />
        public RetryPolicy RetryPolicy => _retryPolicy.Value;

        /// <nodoc />
        public ServiceClientContentStoreConfiguration(
            string cacheName,
            ServiceClientRpcConfiguration rpcConfiguration,
            string scenario = null)
        {
            Contract.Requires(cacheName != null);
            CacheName = cacheName;
            RpcConfiguration = rpcConfiguration;
            Scenario = scenario;

            _retryPolicy = new Lazy<RetryPolicy>(
                () => new RetryPolicy(
                    new TransientErrorDetectionStrategy(),
                    new FixedInterval("RetryInterval", (int)RetryCount, TimeSpan.FromSeconds(RetryIntervalSeconds), false)));
        }

        /// <nodoc />
        public ServiceClientContentStoreConfiguration(
            string cacheName,
            ServiceClientRpcConfiguration rpcConfiguration,
            string scenario,
            RetryPolicy retryPolicy)
        {
            Contract.Requires(cacheName != null);

            CacheName = cacheName;
            RpcConfiguration = rpcConfiguration;
            Scenario = scenario;

            _retryPolicy = new Lazy<RetryPolicy>(() => retryPolicy);
        }
    }
}
