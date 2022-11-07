// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Text;
using BuildXL.Cache.ContentStore.Grpc;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using GrpcConstants = BuildXL.Cache.ContentStore.Grpc.GrpcConstants;

namespace BuildXL.Cache.ContentStore.Service
{
    /// <summary>
    /// Configuration properties for <see cref="LocalContentServer"/>
    /// </summary>
    public sealed class LocalServerConfiguration
    {
        /// <nodoc />
        public LocalServerConfiguration(
            AbsolutePath dataRootPath,
            IReadOnlyDictionary<string, AbsolutePath> namedCacheRoots,
            int grpcPort,
            IAbsFileSystem fileSystem,
            int? bufferSizeForGrpcCopies = null,
            int? proactivePushCountLimit = null,
            bool traceGrpcOperations = false,
            int? copyRequestHandlingCountLimit = null,
            bool doNotShutdownSessionsInUse = false,
            TimeSpan? asyncSessionShutdownTimeout = null
        )
        {
            DataRootPath = dataRootPath;
            NamedCacheRoots = namedCacheRoots;
            GrpcPort = grpcPort;
            EncryptedGrpcPort = GrpcConstants.DefaultEncryptedGrpcPort;
            BufferSizeForGrpcCopies = bufferSizeForGrpcCopies;
            ProactivePushCountLimit = proactivePushCountLimit;
            CopyRequestHandlingCountLimit = copyRequestHandlingCountLimit;
            FileSystem = fileSystem;
            TraceGrpcOperations = traceGrpcOperations;
            DoNotShutdownSessionsInUse = doNotShutdownSessionsInUse;
            AsyncSessionShutdownTimeout = asyncSessionShutdownTimeout ?? DefaultWaitForShutdownTimeout;
        }

        /// <nodoc />
        public LocalServerConfiguration(ServiceConfiguration serviceConfiguration)
        {
            Contract.Requires(serviceConfiguration.DataRootPath != null);

            DataRootPath = serviceConfiguration.DataRootPath;
            NamedCacheRoots = serviceConfiguration.NamedCacheRoots;
            GrpcPort = (int)serviceConfiguration.GrpcPort;
            EncryptedGrpcPort = (int)serviceConfiguration.EncryptedGrpcPort != ServiceConfiguration.GrpcDisabledPort ? (int)serviceConfiguration.EncryptedGrpcPort : GrpcConstants.DefaultEncryptedGrpcPort;
            GrpcPortFileName = serviceConfiguration.GrpcPortFileName ?? DefaultFileName;
            BufferSizeForGrpcCopies = serviceConfiguration.BufferSizeForGrpcCopies;
            ProactivePushCountLimit = serviceConfiguration.ProactivePushCountLimit;
            CopyRequestHandlingCountLimit = serviceConfiguration.CopyRequestHandlingCountLimit;
            TraceGrpcOperations = serviceConfiguration.TraceGrpcOperation;
            DoNotShutdownSessionsInUse = serviceConfiguration.DoNotShutdownSessionsInUse;
            AsyncSessionShutdownTimeout = serviceConfiguration.AsyncSessionShutdownTimeout ?? DefaultWaitForShutdownTimeout;
        }

        /// <nodoc />
        public LocalServerConfiguration OverrideServiceConfiguration(ServiceConfiguration serviceConfiguration)
        {
            Contract.Requires(serviceConfiguration.DataRootPath != null);
            DataRootPath = serviceConfiguration.DataRootPath;
            NamedCacheRoots = serviceConfiguration.NamedCacheRoots;
            GrpcPort = (int)serviceConfiguration.GrpcPort;
            EncryptedGrpcPort = (int)serviceConfiguration.EncryptedGrpcPort != ServiceConfiguration.GrpcDisabledPort ? (int)serviceConfiguration.EncryptedGrpcPort : GrpcConstants.DefaultEncryptedGrpcPort;
            GrpcPortFileName = serviceConfiguration.GrpcPortFileName ?? DefaultFileName;
            BufferSizeForGrpcCopies = serviceConfiguration.BufferSizeForGrpcCopies;
            ProactivePushCountLimit = serviceConfiguration.ProactivePushCountLimit;
            CopyRequestHandlingCountLimit = serviceConfiguration.CopyRequestHandlingCountLimit;
            TraceGrpcOperations = serviceConfiguration.TraceGrpcOperation;
            DoNotShutdownSessionsInUse = serviceConfiguration.DoNotShutdownSessionsInUse;
            AsyncSessionShutdownTimeout = serviceConfiguration.AsyncSessionShutdownTimeout ?? DefaultWaitForShutdownTimeout;
            return this;
        }

        /// <summary>
        ///     Gets the service data root directory path.
        /// </summary>
        public AbsolutePath DataRootPath { get; private set; }

