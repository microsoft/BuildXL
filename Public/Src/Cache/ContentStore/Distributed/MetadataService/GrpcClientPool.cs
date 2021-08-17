// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using Grpc.Core;
using ProtoBuf.Grpc.Client;
using ProtoBuf.Grpc.Configuration;

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    public class ClientPool<TClient> : ResourcePool<MachineLocation, ClientHandle<TClient>>, IClientAccessor<MachineLocation, TClient>
        where TClient : class
    {
        public ClientPool(Context context, ClientResourcePoolConfiguration configuration, IClock clock = null)
            : base(context, configuration, location => new ClientHandle<TClient>(location, configuration), clock)
        {
        }

        public Task<TResult> UseAsync<TResult>(Context context, MachineLocation key, Func<TClient, Task<TResult>> operation)
        {
            return base.UseAsync(context, key, wrapper => operation(wrapper.Value.Client));
        }
    }

    public class ClientHandle<TClient> : StartupShutdownSlimBase
        where TClient : class
    {
        protected override Tracer Tracer { get; } = new Tracer(nameof(ClientHandle<TClient>));

        public MachineLocation Location { get; }

        public int Port { get; }

        public TClient Client { get; }

        private Channel Channel { get; }

        private readonly ClientResourcePoolConfiguration _configuration;

        public ClientHandle(MachineLocation location, ClientResourcePoolConfiguration configuration)
        {
            Location = location;
            _configuration = configuration;

            var hostInfo = location.ExtractHostInfo();
            Port = hostInfo.port ?? _configuration.DefaultPort;
            Channel = new Channel(hostInfo.host, Port, ChannelCredentials.Insecure);
            Client = Channel.CreateGrpcService<TClient>(ClientFactory.Create(MetadataServiceSerializer.BinderConfiguration));
        }

        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            await Channel.ConnectAsync(DateTime.UtcNow + _configuration.ConnectTimeout);

            return BoolResult.Success;
        }

        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            await Channel.ShutdownAsync();

            return BoolResult.Success;
        }
    }

    public class ClientResourcePoolConfiguration : ResourcePoolConfiguration
    {
        public int DefaultPort { get; set; }

        public TimeSpan ConnectTimeout { get; set; } = ContentStore.Grpc.GrpcConstants.DefaultTimeout;
    }
}
