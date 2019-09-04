// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    ///     An IContentStore fronting a cache in a separate process.
    /// </summary>
    public class ServiceClientContentStore : StartupShutdownBase, IContentStore
    {
        /// <summary>
        ///     Default interval, in seconds, between client retries.
        /// </summary>
        public const uint DefaultRetryIntervalSeconds = 5;

        /// <summary>
        ///     Default number of client retries to attempt before giving up.
        /// </summary>
        public const uint DefaultRetryCount = 12;

        /// <summary>
        ///     Execution tracer.
        /// </summary>
        protected virtual ContentStoreTracer ExecutionTracer { get; } = new ContentStoreTracer(nameof(ServiceClientContentStore));

        /// <summary>
        /// Execution tracer for the session.
        /// </summary>
        protected virtual ServiceClientContentSessionTracer SessionTracer { get; } = new ServiceClientContentSessionTracer(nameof(ServiceClientContentSession));

        /// <summary>
        ///     The filesystem to use for temporary files.
        /// </summary>
        protected readonly IAbsFileSystem FileSystem;

        /// <nodoc />
        protected readonly ILogger Logger;

        /// <summary>
        /// GrpcClient for retrieving stats
        /// </summary>
        private GrpcContentClient _grpcClient;

        /// <nodoc />
        protected readonly ServiceClientContentStoreConfiguration Configuration;

        /// <inheritdoc />
        protected override Tracer Tracer => ExecutionTracer;

        /// <summary>
        /// Backward compat constructor.
        /// </summary>
        public ServiceClientContentStore(
            ILogger logger,
            IAbsFileSystem fileSystem,
            string cacheName,
            ServiceClientRpcConfiguration rpcConfiguration,
            uint retryIntervalSeconds,
            uint retryCount,
            Sensitivity sensitivity, // Not used. Left for backward compatibility.
            string scenario = null)
            : this(
                  logger,
                  fileSystem,
                  cacheName,
                  rpcConfiguration,
                  retryIntervalSeconds,
                  retryCount,
                  scenario)
        {
        }

        /// <nodoc />
        public ServiceClientContentStore(
            ILogger logger,
            IAbsFileSystem fileSystem,
            string cacheName,
            ServiceClientRpcConfiguration rpcConfiguration,
            uint retryIntervalSeconds,
            uint retryCount,
            string scenario = null)
            : this(
                  logger,
                  fileSystem,
                  new ServiceClientContentStoreConfiguration(cacheName, rpcConfiguration, scenario)
                  {
                      RetryCount = retryCount,
                      RetryIntervalSeconds = retryIntervalSeconds
                  })
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ServiceClientContentStore"/> class.
        /// </summary>
        public ServiceClientContentStore(
            ILogger logger,
            IAbsFileSystem fileSystem,
            ServiceClientContentStoreConfiguration configuration)
        {
            Contract.Requires(logger != null);
            Contract.Requires(fileSystem != null);
            Contract.Requires(configuration != null);
            Logger = logger;
            FileSystem = fileSystem;
            Configuration = configuration;
        }

        /// <summary>
        ///     Extension point for test class to setup before main StartupAsync body.
        /// </summary>
        protected virtual Task<BoolResult> PreStartupAsync(Context context)
        {
            return Task.FromResult(BoolResult.Success);
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            BoolResult result = await PreStartupAsync(context);

            var rpcConfiguration = Configuration.RpcConfiguration;
            if (result.Succeeded)
            {
                _grpcClient = new GrpcContentClient(SessionTracer, FileSystem, rpcConfiguration.GrpcPort, Configuration.Scenario, rpcConfiguration.HeartbeatInterval);
                result = await Configuration.RetryPolicy.ExecuteAsync(() => _grpcClient.StartupAsync(context, waitMs: 0));

                if (!result)
                {
                    await Configuration.RetryPolicy.ExecuteAsync(() => _grpcClient.ShutdownAsync(context)).ThrowIfFailure();
                }
            }

            if (!result)
            {
                await PostShutdownAsync(context).ThrowIfFailure();
                return result;
            }

            return result;
        }

        /// <summary>
        ///     Extension point for test class to teardown after ShutdownAsync body.
        /// </summary>
        protected virtual Task<BoolResult> PostShutdownAsync(Context context)
        {
            return Task.FromResult(BoolResult.Success);
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            if (_grpcClient != null)
            {
                // _grpcClient is null if initialization fails.
                await _grpcClient.ShutdownAsync(context).ThrowIfFailure();
            }

            BoolResult result = await PostShutdownAsync(context);
            return result;
        }

        /// <nodoc />
        protected override void DisposeCore()
        {
            base.DisposeCore();
            _grpcClient?.Dispose();
        }

        /// <inheritdoc />
        public virtual CreateSessionResult<IReadOnlyContentSession> CreateReadOnlySession(
            Context context, string name, ImplicitPin implicitPin)
        {
            return CreateReadOnlySessionCall.Run(ExecutionTracer, OperationContext(context), name, () =>
            {
                var session = new ReadOnlyServiceClientContentSession(
                    name,
                    implicitPin,
                    Logger,
                    FileSystem,
                    SessionTracer,
                    Configuration);
                return new CreateSessionResult<IReadOnlyContentSession>(session);
            });
        }

        /// <inheritdoc />
        public virtual CreateSessionResult<IContentSession> CreateSession(Context context, string name, ImplicitPin implicitPin)
        {
            return CreateSessionCall.Run(ExecutionTracer, OperationContext(context), name, () =>
            {
                var session = new ServiceClientContentSession(
                    name,
                    implicitPin,
                    Logger,
                    FileSystem,
                    SessionTracer,
                    Configuration);

                return new CreateSessionResult<IContentSession>(session);
            });
        }

        /// <inheritdoc />
        public Task<GetStatsResult> GetStatsAsync(Context context)
        {
            return GetStatsCall<ContentStoreTracer>.RunAsync(
               ExecutionTracer,
               OperationContext(context),
               async () =>
               {
                   CounterSet aggregatedCounters = new CounterSet();
                   aggregatedCounters.Merge(SessionTracer.GetCounters(), $"{nameof(ServiceClientContentSession)}.");
                   // Getting the stats from the remote as well.
                   if (_grpcClient != null)
                   {
                       var getStats = await _grpcClient.GetStatsAsync(context);
                       if (getStats.Succeeded)
                       {
                           aggregatedCounters.Merge(getStats.CounterSet, "ContentServer.");
                       }
                   }

                   return new GetStatsResult(aggregatedCounters);
               });
        }

        /// <inheritdoc />
        public Task<DeleteResult> DeleteAsync(Context context, ContentHash contentHash)
        {
            throw new System.NotImplementedException();
        }

        /// <inheritdoc />
        public void PostInitializationCompleted(Context context, BoolResult result) { }
    }
}
