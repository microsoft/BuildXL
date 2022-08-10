// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.ServiceModel;
using System.Threading.Tasks;
using ProtoBuf.Grpc;

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    /// <summary>
    /// Public interface provided via gRPC by the content metadata service.
    /// </summary>
    [ServiceContract]
    public interface IGlobalCacheService
    {
        /// <nodoc />
        Task<GetContentLocationsResponse> GetContentLocationsAsync(GetContentLocationsRequest request, CallContext callContext = default);

        /// <nodoc />
        Task<RegisterContentLocationsResponse> RegisterContentLocationsAsync(RegisterContentLocationsRequest request, CallContext callContext = default);

        /// <nodoc />
        Task<CompareExchangeResponse> CompareExchangeAsync(CompareExchangeRequest request, CallContext callContext = default);

        /// <nodoc />
        Task<GetLevelSelectorsResponse> GetLevelSelectorsAsync(GetLevelSelectorsRequest request, CallContext callContext = default);

        /// <nodoc />
        Task<GetContentHashListResponse> GetContentHashListAsync(GetContentHashListRequest request, CallContext callContext = default);
    }
}
