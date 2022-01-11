// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <summary>
    /// Adapter interface for the different wrappers that resource pools have.
    /// </summary>
    public interface IResourceWrapperAdapter<T>
    {
        /// <nodoc />
        public T Value { get; }

        /// <nodoc />
        public void Invalidate(Context context);
    }

    /// <nodoc />
    public class DefaultResourceWrapperAdapter<T> : IResourceWrapperAdapter<T>
        where T : IStartupShutdownSlim
    {
        /// <nodoc />
        public T Value { get; }

        /// <nodoc />
        public void Invalidate(Context context) { }

        /// <nodoc />
        public DefaultResourceWrapperAdapter(T value)
        {
            Value = value;
        }
    }

    /// <nodoc />
    public class ResourceWrapperAdapter<T> : IResourceWrapperAdapter<T>
        where T : IStartupShutdownSlim
    {
        private readonly ResourceWrapper<T> _wrapper;

        /// <nodoc />
        public T Value => _wrapper.Value;

        /// <nodoc />
        public void Invalidate(Context context) => _wrapper.Invalidate(context);

        /// <nodoc />
        public ResourceWrapperAdapter(ResourceWrapper<T> wrapper)
        {
            _wrapper = wrapper;
        }
    }

    /// <summary>
    /// Cache for <see cref="GrpcCopyClient"/>.
    /// </summary>
    public sealed class GrpcCopyClientCache : IDisposable
    {
        private readonly GrpcCopyClientCacheConfiguration _configuration;
        private readonly ByteArrayPool _grpcCopyClientBufferPool;

        private readonly ResourcePool<GrpcCopyClientKey, GrpcCopyClient>? _resourcePool;

        /// <summary>
        /// Cache for <see cref="GrpcCopyClient"/>.
        /// </summary>
        public GrpcCopyClientCache(Context context, GrpcCopyClientCacheConfiguration? configuration = null, IClock? clock = null)
        {
            configuration ??= new GrpcCopyClientCacheConfiguration();
            _configuration = configuration;

            _grpcCopyClientBufferPool = new ByteArrayPool(configuration.GrpcCopyClientConfiguration.ClientBufferSizeBytes);

            switch (_configuration.ResourcePoolVersion)
            {
                case GrpcCopyClientCacheConfiguration.PoolVersion.Disabled:
                    break;
                case GrpcCopyClientCacheConfiguration.PoolVersion.V1:
                case GrpcCopyClientCacheConfiguration.PoolVersion.V2:
                    _resourcePool = new ResourcePool<GrpcCopyClientKey, GrpcCopyClient>(
                        context,
                        _configuration.ResourcePoolConfiguration,
                        (key) => new GrpcCopyClient(context, key, _configuration.GrpcCopyClientConfiguration, sharedBufferPool: _grpcCopyClientBufferPool),
                        clock);
                    break;
            }
        }

        /// <summary>
        /// Use an existing <see cref="GrpcCopyClient"/> if possible, else create a new one.
        /// </summary>
        public async Task<TResult> UseWithInvalidationAsync<TResult>(OperationContext context, string host, int grpcPort, Func<OperationContext, IResourceWrapperAdapter<GrpcCopyClient>, Task<TResult>> operation)
        {
            var key = new GrpcCopyClientKey(host, grpcPort);
            switch (_configuration.ResourcePoolVersion)
            {
                case GrpcCopyClientCacheConfiguration.PoolVersion.Disabled:
                {
                    var client = new GrpcCopyClient(context, key, _configuration.GrpcCopyClientConfiguration, sharedBufferPool: _grpcCopyClientBufferPool);

                    await client.StartupAsync(context).ThrowIfFailure();
                    var result = await operation(context, new DefaultResourceWrapperAdapter<GrpcCopyClient>(client));
                    await client.ShutdownAsync(context).ThrowIfFailure();
                    return result;
                }
                case GrpcCopyClientCacheConfiguration.PoolVersion.V1:
                case GrpcCopyClientCacheConfiguration.PoolVersion.V2:
                {
                    Contract.AssertNotNull(_resourcePool);

                    return await _resourcePool.UseAsync(context, key, async resourceWrapper =>
                    {
                        // This ensures that the operation we want to perform conforms to the cancellation. When the
                        // resource needs to be removed, the token will be cancelled. Once the operation completes, we
                        // will be able to proceed with shutdown.
                        using var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(context.Token, resourceWrapper.ShutdownToken);
                        var nestedContext = new OperationContext(context, cancellationTokenSource.Token);
                        return await operation(nestedContext, new ResourceWrapperAdapter<GrpcCopyClient>(resourceWrapper));
                    });
                }
            }

            throw new NotImplementedException($"Unhandled resource pool version `{_configuration.ResourcePoolVersion}`");
        }

        /// <summary>
        /// Use an existing <see cref="GrpcCopyClient"/> if possible, else create a new one.
        /// </summary>
        public Task<TResult> UseAsync<TResult>(OperationContext context, string host, int grpcPort, Func<OperationContext, GrpcCopyClient, Task<TResult>> operation)
        {
            return UseWithInvalidationAsync(context, host, grpcPort, (context, adapter) => operation(context, adapter.Value));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _resourcePool?.Dispose();
        }
    }
}
