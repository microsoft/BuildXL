// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using BuildXL.Cache.ContentStore.Exceptions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;

#pragma warning disable IDE0044 // Make field readonly

namespace BuildXL.Cache.ContentStore.Service
{
    /// <summary>
    ///     CASaaS configuration
    /// </summary>
    [DataContract]
    public class ServiceConfiguration
    {
        /// <summary>
        ///     A good default for simple uses.
        /// </summary>
        public const uint DefaultGracefulShutdownSeconds = 3;

        /// <summary>
        /// The port number that disables GRPC
        /// </summary>
        public const uint GrpcDisabledPort = 0;

        /// <summary>
        /// The default port for GRPC (Which is disabled).
        /// </summary>
        public const uint DefaultGrpcPort = GrpcDisabledPort;

        /// <summary>
        /// The default port for Encrypted GRPC (Which is disabled).
        /// </summary>
        public const uint DefaultEncryptedGrpcPort = GrpcDisabledPort;

        [DataMember(Name = "NamedCacheRoots")]
        private IDictionary<string, string> _namedCacheRootsRaw;
        private Dictionary<string, AbsolutePath> _namedCacheRoots;

        [DataMember(Name = "DataRootPath")]
        private string? _dataRootPathRaw;
        private AbsolutePath? _dataRootPath;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ServiceConfiguration"/> class.
        /// </summary>
        public ServiceConfiguration(
            IDictionary<string, AbsolutePath> namedCacheRoots,
            AbsolutePath? dataRootPath,
            uint gracefulShutdownSeconds,
            int grpcPort,
            string? grpcPortFileName = null,
            int? bufferSizeForGrpcCopies = null,
            int? proactivePushCountLimit = null,
            TimeSpan? asyncSessionShutdownTimeout = null
            )
        {
            Contract.Requires(namedCacheRoots != null);

            _namedCacheRootsRaw = namedCacheRoots.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Path);
            _dataRootPathRaw = dataRootPath?.Path;
            GracefulShutdownSeconds = gracefulShutdownSeconds;
            GrpcPort = (uint)grpcPort;
            GrpcPortFileName = grpcPortFileName;
            BufferSizeForGrpcCopies = bufferSizeForGrpcCopies;
            ProactivePushCountLimit = proactivePushCountLimit;
            _namedCacheRoots = new Dictionary<string, AbsolutePath>();
            AsyncSessionShutdownTimeout = asyncSessionShutdownTimeout;
            Initialize();
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ServiceConfiguration"/> class.
        /// </summary>
        public ServiceConfiguration(
            IDictionary<string, string> namedCacheRootsRaw,
            AbsolutePath dataRootPath,
            uint gracefulShutdownSeconds,
            int grpcPort,
            string? grpcPortFileName = null)
            : this(namedCacheRootsRaw.ToDictionary(x => x.Key, v => new AbsolutePath(v.Value)), dataRootPath, gracefulShutdownSeconds, grpcPort, grpcPortFileName)
        {
            Contract.Requires(dataRootPath != null);
        }

        /// <summary>
        ///     Gets a value indicating whether the state is valid after deserialization.
        /// </summary>
        public bool IsValid => Error == null;

        /// <summary>
        ///     Gets a descriptive error when IsValid gives false.
        /// </summary>
        public string? Error { get; private set; }

        /// <summary>
        ///     Gets number of seconds to give clients to disconnect before connections are closed hard.
        /// </summary>
        [DataMember]
        public uint GracefulShutdownSeconds { get; private set; }

        /// <summary>
        /// Gets the GRPC port to use for server.
        /// </summary>
        [DataMember]
        public uint GrpcPort { get; set; }

        /// <summary>
        /// Gets the GRPC port to use for server on encrypted channel.
        /// </summary>
        [DataMember]
        public uint EncryptedGrpcPort { get; set; }

        /// <summary>
        /// Name of the non persistent memory-maped file where GRPC port will be exposed to allow for port auto-detection by clients.
        /// A default value will be used if none is provided. Should only override to allow for multiple instances of the service to run concurrently (not recommended).
        /// </summary>
        [DataMember]
        public string? GrpcPortFileName { get; private set; }

        /// <summary>
        /// Gets the buffer size used during streaming for GRPC copies.
        /// </summary>
        [DataMember]
        public int? BufferSizeForGrpcCopies { get; set; }

        /// <summary>
        /// The max number of proactive pushes that can happen at the same time.
        /// </summary>
        public int? ProactivePushCountLimit { get; set; }

        /// <summary>
        /// The max number of copy operations that can happen at the same time from this machine.
        /// </summary>
        public int? CopyRequestHandlingCountLimit { get; set; }

        /// <summary>
        ///     Gets the named cache roots.
        /// </summary>
        public IReadOnlyDictionary<string, AbsolutePath> NamedCacheRoots
        {
            get
            {
                if (!IsValid)
                {
                    throw new CacheException("Invalid service configuration");
                }

                return _namedCacheRoots;
            }
        }

        /// <summary>
        ///     Gets the service data root directory path.
        /// </summary>
        public AbsolutePath? DataRootPath
        {
            get
            {
                if (!IsValid)
                {
                    throw new CacheException("Invalid service configuration");
                }

                return _dataRootPath;
            }
        }

