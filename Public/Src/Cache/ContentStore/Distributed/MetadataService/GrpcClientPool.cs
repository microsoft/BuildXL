// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Grpc;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using Grpc.Core;
using ProtoBuf.Grpc.Client;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    public class GrpcClientAccessor<TService> : StartupShutdownComponentBase, IClientAccessor<MachineLocation, TService>
        where TService : class
    {
        protected override Tracer Tracer { get; } = new Tracer($"{nameof(GrpcClientAccessor<TService>)}<{typeof(TService).Name}>");

        private readonly ConditionalWeakTable<ConnectionHandle, TService> _clientTable = new ConditionalWeakTable<ConnectionHandle, TService>();
        private readonly IClientAccessor<MachineLocation, ConnectionHandle> _connectionAccessor;
        private readonly LocalClient<TService>? _localClient;

        public GrpcClientAccessor(IClientAccessor<MachineLocation, ConnectionHandle> connectionAccessor, LocalClient<TService>? localClient = null)
        {
            _connectionAccessor = connectionAccessor;
            _localClient = localClient;
            LinkLifetime(connectionAccessor);

            if (_localClient != null)
            {
                LinkLifetime(_localClient);
            }
        }

        public Task<TResult> UseAsync<TResult>(OperationContext context, MachineLocation key, Func<TService, Task<TResult>> operation)
        {
            if (_localClient?.Location.Equals(key) == true)
            {
                return operation(_localClient.Client);
            }

            return _connectionAccessor.UseAsync(context, key, connectionHandle =>
            {
                var client = _clientTable.GetValue(connectionHandle, static h => h.Channel.CreateGrpcService<TService>(MetadataServiceSerializer.ClientFactory));
                return operation(client);
            });
        }
    }

    public class GrpcConnectionPool : StartupShutdownComponentBase, IClientAccessor<MachineLocation, ConnectionHandle>
    {
        protected override Tracer Tracer { get; } = new Tracer(nameof(GrpcConnectionPool));

        private readonly ResourcePool<MachineLocation, ConnectionHandle> _pool;

        /// <nodoc />
        public GrpcConnectionPool(ConnectionPoolConfiguration configuration, OperationContext context, IClock? clock = null)
        {
            _pool = new ResourcePool<MachineLocation, ConnectionHandle>(
                context, configuration, location => new ConnectionHandle(context, location, configuration), clock);
        }

        /// <inheritdoc />
        protected override Task<BoolResult> ShutdownComponentAsync(OperationContext context)
        {
            _pool.Dispose();
            return BoolResult.SuccessTask;
        }

        /// <nodoc />
        public Task<TResult> UseAsync<TResult>(OperationContext context, MachineLocation key, Func<ConnectionHandle, Task<TResult>> operation)
        {
            return _pool.UseAsync(context, key, wrapper => operation(wrapper.Value));
        }
    }

    public class ConnectionHandle : StartupShutdownSlimBase
    {
        protected override Tracer Tracer { get; } = new Tracer(nameof(ConnectionHandle));

        public MachineLocation Location { get; }

        public string Host { get; }
        public int Port { get; }

        internal ChannelBase Channel { get; }

        private readonly ConnectionPoolConfiguration _configuration;

        protected override string GetArgumentsMessage() => $"{Host}:{Port}";

        public ConnectionHandle(OperationContext context, MachineLocation location, ConnectionPoolConfiguration configuration)
        {
            Location = location;
            _configuration = configuration;

            var hostInfo = location.ExtractHostInfo();
            Host = hostInfo.host;
            Port = hostInfo.port ?? _configuration.DefaultPort;
            Channel = GrpcChannelFactory.CreateChannel(context, GetChannelCreationOptions(Host, Port));
        }

        private ChannelCreationOptions GetChannelCreationOptions(string host, int port) =>
            new ChannelCreationOptions(_configuration.UseGrpcDotNet, host, port, GrpcCoreOptions: Enumerable.Empty<ChannelOption>(), _configuration.GrpcDotNetOptions);

        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            await Channel.ConnectAsync(SystemClock.Instance, _configuration.ConnectTimeout);
            return BoolResult.Success;
        }

        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            await Channel.ShutdownAsync();
            return BoolResult.Success;
        }
    }

    public class ConnectionPoolConfiguration : ResourcePoolConfiguration
    {
        public int DefaultPort { get; set; }

        public TimeSpan ConnectTimeout { get; set; } = ContentStore.Grpc.GrpcConstants.DefaultTimeout;

        public bool UseGrpcDotNet { get; set; }

        public GrpcDotNetClientOptions GrpcDotNetOptions { get; set; } = GrpcDotNetClientOptions.Default;
    }
}
