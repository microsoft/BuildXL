// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace BuildXL.Cache.Host.Configuration
{
    [DataContract]
    public class LocalCasServiceSettings
    {
        /// <nodoc />
        public const string DefaultFileName = "CASaaS GRPC port";

        public const string DefaultCacheName = "DEFAULT";
        public const string InProcCacheName = "INPROC";

        public const uint DefaultGracefulShutdownSeconds = 15;
        public const uint DefaultMaxPipeListeners = 128;

        [JsonConstructor]
        public LocalCasServiceSettings()
        {
        }

        public LocalCasServiceSettings(
            long defaultSingleInstanceTimeoutSec,
            uint gracefulShutdownSeconds = DefaultGracefulShutdownSeconds,
            uint maxPipeListeners = DefaultMaxPipeListeners,
            string scenarioName = null,
            uint grpcPort = 0,
            string grpcPortFileName = null,
            int? bufferSizeForGrpcCopies = null,
            int? gzipBarrierSizeForGrpcCopies = null
            )
        {
            DefaultSingleInstanceTimeoutSec = defaultSingleInstanceTimeoutSec;
            GracefulShutdownSeconds = gracefulShutdownSeconds;
            MaxPipeListeners = maxPipeListeners;
            ScenarioName = scenarioName;
            GrpcPort = grpcPort;
            GrpcPortFileName = grpcPortFileName;
            BufferSizeForGrpcCopies = bufferSizeForGrpcCopies;
            GzipBarrierSizeForGrpcCopies = gzipBarrierSizeForGrpcCopies;
        }

        /// <summary>
        /// Default time to wait for an instance of the CAS to start up.
        /// </summary>
        [DataMember]
        public long DefaultSingleInstanceTimeoutSec { get; private set; }

        /// <summary>
        /// Server-side time allowed on shutdown for clients to gracefully close connections.
        /// </summary>
        [DataMember]
        public uint GracefulShutdownSeconds { get; set; } = DefaultGracefulShutdownSeconds;

        /// <summary>
        /// Number of CASaaS listening threads (pipe servers), max 254. This restricts
        /// the total connections allowed across the entire machine.
        /// </summary>
        [DataMember]
        public uint MaxPipeListeners { get; set; } = DefaultMaxPipeListeners;

        /// <summary>
        /// The GRPC port to use.
        /// </summary>
        [DataMember]
        public uint GrpcPort { get; set; }

        /// <summary>
        /// Name of the memory mapped file where the GRPC port number is saved.
        /// </summary>
        [DataMember]
        public string GrpcPortFileName { get; set; } = DefaultFileName;

        /// <summary>
        /// Name of the custom scenario that the CAS connects to.
        /// allows multiple CAS services to coexist in a machine
        /// since this factors into the cache root and the event that
        /// identifies a particular CAS instance.
        /// </summary>
        [DataMember]
        public string ScenarioName { get; set; }

        /// <summary>
        /// Period of inactivity after which sessions are shutdown and forgotten.
        /// </summary>
        [DataMember]
        public int? UnusedSessionTimeoutMinutes { get; set; } = null;

        /// <summary>
        /// Period of inactivity after which sessions with a heartbeat are shutdown and forgotten.
        /// </summary>
        [DataMember]
        public int? UnusedSessionHeartbeatTimeoutMinutes { get; set; } = null;

        /// <summary>
        /// Gets the buffer size used during streaming for GRPC copies.
        /// </summary>
        [DataMember]
        public int? BufferSizeForGrpcCopies { get; set; } = null;

        /// <summary>
        /// Files greater than this size will be compressed via GZip when GZip is enabled.
        /// </summary>
        [DataMember]
        public int? GzipBarrierSizeForGrpcCopies { get; set; } = null;
    }
}
