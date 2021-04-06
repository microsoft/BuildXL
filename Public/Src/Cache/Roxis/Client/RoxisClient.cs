// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Roxis.Common;
using BuildXL.Utilities;
using Grpc.Core;

namespace BuildXL.Cache.Roxis.Client
{
    public class RoxisClient : StartupShutdownSlimBase, IRoxisClient
    {
        private readonly SerializationPool _serializationPool = new SerializationPool();

        private readonly RoxisClientConfiguration _configuration;

        private Channel? _channel;
        private Grpc.RoxisService.RoxisServiceClient? _client;

        protected override Tracer Tracer { get; } = new Tracer(nameof(RoxisClient));

        public RoxisClient(RoxisClientConfiguration configuration)
        {
            Contract.AssertNotNullOrEmpty(configuration.GrpcHost);
            Contract.Assert(configuration.GrpcPort > 0);

            _configuration = configuration;
        }

        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            _channel = new Channel(_configuration.GrpcHost, _configuration.GrpcPort, ChannelCredentials.Insecure);
            await _channel.ConnectAsync();

            _client = new Grpc.RoxisService.RoxisServiceClient(_channel);

            return BoolResult.Success;
        }

        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            Contract.AssertNotNull(_channel);
            await _channel.ShutdownAsync();
            return BoolResult.Success;
        }

        public Task<Result<CommandResponse>> ExecuteAsync(OperationContext context, CommandRequest request)
        {
            return context.PerformOperationAsync(Tracer, async () =>
            {
                var requestAsBytes = _serializationPool.Serialize(request, (value, writer) => value.Serialize(writer));
                var requestAsProto = new Grpc.Request()
                {
                    Request_ = Google.Protobuf.ByteString.CopyFrom((byte[])requestAsBytes),
                };

                var callOptions = new CallOptions()
                    .WithCancellationToken(context.Token)
                    .WithHeaders(new Metadata() {
                        { "X-Cache-Client-Version", "0.0" },
                        { "X-Cache-Operation-Id", context.TracingContext.TraceId },
                    });

                Contract.AssertNotNull(_client);
                var asyncUnaryCall = _client.ExecuteAsync(requestAsProto, callOptions);

                Grpc.Reply responseAsProto = await asyncUnaryCall;
                var responseAsBytes = responseAsProto.Reply_.ToByteArray();
                var response = _serializationPool.Deserialize(responseAsBytes, reader => CommandResponse.Deserialize(reader));
                Contract.AssertNotNull(response);

                return new Result<CommandResponse>(response);
            }, traceErrorsOnly: true);
        }

        #region Serialization / Deserialization

        #endregion
    }
}