        /// <inheritdoc cref="LocalServerConfiguration.TraceGrpcOperations"/>
        public bool TraceGrpcOperation { get; set; }

        /// <inheritdoc cref="LocalServerConfiguration.DoNotShutdownSessionsInUse"/>
        public bool DoNotShutdownSessionsInUse { get; set; }

        /// <inheritdoc cref="LocalServerConfiguration.AsyncSessionShutdownTimeout"/>
        public TimeSpan? AsyncSessionShutdownTimeout { get; set; }

        /// <summary>
        /// Gets the verb on ContentStoreApp.exe to use.
        /// </summary>
        public virtual string GetVerb()
        {
            return "service";
        }

        /// <summary>
        /// Create the command line arguments to match this configuration.
        /// </summary>
        public virtual string GetCommandLineArgs(
            LocalServerConfiguration? localContentServerConfiguration = null,
            string? scenario = null,
            bool logAutoFlush = false)
        {
            var args = new StringBuilder(GetVerb());

            if (GrpcPort != ServiceConfiguration.GrpcDisabledPort)
            {
                args.AppendFormat(" /grpcPort={0}", GrpcPort);
            }

            if (EncryptedGrpcPort != ServiceConfiguration.GrpcDisabledPort)
            {
                args.AppendFormat(" /encryptedGrpcPort={0}", EncryptedGrpcPort);
            }

            if (GrpcPortFileName != null)
            {
                args.AppendFormat(" /grpcPortFileName={0}", GrpcPortFileName);
            }

            if (localContentServerConfiguration?.UnusedSessionTimeout != null)
            {
                args.AppendFormat(" /unusedSessionTimeoutSeconds={0}", localContentServerConfiguration.UnusedSessionTimeout.TotalSeconds);
            }

            if (localContentServerConfiguration?.UnusedSessionHeartbeatTimeout != null)
            {
                args.AppendFormat(" /unusedSessionHeartbeatTimeoutSeconds={0}", localContentServerConfiguration.UnusedSessionHeartbeatTimeout.TotalSeconds);
            }

            var namedCacheRoots = NamedCacheRoots;
            if (namedCacheRoots.Any())
            {
                var sbNames = new StringBuilder();
                var sbRoots = new StringBuilder();

                foreach (var kvp in namedCacheRoots)
                {
                    sbNames.AppendFormat(",{0}", kvp.Key);
                    sbRoots.AppendFormat(",{0}", kvp.Value);
                }

                args.AppendFormat(" /names={0}", sbNames.ToString().TrimStart(','));
                args.AppendFormat(" /paths={0}", sbRoots.ToString().TrimStart(','));
            }

            if (DataRootPath != null)
            {
                args.AppendFormat(" /dataRootPath={0}", DataRootPath.Path);
            }

            if (scenario != null)
            {
                args.AppendFormat(" /scenario={0}", scenario);
            }

            if (logAutoFlush)
            {
                args.Append(" /logautoflush");
            }

            return args.ToString();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            var sb = new StringBuilder();
            var i = 0;

            sb.AppendFormat("{0}=[", nameof(NamedCacheRoots));

            foreach (var kvp in _namedCacheRoots)
            {
                if (i++ > 0)
                {
                    sb.Append(", ");
                }

                sb.AppendFormat("name=[{0}] path=[{1}]", kvp.Key, kvp.Value);
            }

            sb.Append("]");

            if (_dataRootPath != null)
            {
                sb.AppendFormat(", DataRootPath={0}", _dataRootPath);
            }

            sb.AppendFormat(", GracefulShutdownSeconds={0}", GracefulShutdownSeconds);
            sb.AppendFormat(", GrpcPort={0}", GrpcPort);
            sb.AppendFormat(", EncryptedGrpcPort={0}", EncryptedGrpcPort);
            sb.AppendFormat(", GrcpPortFileName={0}", GrpcPortFileName);
            sb.AppendFormat(", BufferSizeForGrpcCopies={0}", BufferSizeForGrpcCopies);

            return sb.ToString();
        }

        [OnDeserialized]
#pragma warning disable IDE0060 // Remove unused parameter
        private void OnDeserialized(StreamingContext context)
#pragma warning restore IDE0060 // Remove unused parameter
        {
            Initialize();
        }

        private void Initialize()
        {
            if (GracefulShutdownSeconds == 0)
            {
                GracefulShutdownSeconds = DefaultGracefulShutdownSeconds;
            }

            _namedCacheRoots ??= new Dictionary<string, AbsolutePath>();

            if (_namedCacheRootsRaw == null)
            {
                return;
            }

            foreach (var kvp in _namedCacheRootsRaw)
            {
                try
                {
                    _namedCacheRoots.Add(kvp.Key, new AbsolutePath(kvp.Value));
                }
                catch (ArgumentException e)
                {
                    Error = $"Cache with name=[{kvp.Key}] has invalid path=[{kvp.Value}], reason=[{e}]";
                    return;
                }
            }

            if (_dataRootPathRaw != null)
            {
                try
                {
                    _dataRootPath = new AbsolutePath(_dataRootPathRaw);
                }
                catch (ArgumentException e)
                {
                    Error = $"DataRootPath=[{_dataRootPathRaw}] is not a valid path, reason=[{e}].";
                }
            }
        }
    }
}
