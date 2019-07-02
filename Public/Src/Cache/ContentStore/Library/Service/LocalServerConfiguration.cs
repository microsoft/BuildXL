// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Text;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;

namespace BuildXL.Cache.ContentStore.Service
{
    /// <summary>
    /// Configuration properties for <see cref="LocalContentServer"/>
    /// </summary>
    public sealed class LocalServerConfiguration
    {
        /// <nodoc />
        public LocalServerConfiguration(AbsolutePath dataRootPath, IReadOnlyDictionary<string, AbsolutePath> namedCacheRoots, int grpcPort, int? bufferSizeForGrpcCopies = null, int? gzipBarrierSizeForGrpcCopies = null)
        {
            DataRootPath = dataRootPath;
            NamedCacheRoots = namedCacheRoots;
            GrpcPort = grpcPort;
            BufferSizeForGrpcCopies = bufferSizeForGrpcCopies;
            GzipBarrierSizeForGrpcCopies = gzipBarrierSizeForGrpcCopies;
        }

        /// <nodoc />
        public LocalServerConfiguration(ServiceConfiguration serviceConfiguration)
        {
            OverrideServiceConfiguration(serviceConfiguration);
        }

        /// <nodoc />
        public LocalServerConfiguration()
        {
            GrpcPort = DefaultGrpcPort;
        }

        /// <nodoc />
        public LocalServerConfiguration OverrideServiceConfiguration(ServiceConfiguration serviceConfiguration)
        {
            DataRootPath = serviceConfiguration.DataRootPath;
            NamedCacheRoots = serviceConfiguration.NamedCacheRoots;
            GrpcPort = (int)serviceConfiguration.GrpcPort;
            GrpcPortFileName = serviceConfiguration.GrpcPortFileName;
            BufferSizeForGrpcCopies = serviceConfiguration.BufferSizeForGrpcCopies;
            GzipBarrierSizeForGrpcCopies = serviceConfiguration.GzipBarrierSizeForGrpcCopies;
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
        /// Gets or sets the time period between logging incremental stats
        /// </summary>
        public TimeSpan LogIncrementalStatsInterval { get; set; } = TimeSpan.FromMinutes(15);

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
        public static readonly int DefaultGrpcPort = 7089;

        /// <nodoc />
        public int GrpcPort { get; private set; }

        /// <nodoc />
        public int? BufferSizeForGrpcCopies { get; private set; }

        /// <summary>
        /// Files greater than this size will be compressed via GZip when GZip is enabled.
        /// </summary>
        public int? GzipBarrierSizeForGrpcCopies { get; private set; }

        /// <nodoc />
        public static readonly string DefaultFileName = "CASaaS GRPC port";

        /// <nodoc />
        public string GrpcPortFileName { get; set; } = DefaultFileName;

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
            sb.Append($", GzipBarrierSizeForGrpcCopies={GzipBarrierSizeForGrpcCopies}");

            return sb.ToString();
        }
    }
}