        /// <summary>
        ///     Gets the named cache roots.
        /// </summary>
        public IReadOnlyDictionary<string, AbsolutePath> NamedCacheRoots { get; private set; }

        /// <summary>
        /// Indicates whether to disable the internal gprc server. Presumably, to allow external GPRC server creation
        /// (i.e. via ASP.Net Core)
        /// </summary>
        public bool DisableGrpcServer { get; set; }

        /// <summary>
        /// Gets or sets the duration of inactivity after which a session will be timed out.
        /// </summary>
        public TimeSpan UnusedSessionTimeout { get; set; } = TimeSpan.FromHours(6);

        /// <summary>
        /// Gets or sets the shorter duration of inactivity after which a session with a heartbeat will be timed out.
        /// </summary>
        /// <remarks>
        /// This is also the minimum amount of time given to clients to reconnect even if their calls started during
        /// service hibernation. This works as long as the client's polling period is less than this timeout.
        /// </remarks>
        public TimeSpan UnusedSessionHeartbeatTimeout { get; set; } = TimeSpan.FromMinutes(10);

        #region gRPC Internal Options

        /// <nodoc />
        public const int DefaultRequestCallTokensPerCompletionQueue = 7000;

        /// <summary>
        /// Number or calls requested via grpc_server_request_call at any given time for each completion queue.
        /// </summary>
        /// <remarks>
        /// Need a higher number here to avoid throttling: 7000 worked for initial experiments.
        /// </remarks>
        public int RequestCallTokensPerCompletionQueue { get; set; } = DefaultRequestCallTokensPerCompletionQueue;

        /// <nodoc />
        public int GrpcPort { get; private set; } = GrpcConstants.DefaultGrpcPort;

        public int EncryptedGrpcPort { get; private set; } = GrpcConstants.DefaultEncryptedGrpcPort;

        /// <nodoc />
        public GrpcCoreServerOptions? GrpcCoreServerOptions { get; set; }

        /// <nodoc />
        public GrpcEnvironmentOptions? GrpcEnvironmentOptions { get; set; }

        #endregion

        /// <nodoc />
        public int? BufferSizeForGrpcCopies { get; private set; }

        /// <nodoc />
        public const int DefaultProactivePushCountLimit = 128;

        /// <summary>
        /// The max number of proactive pushes that can happen at the same time.
        /// </summary>
        public int? ProactivePushCountLimit { get; private set; }

        /// <nodoc />
        public const int DefaultCopyRequestHandlingCountLimit = 128;

        /// <summary>
        /// The max number of copy operations that can happen at the same time from this machine.
        /// </summary>
        /// <remarks>
        /// Once the limit is reached the server starts returning error responses but only when the client is willing to fail fast.
        /// </remarks>
        public int? CopyRequestHandlingCountLimit { get; private set; }

        /// <nodoc />
        public static readonly string DefaultFileName = "CASaaS GRPC port";

        /// <nodoc />
        public string? GrpcPortFileName { get; set; } = DefaultFileName;

        /// <nodoc />
        public IAbsFileSystem? FileSystem { get; set; }

        /// <summary>
        /// Whether to trace the operation's start and stop messages on the grpc level.
        /// </summary>
        public bool TraceGrpcOperations { get; set; }

        /// <summary>
        /// Whether to respect the fact the sessions is being used and not shut it down.
        /// </summary>
        /// <remarks>
        /// If the heartbeat for the session is equals to the sessions TTL, and a single operation runs longer then the TTL, then
        /// the backend will close the session due to the expiry.
        /// If this flag is set, the session won't be closed in this case.
        /// </remarks>
        public bool DoNotShutdownSessionsInUse { get; set; }

        private static TimeSpan DefaultWaitForShutdownTimeout = TimeSpan.FromHours(1);

        /// <summary>
        /// For sessions that should not shut down until their async operations are completed, we should set a limit to how much we're willing to wait
        /// until we force shutdown.
        /// </summary>
        public TimeSpan AsyncSessionShutdownTimeout { get; set; }

        /// <inheritdoc />
        public override string ToString()
        {
            var sb = new StringBuilder();
            var i = 0;

            sb.Append($"{nameof(NamedCacheRoots)}=[");

            foreach (var kvp in NamedCacheRoots)
            {
                if (i++ > 0)
                {
                    sb.Append(", ");
                }

                sb.Append($"name=[{kvp.Key}] path=[{kvp.Value}]");
            }

            sb.Append("]");

            sb.Append($", DataRootPath={DataRootPath}");
            sb.Append($", GrpcPort={GrpcPort}");
            sb.Append($", EncryptedGrpcPort={EncryptedGrpcPort}");
            sb.Append($", GrpcPortFileName={GrpcPortFileName}");
            sb.Append($", BufferSizeForGrpcCopies={BufferSizeForGrpcCopies}");
            sb.Append($", TraceGrpcOperations={TraceGrpcOperations}");

            return sb.ToString();
        }
    }
}
