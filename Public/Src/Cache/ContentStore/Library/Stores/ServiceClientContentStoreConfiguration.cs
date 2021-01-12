// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Grpc;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Utils;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    /// Configuration object for <see cref="ServiceClientContentStore"/>.
    /// </summary>
    public sealed class ServiceClientContentStoreConfiguration
    {
        private readonly Lazy<IRetryPolicy> _retryPolicy;

        /// <summary>
        ///     Default interval, in seconds, between client retries.
        /// </summary>
        public const int DefaultRetryIntervalSeconds = 10;

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
        public string? Scenario { get; }

        /// <nodoc />
        public IRetryPolicy RetryPolicy => _retryPolicy.Value;

        /// <nodoc />
        public bool TraceOperationStarted { get; set; }

        /// <nodoc />
        public GrpcEnvironmentOptions? GrpcEnvironmentOptions { get; set; }

        /// <nodoc />
        public ServiceClientContentStoreConfiguration(
            string cacheName,
            ServiceClientRpcConfiguration rpcConfiguration,
            string? scenario = null)
        {
            Contract.RequiresNotNullOrEmpty(cacheName);
            CacheName = cacheName;
            RpcConfiguration = rpcConfiguration;
            Scenario = scenario;

            _retryPolicy = new Lazy<IRetryPolicy>(
                () =>
                {
                    var strategy = new TransientErrorDetectionStrategy();
                    return RetryPolicyFactory.GetLinearPolicy(
                        shouldRetry: e => strategy.IsTransient(e),
                        (int)RetryCount,
                        TimeSpan.FromSeconds(RetryIntervalSeconds));
                });
        }

        /// <nodoc />
        public ServiceClientContentStoreConfiguration(
            string cacheName,
            ServiceClientRpcConfiguration rpcConfiguration,
            string scenario,
            IRetryPolicy retryPolicy)
        {
            Contract.Requires(cacheName != null);

            CacheName = cacheName;
            RpcConfiguration = rpcConfiguration;
            Scenario = scenario;

            _retryPolicy = new Lazy<IRetryPolicy>(() => retryPolicy);
        }
    }
}
