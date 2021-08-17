// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Utils;
using Grpc.Core;
using ProtoBuf.Grpc.Server;
using static Grpc.Core.Server;

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    /// <summary>
    /// Provides binding for code-first protobuf-net service by implementing generic <see cref="IGrpcServiceEndpoint"/>.
    /// </summary>
    public class ProtobufNetGrpcServiceEndpoint<TService, TServiceImpl> : StartupShutdownComponentBase, IGrpcServiceEndpoint
        where TServiceImpl : TService, IStartupShutdownSlim
        where TService : class
    {
        protected override Tracer Tracer { get; }

        private readonly TServiceImpl _service;

        public ProtobufNetGrpcServiceEndpoint(string name, TServiceImpl service)
        {
            Tracer = new Tracer(name + ".Endpoint");
            _service = service;
            LinkLifetime(service);
        }

        /// <inheritdoc />
        public void BindServices(ServiceDefinitionCollection services)
        {
            var textWriterAdapter = new TextWriterAdapter(
                StartupContext,
                Interfaces.Logging.Severity.Info,
                component: Tracer.Name,
                operation: "Grpc");
            services.AddCodeFirst<TService>(_service, MetadataServiceSerializer.BinderConfiguration, textWriterAdapter);
        }
    }
}
