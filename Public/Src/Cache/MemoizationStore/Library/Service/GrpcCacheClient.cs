// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
using static global::ContentStore.Grpc.ContentServer;
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
            ServiceClientContentSessionTracer tracer,
            IAbsFileSystem fileSystem,
            int grpcPort,
            string scenario,
            TimeSpan? heartbeatInterval = null)
            : base(tracer, fileSystem, grpcPort, scenario, heartbeatInterval, Capabilities.All)
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
                    Header = new RequestHeader(context.TracingContext.Id, sessionContext.SessionId),
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
            using (var enumerator = strongFingerprints.ToResultsAsyncEnumerable().Buffer(BatchSize).GetEnumerator())
            {
                while (await enumerator.MoveNext(context.Token))
                {
                    var sfBatch = enumerator.Current;
                    var result = await PerformOperationAsync(
                        context,
                        async sessionContext =>
                    {
                        var request = new IncorporateStrongFingerprintsRequest()
                        {
                            Header = new RequestHeader(context.TracingContext.Id, sessionContext.SessionId),
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
            }

            return BoolResult.Success;
        }
    }
}
