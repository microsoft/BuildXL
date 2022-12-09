// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using ContentStore.Grpc;
using static BuildXL.Cache.MemoizationStore.Service.GrpcDataConverter;

namespace BuildXL.Cache.MemoizationStore.Service
{
    /// <summary>
    /// An implementation of a cache service client based on GRPC.
    /// </summary>
    public class GrpcCacheClient : GrpcContentClient
    {
        /// <nodoc />
        public GrpcCacheClient(
            OperationContext context,
            ServiceClientContentSessionTracer tracer,
            IAbsFileSystem fileSystem,
            ServiceClientRpcConfiguration configuration,
            string scenario,
            Capabilities capabilities = Capabilities.AllNonPublishing)
            : base(context, tracer, fileSystem, configuration, scenario, capabilities)
        {
        }

        /// <summary>
        /// Add or get content hash list for strong fingerprint
        /// </summary>
        public Task<AddOrGetContentHashListResult> AddOrGetContentHashListAsync(OperationContext context, StrongFingerprint strongFingerprint, ContentHashListWithDeterminism contentHashListWithDeterminism)
        {
            return PerformOperationAsync(
                context,
                async sessionContext =>
            {
                var request = new AddOrGetContentHashListRequest
                {
                    Header = sessionContext.CreateHeader(),
                    Fingerprint = strongFingerprint.ToGrpc(),
                    HashList = contentHashListWithDeterminism.ToGrpc(),
                };

                AddOrGetContentHashListResponse response = await SendGrpcRequestAndThrowIfFailedAsync(
                    sessionContext,
                    async () => await Client.AddOrGetContentHashListAsync(request),
                    throwFailures: false);

                return response.FromGrpc();
            });
        }

        /// <summary>
        /// Get content hash list for strong fingerprint
        /// </summary>
        public Task<GetContentHashListResult> GetContentHashListAsync(OperationContext context, StrongFingerprint strongFingerprint)
        {
            return PerformOperationAsync(
                context,
                async sessionContext =>
            {
                var request = new GetContentHashListRequest()
                {
                    Header = sessionContext.CreateHeader(),
                    Fingerprint = strongFingerprint.ToGrpc(),
                };

                GetContentHashListResponse response = await SendGrpcRequestAndThrowIfFailedAsync(
                    sessionContext,
                    async () => await Client.GetContentHashListAsync(request));

                return response.FromGrpc();
            });
        }

        /// <summary>
        /// Get selectors for weak fingerprint
        /// </summary>
        public Task<Result<LevelSelectors>> GetLevelSelectorsAsync(OperationContext context, Fingerprint weakFingerprint, int level)
        {
            return PerformOperationAsync(
                context,
                async sessionContext =>
            {
                var request = new GetSelectorsRequest()
                {
                    Header = new RequestHeader(context.TracingContext.TraceId, sessionContext.SessionId),
                    WeakFingerprint = FromGrpc(weakFingerprint),
                    Level = level,
                };

                GetSelectorsResponse response = await SendGrpcRequestAndThrowIfFailedAsync(
                    sessionContext,
                    async () => await Client.GetSelectorsAsync(request));

                return response.FromGrpc();
            });
        }

        /// <summary>
        /// Touch strong fingerprints
        /// </summary>
        public async Task<BoolResult> IncorporateStrongFingerprintsAsync(OperationContext context, IEnumerable<Task<StrongFingerprint>> strongFingerprints)
        {
            await foreach (var sfBatch in strongFingerprints.ToResultsAsyncEnumerable().Buffer(BatchSize).WithCancellation(context.Token))
            {
                var result = await PerformOperationAsync(
                    context,
                    async sessionContext =>
                {
                    var request = new IncorporateStrongFingerprintsRequest()
                    {
                        Header = new RequestHeader(context.TracingContext.TraceId, sessionContext.SessionId),
                    };

                    request.StrongFingerprints.AddRange(sfBatch.Select(sf => sf.ToGrpc()));

                    var response = await SendGrpcRequestAndThrowIfFailedAsync(
                        sessionContext,
                        async () => await Client.IncorporateStrongFingerprintsAsync(request));

                    return BoolResult.Success;
                });

                if (!result)
                {
                    return result;
                }
            }

            return BoolResult.Success;
        }
    }
}
