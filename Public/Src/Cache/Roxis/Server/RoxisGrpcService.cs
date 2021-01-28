using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Roxis.Common;
using BuildXL.Cache.Roxis.Grpc;
using BuildXL.Utilities;
using Grpc.Core;
using GrpcCore = global::Grpc.Core;
using GrpcEnvironment = BuildXL.Cache.ContentStore.Service.Grpc.GrpcEnvironment;

namespace BuildXL.Cache.Roxis.Server
{
    /// <summary>
    /// Grpc frontend for an <see cref="IRoxisService"/>. Handles all aspects related to Grpc communication with
    /// clients.
    /// </summary>
    public class RoxisGrpcService : StartupShutdownSlimBase
    {
        private readonly RoxisGrpcServiceConfiguration _configuration;
        private readonly IRoxisService _service;
        private GrpcCore.Server? _grpcServer;

        protected override Tracer Tracer { get; } = new Tracer(nameof(RoxisGrpcService));

        public RoxisGrpcService(RoxisGrpcServiceConfiguration configuration, IRoxisService service)
        {
            _configuration = configuration;
            _service = service;
        }

        protected override Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            var bindAddress = _configuration.BindAddress;
            context.TraceInfo($"gRPC service binding on address {bindAddress}:{_configuration.Port}", component: nameof(RoxisGrpcService));

            _grpcServer = new GrpcCore.Server(GrpcEnvironment.GetServerOptions())
            {
                // TODO: perhaps multi-bind for the frontend + backend scenario?
                Ports = { new ServerPort(bindAddress, _configuration.Port, ServerCredentials.Insecure) },
                RequestCallTokensPerCompletionQueue = _configuration.RequestCallTokensPerCompletionQueue,
            };

            var metadataServiceGrpcAdapter = new RoxisGrpcAdapter(
                            context.CreateNested(nameof(RoxisGrpcService)),
                            _service);
            var serviceDefinition = Grpc.RoxisService.BindService(metadataServiceGrpcAdapter);
            _grpcServer.Services.Add(serviceDefinition);

            _grpcServer.Start();
            return BoolResult.SuccessTask;
        }

        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            if (_grpcServer != null)
            {
                await _grpcServer.ShutdownAsync();
            }

            return BoolResult.Success;
        }


        internal sealed class RoxisGrpcAdapter : Grpc.RoxisService.RoxisServiceBase
        {
            private readonly SerializationPool _serializationPool = new SerializationPool();

            private readonly IRoxisService _service;
            private readonly OperationContext _context;

            private Tracer Tracer { get; } = new Tracer(nameof(RoxisGrpcService));

            public RoxisGrpcAdapter(OperationContext context, IRoxisService service)
            {
                _context = context;
                _service = service;
            }

            public override Task<Reply> Execute(Request request, ServerCallContext callContext)
            {
                // TODO: read-only stream wrap over a span
                // TODO: unsafe byte string
                // TODO: error handling?
                // TODO: operation context via HTTP headers
                // TODO: resource handling here.

                Guid operationId = _context.TracingContext.Id;
                foreach (var header in callContext.RequestHeaders)
                {
                    if (header.Key.Equals("X-Cache-Operation-Id", StringComparison.OrdinalIgnoreCase))
                    {
                        Guid.TryParse(header.Value, out operationId);
                        break;
                    }
                }

                var tracingContext = new Context(operationId, _context.TracingContext.Logger);
                var operationContext = new OperationContext(tracingContext, token: callContext.CancellationToken);

                return operationContext.PerformNonResultOperationAsync(Tracer, async () =>
                {
                    var commandRequest = _serializationPool.Deserialize(request.Request_.ToByteArray(), reader => CommandRequest.Deserialize(reader));

                    try
                    {
                        var commandResponse = await _service.HandleAsync(commandRequest);
                        var serialized = _serializationPool.Serialize(commandResponse, (value, writer) => value.Serialize(writer));
                        return new Reply()
                        {
                            Reply_ = Google.Protobuf.ByteString.CopyFrom(serialized),
                        };
                    }
                    catch (Exception e)
                    {
                        // TODO: do this properly.
                        throw new RpcException(new Status(StatusCode.Internal, detail: e.ToString()), message: "Internal server error");
                    }
                }, traceErrorsOnly: true);
            }
        }

    }
}
