// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Utils;

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <summary>
    /// Settings for <see cref="GrpcCopyClientCache"/>
    /// </summary>
    public class GrpcCopyClientCacheConfiguration
    {
        /// <summary>
        /// Maximum number of clients to cache
        /// </summary>
        public int MaxClientCount { get; set; } = 512;

        /// <summary>
        /// Maximum age of cached clients
        /// </summary>
        public int MaxClientAgeMinutes { get; set; } = 55;

        /// <summary>
        /// Buffer size used to read files from disk
        /// </summary>
        public int? BufferSize { get; set; } = null;

        /// <summary>
        /// How long to wait until the first byte of the copy response arrives, in seconds. This may include the time
        /// to connect to the other machine if the gRPC connection dies.
        /// </summary>
        public int? CopyTimeoutSeconds { get; set; } = null;

        /// <summary>
        /// Enable clients to invalidate instances at will
        /// </summary>
        public bool EnableInstanceInvalidation { get; set; } = false;

        /// <summary>
        /// Disable caching completely
        /// </summary>
        public bool DisableInstanceCaching { get; set; } = false;
    }

    /// <summary>
    /// Cache for <see cref="GrpcCopyClient"/>.
    /// </summary>
    public sealed class GrpcCopyClientCache : ResourcePool<GrpcCopyClientKey, GrpcCopyClient>
    {
        private readonly bool _disableInstanceCaching;
        /// <summary>
        /// Cache for <see cref="GrpcCopyClient"/>.
        /// </summary>
        public GrpcCopyClientCache(Context context, GrpcCopyClientCacheConfiguration? configuration = null)
            : base(
                  context,
                  configuration?.MaxClientCount ?? 512,
                  configuration?.MaxClientAgeMinutes ?? 55,
                  (key) => new GrpcCopyClient(key, configuration?.BufferSize ?? null, configuration?.CopyTimeoutSeconds ?? null),
                  enableInstanceInvalidation: configuration?.EnableInstanceInvalidation ?? false)
        {
            _disableInstanceCaching = configuration?.DisableInstanceCaching ?? false;
        }

        /// <summary>
        /// Use an existing <see cref="GrpcCopyClient"/> if possible, else create a new one.
        /// </summary>
        public Task<ResourceWrapper<GrpcCopyClient>> CreateAsync(string host, int grpcPort, bool useCompression)
        {
            var key = new GrpcCopyClientKey(host, grpcPort, useCompression);
            if (_disableInstanceCaching)
            {
                return Task.FromResult(CreateResourceWrapper(key, shutdownOnDispose: true));
            }

            return base.CreateAsync(key);
        }
    }
}
