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
            TimeSpan? logIncrementalStatsInterval = null,
            TimeSpan? logMachineStatsInterval = null,
            bool traceGrpcOperations = false,
            int? copyRequestHandlingCountLimit = null
        )
        {
            DataRootPath = dataRootPath;
            NamedCacheRoots = namedCacheRoots;
            GrpcPort = grpcPort;
            BufferSizeForGrpcCopies = bufferSizeForGrpcCopies;
            ProactivePushCountLimit = proactivePushCountLimit;
            CopyRequestHandlingCountLimit = copyRequestHandlingCountLimit;
            FileSystem = fileSystem;

            LogIncrementalStatsInterval = logIncrementalStatsInterval ?? DefaultLogIncrementalStatsInterval;
            LogMachineStatsInterval = logMachineStatsInterval ?? DefaultLogMachineStatsInterval;
            TraceGrpcOperations = traceGrpcOperations;
        }

        /// <nodoc />
        public LocalServerConfiguration(ServiceConfiguration serviceConfiguration)
        {
            Contract.Requires(serviceConfiguration.DataRootPath != null);

            DataRootPath = serviceConfiguration.DataRootPath;
            NamedCacheRoots = serviceConfiguration.NamedCacheRoots;
            GrpcPort = (int)serviceConfiguration.GrpcPort;
            GrpcPortFileName = serviceConfiguration.GrpcPortFileName ?? DefaultFileName;
            BufferSizeForGrpcCopies = serviceConfiguration.BufferSizeForGrpcCopies;
            ProactivePushCountLimit = serviceConfiguration.ProactivePushCountLimit;
            CopyRequestHandlingCountLimit = serviceConfiguration.CopyRequestHandlingCountLimit;
            LogMachineStatsInterval = serviceConfiguration.LogMachineStatsInterval ?? DefaultLogMachineStatsInterval;
            LogIncrementalStatsInterval = serviceConfiguration.LogIncrementalStatsInterval ?? DefaultLogIncrementalStatsInterval;
            TraceGrpcOperations = serviceConfiguration.TraceGrpcOperation;
            IncrementalStatsCounterNames = serviceConfiguration.IncrementalStatsCounterNames ?? new string[0];
        }

        /// <nodoc />
        public LocalServerConfiguration OverrideServiceConfiguration(ServiceConfiguration serviceConfiguration)
        {
            Contract.Requires(serviceConfiguration.DataRootPath != null);
            DataRootPath = serviceConfiguration.DataRootPath;
            NamedCacheRoots = serviceConfiguration.NamedCacheRoots;
            GrpcPort = (int)serviceConfiguration.GrpcPort;
            GrpcPortFileName = serviceConfiguration.GrpcPortFileName ?? DefaultFileName;
            BufferSizeForGrpcCopies = serviceConfiguration.BufferSizeForGrpcCopies;
            ProactivePushCountLimit = serviceConfiguration.ProactivePushCountLimit;
            CopyRequestHandlingCountLimit = serviceConfiguration.CopyRequestHandlingCountLimit;
            LogMachineStatsInterval = serviceConfiguration.LogMachineStatsInterval ?? DefaultLogMachineStatsInterval;
            LogIncrementalStatsInterval = serviceConfiguration.LogIncrementalStatsInterval ?? DefaultLogIncrementalStatsInterval;
            TraceGrpcOperations = serviceConfiguration.TraceGrpcOperation;
            IncrementalStatsCounterNames = serviceConfiguration.IncrementalStatsCounterNames ?? new string[0];
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

        /// <nodoc />
        public static TimeSpan DefaultLogIncrementalStatsInterval { get; } = TimeSpan.FromHours(2);

        /// <summary>
        /// Gets or sets the time period between logging incremental stats
        /// </summary>
        public TimeSpan LogIncrementalStatsInterval { get; set; } = DefaultLogIncrementalStatsInterval;

        /// <summary>
        /// A list of counters that will be printed as part of incremental statistics.
        /// </summary>
        /// <remarks>
        /// The name should just be the counter name itself, like 'RemoteCopyFile' and not 'DistributedContentCopier.DistributedContentCopierCounters.RemoteCopyFile'.
        /// </remarks>
        public string[] IncrementalStatsCounterNames { get; set; } = new string[0];

        /// <nodoc />
        public static TimeSpan DefaultLogMachineStatsInterval { get; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Gets or sets the time period between logging machine-specific performance statistics.
        /// </summary>
        public TimeSpan LogMachineStatsInterval { get; set; } = DefaultLogMachineStatsInterval;

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

        /// <nodoc />
        public GrpcCoreServerOptions? GrpcCoreServerOptions { get; set; }

        /// <nodoc />
        public GrpcEnvironmentOptions? GrpcEnvironmentOptions { get; set; }

        #endregion

        /// <nodoc />
        public int? BufferSizeForGrpcCopies { get; private set; }

        /// <summary>
        /// If true, then the unsafe version of ByteString construction is used that avoids extra copy of the byte[].
        /// </summary>
        public bool UseUnsafeByteStringConstruction { get; set; }

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
        /// When set to true, we will shut down the quota keeper before hibernating sessions to prevent a race condition of evicting pinned content
        /// </summary>
        public bool ShutdownEvictionBeforeHibernation { get; set; }

        /// <summary>
        /// Whether to trace the operation's start and stop messages on the grpc level.
        /// </summary>
        public bool TraceGrpcOperations { get; set; }

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
            sb.Append($", GrpcPortFileName={GrpcPortFileName}");
            sb.Append($", BufferSizeForGrpcCopies={BufferSizeForGrpcCopies}");
            sb.Append($", TraceGrpcOperations={TraceGrpcOperations}");

            return sb.ToString();
        }
    }
}
