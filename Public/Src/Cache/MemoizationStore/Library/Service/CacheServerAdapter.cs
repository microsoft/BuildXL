// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Service.Grpc;
using ContentStore.Grpc;
using Grpc.Core;

namespace BuildXL.Cache.MemoizationStore.Sessions.Grpc;

/// <inheritdoc />
public class CacheServerAdapter : ContentServerAdapter
{
    private readonly GrpcCacheServer _cacheServer;

    /// <nodoc />
    public CacheServerAdapter(GrpcCacheServer cacheServer)
        : base(cacheServer)
    {
        _cacheServer = cacheServer;
    }

    /// <nodoc />
    public override Task<AddOrGetContentHashListResponse> AddOrGetContentHashList(AddOrGetContentHashListRequest request, ServerCallContext context) => _cacheServer.AddOrGetContentHashListAsync(request, context);

    /// <nodoc />
    public override Task<GetContentHashListResponse> GetContentHashList(GetContentHashListRequest request, ServerCallContext context) => _cacheServer.GetContentHashListAsync(request, context);

    /// <nodoc />
    public override Task<GetSelectorsResponse> GetSelectors(GetSelectorsRequest request, ServerCallContext context) => _cacheServer.GetSelectorsAsync(request, context);

    /// <nodoc />
    public override Task<IncorporateStrongFingerprintsResponse> IncorporateStrongFingerprints(IncorporateStrongFingerprintsRequest request, ServerCallContext context) => _cacheServer.IncorporateStrongFingerprints(request, context);
}
