// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
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
        public void Invalidate();
    }

    /// <nodoc />
    public struct DefaultResourceWrapperAdapter<T> : IResourceWrapperAdapter<T>
        where T : IStartupShutdownSlim
    {
        /// <nodoc />
        public T Value { get; private set; }

        /// <nodoc />
        public void Invalidate() { }

        /// <nodoc />
        public DefaultResourceWrapperAdapter(T value)
        {
            Value = value;
        }
    }

    /// <nodoc />
    public struct ResourceWrapperV1Adapter<T> : IResourceWrapperAdapter<T>
        where T : IStartupShutdownSlim
    {
        private readonly ResourceWrapper<T> _wrapper;

        /// <nodoc />
        public T Value => _wrapper.Value;

        /// <nodoc />
        public void Invalidate() => _wrapper.Invalidate();

        /// <nodoc />
        public ResourceWrapperV1Adapter(ResourceWrapper<T> wrapper)
        {
            _wrapper = wrapper;
        }
    }

    /// <nodoc />
    public struct ResourceWrapperV2Adapter<T> : IResourceWrapperAdapter<T>
        where T : IStartupShutdownSlim
    {
        private readonly ResourceWrapperV2<T> _wrapper;

        /// <nodoc />
        public T Value => _wrapper.Value;

        /// <nodoc />
        public void Invalidate() => _wrapper.Invalidate();

        /// <nodoc />
        public ResourceWrapperV2Adapter(ResourceWrapperV2<T> wrapper)
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

        // NOTE: Unifying interfaces here is pain for no gain. Just dispatch manually and remove when obsolete later.
        private readonly ResourcePool<GrpcCopyClientKey, GrpcCopyClient>? _resourcePool;
        private readonly ResourcePoolV2<GrpcCopyClientKey, GrpcCopyClient>? _resourcePoolV2;

        /// <summary>
        /// Cache for <see cref="GrpcCopyClient"/>.
        /// </summary>
        public GrpcCopyClientCache(Context context, GrpcCopyClientCacheConfiguration? configuration = null, IClock? clock = null)
        {
            configuration ??= new GrpcCopyClientCacheConfiguration();
            _configuration = configuration;

            switch (_configuration.ResourcePoolVersion)
            {
                case GrpcCopyClientCacheConfiguration.PoolVersion.Disabled:
                    break;
                case GrpcCopyClientCacheConfiguration.PoolVersion.V1:
                    _resourcePool = new ResourcePool<GrpcCopyClientKey, GrpcCopyClient>(
                        context,
                        _configuration.ResourcePoolConfiguration,
                        (key) => new GrpcCopyClient(key, _configuration.GrpcCopyClientConfiguration),
                        clock);
                    break;
                case GrpcCopyClientCacheConfiguration.PoolVersion.V2:
                    _resourcePoolV2 = new ResourcePoolV2<GrpcCopyClientKey, GrpcCopyClient>(
                        context,
                        _configuration.ResourcePoolConfiguration,
                        (key) => new GrpcCopyClient(key, _configuration.GrpcCopyClientConfiguration),
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
                    var client = new GrpcCopyClient(key, _configuration.GrpcCopyClientConfiguration);

                    await client.StartupAsync(context).ThrowIfFailure();
                    var result = await operation(context, new DefaultResourceWrapperAdapter<GrpcCopyClient>(client));
                    await client.ShutdownAsync(context).ThrowIfFailure();
                    return result;
                }
                case GrpcCopyClientCacheConfiguration.PoolVersion.V1:
                {
                    Contract.AssertNotNull(_resourcePool);
                    using var resourceWrapper = await _resourcePool.CreateAsync(key);
                    return await operation(context, new ResourceWrapperV1Adapter<GrpcCopyClient>(resourceWrapper));
                }
                case GrpcCopyClientCacheConfiguration.PoolVersion.V2:
                {
                    Contract.AssertNotNull(_resourcePoolV2);

                    return await _resourcePoolV2.UseAsync(key, async resourceWrapper =>
                    {
                        // This ensures that the operation we want to perform conforms to the cancellation. When the
                        // resource needs to be removed, the token will be cancelled. Once the operation completes, we
                        // will be able to proceed with shutdown.
                        using var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(context.Token, resourceWrapper.ShutdownToken);
                        var nestedContext = new OperationContext(context, cancellationTokenSource.Token);
                        return await operation(nestedContext, new ResourceWrapperV2Adapter<GrpcCopyClient>(resourceWrapper));
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
            _resourcePoolV2?.Dispose();
        }
    }
}
